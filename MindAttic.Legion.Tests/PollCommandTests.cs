using MindAttic.Legion;
using MindAttic.Legion.Cli;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="PollCommand"/>'s pure helpers — provider
/// resolution, round-robin assignment, option snapping, distribution
/// aggregation, and the small string utilities. The live HTTP fan-out
/// (<see cref="PollCommand.FanOutAsync"/>) is exercised end-to-end when
/// a developer runs <c>legion poll</c>.
/// </summary>
[TestFixture]
public class PollCommandTests
{
    [Test]
    public void TrustedProviderIds_MatchAskCommandTrustList()
    {
        Assert.That(PollCommand.TrustedProviderIds,
            Is.EquivalentTo(AskCommand.TrustedProviderIds));
    }

    [Test]
    public void DefaultTier_IsLow()
    {
        // Poll's whole purpose is sampling distributions cheaply across many
        // voters — Low tier scales to count=100 without burning budget.
        // Architecture decisions belong on `legion ask`, not `poll`.
        Assert.That(PollCommand.DefaultTier, Is.EqualTo(ModelTier.Low));
    }

    [Test]
    public void DefaultCount_IsTen()
    {
        Assert.That(PollCommand.DefaultCount, Is.EqualTo(10));
    }

    // ── ResolveProviders ───────────────────────────────────────────────────

    [Test]
    public void ResolveProviders_NullRequest_ReturnsFullTrustedSet()
    {
        Assert.That(PollCommand.ResolveProviders(null),
            Is.EquivalentTo(PollCommand.TrustedProviderIds));
    }

    [Test]
    public void ResolveProviders_DropsUntrustedAndDeduplicates()
    {
        var result = PollCommand.ResolveProviders(new[] { "Claude-Api", "claude-api", "mistral", "OPENAI" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude-api", "openai" }));
    }

    // ── AssignRoundRobin ───────────────────────────────────────────────────

    [Test]
    public void AssignRoundRobin_TenVoters_FourProviders_DistributesEvenlyToBoundary()
    {
        // 10 voters / 4 providers = 3,3,2,2 (10 = 4×2 + 2).
        var providers = new[] { "claude", "openai", "gemini", "deepseek" };
        var assignments = PollCommand.AssignRoundRobin(10, providers, ModelTier.Low);

        Assert.That(assignments, Has.Count.EqualTo(10));
        Assert.That(assignments.Count(a => a.ProviderId == "claude"),   Is.EqualTo(3));
        Assert.That(assignments.Count(a => a.ProviderId == "openai"),   Is.EqualTo(3));
        Assert.That(assignments.Count(a => a.ProviderId == "gemini"),   Is.EqualTo(2));
        Assert.That(assignments.Count(a => a.ProviderId == "deepseek"), Is.EqualTo(2));
    }

    [Test]
    public void AssignRoundRobin_HundredVoters_FourProviders_TwentyFiveEach()
    {
        // The user's stated workflow: "100 voters on Low tier" → exactly 25 per provider.
        var providers = new[] { "claude", "openai", "gemini", "deepseek" };
        var assignments = PollCommand.AssignRoundRobin(100, providers, ModelTier.Low);

        Assert.That(assignments, Has.Count.EqualTo(100));
        foreach (var p in providers)
            Assert.That(assignments.Count(a => a.ProviderId == p), Is.EqualTo(25), $"provider {p}");
    }

    [Test]
    public void AssignRoundRobin_PreservesOrderForReproducibility()
    {
        // Round-robin order must be deterministic (provider[0], [1], [2], [3], [0]…)
        // so two runs of `legion poll --count 8` route the same indices to the
        // same providers — important when callers correlate logs.
        var providers = new[] { "claude", "openai", "gemini", "deepseek" };
        var assignments = PollCommand.AssignRoundRobin(8, providers, ModelTier.Low);
        Assert.That(assignments[0].ProviderId, Is.EqualTo("claude"));
        Assert.That(assignments[1].ProviderId, Is.EqualTo("openai"));
        Assert.That(assignments[2].ProviderId, Is.EqualTo("gemini"));
        Assert.That(assignments[3].ProviderId, Is.EqualTo("deepseek"));
        Assert.That(assignments[4].ProviderId, Is.EqualTo("claude"));
    }

    [Test]
    public void AssignRoundRobin_ResolvesTierModelPerAssignment()
    {
        var providers = new[] { "claude-api", "openai" };
        var assignments = PollCommand.AssignRoundRobin(2, providers, ModelTier.High);

        Assert.That(assignments[0].Model, Is.EqualTo("claude-opus-4-7"));
        Assert.That(assignments[1].Model, Is.EqualTo("gpt-5.4"));
    }

    [Test]
    public void AssignRoundRobin_OneProvider_AllVotersGoThere()
    {
        var assignments = PollCommand.AssignRoundRobin(5, new[] { "claude" }, ModelTier.Low);
        Assert.That(assignments.All(a => a.ProviderId == "claude"), Is.True);
        Assert.That(assignments, Has.Count.EqualTo(5));
    }

    // ── SnapToOption ───────────────────────────────────────────────────────

    [Test]
    public void SnapToOption_ExactMatch_CaseInsensitive()
    {
        Assert.That(PollCommand.SnapToOption("YES", new[] { "Yes", "No" }),
            Is.EqualTo("Yes"));
    }

    [Test]
    public void SnapToOption_ContainsMatch_PrefersLongest()
    {
        // Same contract as AskCommand.SnapToOption — longer option wins so
        // "FirstChoiceOption" beats its substring "First".
        Assert.That(PollCommand.SnapToOption("FirstChoiceOption is best",
            new[] { "First", "FirstChoiceOption" }), Is.EqualTo("FirstChoiceOption"));
    }

    [Test]
    public void SnapToOption_OffBallot_ReturnsNull()
    {
        Assert.That(PollCommand.SnapToOption("None of these", new[] { "A", "B" }),
            Is.Null);
    }

    [Test]
    public void SnapToOption_ShortOptionInsideUnrelatedWord_DoesNotMatch()
    {
        // "cat" must not match inside "communicate" — that would count a wrong vote.
        Assert.That(PollCommand.SnapToOption("Let's communicate clearly", new[] { "cat", "dog" }),
            Is.Null);
    }

    // ── Aggregate ──────────────────────────────────────────────────────────

    [Test]
    public void Aggregate_SortsByDescendingCount()
    {
        var outcomes = new[]
        {
            Outcome("A"), Outcome("B"), Outcome("A"), Outcome("C"),
            Outcome("A"), Outcome("B"),
        };
        var dist = PollCommand.Aggregate(outcomes);
        Assert.That(dist[0], Is.EqualTo(("A", 3)));
        Assert.That(dist[1], Is.EqualTo(("B", 2)));
        Assert.That(dist[2], Is.EqualTo(("C", 1)));
    }

    [Test]
    public void Aggregate_GroupsCaseInsensitively()
    {
        // Free-form answers from different providers may differ in case
        // ("Yes" / "yes" / "YES") — they should aggregate as one bucket.
        var outcomes = new[] { Outcome("Yes"), Outcome("yes"), Outcome("YES") };
        var dist = PollCommand.Aggregate(outcomes);
        Assert.That(dist, Has.Count.EqualTo(1));
        Assert.That(dist[0].Count, Is.EqualTo(3));
    }

    [Test]
    public void Aggregate_SkipsFailedAndEmptyOutcomes()
    {
        var outcomes = new[]
        {
            Outcome("A"),
            new PollCommand.VoterOutcome(1, "claude", "m", Ok: false, 0, Answer: null, Error: "boom"),
            new PollCommand.VoterOutcome(2, "claude", "m", Ok: true,  0, Answer: "",   Error: null),
            Outcome("A"),
        };
        var dist = PollCommand.Aggregate(outcomes);
        Assert.That(dist, Has.Count.EqualTo(1));
        Assert.That(dist[0], Is.EqualTo(("A", 2)));
    }

    [Test]
    public void Aggregate_AllFailed_ReturnsEmpty()
    {
        var outcomes = new[]
        {
            new PollCommand.VoterOutcome(0, "x", "m", false, 0, null, "boom"),
        };
        Assert.That(PollCommand.Aggregate(outcomes), Is.Empty);
    }

    // ── Truncate / IsHelp ──────────────────────────────────────────────────

    [Test]
    public void Truncate_FlattensNewlines()
    {
        Assert.That(PollCommand.Truncate("a\nb\r\nc", 50), Is.EqualTo("a b  c"));
    }

    [Test]
    public void IsHelp_RecognizesEveryHelpFlag()
    {
        Assert.That(PollCommand.IsHelp("-h"),     Is.True);
        Assert.That(PollCommand.IsHelp("--help"), Is.True);
        Assert.That(PollCommand.IsHelp("help"),   Is.True);
        Assert.That(PollCommand.IsHelp("/?"),     Is.True);
        Assert.That(PollCommand.IsHelp("--json"), Is.False);
    }

    private static PollCommand.VoterOutcome Outcome(string answer) =>
        new(0, "claude", "claude-haiku-4-5-20251001",
            Ok: true, ElapsedMs: 100, Answer: answer, Error: null);
}
