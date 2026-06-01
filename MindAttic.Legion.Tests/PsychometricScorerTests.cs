using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Deterministic scoring tests for <see cref="PsychometricScorer"/> — feed known
/// item-response vectors and assert the framework outputs. No LLM, no network:
/// these pin the maths (reverse-keying, normalization, tie-breaks, wing/triad).
/// </summary>
[TestFixture]
public class PsychometricScorerTests
{
    private static Dictionary<int, int> Answers(params (int id, int value)[] items)
    {
        var d = new Dictionary<int, int>();
        foreach (var (id, value) in items) d[id] = value;
        return d;
    }

    [Test]
    public void EmptyAnswers_ProduceNeutralMidpointEverywhere()
    {
        var empty = new Dictionary<int, int>();
        var ocean = PsychometricScorer.ScoreBigFive(empty);
        Assert.Multiple(() =>
        {
            Assert.That(ocean.Openness, Is.EqualTo(50.0));
            Assert.That(ocean.Conscientiousness, Is.EqualTo(50.0));
            Assert.That(ocean.Extraversion, Is.EqualTo(50.0));
            Assert.That(ocean.Agreeableness, Is.EqualTo(50.0));
            Assert.That(ocean.Neuroticism, Is.EqualTo(50.0));
        });
    }

    [Test]
    public void Openness_MaxedOut_RespectsReverseKeying()
    {
        // Item 5 is positive; 10/15/20 are reverse-keyed. Agreeing with 5 and
        // disagreeing with the reverse items should drive Openness to 100.
        var ocean = PsychometricScorer.ScoreBigFive(Answers((5, 5), (10, 1), (15, 1), (20, 1)));
        Assert.That(ocean.Openness, Is.EqualTo(100.0));
        Assert.That(ocean.Conscientiousness, Is.EqualTo(50.0), "untouched scales stay neutral");
    }

    [Test]
    public void Openness_BottomedOut_RespectsReverseKeying()
    {
        var ocean = PsychometricScorer.ScoreBigFive(Answers((5, 1), (10, 5), (15, 5), (20, 5)));
        Assert.That(ocean.Openness, Is.EqualTo(0.0));
    }

    [Test]
    public void Mbti_ExtraversionPole_ResolvesToE()
    {
        // EI items: 45/47/49 positive (toward E), 46/48/50 reverse (toward I).
        var mbti = PsychometricScorer.ScoreMbti(Answers((45, 5), (47, 5), (49, 5), (46, 1), (48, 1), (50, 1)));
        Assert.That(mbti.ExtraversionPct, Is.EqualTo(100.0));
        Assert.That(mbti.Type, Does.StartWith("E"));
    }

    [Test]
    public void Mbti_AllNeutral_DefaultsToFirstPolesESTJ()
    {
        var mbti = PsychometricScorer.ScoreMbti(new Dictionary<int, int>());
        Assert.That(mbti.Type, Is.EqualTo("ESTJ"));
        Assert.That(mbti.ThinkingPct, Is.EqualTo(50.0));
    }

    [Test]
    public void Disc_InfluenceDominant_PicksI()
    {
        var disc = PsychometricScorer.ScoreDisc(Answers((75, 5), (76, 5), (77, 5), (78, 5), (79, 5), (80, 5)));
        Assert.That(disc.Influence, Is.EqualTo(100.0));
        Assert.That(disc.PrimaryStyle, Is.EqualTo("I"));
    }

    [Test]
    public void Disc_AllNeutral_TieBreaksToD()
    {
        var disc = PsychometricScorer.ScoreDisc(new Dictionary<int, int>());
        Assert.That(disc.PrimaryStyle, Is.EqualTo("D"));
    }

    [Test]
    public void Enneagram_Type7_HasHeadTriadAndLowerWing()
    {
        var enn = PsychometricScorer.ScoreEnneagram(Answers((105, 5), (106, 5)));
        Assert.Multiple(() =>
        {
            Assert.That(enn.Type, Is.EqualTo(7));
            Assert.That(enn.Triad, Is.EqualTo("Head"));
            Assert.That(enn.Wing, Is.EqualTo(6), "neighbours 6 and 8 tie at neutral → lower number");
            Assert.That(enn.Notation(), Is.EqualTo("7w6"));
        });
    }

    [Test]
    public void Enneagram_AllNeutral_DefaultsToType1Gut()
    {
        var enn = PsychometricScorer.ScoreEnneagram(new Dictionary<int, int>());
        Assert.That(enn.Type, Is.EqualTo(1));
        Assert.That(enn.Triad, Is.EqualTo("Gut"));
    }

    [Test]
    public void ScoreAll_StampsProvenanceAndAllFrameworks()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var answers = new Dictionary<string, IReadOnlyDictionary<int, int>>();
        var profile = PsychometricScorer.ScoreAll("persona-0000", answers, "claude", "claude-opus-4-8", when);

        Assert.Multiple(() =>
        {
            Assert.That(profile.PersonaId, Is.EqualTo("persona-0000"));
            Assert.That(profile.AdministeredByProvider, Is.EqualTo("claude"));
            Assert.That(profile.AdministeredByModel, Is.EqualTo("claude-opus-4-8"));
            Assert.That(profile.InstrumentSetVersion, Is.EqualTo(PsychometricInstruments.SetVersion));
            Assert.That(profile.ScoredAtUtc, Is.EqualTo(when));
            Assert.That(profile.Mbti.Type, Has.Length.EqualTo(4));
            Assert.That(profile.Summary(), Is.Not.Empty);
        });
    }
}
