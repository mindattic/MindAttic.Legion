using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.LLMVoting;
using MindAttic.LLMVoting.Providers;
using NUnit.Framework;

namespace LLMVoting.Tests;

/// <summary>
/// Unit tests for LLMVotingService.
/// All tests use stub HTTP handlers — no real API calls are made.
/// </summary>

// ── Model tests ───────────────────────────────────────────────────────────────

[TestFixture]
public class QuorumTests
{
    [Test]
    public void Plurality_HasZeroThreshold()        => Assert.That(Quorum.Plurality.Threshold(),      Is.EqualTo(0.0));
    [Test]
    public void SimpleMajority_HasFiftyPercent()    => Assert.That(Quorum.SimpleMajority.Threshold(), Is.EqualTo(0.50));
    [Test]
    public void TwoThirds_HasSixtySeven()           => Assert.That(Quorum.TwoThirds.Threshold(),      Is.EqualTo(0.67));
    [Test]
    public void Unanimous_HasOneHundredPercent()    => Assert.That(Quorum.Unanimous.Threshold(),      Is.EqualTo(1.00));
}

[TestFixture]
public class VotingConfigurationTests
{
    [Test]
    public void ActiveProviders_ExcludesEmptyKeys()
    {
        var config = new VotingConfiguration
        {
            ApiKeys = { ["claude"] = "real-key", ["openai"] = "", ["gemini"] = "   " }
        };
        Assert.That(config.ActiveProviderIds, Has.Count.EqualTo(1));
        Assert.That(config.ActiveProviderIds[0], Is.EqualTo("claude"));
    }

    [Test]
    public void ActiveProviders_EmptyConfig_ReturnsEmpty()
    {
        var config = new VotingConfiguration();
        Assert.That(config.ActiveProviderIds, Is.Empty);
    }
}

[TestFixture]
public class VoterProfileTests
{
    [Test]
    public void DefaultVoterProfile_HasEmptyPersonality()
    {
        var profile = new VoterProfile { ProviderId = "claude", Name = "Claude" };
        Assert.That(profile.PersonalityMarkdown, Is.Empty);
    }

    [Test]
    public void ForCharacter_SetsCharacterNameInPersonality()
    {
        var profile = VoterProfile.ForCharacter(
            "Sable Chen",
            "Ruthless pragmatist. Values information above all.",
            "claude",
            apiKey: "key");

        Assert.That(profile.Name, Is.EqualTo("Sable Chen"));
        Assert.That(profile.PersonalityMarkdown, Does.Contain("Sable Chen"));
        Assert.That(profile.PersonalityMarkdown, Does.Contain("Ruthless pragmatist"));
        Assert.That(profile.ApiKeyOverride, Is.EqualTo("key"));
    }

    [Test]
    public void ForCharacter_DifferentVoterIds()
    {
        var p1 = VoterProfile.ForCharacter("A", "psych A", "claude");
        var p2 = VoterProfile.ForCharacter("B", "psych B", "claude");
        Assert.That(p1.VoterId, Is.Not.EqualTo(p2.VoterId));
    }
}

[TestFixture]
public class VoteResultTests
{
    [Test]
    public void VotingResult_SuccessfulVoters_ExcludesErrors()
    {
        var result = new VotingResult
        {
            IndividualVotes =
            [
                new VoteResult { IsError = false },
                new VoteResult { IsError = true },
                new VoteResult { IsError = false },
            ]
        };
        Assert.That(result.SuccessfulVoters, Is.EqualTo(2));
    }

    [Test]
    public void ScoredVotingResult_DefaultValues()
    {
        var result = new ScoredVotingResult();
        Assert.That(result.AggregateScores, Is.Empty);
        Assert.That(result.FailingDimensions, Is.Empty);
        Assert.That(result.ImprovementDirectives, Is.Empty);
        Assert.That(result.WeakestDimension, Is.Empty);
    }
}

[TestFixture]
public class VoteRequestTests
{
    [Test]
    public void DefaultRequest_HasEmptyOptions()
    {
        var req = new VoteRequest { Question = "Should we?" };
        Assert.That(req.Options, Is.Empty);
        Assert.That(req.Dimensions, Is.Empty);
        Assert.That(req.MaxTokens, Is.EqualTo(2048));
    }

    [Test]
    public void ScoredRequest_Inherits_MaxTokens()
    {
        var req = new ScoredVoteRequest
        {
            Question   = "Rate this.",
            Dimensions = ["VOICE", "PACING"],
            MaxTokens  = 1024,
        };
        Assert.That(req.MaxTokens, Is.EqualTo(1024));
        Assert.That(req.Dimensions, Has.Count.EqualTo(2));
    }
}

// ── Service tests with stub HTTP ──────────────────────────────────────────────

[TestFixture]
public class LLMVotingServiceTests
{
    private LLMVotingService BuildService(string stubResponse, string? apiKey = "test-key")
    {
        var config = new VotingConfiguration
        {
            ApiKeys = { ["claude"] = apiKey ?? "" },
            JudgeProviderId = "claude",
        };
        var handler = new StubHttpHandler(stubResponse);
        var http    = new HttpClient(handler);
        var prov    = new LlmVotingProvider(http, config);
        return new LLMVotingService(prov, config, NullLogger<LLMVotingService>.Instance);
    }

    [Test]
    public void GetActiveProviderIds_NoKeys_ReturnsEmpty()
    {
        var svc = BuildService("{}", apiKey: null);
        Assert.That(svc.GetActiveProviderIds(), Is.Empty);
    }

    [Test]
    public void GetActiveProviderIds_WithKey_ReturnsClaude()
    {
        var svc = BuildService("{}");
        Assert.That(svc.GetActiveProviderIds(), Contains.Item("claude"));
    }

    [Test]
    public async Task VoteAsync_NoVoters_ReturnsQuorumNotReached()
    {
        var svc    = BuildService("{}", apiKey: null);
        var result = await svc.VoteAsync("Question?", "context", Quorum.SimpleMajority);
        Assert.That(result.QuorumReached, Is.False);
        Assert.That(result.IndividualVotes, Is.Empty);
    }

    [Test]
    public async Task VoteAsync_ChoiceVote_ParsesDecision()
    {
        var stubJson = """{"content":[{"text":"{\"decision\":\"Yes\",\"reasoning\":\"Makes sense.\",\"confidence\":8}"}]}""";
        var svc      = BuildService(stubJson);
        var request  = new VoteRequest
        {
            Question = "Should we proceed?",
            Options  = ["Yes", "No"],
        };
        var result = await svc.VoteAsync(request, Quorum.SimpleMajority);

        Assert.That(result, Is.Not.Null);
        // With one voter and a parsed "Yes" decision, quorum should be reached (plurality ≥ 0)
        Assert.That(result.IndividualVotes, Has.Count.EqualTo(1));
        Assert.That(result.IndividualVotes[0].Decision, Is.EqualTo("Yes"));
        Assert.That(result.IndividualVotes[0].Confidence, Is.EqualTo(8));
    }

    [Test]
    public async Task VoteAsync_ProviderFails_MarkedAsError()
    {
        var svc = new LLMVotingService(
            new LlmVotingProvider(new HttpClient(new ErrorHttpHandler()), new VotingConfiguration
            {
                ApiKeys = { ["claude"] = "key" }
            }),
            new VotingConfiguration { ApiKeys = { ["claude"] = "key" } },
            NullLogger<LLMVotingService>.Instance);

        var result = await svc.VoteAsync("Question?", "context", Quorum.SimpleMajority);
        Assert.That(result.SuccessfulVoters, Is.EqualTo(0));
    }

    [Test]
    public async Task ScoreAsync_NoVoters_ReturnsEmpty()
    {
        var svc = BuildService("{}", apiKey: null);
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await svc.ScoreAsync(new ScoredVoteRequest { Question = "Rate this.", Dimensions = [] }));
    }

    [Test]
    public async Task ScoreAsync_WithDimensions_ReturnsAggregateScores()
    {
        var stubJson = """{"content":[{"text":"{\"scores\":{\"VOICE\":8,\"PACING\":6},\"reasoning\":\"Good voice.\",\"flags_good\":[\"strong opening\"],\"flags_bad\":[],\"improvement_directive\":\"Tighten act 2\"}"}]}""";
        var svc      = BuildService(stubJson);
        var request  = new ScoredVoteRequest
        {
            Question   = "Rate this story.",
            Dimensions = ["VOICE", "PACING"],
            SynthesizeNarrative = false, // avoid second LLM call in unit test
        };

        var result = await svc.ScoreAsync(request);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.AggregateScores.ContainsKey("VOICE"), Is.True);
        Assert.That(result.AggregateScores["VOICE"], Is.EqualTo(8.0));
        Assert.That(result.AggregateScores["PACING"], Is.EqualTo(6.0));
        Assert.That(result.WeakestDimension, Is.EqualTo("PACING"));
    }

    [Test]
    public async Task VoteWithProfilesAsync_UsesPersonality()
    {
        var stubJson = """{"content":[{"text":"{\"decision\":\"Refuse\",\"reasoning\":\"Too risky.\",\"confidence\":9}"}]}""";
        var svc      = BuildService(stubJson);

        var persona = VoterProfile.ForCharacter(
            "Sable Chen",
            "Risk-averse. Never takes contracts without exit strategies.",
            "claude",
            apiKey: "test-key");

        var result = await svc.VoteWithProfilesAsync(
            new VoteRequest { Question = "Take the contract?", Options = ["Accept", "Refuse"] },
            Quorum.Plurality,
            [persona]);

        Assert.That(result.IndividualVotes[0].VoterName, Is.EqualTo("Sable Chen"));
        Assert.That(result.IndividualVotes[0].Decision, Is.EqualTo("Refuse"));
    }

    [Test]
    public async Task VoteWithPersonasAsync_SimpleCall_DoesNotThrow()
    {
        var stubJson = """{"content":[{"text":"{\"decision\":\"Yes\",\"reasoning\":\"Character reasoning.\",\"confidence\":7}"}]}""";
        var svc      = BuildService(stubJson);
        var persona  = VoterProfile.ForCharacter("Kyle", "Pragmatic.", "claude", "test-key");

        Assert.DoesNotThrowAsync(async () =>
            await svc.VoteWithPersonasAsync("Act?", "context", Quorum.Plurality, [persona]));
    }
}

// ── Stub infrastructure ───────────────────────────────────────────────────────

/// <summary>Returns a fixed response body for any HTTP request.</summary>
internal class StubHttpHandler : HttpMessageHandler
{
    private readonly string responseBody;
    public StubHttpHandler(string body) => responseBody = body;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        });
}

/// <summary>Always throws an HttpRequestException — simulates network failure.</summary>
internal class ErrorHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("Simulated network error");
}
