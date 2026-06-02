using MindAttic.Legion;
using MindAttic.Legion.Providers;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Live, on-demand check that a real trusted model can take the instruments and
/// produce a well-formed profile. Kept <see cref="ExplicitAttribute"/> and in
/// its own <c>LivePsychometrics</c> category (NOT the pre-commit
/// <c>LiveKeysTrusted</c> gate) because each run spends ~5 Opus calls — you opt
/// into that cost explicitly:
/// <code>dotnet test --filter "Category=LivePsychometrics"</code>
/// </summary>
[TestFixture]
[Category("LivePsychometrics")]
[Explicit("Hits the real provider API (≈5 Opus calls) — costs money. Run on demand.")]
public class PsychometricAssessorLiveTests
{
    [Test]
    public async Task RealModel_ProducesAWellFormedProfile()
    {
        var config = new VotingConfiguration
        {
            UseSharedCredentials = true,
            ProviderTimeout = TimeSpan.FromSeconds(60),
            ModelOverrides = MindAttic.Legion.Cli.AskCommand.BuildTierModelOverrides(ModelTier.High),
        };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
        var provider = new LlmVotingProvider(http, config);
        if (string.IsNullOrWhiteSpace(provider.GetApiKey("claude")))
            Assert.Ignore("No claude key in the Vault — skipping live assessment.");

        var assessor = new LlmPsychometricAssessor(provider, "claude", ModelTier.High);
        var persona = PersonaLibrary.Get(0); // first persona
        var result = await assessor.AssessAsync(persona, DateTime.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(result.Profile.Mbti.Type, Has.Length.EqualTo(4));
            Assert.That(result.Profile.Enneagram.Type, Is.InRange(1, 9));
            Assert.That("DISC", Does.Contain(result.Profile.Disc.PrimaryStyle));
            Assert.That(result.Profile.Ocean.Openness, Is.InRange(0, 100));
            Assert.That(result.RawAnswers.Values.Sum(a => a.Count), Is.GreaterThan(0), "got at least some item answers");
        });
        TestContext.Out.WriteLine($"{persona.Id}: {result.Profile.Summary()}");
    }
}
