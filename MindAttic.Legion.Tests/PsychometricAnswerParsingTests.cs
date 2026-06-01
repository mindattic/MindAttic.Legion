using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// <see cref="LlmPsychometricAssessor.ParseAnswers"/> must survive the shapes
/// models actually emit: the requested {"answers":[{id,value}]}, an id→value
/// map, a bare array, and payloads wrapped in prose or markdown fences.
/// </summary>
[TestFixture]
public class PsychometricAnswerParsingTests
{
    private static readonly PsychometricInstrument BigFive = PsychometricInstruments.BigFive;

    [Test]
    public void Parses_RequestedAnswersArrayShape()
    {
        var reply = """{"answers":[{"id":1,"value":4},{"id":2,"value":2}]}""";
        var parsed = LlmPsychometricAssessor.ParseAnswers(reply, BigFive);
        Assert.That(parsed[1], Is.EqualTo(4));
        Assert.That(parsed[2], Is.EqualTo(2));
    }

    [Test]
    public void Parses_AnswersAsIdValueMap()
    {
        var reply = """{"answers":{"1":5,"3":1}}""";
        var parsed = LlmPsychometricAssessor.ParseAnswers(reply, BigFive);
        Assert.That(parsed[1], Is.EqualTo(5));
        Assert.That(parsed[3], Is.EqualTo(1));
    }

    [Test]
    public void Parses_BareIdValueMap()
    {
        var reply = """{"1":3,"2":4}""";
        var parsed = LlmPsychometricAssessor.ParseAnswers(reply, BigFive);
        Assert.That(parsed[1], Is.EqualTo(3));
        Assert.That(parsed[2], Is.EqualTo(4));
    }

    [Test]
    public void Parses_ThroughMarkdownFenceAndProse()
    {
        var reply = "Sure! Here are the answers:\n```json\n{\"answers\":[{\"id\":1,\"value\":4}]}\n```\nHope that helps.";
        var parsed = LlmPsychometricAssessor.ParseAnswers(reply, BigFive);
        Assert.That(parsed[1], Is.EqualTo(4));
    }

    [Test]
    public void Parses_BareArrayPositionally()
    {
        // A bare array maps positionally to the instrument's item order.
        var values = Enumerable.Repeat("3", BigFive.Items.Count);
        var reply = "[" + string.Join(",", values) + "]";
        var parsed = LlmPsychometricAssessor.ParseAnswers(reply, BigFive);
        Assert.That(parsed[BigFive.Items[0].Id], Is.EqualTo(3));
        Assert.That(parsed, Has.Count.EqualTo(BigFive.Items.Count));
    }

    [Test]
    public void Parses_StringNumbers()
    {
        var reply = """{"answers":[{"id":1,"value":"5"}]}""";
        var parsed = LlmPsychometricAssessor.ParseAnswers(reply, BigFive);
        Assert.That(parsed[1], Is.EqualTo(5));
    }

    [Test]
    public void Junk_ReturnsEmptyRatherThanThrowing()
    {
        var parsed = LlmPsychometricAssessor.ParseAnswers("no json here at all", BigFive);
        Assert.That(parsed, Is.Empty);
    }
}
