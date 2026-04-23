using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MindAttic.LLMVoting.Providers;

namespace MindAttic.LLMVoting;

/// <summary>
/// Multi-LLM consensus voting service.
///
/// Every configured LLM provider becomes a voter. Call <see cref="VoteAsync"/> with a
/// question and quorum requirement — the service calls all voters in parallel, tallies
/// their decisions, and returns a <see cref="VotingResult"/> with the consensus and
/// individual rationales.
///
/// Voting modes:
///   <list type="bullet">
///     <item>Binary/choice — voters pick from Options; decision is majority winner</item>
///     <item>Free-form — voters write answers; judge LLM synthesizes consensus</item>
///     <item>Scored — voters rate Dimensions 1-10; averages drive ScoredVotingResult</item>
///   </list>
///
/// Quorum options: Plurality, SimpleMajority (>50%), TwoThirds (≥67%), Unanimous.
///
/// Personality support: pass <see cref="VoterProfile"/> instances with custom
/// PersonalityMarkdown to simulate specific worldviews, including character personas
/// for in-story psychological decision-making.
///
/// Portable: this service has no dependency on StreetSamurai, LLMThinkTank, or any
/// other MindAttic project. Configure it with a <see cref="VotingConfiguration"/>
/// at startup.
/// </summary>
public class LLMVotingService
{
    private readonly LlmVotingProvider provider;
    private readonly VotingConfiguration config;
    private readonly ILogger<LLMVotingService> log;

    public LLMVotingService(
        LlmVotingProvider provider,
        VotingConfiguration config,
        ILogger<LLMVotingService> log)
    {
        this.provider = provider;
        this.config   = config;
        this.log      = log;
    }

    /// <summary>
    /// Vote on a question using all configured providers.
    /// The simplest call pattern:
    /// <code>
    ///   var result = await voting.VoteAsync("Should Kyle take the contract?", context, Quorum.TwoThirds);
    /// </code>
    /// </summary>
    public Task<VotingResult> VoteAsync(
        string question,
        string context,
        Quorum quorum,
        CancellationToken ct = default) =>
        VoteAsync(new VoteRequest { Question = question, Context = context }, quorum, ct: ct);

    /// <summary>
    /// Vote using a structured request with all configured providers.
    /// </summary>
    public Task<VotingResult> VoteAsync(
        VoteRequest request,
        Quorum quorum,
        CancellationToken ct = default)
    {
        var voterProfiles = config.ActiveProviderIds.Select(id => new VoterProfile
        {
            ProviderId          = id,
            Name                = id,
            PersonalityMarkdown = config.DefaultPersonalityMarkdown,
        }).ToList();
        return VoteWithProfilesAsync(request, quorum, voterProfiles, ct);
    }

    /// <summary>
    /// Vote using a specific subset of providers (by provider ID).
    /// </summary>
    public Task<VotingResult> VoteAsync(
        VoteRequest request,
        Quorum quorum,
        IEnumerable<string> providerIds,
        CancellationToken ct = default)
    {
        var voterProfiles = providerIds.Select(id => new VoterProfile
        {
            ProviderId          = id,
            Name                = id,
            PersonalityMarkdown = config.DefaultPersonalityMarkdown,
        }).ToList();
        return VoteWithProfilesAsync(request, quorum, voterProfiles, ct);
    }

    /// <summary>
    /// Vote using custom voter profiles (with personalities/personas).
    /// Use this to simulate how specific characters or viewpoints would decide.
    /// </summary>
    public Task<VotingResult> VoteWithProfilesAsync(
        VoteRequest request,
        Quorum quorum,
        IEnumerable<VoterProfile> voters,
        CancellationToken ct = default) =>
        RunVoteAsync(request, quorum, voters.ToList(), ct);

    /// <summary>
    /// Convenience: vote using character personas.
    /// Each persona wraps a VoterProfile with a character's psychology as the system prompt.
    /// </summary>
    public Task<VotingResult> VoteWithPersonasAsync(
        string question,
        string context,
        Quorum quorum,
        IEnumerable<VoterProfile> characterVoters,
        CancellationToken ct = default) =>
        VoteWithProfilesAsync(
            new VoteRequest { Question = question, Context = context },
            quorum, characterVoters, ct);

    /// <summary>
    /// Multi-dimensional scoring vote — each voter rates each dimension 1-10.
    /// Returns a <see cref="ScoredVotingResult"/> with aggregate scores, failures, and improvement directives.
    /// </summary>
    public async Task<ScoredVotingResult> ScoreAsync(
        ScoredVoteRequest request,
        CancellationToken ct = default)
    {
        if (request.Dimensions.Count == 0)
            throw new ArgumentException("ScoredVoteRequest must have at least one dimension.", nameof(request));

        var voterProfiles = config.ActiveProviderIds.Select(id => new VoterProfile
        {
            ProviderId          = id,
            Name                = id,
            PersonalityMarkdown = config.DefaultPersonalityMarkdown,
        }).ToList();
        return await RunScoredVoteAsync(request, voterProfiles, ct);
    }

    /// <summary>
    /// Multi-dimensional scoring with custom voter profiles.
    /// </summary>
    public Task<ScoredVotingResult> ScoreWithProfilesAsync(
        ScoredVoteRequest request,
        IEnumerable<VoterProfile> voters,
        CancellationToken ct = default) =>
        RunScoredVoteAsync(request, voters.ToList(), ct);

    /// <summary>List provider IDs that have a configured API key.</summary>
    public List<string> GetActiveProviderIds() => config.ActiveProviderIds;

    // ── Core vote execution ─────────────────────────────────────────────────────

    private async Task<VotingResult> RunVoteAsync(
        VoteRequest request,
        Quorum quorum,
        List<VoterProfile> voters,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        log.LogInformation("LLMVoting: starting vote — question='{Question}', voters={Count}, quorum={Quorum}",
            request.Question[..Math.Min(80, request.Question.Length)], voters.Count, quorum);

        if (voters.Count == 0)
            return EmptyResult(request.Question, quorum, sw.Elapsed, "No voters configured.");

        // Build system prompt per voter (persona + task)
        var isChoice = request.Options.Count > 0;
        var tasks = voters.Select(v => CallVoterAsync(v, request, isChoice, ct)).ToList();
        var individualVotes = await Task.WhenAll(tasks);

        var successful = individualVotes.Where(v => !v.IsError).ToList();
        if (successful.Count == 0)
            return EmptyResult(request.Question, quorum, sw.Elapsed, "All voters failed.");

        // Tally decisions
        VotingResult result;
        if (isChoice)
            result = TallyChoiceVote(request, quorum, individualVotes.ToList(), sw);
        else
            result = await TallyFreeFormVoteAsync(request, quorum, individualVotes.ToList(), sw, ct);

        log.LogInformation("LLMVoting: vote complete — consensus='{Consensus}', strength={Strength:P0}, quorumReached={Reached}, duration={Duration}",
            result.Consensus[..Math.Min(60, result.Consensus.Length)],
            result.ConsensusStrength, result.QuorumReached, sw.Elapsed);

        return result;
    }

    private async Task<ScoredVotingResult> RunScoredVoteAsync(
        ScoredVoteRequest request,
        List<VoterProfile> voters,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        log.LogInformation("LLMVoting: starting scored vote — dimensions={Count}, voters={Voters}",
            request.Dimensions.Count, voters.Count);

        if (voters.Count == 0)
        {
            return new ScoredVotingResult
            {
                Question       = request.Question,
                QuorumType     = Quorum.Plurality,
                QuorumReached  = false,
                Consensus      = "No voters configured.",
                Duration       = sw.Elapsed,
            };
        }

        var tasks = voters.Select(v => CallScoredVoterAsync(v, request, ct)).ToList();
        var individualVotes = await Task.WhenAll(tasks);
        var successful = individualVotes.Where(v => !v.IsError).ToList();

        // Aggregate scores
        var aggregateScores = new Dictionary<string, double>();
        foreach (var dim in request.Dimensions)
        {
            var scores = successful
                .Where(v => v.Scores.ContainsKey(dim))
                .Select(v => (double)v.Scores[dim])
                .ToList();
            aggregateScores[dim] = scores.Count > 0 ? scores.Average() : 0.0;
        }

        var failingDimensions = aggregateScores
            .Where(kv => kv.Value < request.FailureThreshold)
            .Select(kv => kv.Key).ToList();

        var weakest = aggregateScores.Count > 0
            ? aggregateScores.OrderBy(kv => kv.Value).First().Key
            : "";

        // Aggregate flags
        var allFlags    = successful.SelectMany(v => v.Flags).ToList();
        var flagCounts  = allFlags.GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        var minConsensus = Math.Max(2, (int)Math.Ceiling(successful.Count * 0.5));
        var consensusStrengths = flagCounts.Where(kv => kv.Value >= minConsensus).Select(kv => kv.Key).ToList();
        var consensusFailures  = allFlags.Distinct().Except(consensusStrengths).Take(10).ToList();

        // Synthesize improvement directives via judge if narrative synthesis is on
        var directives = new List<string>();
        if (request.SynthesizeNarrative && successful.Count > 0)
        {
            var voteTexts = string.Join("\n\n---\n\n", successful.Select(v =>
                $"[{v.VoterName}]:\n{v.Reasoning}"));
            try
            {
                var judgeDirectives = await SynthesizeImprovementDirectivesAsync(
                    request.Question, voteTexts, aggregateScores, failingDimensions, ct);
                directives.AddRange(judgeDirectives);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "LLMVoting: improvement directive synthesis failed");
            }
        }

        var overallScore = aggregateScores.Count > 0 ? aggregateScores.Values.Average() : 0.0;
        aggregateScores["OVERALL"] = overallScore;

        log.LogInformation("LLMVoting: scored vote complete — overall={Overall:F1}/10, failing={Failing}, duration={Duration}",
            overallScore, string.Join(",", failingDimensions), sw.Elapsed);

        return new ScoredVotingResult
        {
            Question              = request.Question,
            QuorumType            = Quorum.SimpleMajority,
            QuorumReached         = successful.Count > 0,
            Consensus             = $"Overall score: {overallScore:F1}/10",
            ConsensusStrength     = successful.Count > 0 ? 1.0 : 0.0,
            IndividualVotes       = individualVotes.ToList(),
            Duration              = sw.Elapsed,
            AggregateScores       = aggregateScores,
            FailingDimensions     = failingDimensions,
            ConsensusStrengths    = consensusStrengths,
            ConsensusFailures     = consensusFailures,
            ImprovementDirectives = directives,
            WeakestDimension      = weakest,
        };
    }

    // ── Individual voter calls ──────────────────────────────────────────────────

    private async Task<VoteResult> CallVoterAsync(
        VoterProfile voter, VoteRequest request, bool isChoice, CancellationToken ct)
    {
        try
        {
            var system = BuildVoterSystemPrompt(voter, request, isChoice);
            var user   = BuildVoterUserMessage(request, isChoice);
            var raw    = await provider.CallAsync(
                voter.ProviderId, system, user, request.MaxTokens, request.Temperature, voter, ct);
            return ParseVoteResponse(voter, raw, request, isChoice);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LLMVoting: voter {Name} ({Provider}) failed", voter.Name, voter.ProviderId);
            return new VoteResult
            {
                VoterId      = voter.VoterId,
                VoterName    = voter.Name,
                ProviderId   = voter.ProviderId,
                IsError      = true,
                ErrorMessage = ex.Message,
            };
        }
    }

    private async Task<VoteResult> CallScoredVoterAsync(
        VoterProfile voter, ScoredVoteRequest request, CancellationToken ct)
    {
        try
        {
            var dimList  = string.Join(", ", request.Dimensions);
            var system   = BuildScoredVoterSystemPrompt(voter, request);
            var user     = BuildScoredVoterUserMessage(request);
            var raw      = await provider.CallAsync(
                voter.ProviderId, system, user, request.MaxTokens, request.Temperature, voter, ct);
            return ParseScoredVoteResponse(voter, raw, request.Dimensions);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LLMVoting: scored voter {Name} ({Provider}) failed", voter.Name, voter.ProviderId);
            return new VoteResult
            {
                VoterId      = voter.VoterId,
                VoterName    = voter.Name,
                ProviderId   = voter.ProviderId,
                IsError      = true,
                ErrorMessage = ex.Message,
            };
        }
    }

    // ── Prompt builders ─────────────────────────────────────────────────────────

    private static string BuildVoterSystemPrompt(VoterProfile voter, VoteRequest request, bool isChoice)
    {
        var sb = new StringBuilder();

        // Personality overlay
        if (!string.IsNullOrWhiteSpace(voter.PersonalityMarkdown))
        {
            sb.AppendLine(voter.PersonalityMarkdown.Trim());
            sb.AppendLine();
        }

        // Task instructions
        const string choiceSchema = """
            {
              "decision": "<exact option text>",
              "reasoning": "<your reasoning — 2-4 sentences>",
              "confidence": <1-10>
            }
            """;
        const string freeformSchema = """
            {
              "decision": "<your direct answer>",
              "reasoning": "<your reasoning — 2-4 sentences>",
              "confidence": <1-10>
            }
            """;

        if (isChoice)
        {
            sb.AppendLine($"You are a deliberate decision-maker. You will be given a question and a set of options.");
            sb.AppendLine($"Choose exactly ONE option. Return ONLY a JSON object:");
            sb.AppendLine(choiceSchema);
        }
        else
        {
            sb.AppendLine("You are a deliberate decision-maker. You will be given a question and context.");
            sb.AppendLine("Answer the question directly and completely. Return ONLY a JSON object:");
            sb.AppendLine(freeformSchema);
        }

        return sb.ToString().Trim();
    }

    private static string BuildVoterUserMessage(VoteRequest request, bool isChoice)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine("CONTEXT:");
            sb.AppendLine(request.Context);
            sb.AppendLine();
        }
        sb.AppendLine($"QUESTION: {request.Question}");
        if (isChoice)
        {
            sb.AppendLine();
            sb.AppendLine("OPTIONS (choose exactly one):");
            foreach (var opt in request.Options)
                sb.AppendLine($"  - {opt}");
        }
        return sb.ToString().Trim();
    }

    private static string BuildScoredVoterSystemPrompt(VoterProfile voter, ScoredVoteRequest request)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(voter.PersonalityMarkdown))
        {
            sb.AppendLine(voter.PersonalityMarkdown.Trim());
            sb.AppendLine();
        }

        var scoreKeys = string.Join(", ", request.Dimensions.Select(d => $"\"{d}\": <1-10>"));

        if (!string.IsNullOrWhiteSpace(request.EvaluatorContext))
        {
            // Domain-specific framing provided by the caller
            sb.AppendLine(request.EvaluatorContext.Trim());
            sb.AppendLine();
        }
        else
        {
            // Generic evaluator framing
            var dimList = string.Join(", ", request.Dimensions);
            sb.AppendLine($"You are a structured evaluator. Score the provided material on these dimensions: {dimList}.");
            sb.AppendLine("Each score is 1-10 (1=terrible, 5=acceptable, 10=exceptional).");
            sb.AppendLine("Also identify specific strengths (flags_good) and failures (flags_bad).");
        }

        // JSON schema is always enforced
        sb.AppendLine("Return ONLY a JSON object:");
        sb.AppendLine("{");
        sb.AppendLine($"  \"scores\": {{ {scoreKeys} }},");
        sb.AppendLine("  \"reasoning\": \"<overall assessment — 2-4 sentences>\",");
        sb.AppendLine("  \"flags_good\": [\"<specific strength>\", ...],");
        sb.AppendLine("  \"flags_bad\": [\"<specific failure>\", ...],");
        sb.AppendLine("  \"best_moment\": \"<quote or description of the strongest moment>\",");
        sb.AppendLine("  \"worst_moment\": \"<quote or description of the weakest moment>\",");
        sb.AppendLine("  \"improvement_directive\": \"<single most important change for next time>\"");
        sb.AppendLine("}");
        return sb.ToString().Trim();
    }

    private static string BuildScoredVoterUserMessage(ScoredVoteRequest request)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Context))
        {
            sb.AppendLine("CONTEXT:");
            sb.AppendLine(request.Context);
            sb.AppendLine();
        }
        sb.AppendLine($"EVALUATE: {request.Question}");
        return sb.ToString().Trim();
    }

    // ── Response parsers ────────────────────────────────────────────────────────

    private static VoteResult ParseVoteResponse(
        VoterProfile voter, string raw, VoteRequest request, bool isChoice)
    {
        try
        {
            var json = ExtractJson(raw);
            var doc  = JsonDocument.Parse(json).RootElement;

            var decision   = doc.TryGetProperty("decision",   out var d) ? d.GetString() ?? "" : "";
            var reasoning  = doc.TryGetProperty("reasoning",  out var r) ? r.GetString() ?? "" : "";
            var confidence = doc.TryGetProperty("confidence", out var c) ? c.GetInt32() : 5;

            // Validate choice vote
            if (isChoice && request.Options.Count > 0)
            {
                var matched = request.Options.FirstOrDefault(o =>
                    o.Equals(decision, StringComparison.OrdinalIgnoreCase));
                decision = matched ?? decision;
            }

            return new VoteResult
            {
                VoterId    = voter.VoterId,
                VoterName  = voter.Name,
                ProviderId = voter.ProviderId,
                Decision   = decision,
                Reasoning  = reasoning,
                Confidence = Math.Clamp(confidence, 1, 10),
            };
        }
        catch
        {
            // Fallback: use the raw response as free-form decision
            return new VoteResult
            {
                VoterId    = voter.VoterId,
                VoterName  = voter.Name,
                ProviderId = voter.ProviderId,
                Decision   = raw.Trim()[..Math.Min(200, raw.Trim().Length)],
                Reasoning  = "Response parsing failed — using raw text.",
            };
        }
    }

    private static VoteResult ParseScoredVoteResponse(
        VoterProfile voter, string raw, List<string> dimensions)
    {
        try
        {
            var json = ExtractJson(raw);
            var doc  = JsonDocument.Parse(json).RootElement;

            var scores = new Dictionary<string, int>();
            if (doc.TryGetProperty("scores", out var scoresEl))
            {
                foreach (var dim in dimensions)
                {
                    if (scoresEl.TryGetProperty(dim, out var sv))
                        scores[dim] = Math.Clamp(sv.GetInt32(), 1, 10);
                }
            }

            var reasoning   = doc.TryGetProperty("reasoning",            out var r)  ? r.GetString()  ?? "" : "";
            var directive   = doc.TryGetProperty("improvement_directive", out var id) ? id.GetString() ?? "" : "";
            var bestMoment  = doc.TryGetProperty("best_moment",           out var bm) ? bm.GetString() ?? "" : "";
            var worstMoment = doc.TryGetProperty("worst_moment",          out var wm) ? wm.GetString() ?? "" : "";

            var flags = new List<string>();
            if (doc.TryGetProperty("flags_good", out var fg))
                flags.AddRange(fg.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
            if (doc.TryGetProperty("flags_bad", out var fb))
                flags.AddRange(fb.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
            if (!string.IsNullOrWhiteSpace(directive))
                flags.Add(directive);

            var overallScore = scores.Count > 0 ? (int)Math.Round(scores.Values.Average()) : 5;

            return new VoteResult
            {
                VoterId     = voter.VoterId,
                VoterName   = voter.Name,
                ProviderId  = voter.ProviderId,
                Decision    = $"Overall: {overallScore}/10",
                Reasoning   = reasoning,
                Scores      = scores,
                Flags       = flags,
                BestMoment  = bestMoment,
                WorstMoment = worstMoment,
                Confidence  = overallScore,
            };
        }
        catch
        {
            return new VoteResult
            {
                VoterId      = voter.VoterId,
                VoterName    = voter.Name,
                ProviderId   = voter.ProviderId,
                IsError      = true,
                ErrorMessage = "Score parsing failed.",
            };
        }
    }

    // ── Aggregation ─────────────────────────────────────────────────────────────

    private VotingResult TallyChoiceVote(
        VoteRequest request, Quorum quorum,
        List<VoteResult> votes, Stopwatch sw)
    {
        var successful = votes.Where(v => !v.IsError).ToList();
        if (successful.Count == 0)
            return EmptyResult(request.Question, quorum, sw.Elapsed, "All voters failed.");

        var tally = successful
            .GroupBy(v => v.Decision, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        var winner           = tally[0].Key;
        var winCount         = tally[0].Count();
        var fraction         = (double)winCount / successful.Count;
        var quorumReached    = fraction >= quorum.Threshold() || quorum == Quorum.Plurality && winCount > 0;
        var dissenters       = successful.Where(v => !v.Decision.Equals(winner, StringComparison.OrdinalIgnoreCase))
                                         .Select(v => v.Reasoning).ToList();

        return new VotingResult
        {
            Question          = request.Question,
            QuorumType        = quorum,
            QuorumReached     = quorumReached,
            Consensus         = quorumReached ? winner : "",
            ConsensusStrength = fraction,
            IndividualVotes   = votes,
            DissenterReasons  = dissenters,
            Duration          = sw.Elapsed,
        };
    }

    private async Task<VotingResult> TallyFreeFormVoteAsync(
        VoteRequest request, Quorum quorum,
        List<VoteResult> votes, Stopwatch sw,
        CancellationToken ct)
    {
        var successful = votes.Where(v => !v.IsError).ToList();
        if (successful.Count == 0)
            return EmptyResult(request.Question, quorum, sw.Elapsed, "All voters failed.");

        // Synthesize consensus from free-form responses
        string consensus    = "";
        string narrative    = "";
        double strength     = 1.0;

        if (successful.Count == 1)
        {
            consensus = successful[0].Decision;
            narrative = successful[0].Reasoning;
        }
        else if (request.SynthesizeNarrative)
        {
            try
            {
                (consensus, narrative) = await SynthesizeConsensusAsync(
                    request.Question, successful, quorum, ct);
                // For free-form, strength reflects how many voters the judge agrees with
                strength = successful.Count > 0 ? 1.0 : 0.0;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "LLMVoting: consensus synthesis failed — using first response");
                consensus = successful[0].Decision;
            }
        }
        else
        {
            consensus = successful[0].Decision;
        }

        return new VotingResult
        {
            Question          = request.Question,
            QuorumType        = quorum,
            QuorumReached     = true,
            Consensus         = consensus,
            ConsensusStrength = strength,
            IndividualVotes   = votes,
            NarrativeSummary  = narrative,
            Duration          = sw.Elapsed,
        };
    }

    // ── Judge calls ─────────────────────────────────────────────────────────────

    private async Task<(string consensus, string narrative)> SynthesizeConsensusAsync(
        string question, List<VoteResult> votes, Quorum quorum, CancellationToken ct)
    {
        var judgeProviderId = GetJudgeProviderId();
        var threshold       = (int)(quorum.Threshold() * 100);

        var voteText = string.Join("\n\n---\n\n",
            votes.Select(v => $"[{v.VoterName}]:\nDecision: {v.Decision}\nReasoning: {v.Reasoning}"));

        var system = $$"""
            You are a consensus judge. Multiple decision-makers have answered the same question.
            Your job is to synthesize a single consensus response.

            Rules:
            - An answer must appear in at least {{threshold}}% of responses to form consensus
            - If models substantially agree, state what they agree on
            - Capture the key shared reasoning across responses
            - Note any significant dissent briefly

            Return ONLY a JSON object:
            {
              "consensus": "<synthesized consensus answer>",
              "narrative": "<2-3 sentence summary of the vote outcome and key reasoning>"
            }
            """;

        var user = $"QUESTION: {question}\n\nINDIVIDUAL RESPONSES:\n\n{voteText}";

        try
        {
            var raw  = await provider.CallAsync(judgeProviderId, system, user, 1024, 0.2, null, ct);
            var json = ExtractJson(raw);
            var doc  = JsonDocument.Parse(json).RootElement;
            var consensus = doc.TryGetProperty("consensus", out var c) ? c.GetString() ?? "" : votes[0].Decision;
            var narrative = doc.TryGetProperty("narrative", out var n) ? n.GetString() ?? "" : "";
            return (consensus, narrative);
        }
        catch
        {
            return (votes[0].Decision, "");
        }
    }

    private async Task<List<string>> SynthesizeImprovementDirectivesAsync(
        string topic,
        string voteTexts,
        Dictionary<string, double> aggregateScores,
        List<string> failingDimensions,
        CancellationToken ct)
    {
        var judgeProviderId = GetJudgeProviderId();
        var failList = failingDimensions.Count > 0
            ? string.Join(", ", failingDimensions)
            : "none";

        var system = """
            You are a quality improvement analyst. Multiple evaluators have assessed the same work.
            Synthesize 3-5 specific, actionable improvement directives from their feedback.
            Each directive should start with a verb and be directly implementable.
            Return ONLY a JSON array: ["Directive 1", "Directive 2", ...]
            """;

        var user = $"""
            EVALUATION TOPIC: {topic}
            FAILING DIMENSIONS: {failList}

            EVALUATOR FEEDBACK:
            {voteTexts}

            Synthesize specific improvement directives.
            """;

        var raw  = await provider.CallAsync(judgeProviderId, system, user, 512, 0.3, null, ct);
        var json = ExtractJsonArray(raw);
        return JsonDocument.Parse(json).RootElement
            .EnumerateArray()
            .Select(e => e.GetString() ?? "")
            .Where(s => s.Length > 0)
            .ToList();
    }

    private string GetJudgeProviderId()
    {
        var preferred = config.JudgeProviderId;
        if (!string.IsNullOrWhiteSpace(preferred) && provider.GetApiKey(preferred) != null)
            return preferred;
        return config.ActiveProviderIds.FirstOrDefault() ?? "claude";
    }

    // ── Utilities ───────────────────────────────────────────────────────────────

    private static VotingResult EmptyResult(string question, Quorum quorum, TimeSpan duration, string reason) =>
        new()
        {
            Question      = question,
            QuorumType    = quorum,
            QuorumReached = false,
            Consensus     = reason,
            Duration      = duration,
        };

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }

    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[');
        var end   = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : "[]";
    }
}
