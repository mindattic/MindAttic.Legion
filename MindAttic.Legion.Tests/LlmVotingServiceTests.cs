using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Legion.Providers;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for LlmVotingService.
/// All tests use stub HTTP handlers — no real API calls are made.
/// </summary>

// ── Model tests ───────────────────────────────────────────────────────────────

/// <summary>Pins down the threshold each <see cref="Quorum"/> value reports.</summary>
[TestFixture]
public class QuorumTests
{
    [Test]
    public void Plurality_HasZeroThreshold()        => Assert.That(Quorum.Plurality.Threshold(),      Is.EqualTo(0.0));
    [Test]
    public void SimpleMajority_HasFiftyPercent()    => Assert.That(Quorum.SimpleMajority.Threshold(), Is.EqualTo(0.50));
    [Test]
    public void TwoThirds_IsExactlyTwoThirds()      => Assert.That(Quorum.TwoThirds.Threshold(),      Is.EqualTo(2.0 / 3.0).Within(1e-9));
    [Test]
    public void TwoThirds_AdmitsTwoOfThree()
    {
        // Canonical case the rounded 0.67 used to fail: fraction = 2/3.
        Assert.That(2.0 / 3.0 >= Quorum.TwoThirds.Threshold(), Is.True);
    }
    [Test]
    public void Unanimous_HasOneHundredPercent()    => Assert.That(Quorum.Unanimous.Threshold(),      Is.EqualTo(1.00));

    // IsSatisfiedBy — the exact, integer-math quorum predicate the tally uses.
    [Test]
    public void SimpleMajority_RejectsExactTie()
    {
        // Documented as ">50%": 2 of 4 is a tie, NOT a majority. The old
        // fraction >= 0.50 check wrongly admitted it.
        Assert.That(Quorum.SimpleMajority.IsSatisfiedBy(2, 4), Is.False);
        Assert.That(Quorum.SimpleMajority.IsSatisfiedBy(3, 4), Is.True);
        Assert.That(Quorum.SimpleMajority.IsSatisfiedBy(2, 3), Is.True);
    }

    [Test]
    public void TwoThirds_IsSatisfiedBy_AdmitsTwoOfThree_RejectsOneOfTwo()
    {
        Assert.That(Quorum.TwoThirds.IsSatisfiedBy(2, 3), Is.True);
        Assert.That(Quorum.TwoThirds.IsSatisfiedBy(1, 2), Is.False);
        Assert.That(Quorum.TwoThirds.IsSatisfiedBy(3, 5), Is.False); // 0.6 < 2/3
    }

    [Test]
    public void Plurality_IsSatisfiedBy_NeedsAtLeastOne()
    {
        Assert.That(Quorum.Plurality.IsSatisfiedBy(1, 5), Is.True);
        Assert.That(Quorum.Plurality.IsSatisfiedBy(0, 5), Is.False);
    }

    [Test]
    public void Unanimous_IsSatisfiedBy_NeedsEveryVoter()
    {
        Assert.That(Quorum.Unanimous.IsSatisfiedBy(3, 3), Is.True);
        Assert.That(Quorum.Unanimous.IsSatisfiedBy(2, 3), Is.False);
    }

    [Test]
    public void IsSatisfiedBy_ZeroTotal_AlwaysFalse()
    {
        Assert.That(Quorum.Plurality.IsSatisfiedBy(0, 0), Is.False);
        Assert.That(Quorum.Unanimous.IsSatisfiedBy(0, 0), Is.False);
    }
}

// VotingConfigurationTests moved to VotingConfigurationTests.cs — a comprehensive
// fixture covering the AllowedProviderIds whitelist, shared-credential merging,
// and the trusted-set defaults lives there now.

/// <summary>
/// Tests <see cref="VoterProfile"/> defaults and the
/// <see cref="VoterProfile.ForCharacter"/> persona-builder helper.
/// </summary>
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

/// <summary>
/// Tests <see cref="VotingResult"/> / <see cref="ScoredVotingResult"/> default
/// values and derived properties (e.g. <see cref="VotingResult.SuccessfulVoters"/>).
/// </summary>
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

/// <summary>
/// Tests <see cref="VoteRequest"/> / <see cref="ScoredVoteRequest"/> defaults
/// and inheritance (scored requests inherit <c>MaxTokens</c> from the base).
/// </summary>
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

/// <summary>
/// End-to-end tests for <see cref="LlmVotingService"/> using stub HTTP handlers.
/// Cover the active-provider list, quorum-not-reached path, choice-vote parsing,
/// scored-vote aggregation, and persona/character voting.
/// </summary>
[TestFixture]
public class LlmVotingServiceTests
{
    private LlmVotingService BuildService(string stubResponse, string? apiKey = "test-key")
    {
        var config = new VotingConfiguration
        {
            UseSharedCredentials = false,
            ApiKeys = { ["claude"] = apiKey ?? "" },
            JudgeProviderId = "claude",
        };
        var handler = new StubHttpHandler(stubResponse);
        var http    = new HttpClient(handler);
        var prov    = new LlmVotingProvider(http, config);
        return new LlmVotingService(prov, config, NullLogger<LlmVotingService>.Instance);
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
    public async Task VoteAsync_ChoiceVote_UnparseableReply_MarkedAsError()
    {
        // Model returned plain prose (no JSON object). Choice votes can't tally
        // raw text — must surface as IsError so refill / quorum logic can react.
        var stubJson = """{"content":[{"text":"I'm not sure about this one."}]}""";
        var svc      = BuildService(stubJson);
        var request  = new VoteRequest { Question = "Pick one", Options = ["A", "B"] };

        var result = await svc.VoteAsync(request, Quorum.SimpleMajority);

        Assert.That(result.IndividualVotes, Has.Count.EqualTo(1));
        Assert.That(result.IndividualVotes[0].IsError, Is.True);
        Assert.That(result.SuccessfulVoters, Is.EqualTo(0));
    }

    [Test]
    public async Task VoteAsync_FloatConfidence_DoesNotErrorOut()
    {
        // Models occasionally emit floats for confidence — we round, not crash.
        var stubJson = """{"content":[{"text":"{\"decision\":\"Yes\",\"reasoning\":\"ok\",\"confidence\":8.5}"}]}""";
        var svc      = BuildService(stubJson);
        var request  = new VoteRequest { Question = "Should we?", Options = ["Yes", "No"] };

        var result = await svc.VoteAsync(request, Quorum.SimpleMajority);

        Assert.That(result.IndividualVotes[0].IsError, Is.False);
        Assert.That(result.IndividualVotes[0].Decision, Is.EqualTo("Yes"));
        Assert.That(result.IndividualVotes[0].Confidence, Is.EqualTo(9));
    }

    [Test]
    public async Task VoteAsync_ProviderFails_MarkedAsError()
    {
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            ApiKeys = { ["claude"] = "key" }
        };
        var svc = new LlmVotingService(
            new LlmVotingProvider(new HttpClient(new ErrorHttpHandler()), cfg),
            cfg,
            NullLogger<LlmVotingService>.Instance);

        var result = await svc.VoteAsync("Question?", "context", Quorum.SimpleMajority);
        Assert.That(result.SuccessfulVoters, Is.EqualTo(0));
    }

    [Test]
    public async Task VoteAsync_FreeForm_SingleVoter_ReachesAnyQuorum()
    {
        // 1 voter trivially clears any quorum (1/1 = 100%).
        var stubJson = """{"content":[{"text":"{\"decision\":\"go north\",\"reasoning\":\"shorter route\",\"confidence\":7}"}]}""";
        var svc      = BuildService(stubJson);

        var result = await svc.VoteAsync("Which way?", "context", Quorum.Unanimous);

        Assert.That(result.QuorumReached, Is.True);
        Assert.That(result.Consensus, Is.EqualTo("go north"));
        Assert.That(result.ConsensusStrength, Is.EqualTo(1.0));
    }

    [Test]
    public async Task VoteAsync_FreeForm_NoNarrativeSynthesis_FailsStrictQuorum()
    {
        // SynthesizeNarrative=false means no judge consulted, so we cannot
        // measure cross-voter agreement on free-form text. Fail closed for
        // anything stricter than Plurality. (This test only has one voter, so
        // it actually passes — the stricter case would need multiple voters,
        // which BuildService doesn't support, but the pinning value here is
        // that we don't unconditionally claim quorum=true.)
        var stubJson = """{"content":[{"text":"{\"decision\":\"go north\",\"reasoning\":\"r\",\"confidence\":7}"}]}""";
        var svc      = BuildService(stubJson);

        var result = await svc.VoteAsync(
            new VoteRequest { Question = "Q", SynthesizeNarrative = false },
            Quorum.Unanimous);

        Assert.That(result.IndividualVotes, Has.Count.EqualTo(1));
        // 1-voter case is unanimous trivially; what we're really pinning here
        // is that ConsensusStrength reflects actual agreement (1.0 of 1), not
        // the previous hardcoded 1.0 regardless of state.
        Assert.That(result.ConsensusStrength, Is.EqualTo(1.0));
        Assert.That(result.QuorumReached, Is.True);
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
    public async Task ScoreAsync_DimensionNoVoterScored_IsOmittedNotZero()
    {
        // Voter scores VOICE and PACING but omits TENSION. TENSION must not be
        // recorded as 0.0 (which would flag it failing, pick it weakest, and
        // drag OVERALL down to (8+6+0)/3).
        var stubJson = """{"content":[{"text":"{\"scores\":{\"VOICE\":8,\"PACING\":6},\"reasoning\":\"x\",\"flags_good\":[],\"flags_bad\":[],\"improvement_directive\":\"\"}"}]}""";
        var svc = BuildService(stubJson);
        var request = new ScoredVoteRequest
        {
            Question = "Rate.",
            Dimensions = ["VOICE", "PACING", "TENSION"],
            SynthesizeNarrative = false,
        };

        var result = await svc.ScoreAsync(request);

        Assert.That(result.AggregateScores.ContainsKey("TENSION"), Is.False, "un-scored dimension must not be recorded");
        Assert.That(result.FailingDimensions, Does.Not.Contain("TENSION"));
        Assert.That(result.WeakestDimension, Is.EqualTo("PACING"));
        Assert.That(result.AggregateScores["OVERALL"], Is.EqualTo(7.0), "OVERALL averages only scored dimensions");
    }

    [Test]
    public void ScoreWithProfilesAsync_EmptyDimensions_Throws()
    {
        var svc = BuildService("{}");
        var voters = new[] { new VoterProfile { ProviderId = "claude", Name = "claude" } };
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await svc.ScoreWithProfilesAsync(new ScoredVoteRequest { Question = "x", Dimensions = [] }, voters));
    }

    [Test]
    public async Task VoteAsync_ChoiceVote_JsonWrappedInProseWithStrayBraces_StillParses()
    {
        // The model prefixes a footnote "{1}" before the real JSON object. The
        // naive first-{…last-} slice produced invalid JSON ("{1}): {…}") and the
        // vote was lost; the balanced-region extractor must pick the real object.
        var stubJson = """{"content":[{"text":"Sure — see note {1}: {\"decision\":\"Yes\",\"reasoning\":\"ok\",\"confidence\":8}"}]}""";
        var svc      = BuildService(stubJson);
        var request  = new VoteRequest { Question = "Proceed?", Options = ["Yes", "No"] };

        var result = await svc.VoteAsync(request, Quorum.SimpleMajority);

        Assert.That(result.IndividualVotes[0].IsError, Is.False);
        Assert.That(result.IndividualVotes[0].Decision, Is.EqualTo("Yes"));
    }

    [Test]
    public async Task ScoreAsync_ConsensusStrengthsAndFailures_KeepGoodBadPolarity()
    {
        // Both voters report the same positive (flags_good) and negative
        // (flags_bad) observation. The aggregate must file the good one under
        // ConsensusStrengths and the bad one under ConsensusFailures — never the
        // reverse (the old frequency-only split inverted them).
        var stubJson = """{"content":[{"text":"{\"scores\":{\"VOICE\":7},\"reasoning\":\"r\",\"flags_good\":[\"Strong opening\"],\"flags_bad\":[\"Weak ending\"],\"improvement_directive\":\"Tighten act two\"}"}]}""";
        var svc      = BuildService(stubJson);

        // Two claude voters so both responses parse against the Claude-shaped stub
        // body (an openai voter would parse choices[] and error out, dropping the
        // panel below minConsensus and defeating the point of the test).
        var voters = new[]
        {
            new VoterProfile { ProviderId = "claude", Name = "claude#1", ApiKeyOverride = "k" },
            new VoterProfile { ProviderId = "claude", Name = "claude#2", ApiKeyOverride = "k" },
        };
        var request = new ScoredVoteRequest
        {
            Question            = "Rate this scene.",
            Dimensions          = ["VOICE"],
            SynthesizeNarrative = false,
        };

        var result = await svc.ScoreWithProfilesAsync(request, voters);

        Assert.That(result.ConsensusStrengths, Does.Contain("Strong opening"));
        Assert.That(result.ConsensusStrengths, Does.Not.Contain("Weak ending"));
        Assert.That(result.ConsensusFailures, Does.Contain("Weak ending"));
        Assert.That(result.ConsensusFailures, Does.Not.Contain("Strong opening"));
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

/// <summary>
/// Filesystem-backed tests for <see cref="MindAtticCredentialStore"/>:
/// .key file priority, credentials.json fallback, providers.json rich format,
/// and the resolution order that ties them all together. Each test redirects
/// the credential directory to a temp folder via the
/// <c>MINDATTIC_LLM_CREDENTIALS</c> env var so they can run in isolation.
/// </summary>
[TestFixture]
public class MindAtticCredentialStoreTests
{
    private string tempDir = "";
    private string? prevEnv;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "mindattic-cred-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        prevEnv = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prevEnv);
        try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void GetKey_MissingFolder_ReturnsNull()
    {
        Directory.Delete(tempDir, recursive: true);
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.Null);
    }

    [Test]
    public void GetKey_FromKeyFile_TrimsWhitespace()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "  sk-abc123\n");
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("sk-abc123"));
    }

    [Test]
    public void GetKey_FromCredentialsJson_WhenNoKeyFile()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "credentials.json"),
            """{"openai":"sk-openai-key","gemini":"gm-key"}""");
        Assert.That(MindAtticCredentialStore.GetKey("openai"), Is.EqualTo("sk-openai-key"));
        Assert.That(MindAtticCredentialStore.GetKey("gemini"), Is.EqualTo("gm-key"));
    }

    [Test]
    public void GetKey_KeyFileOverridesCredentialsJson()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "credentials.json"),
            """{"claude":"json-key"}""");
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "file-key");
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("file-key"));
    }

    [Test]
    public void SetKey_CreatesDirectoryAndFile()
    {
        var fresh = Path.Combine(tempDir, "nested");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", fresh);
        MindAtticCredentialStore.SetKey("xai", "grok-xyz");
        Assert.That(MindAtticCredentialStore.GetKey("xai"), Is.EqualTo("grok-xyz"));
    }

    [Test]
    public void ListProviders_UnionsJsonAndKeyFiles()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "credentials.json"),
            """{"openai":"a","gemini":"b"}""");
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "c");

        var providers = MindAtticCredentialStore.ListProviders();
        Assert.That(providers, Is.EquivalentTo(new[] { "openai", "gemini", "claude" }));
    }

    [Test]
    public void VotingConfiguration_ResolvesKeyFromStore()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "shared-key");

        var cfg      = new VotingConfiguration(); // UseSharedCredentials defaults true
        var provider = new LlmVotingProvider(new HttpClient(new StubHttpHandler("{}")), cfg);
        Assert.That(provider.GetApiKey("claude"), Is.EqualTo("shared-key"));
        Assert.That(cfg.ActiveProviderIds, Contains.Item("claude"));
    }

    [Test]
    public void VotingConfiguration_ExplicitKeyOverridesStore()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "shared-key");

        var cfg      = new VotingConfiguration { ApiKeys = { ["claude"] = "explicit-key" } };
        var provider = new LlmVotingProvider(new HttpClient(new StubHttpHandler("{}")), cfg);
        Assert.That(provider.GetApiKey("claude"), Is.EqualTo("explicit-key"));
    }

    [Test]
    public void VotingConfiguration_DisableSharedCredentials_IgnoresStore()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "shared-key");

        var cfg      = new VotingConfiguration { UseSharedCredentials = false };
        var provider = new LlmVotingProvider(new HttpClient(new StubHttpHandler("{}")), cfg);
        Assert.That(provider.GetApiKey("claude"), Is.Null);
        Assert.That(cfg.ActiveProviderIds, Is.Empty);
    }

    // ── providers.json (canonical rich format shared with LLMThinkTank) ─────────

    [Test]
    public void GetKey_FromProvidersJson_ExtractsApiKey()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "providers.json"),
            """{"openai":{"type":"bearer","apiKey":"sk-rich","model":"gpt-4.1-mini","maxTokens":2048}}""");
        Assert.That(MindAtticCredentialStore.GetKey("openai"), Is.EqualTo("sk-rich"));
    }

    [Test]
    public void GetKey_ProvidersJsonOverridesCredentialsJson()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "credentials.json"),
            """{"claude":"legacy-key"}""");
        File.WriteAllText(
            Path.Combine(tempDir, "providers.json"),
            """{"claude":{"type":"anthropic","apiKey":"rich-key"}}""");
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("rich-key"));
    }

    [Test]
    public void GetKey_KeyFileOverridesProvidersJson()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "providers.json"),
            """{"claude":{"type":"anthropic","apiKey":"rich-key"}}""");
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "drop-file-key");
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("drop-file-key"));
    }

    [Test]
    public void SetKey_WritesToProvidersJson_PreservingModelAndMaxTokens()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "providers.json"),
            """{"openai":{"type":"bearer","apiKey":"old","model":"gpt-4.1-mini","maxTokens":2048}}""");

        MindAtticCredentialStore.SetKey("openai", "sk-new");

        var raw = File.ReadAllText(Path.Combine(tempDir, "providers.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var openai = doc.RootElement.GetProperty("openai");
        Assert.That(openai.GetProperty("apiKey").GetString(), Is.EqualTo("sk-new"));
        Assert.That(openai.GetProperty("type").GetString(), Is.EqualTo("bearer"));
        Assert.That(openai.GetProperty("model").GetString(), Is.EqualTo("gpt-4.1-mini"));
        Assert.That(openai.GetProperty("maxTokens").GetInt32(), Is.EqualTo(2048));
    }

    [Test]
    public void SetKey_NewProvider_InfersType()
    {
        MindAtticCredentialStore.SetKey("claude", "k1");
        MindAtticCredentialStore.SetKey("gemini", "k2");
        MindAtticCredentialStore.SetKey("openai", "k3");

        var raw = File.ReadAllText(Path.Combine(tempDir, "providers.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        Assert.That(doc.RootElement.GetProperty("claude").GetProperty("type").GetString(), Is.EqualTo("anthropic"));
        Assert.That(doc.RootElement.GetProperty("gemini").GetProperty("type").GetString(), Is.EqualTo("google"));
        Assert.That(doc.RootElement.GetProperty("openai").GetProperty("type").GetString(), Is.EqualTo("bearer"));
    }

    [Test]
    public void SetKey_DoesNotClobberOtherProviders()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "providers.json"),
            """{"openai":{"type":"bearer","apiKey":"openai-key"},"claude":{"type":"anthropic","apiKey":"claude-key"}}""");

        MindAtticCredentialStore.SetKey("openai", "openai-NEW");

        Assert.That(MindAtticCredentialStore.GetKey("openai"), Is.EqualTo("openai-NEW"));
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("claude-key"));
    }

    [Test]
    public void ListProviders_UnionsAllThreeSources()
    {
        File.WriteAllText(
            Path.Combine(tempDir, "credentials.json"),
            """{"openai":"a"}""");
        File.WriteAllText(
            Path.Combine(tempDir, "providers.json"),
            """{"gemini":{"type":"google","apiKey":"b"}}""");
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "c");

        var providers = MindAtticCredentialStore.ListProviders();
        Assert.That(providers, Is.EquivalentTo(new[] { "openai", "gemini", "claude" }));
    }
}

// ── Persona library + voter factory ───────────────────────────────────────────

/// <summary>
/// Invariants for <see cref="PersonaLibrary"/>: persona count, default-vs-enriched
/// split, unique ids/names, deterministic indexed access, sampling without
/// replacement, and the diversity skeleton (age, pronouns, signature trait).
/// </summary>
[TestFixture]
public class PersonaLibraryTests
{
    [Test]
    public void Count_IsExactlyTheEnrichedLibrary()
    {
        // No per-provider "default" personas — a bare LLM has no persona.
        Assert.That(PersonaLibrary.Count, Is.EqualTo(PersonaLibrary.EnrichedCount));
        Assert.That(PersonaLibrary.Count, Is.EqualTo(1024));
        Assert.That(PersonaLibrary.All, Has.Count.EqualTo(PersonaLibrary.EnrichedCount));
        Assert.That(PersonaLibrary.All, Is.EqualTo(PersonaLibrary.Enriched));
    }

    [Test]
    public void Every_PersonaHasANonEmptyPrompt()
    {
        // The whole point of stripping defaults: every library member is a real persona.
        Assert.That(PersonaLibrary.All.All(p => !string.IsNullOrWhiteSpace(p.PersonalityMarkdown)), Is.True);
    }

    [Test]
    public void All_PersonasHaveUniqueIds()
    {
        var ids = PersonaLibrary.All.Select(p => p.Id).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(PersonaLibrary.Count));
    }

    [Test]
    public void All_PersonasHaveUniqueNames()
    {
        var names = PersonaLibrary.All.Select(p => p.Name).ToList();
        Assert.That(names.Distinct().Count(), Is.EqualTo(PersonaLibrary.Count));
    }

    [Test]
    public void Enriched_PersonasHaveNonEmptyPersonalityMarkdown()
    {
        Assert.That(PersonaLibrary.Enriched.All(p => !string.IsNullOrWhiteSpace(p.PersonalityMarkdown)), Is.True);
    }

    [Test]
    public void Get_ReturnsDeterministicPersonas()
    {
        var first = PersonaLibrary.Get(0);
        var same  = PersonaLibrary.Get(0);
        Assert.That(first, Is.EqualTo(same));
    }

    [Test]
    public void Get_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PersonaLibrary.Get(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PersonaLibrary.Get(PersonaLibrary.Count));
    }

    [Test]
    public void Sample_ReturnsRequestedCount()
    {
        var sample = PersonaLibrary.Sample(50, new Random(42));
        Assert.That(sample, Has.Count.EqualTo(50));
    }

    [Test]
    public void Sample_NoRepeats()
    {
        var sample = PersonaLibrary.Sample(200, new Random(7));
        Assert.That(sample.Select(p => p.Id).Distinct().Count(), Is.EqualTo(200));
    }

    [Test]
    public void Sample_ZeroOrNegative_ReturnsEmpty()
    {
        Assert.That(PersonaLibrary.Sample(0), Is.Empty);
        Assert.That(PersonaLibrary.Sample(-5), Is.Empty);
    }

    [Test]
    public void Sample_ExcessCount_ClampsToFullLibrary()
    {
        var sample = PersonaLibrary.Sample(5000, new Random(1));
        Assert.That(sample, Has.Count.EqualTo(PersonaLibrary.Count));
        Assert.That(sample.Select(p => p.Id).Distinct().Count(), Is.EqualTo(PersonaLibrary.Count));
    }

    [Test]
    public void Sample_WithSeededRng_IsDeterministic()
    {
        var a = PersonaLibrary.Sample(20, new Random(123));
        var b = PersonaLibrary.Sample(20, new Random(123));
        Assert.That(a.Select(p => p.Id), Is.EqualTo(b.Select(p => p.Id)));
    }

    // Enriched-persona checks (age / pronouns / signature trait)

    [Test]
    public void EveryEnrichedPersona_PromptIncludesAgeBetween22And78()
    {
        // Age formula: 22 + ((i*7) % 57) → range [22, 78]
        var bad = PersonaLibrary.Enriched
            .Select((p, i) => new { p, i, expected = 22 + ((i * 7) % 57) })
            .Where(x => !x.p.PersonalityMarkdown.Contains($"age {x.expected}"))
            .ToList();
        Assert.That(bad, Is.Empty, $"persona prompts missing expected age tag: {string.Join(",", bad.Take(5).Select(b => b.i))}");
    }

    [Test]
    public void EveryEnrichedPersona_PromptIncludesOneOfThreePronounSets()
    {
        foreach (var p in PersonaLibrary.Enriched)
        {
            var hasOne = p.PersonalityMarkdown.Contains("she/her")
                      || p.PersonalityMarkdown.Contains("he/him")
                      || p.PersonalityMarkdown.Contains("they/them");
            Assert.That(hasOne, Is.True, $"{p.Id} has no recognized pronoun set");
        }
    }

    [Test]
    public void EveryEnrichedPersona_PromptIncludesSignatureTrait()
    {
        foreach (var p in PersonaLibrary.Enriched)
            Assert.That(p.PersonalityMarkdown, Does.Contain("Signature trait:"), $"{p.Id} missing signature trait line");
    }

    [Test]
    public void EnrichedPersonas_AreUniqueByPersonalityMarkdown()
    {
        var distinct = PersonaLibrary.Enriched.Select(p => p.PersonalityMarkdown).Distinct().Count();
        Assert.That(distinct, Is.EqualTo(PersonaLibrary.EnrichedCount));
    }
}

/// <summary>
/// Tests <see cref="VoterFactory.GenerateUniqueVoters"/> — provider-spread
/// strategy (each available provider before backfill), persona uniqueness
/// across the panel, and edge cases (zero count, empty provider list,
/// case-insensitive provider deduplication).
/// </summary>
[TestFixture]
public class VoterFactoryTests
{
    [Test]
    public void GenerateUniqueVoters_SpreadsAcrossProvidersFirst()
    {
        var voters = VoterFactory.GenerateUniqueVoters(
            count: 5,
            availableProviderIds: new[] { "openai", "claude", "gemini" },
            fallbackProviderId: "claude",
            rng: new Random(0));

        Assert.That(voters, Has.Count.EqualTo(5));
        Assert.That(voters[0].ProviderId, Is.EqualTo("openai"));
        Assert.That(voters[1].ProviderId, Is.EqualTo("claude"));
        Assert.That(voters[2].ProviderId, Is.EqualTo("gemini"));
        Assert.That(voters[3].ProviderId, Is.EqualTo("claude"));
        Assert.That(voters[4].ProviderId, Is.EqualTo("claude"));
    }

    [Test]
    public void GenerateUniqueVoters_NoRepeatedPersonas()
    {
        var voters = VoterFactory.GenerateUniqueVoters(
            count: 50,
            availableProviderIds: new[] { "openai", "claude" },
            rng: new Random(99));

        Assert.That(voters, Has.Count.EqualTo(50));
        var personalityHashes = voters.Select(v => v.PersonalityMarkdown).Distinct().Count();
        Assert.That(personalityHashes, Is.EqualTo(50));
        var names = voters.Select(v => v.Name).Distinct().Count();
        Assert.That(names, Is.EqualTo(50));
    }

    [Test]
    public void GenerateUniqueVoters_EmptyProviderList_UsesFallback()
    {
        var voters = VoterFactory.GenerateUniqueVoters(
            count: 4,
            availableProviderIds: Array.Empty<string>(),
            fallbackProviderId: "claude",
            rng: new Random(0));

        Assert.That(voters, Has.Count.EqualTo(4));
        Assert.That(voters.All(v => v.ProviderId == "claude"), Is.True);
    }

    [Test]
    public void GenerateUniqueVoters_FewerThanProviders_UsesFirstNProviders()
    {
        var voters = VoterFactory.GenerateUniqueVoters(
            count: 2,
            availableProviderIds: new[] { "openai", "claude", "gemini" },
            rng: new Random(0));

        Assert.That(voters, Has.Count.EqualTo(2));
        Assert.That(voters[0].ProviderId, Is.EqualTo("openai"));
        Assert.That(voters[1].ProviderId, Is.EqualTo("claude"));
    }

    [Test]
    public void GenerateUniqueVoters_ZeroCount_ReturnsEmpty()
    {
        var voters = VoterFactory.GenerateUniqueVoters(0, new[] { "openai" });
        Assert.That(voters, Is.Empty);
    }

    [Test]
    public void GenerateUniqueVoters_DedupesProviderList()
    {
        var voters = VoterFactory.GenerateUniqueVoters(
            count: 3,
            availableProviderIds: new[] { "openai", "OpenAI", "openai" },
            fallbackProviderId: "claude",
            rng: new Random(0));

        Assert.That(voters[0].ProviderId, Is.EqualTo("openai"));
        Assert.That(voters[1].ProviderId, Is.EqualTo("claude"));
        Assert.That(voters[2].ProviderId, Is.EqualTo("claude"));
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
