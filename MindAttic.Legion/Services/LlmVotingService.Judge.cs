using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MindAttic.Legion;

public partial class LlmVotingService
{
    /// <summary>
    /// Grounded judging with every active provider as a judge. Each judge scores every rubric
    /// criterion 1-10 against the numbered evidence and cites the passage ids that support the
    /// score; replies are strictly validated (one retry on malformed output) and aggregated through
    /// the same scored-vote pipeline as <see cref="ScoreAsync"/>. Wrap the call in
    /// <see cref="LegionUsage.Track"/> to meter tokens.
    /// </summary>
    public Task<JudgeResult> JudgeAsync(JudgeRequest request, CancellationToken ct = default)
    {
        var voters = config.ActiveProviderIds.Select(id => new VoterProfile
        {
            ProviderId          = id,
            Name                = id,
            PersonalityMarkdown = config.DefaultPersonalityMarkdown,
        }).ToList();
        return JudgeCoreAsync(request, voters, ct);
    }

    /// <summary>Grounded judging with explicit voter profiles (see <see cref="JudgeAsync"/>).</summary>
    public Task<JudgeResult> JudgeWithProfilesAsync(
        JudgeRequest request, IEnumerable<VoterProfile> voters, CancellationToken ct = default) =>
        JudgeCoreAsync(request, voters.ToList(), ct);

    private async Task<JudgeResult> JudgeCoreAsync(JudgeRequest request, List<VoterProfile> voters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Criteria.Count == 0)
            throw new ArgumentException("JudgeRequest must have at least one criterion.", nameof(request));
        if (request.Criteria.Any(c => string.IsNullOrWhiteSpace(c.Name)))
            throw new ArgumentException("Every criterion needs a name.", nameof(request));
        if (request.Criteria.Any(c => !(c.Weight > 0) || double.IsInfinity(c.Weight)))
            throw new ArgumentException("Every criterion weight must be a positive number.", nameof(request));
        if (request.Criteria.Select(c => c.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Criteria.Count)
            throw new ArgumentException("Criterion names must be unique.", nameof(request));
        if (request.Evidence.Any(e => string.IsNullOrWhiteSpace(e.Id))
            || request.Evidence.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != request.Evidence.Count)
            throw new ArgumentException("Evidence passage ids must be non-empty and unique.", nameof(request));

        var sw = Stopwatch.StartNew();
        var verdicts = new ConcurrentDictionary<string, JudgeVoterVerdict>();

        var scored = new ScoredVoteRequest
        {
            Question            = string.IsNullOrWhiteSpace(request.Question) ? "Grounded judging" : request.Question,
            Dimensions          = request.Criteria.Select(c => c.Name.Trim()).ToList(),
            MaxTokens           = request.MaxTokens,
            Temperature         = request.Temperature,
            SynthesizeNarrative = false,
        };

        var vote = await RunScoredVoteAsync(scored, voters, ct,
            (voter, token) => CallJudgeVoterAsync(voter, request, verdicts, token));

        var failed = vote.IndividualVotes.Where(v => v.IsError)
            .Select(v => new JudgeVoterFailure(v.VoterId, v.VoterName, v.ProviderId, v.ErrorMessage ?? "unknown error"))
            .ToList();
        var good = vote.IndividualVotes.Where(v => !v.IsError && verdicts.ContainsKey(v.VoterId))
            .Select(v => verdicts[v.VoterId]).ToList();

        var criteria = new List<CriterionJudgment>();
        foreach (var c in request.Criteria)
        {
            var name = c.Name.Trim();
            var per = good.Select(g => (Verdict: g, Cv: g.Criteria[name])).ToList();
            if (per.Count == 0)
            {
                criteria.Add(new CriterionJudgment { Name = name, Weight = c.Weight });
                continue;
            }
            var norm = per.Select(p => p.Cv.Score).ToList();
            var mean = norm.Average();
            var dispersion = Math.Sqrt(norm.Select(x => (x - mean) * (x - mean)).Average());
            var closest = per.OrderBy(p => Math.Abs(p.Cv.Score - mean)).First();
            var cited = per.SelectMany(p => p.Cv.CitedPassageIds.Distinct())
                .GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key).ToList();
            criteria.Add(new CriterionJudgment
            {
                Name            = name,
                Weight          = c.Weight,
                Score           = mean,
                RawMean         = per.Average(p => (double)p.Cv.RawScore),
                Dispersion      = dispersion,
                Rationale       = closest.Cv.Rationale,
                CitedPassageIds = cited,
            });
        }

        var totalWeight = criteria.Sum(c => c.Weight);
        var overall = good.Count > 0 && totalWeight > 0 ? criteria.Sum(c => c.Score * c.Weight) / totalWeight : 0.0;
        var agreement = good.Count > 0
            ? Math.Clamp(1.0 - 2.0 * criteria.Average(c => c.Dispersion), 0.0, 1.0)
            : 0.0;

        log.LogInformation("LLMVoting: judging complete — overall={Overall:F2}, agreement={Agreement:F2}, ok={Ok}, failed={Failed}",
            overall, agreement, good.Count, failed.Count);

        return new JudgeResult
        {
            Succeeded    = good.Count > 0,
            Overall      = overall,
            Agreement    = agreement,
            Criteria     = criteria,
            Verdicts     = good,
            FailedVoters = failed,
            Duration     = sw.Elapsed,
        };
    }

    private async Task<VoteResult> CallJudgeVoterAsync(
        VoterProfile voter, JudgeRequest request,
        ConcurrentDictionary<string, JudgeVoterVerdict> verdicts, CancellationToken ct)
    {
        try
        {
            var system = BuildJudgeSystemPrompt(voter, request);
            var user   = BuildJudgeUserMessage(request);
            string? lastError = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var prompt = attempt == 1 ? user
                    : user + "\n\nYour previous reply was rejected: " + lastError
                      + "\nReply again with ONLY the corrected JSON object.";
                var raw = await provider.CallAsync(
                    voter.ProviderId, system, prompt, request.MaxTokens, request.Temperature, voter, ct);

                if (TryParseJudgeReply(raw, request, out var parsed, out lastError))
                {
                    var wsum = request.Criteria.Sum(c => c.Weight);
                    var overall = request.Criteria.Sum(c => parsed![c.Name.Trim()].Score * c.Weight) / wsum;
                    verdicts[voter.VoterId] = new JudgeVoterVerdict
                    {
                        VoterId    = voter.VoterId,
                        VoterName  = voter.Name,
                        ProviderId = voter.ProviderId,
                        Criteria   = parsed!,
                        Overall    = overall,
                        Attempts   = attempt,
                    };
                    return new VoteResult
                    {
                        VoterId    = voter.VoterId,
                        VoterName  = voter.Name,
                        ProviderId = voter.ProviderId,
                        Scores     = parsed!.ToDictionary(kv => kv.Key, kv => kv.Value.RawScore),
                        Reasoning  = string.Join("\n", parsed.Select(kv => $"{kv.Key}: {kv.Value.Rationale}")),
                    };
                }
            }
            throw new InvalidOperationException("Judge reply failed validation after retry: " + lastError);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "LLMVoting: judge {Name} ({Provider}) failed", voter.Name, voter.ProviderId);
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

    private static string BuildJudgeSystemPrompt(VoterProfile voter, JudgeRequest request)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(voter.PersonalityMarkdown))
        {
            sb.AppendLine(voter.PersonalityMarkdown.Trim());
            sb.AppendLine();
        }
        sb.AppendLine(string.IsNullOrWhiteSpace(request.EvaluatorContext)
            ? "You are a strict, evidence-grounded evaluator."
            : request.EvaluatorContext.Trim());
        sb.AppendLine("Judge the ANSWER against each rubric criterion, using only the numbered EVIDENCE passages as ground truth.");
        sb.AppendLine("Score each criterion with an integer from 1 (fails the criterion) to 10 (fully satisfies it).");
        sb.AppendLine("For each criterion give a short rationale and list the ids of the evidence passages that support your score");
        sb.AppendLine("(an empty list when none apply). Cite only ids that appear in the EVIDENCE section.");
        sb.AppendLine("Return ONLY a JSON object, no prose, in exactly this shape:");
        sb.AppendLine("""
            {
              "criteria": [
                { "name": "<criterion name exactly as given>", "score": <integer 1-10>, "rationale": "<1-3 sentences>", "cited": ["<passage id>", "..."] }
              ]
            }
            """);
        sb.AppendLine("Include exactly one entry for every criterion listed, and no others.");
        return sb.ToString().Trim();
    }

    private static string BuildJudgeUserMessage(JudgeRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CRITERIA:");
        foreach (var c in request.Criteria)
            sb.AppendLine($"- {c.Name.Trim()} (weight {c.Weight.ToString("0.###", CultureInfo.InvariantCulture)}): {c.Description}");
        sb.AppendLine();
        sb.AppendLine("EVIDENCE:");
        if (request.Evidence.Count == 0) sb.AppendLine("(none supplied)");
        foreach (var e in request.Evidence)
            sb.AppendLine($"[{e.Id}] {e.Text}");
        if (!string.IsNullOrWhiteSpace(request.PriorContext))
        {
            sb.AppendLine();
            sb.AppendLine("PRIOR CONTEXT:");
            sb.AppendLine(request.PriorContext);
        }
        if (!string.IsNullOrWhiteSpace(request.Question))
        {
            sb.AppendLine();
            sb.AppendLine($"QUESTION: {request.Question}");
        }
        sb.AppendLine();
        sb.AppendLine("ANSWER:");
        sb.AppendLine(request.Answer);
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Strict validation of a judge reply: parseable JSON with a <c>criteria</c> array containing
    /// exactly one entry per requested criterion (no duplicates, no unknown names), an integer
    /// score 1..10, a non-empty rationale, and only cited ids that exist in the evidence.
    /// </summary>
    internal static bool TryParseJudgeReply(
        string raw, JudgeRequest request,
        out Dictionary<string, CriterionVerdict>? result, out string? error)
    {
        result = null;
        try
        {
            using var doc = JsonDocument.Parse(LegionJson.ExtractObject(raw ?? ""));
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("criteria", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                error = "missing \"criteria\" array";
                return false;
            }

            var expected = request.Criteria.Select(c => c.Name.Trim()).ToList();
            var evidenceIds = request.Evidence.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var found = new Dictionary<string, CriterionVerdict>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                {
                    error = "a criteria entry has no string \"name\"";
                    return false;
                }
                var name = expected.FirstOrDefault(e => e.Equals(nameEl.GetString()?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (name is null) { error = $"unknown criterion \"{nameEl.GetString()}\""; return false; }
                if (found.ContainsKey(name)) { error = $"criterion \"{name}\" appears twice"; return false; }

                if (!item.TryGetProperty("score", out var scoreEl)
                    || scoreEl.ValueKind != JsonValueKind.Number || !scoreEl.TryGetInt32(out var raw10)
                    || raw10 < 1 || raw10 > 10)
                {
                    error = $"criterion \"{name}\" needs an integer score from 1 to 10";
                    return false;
                }
                if (!item.TryGetProperty("rationale", out var ratEl) || ratEl.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(ratEl.GetString()))
                {
                    error = $"criterion \"{name}\" needs a non-empty \"rationale\"";
                    return false;
                }

                var cited = new List<string>();
                if (item.TryGetProperty("cited", out var citedEl) && citedEl.ValueKind != JsonValueKind.Null)
                {
                    if (citedEl.ValueKind != JsonValueKind.Array)
                    {
                        error = $"criterion \"{name}\": \"cited\" must be an array";
                        return false;
                    }
                    foreach (var c in citedEl.EnumerateArray())
                    {
                        var id = c.ValueKind switch
                        {
                            JsonValueKind.String => c.GetString(),
                            JsonValueKind.Number => c.GetRawText(),
                            _ => null,
                        };
                        if (id is null || !evidenceIds.Contains(id))
                        {
                            error = $"criterion \"{name}\" cites unknown passage id {c.GetRawText()}";
                            return false;
                        }
                        if (!cited.Contains(id)) cited.Add(id);
                    }
                }

                found[name] = new CriterionVerdict((raw10 - 1) / 9.0, raw10, ratEl.GetString()!.Trim(), cited);
            }

            var missing = expected.Where(e => !found.ContainsKey(e)).ToList();
            if (missing.Count > 0)
            {
                error = "missing criteria: " + string.Join(", ", missing);
                return false;
            }

            result = expected.ToDictionary(e => e, e => found[e]);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = "not valid JSON (" + ex.Message + ")";
            return false;
        }
    }
}
