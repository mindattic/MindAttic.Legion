using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;
using MindAttic.Legion.Data;
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
        var persona = PersonaLibrary.Get(PersonaLibrary.Defaults.Count); // first enriched persona
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

/// <summary>
/// Live, on-demand round-trip against a real SQL Server (LocalDB by default, or
/// <c>MINDATTIC_LEGION_DB</c>). Explicit + <c>LiveDb</c> category so plain
/// <c>dotnet test</c> stays offline:
/// <code>dotnet test --filter "Category=LiveDb"</code>
/// </summary>
[TestFixture]
[Category("LiveDb")]
[Explicit("Requires a reachable SQL Server / LocalDB instance.")]
public class PsychometricLiveDbTests
{
    [Test]
    public async Task Migrate_Save_And_Read_AgainstRealSqlServer()
    {
        var connectionString = LegionConnectionString.Default
            .Replace("Database=MindAtticLegion", $"Database=MindAtticLegionTest_{Guid.NewGuid():N}");

        await using var db = LegionData.CreateContext(connectionString);
        try
        {
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"No reachable SQL Server ({ex.Message}).");
        }

        try
        {
            await new PersonaRepository(db).SyncFromLibraryAsync();
            var run = await new AssessmentRunRepository(db).StartAsync(
                "claude", "claude-opus-4-8", "High", PsychometricInstruments.SetVersion, 1, DateTime.UtcNow);
            var profile = PsychometricScorer.ScoreAll("persona-0000",
                new Dictionary<string, IReadOnlyDictionary<int, int>>(),
                "claude", "claude-opus-4-8", DateTime.UtcNow);
            await new PsychometricProfileRepository(db).SaveAsync(profile, run.Id);

            var latest = await new PsychometricProfileRepository(db).GetLatestAsync("persona-0000");
            Assert.That(latest, Is.Not.Null);
            Assert.That(latest!.Mbti.Type, Is.EqualTo("ESTJ"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
