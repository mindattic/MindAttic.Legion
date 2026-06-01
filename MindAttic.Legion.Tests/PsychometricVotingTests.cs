using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Phase-7 wiring: psychometric-aware panel composition
/// (<see cref="VoterFactory.GenerateDiverseVoters"/>) and trait-segmented vote
/// aggregation (<see cref="PsychometricVoteAnalysis"/>).
/// </summary>
[TestFixture]
public class PsychometricVotingTests
{
    // Build a profile with controllable trait coordinates for spacing tests.
    private static PsychometricProfile Profile(string personaId, double openness, double dominance, string mbti = "INTJ")
    {
        var ocean = new OceanScores(openness, 50, 50, 50, 50);
        var hexaco = new HexacoScores(50, 50, 50, 50, 50, openness);
        var disc = new DiscResult(dominance, 50, 50, 50, dominance >= 50 ? "D" : "S");
        var mbtiResult = new MbtiResult(mbti, 50, 50, 50, 50);
        var enn = new EnneagramResult(5, 4, "Head");
        return new PsychometricProfile(personaId, ocean, hexaco, mbtiResult, enn, disc,
            "claude", "claude-opus-4-8", PsychometricInstruments.SetVersion, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public void GenerateDiverseVoters_OnlyUsesScoredPersonasAndAttachesProfile()
    {
        var ids = PersonaLibrary.Enriched.Take(6).Select(p => p.Id).ToList();
        var profiles = new Dictionary<string, PsychometricProfile>();
        for (var i = 0; i < ids.Count; i++)
            profiles[ids[i]] = Profile(ids[i], openness: i * 20, dominance: 100 - i * 20);

        var voters = VoterFactory.GenerateDiverseVoters(4, new[] { "claude" }, profiles, rng: new Random(1));

        Assert.That(voters, Has.Count.EqualTo(4));
        Assert.That(voters.All(v => v.Psychometrics is not null), Is.True, "each voter carries its profile");
        Assert.That(voters.All(v => profiles.ContainsKey(StripSuffix(v.VoterId))), Is.True, "only scored personas chosen");
        Assert.That(voters.Select(v => v.VoterId).Distinct().Count(), Is.EqualTo(4), "no repeats");
    }

    [Test]
    public void GenerateDiverseVoters_PicksTheExtremesBeforeTheMiddle()
    {
        // Three personas on a line: 0, 50, 100. A 2-pick diverse panel must take
        // the two extremes, never the midpoint.
        var ids = PersonaLibrary.Enriched.Take(3).Select(p => p.Id).ToList();
        var profiles = new Dictionary<string, PsychometricProfile>
        {
            [ids[0]] = Profile(ids[0], openness: 0, dominance: 0),
            [ids[1]] = Profile(ids[1], openness: 50, dominance: 50),
            [ids[2]] = Profile(ids[2], openness: 100, dominance: 100),
        };

        var chosen = VoterFactory.GenerateDiverseVoters(2, new[] { "claude" }, profiles, rng: new Random(7))
            .Select(v => StripSuffix(v.VoterId)).ToHashSet();

        Assert.That(chosen, Does.Contain(ids[0]));
        Assert.That(chosen, Does.Contain(ids[2]));
        Assert.That(chosen, Does.Not.Contain(ids[1]), "midpoint persona is the least diverse pick");
    }

    [Test]
    public void GenerateDiverseVoters_NoProfiles_FallsBackToRandomUnique()
    {
        var voters = VoterFactory.GenerateDiverseVoters(
            3, new[] { "claude" }, new Dictionary<string, PsychometricProfile>(), rng: new Random(1));
        Assert.That(voters, Has.Count.EqualTo(3));
        Assert.That(voters.All(v => v.Psychometrics is null), Is.True);
    }

    [Test]
    public void Segment_SplitsVotesByTrait()
    {
        var voters = new[]
        {
            new VoterProfile { VoterId = "a", Psychometrics = Profile("a", 80, 50, "ENFP") },
            new VoterProfile { VoterId = "b", Psychometrics = Profile("b", 10, 50, "ISTJ") },
            new VoterProfile { VoterId = "c", Psychometrics = Profile("c", 90, 50, "ENTP") },
            new VoterProfile { VoterId = "d", Psychometrics = null }, // unscored → ignored
        };
        var result = new VotingResult
        {
            IndividualVotes =
            {
                new VoteResult { VoterId = "a", Decision = "Yes" },
                new VoteResult { VoterId = "b", Decision = "No" },
                new VoteResult { VoterId = "c", Decision = "Yes" },
                new VoteResult { VoterId = "d", Decision = "Yes" },
                new VoteResult { VoterId = "e", Decision = "No", IsError = true }, // error → ignored
            },
        };

        var byOpenness = PsychometricVoteAnalysis.Segment(voters, result, PsychometricVoteAnalysis.ByOpennessHalf);

        Assert.That(byOpenness["High-Openness"]["Yes"], Is.EqualTo(2));
        Assert.That(byOpenness["Low-Openness"]["No"], Is.EqualTo(1));
        Assert.That(byOpenness.ContainsKey("High-Openness") && byOpenness.ContainsKey("Low-Openness"), Is.True);
        Assert.That(byOpenness.Values.SelectMany(d => d.Values).Sum(), Is.EqualTo(3), "unscored + error votes excluded");
    }

    [Test]
    public void GenerateDiverseVoters_CountExceedingCandidates_ReturnsAllScored()
    {
        var ids = PersonaLibrary.Enriched.Take(3).Select(p => p.Id).ToList();
        var profiles = ids.ToDictionary(id => id, id => Profile(id, 50, 50));

        var voters = VoterFactory.GenerateDiverseVoters(10, new[] { "claude" }, profiles);
        Assert.That(voters, Has.Count.EqualTo(3), "can't exceed the number of scored personas");
    }

    [Test]
    public void GenerateDiverseVoters_SpreadsProvidersThenBackfillsFallback()
    {
        var ids = PersonaLibrary.Enriched.Take(4).Select(p => p.Id).ToList();
        var profiles = new Dictionary<string, PsychometricProfile>();
        for (var i = 0; i < ids.Count; i++) profiles[ids[i]] = Profile(ids[i], i * 30, i * 30);

        var voters = VoterFactory.GenerateDiverseVoters(4, new[] { "claude", "openai" }, profiles, fallbackProviderId: "claude");
        var providers = voters.Select(v => v.ProviderId).ToList();

        Assert.That(providers[0], Is.EqualTo("claude"));
        Assert.That(providers[1], Is.EqualTo("openai"));
        Assert.That(providers.Skip(2), Is.All.EqualTo("claude"), "remaining slots backfill the fallback");
    }

    [Test]
    public void Segment_ByMbtiType_GroupsByExactType()
    {
        var voters = new[]
        {
            new VoterProfile { VoterId = "a", Psychometrics = Profile("a", 50, 50, "INTJ") },
            new VoterProfile { VoterId = "b", Psychometrics = Profile("b", 50, 50, "INTJ") },
            new VoterProfile { VoterId = "c", Psychometrics = Profile("c", 50, 50, "ENFP") },
        };
        var result = new VotingResult
        {
            IndividualVotes =
            {
                new VoteResult { VoterId = "a", Decision = "Yes" },
                new VoteResult { VoterId = "b", Decision = "No" },
                new VoteResult { VoterId = "c", Decision = "Yes" },
            },
        };

        var byType = PsychometricVoteAnalysis.Segment(voters, result, PsychometricVoteAnalysis.ByMbtiType);
        Assert.That(byType["INTJ"]["Yes"], Is.EqualTo(1));
        Assert.That(byType["INTJ"]["No"], Is.EqualTo(1));
        Assert.That(byType["ENFP"]["Yes"], Is.EqualTo(1));
    }

    [Test]
    public void Segment_ByDiscPrimary_LabelsWithDiscPrefix()
    {
        var voters = new[]
        {
            new VoterProfile { VoterId = "a", Psychometrics = Profile("a", 50, 90) }, // dominance≥50 → D
            new VoterProfile { VoterId = "b", Psychometrics = Profile("b", 50, 10) }, // dominance<50 → S
        };
        var result = new VotingResult
        {
            IndividualVotes =
            {
                new VoteResult { VoterId = "a", Decision = "Ship" },
                new VoteResult { VoterId = "b", Decision = "Wait" },
            },
        };

        var byDisc = PsychometricVoteAnalysis.Segment(voters, result, PsychometricVoteAnalysis.ByDiscPrimary);
        Assert.That(byDisc.Keys, Does.Contain("DISC-D"));
        Assert.That(byDisc.Keys, Does.Contain("DISC-S"));
        Assert.That(byDisc["DISC-D"]["Ship"], Is.EqualTo(1));
    }

    private static string StripSuffix(string voterId) => voterId[..voterId.LastIndexOf('-')];
}
