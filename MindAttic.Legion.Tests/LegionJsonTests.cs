using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// The shared JSON slicer used by both the voting service and the psychometric
/// assessor. Pins the tricky cases: prose/fence wrappers, braces inside string
/// literals, and "longest top-level region wins".
/// </summary>
[TestFixture]
public class LegionJsonTests
{
    [Test]
    public void ExtractObject_ReturnsPlainObject() =>
        Assert.That(LegionJson.ExtractObject("""{"a":1}"""), Is.EqualTo("""{"a":1}"""));

    [Test]
    public void ExtractObject_UnwrapsProseAndFences()
    {
        var reply = "Sure:\n```json\n{\"a\":1}\n```\nthanks";
        Assert.That(LegionJson.ExtractObject(reply), Is.EqualTo("""{"a":1}"""));
    }

    [Test]
    public void ExtractObject_IgnoresBracesInsideStrings()
    {
        var reply = """{"note":"a } b { c","ok":true}""";
        Assert.That(LegionJson.ExtractObject(reply), Is.EqualTo(reply));
    }

    [Test]
    public void ExtractObject_LongestTopLevelRegionWins()
    {
        // A stray {1} in the preamble must lose to the real, larger object.
        var reply = """see note {1}: {"decision":"Yes"}""";
        Assert.That(LegionJson.ExtractObject(reply), Is.EqualTo("""{"decision":"Yes"}"""));
    }

    [Test]
    public void ExtractObject_ReturnsEmptySentinelOnMiss() =>
        Assert.That(LegionJson.ExtractObject("no json here"), Is.EqualTo("{}"));

    [Test]
    public void ExtractArray_ReturnsPlainArray() =>
        Assert.That(LegionJson.ExtractArray("prefix [1,2,3] suffix"), Is.EqualTo("[1,2,3]"));

    [Test]
    public void ExtractArray_ReturnsEmptySentinelOnMiss() =>
        Assert.That(LegionJson.ExtractArray("{}"), Is.EqualTo("[]"));
}
