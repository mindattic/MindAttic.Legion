using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Formatting helpers on the psychometric records and the entity ↔ domain
/// mapping. Pure (no EF, no LLM) — these pin the human-facing strings and the
/// round-trip the repository relies on.
/// </summary>
[TestFixture]
public class PsychometricModelTests
{
    private static PsychometricProfile Sample() => new(
        "persona-0042",
        new OceanScores(72, 58, 41, 66, 33),
        new HexacoScores(64, 50, 48, 55, 71, 60),
        new MbtiResult("INTJ", 40, 30, 70, 60),
        new EnneagramResult(9, 1, "Gut"),
        new DiscResult(62, 40, 55, 48, "D"),
        "claude", "claude-opus-4-8", "1.0.0",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Test]
    public void OceanShortCode_FormatsAllFiveDomains() =>
        Assert.That(Sample().Ocean.ShortCode(), Is.EqualTo("O72 C58 E41 A66 N33"));

    [Test]
    public void HexacoShortCode_FormatsAllSixFactors() =>
        Assert.That(Sample().Hexaco.ShortCode(), Is.EqualTo("H64 E50 X48 A55 C71 O60"));

    [Test]
    public void DiscShortCode_FormatsFourDimensions() =>
        Assert.That(Sample().Disc.ShortCode(), Is.EqualTo("D62 I40 S55 C48"));

    [TestCase(9, 1, "9w1")]
    [TestCase(4, 5, "4w5")]
    public void EnneagramNotation_UsesWingForm(int type, int wing, string expected) =>
        Assert.That(new EnneagramResult(type, wing, "Gut").Notation(), Is.EqualTo(expected));

    [Test]
    public void EnneagramNotation_OmitsWingWhenNull() =>
        Assert.That(new EnneagramResult(5, null, "Head").Notation(), Is.EqualTo("5"));

    [Test]
    public void Summary_MentionsEveryFramework()
    {
        var s = Sample().Summary();
        Assert.That(s, Does.Contain("INTJ"));
        Assert.That(s, Does.Contain("9w1"));
        Assert.That(s, Does.Contain("DISC-D"));
        Assert.That(s, Does.Contain("OCEAN"));
        Assert.That(s, Does.Contain("HEXACO"));
    }
}
