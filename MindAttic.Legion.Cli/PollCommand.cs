namespace MindAttic.Legion.Cli;

using System.Diagnostics;
using System.Text.Json;
using MindAttic.Legion;

/// <summary>
/// <c>legion poll</c> — bulk vote across N independent voters round-robined
/// over the trusted providers, all on a single tier. Distinct from
/// <c>legion vote</c> (one-voter-per-provider consensus, requires quorum)
/// and <c>legion ask</c> (architect-framed single decision): poll's job is
/// to sample a *distribution* across many cheap calls.
///
/// Use case: "100 voters on Low tier — how does the panel break on this
/// question?" Defaults reflect that — count=10, tier=Low, fan-out across
/// every trusted provider with an API key. Output is a histogram with the
/// plurality winner; no quorum concept (the winner is whichever option
/// got the most votes, even by 1).
///
/// Round-robin distribution: voter <c>i</c> goes to <c>providers[i % N]</c>
/// where <c>providers</c> is the active trusted set. With four providers
/// and count=100 you get 25 per provider; with count=10 you get
/// (3, 3, 2, 2). Failed voters do NOT shift the index — we'd rather have
/// an uneven distribution that's reproducible than a "rebalance on
/// failure" rule that drifts under retry.
///
/// Direct <see cref="LegionClient"/> fan-out, NOT
/// <see cref="Providers.LlmVotingProvider"/> — voting-service profiles
/// assume one vote per voter, which doesn't model "30 anonymous calls
/// against the same model" cleanly. Concurrency is bounded by
/// <c>--concurrency</c> (default 8) so 100 calls don't burst all at once.
/// </summary>
public static class PollCommand
{
    /// <summary>Trust list — same as ask/tiers; never widens here.</summary>
    internal static readonly string[] TrustedProviderIds =
        { "claude", "openai", "gemini", "deepseek" };

    /// <summary>
    /// Default tier when <c>--tier</c> isn't passed. Low is right for
    /// 100-voter polls — scales cheaply and the distribution is what matters,
    /// not any single voter's reasoning depth.
    /// </summary>
    internal const ModelTier DefaultTier = ModelTier.Low;

    /// <summary>Default voter count when <c>--count</c> isn't passed.</summary>
    internal const int DefaultCount = 10;

    /// <summary>Default in-flight concurrency cap.</summary>
    internal const int DefaultConcurrency = 8;

    /// <summary>One voter's outcome — pure data, used by both emitters.</summary>
    internal sealed record VoterOutcome(
        int Index,
        string ProviderId,
        string Model,
        bool Ok,
        long ElapsedMs,
        string? Answer,
        string? Error);

    /// <summary>
    /// Run the poll, emit a histogram, return the exit code:
    /// 0 if at least one voter succeeded and a winner was chosen,
    /// 1 if every voter errored or args were malformed.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var question         = args[0];
        var count            = DefaultCount;
        var tier             = DefaultTier;
        var options          = new List<string>();
        var providerFilter   = new List<string>();
        var maxTokens        = 200;
        var timeoutSec       = 30.0;
        var concurrency      = DefaultConcurrency;
        var emitJson         = false;
        var context          = "";

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--count":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var n) && n > 0)
                        count = n;
                    break;
                case "--tier":
                    if (i + 1 < args.Length && Enum.TryParse<ModelTier>(args[++i], ignoreCase: true, out var t))
                        tier = t;
                    break;
                case "--options":
                    if (i + 1 < args.Length)
                        options = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                case "--providers":
                    if (i + 1 < args.Length)
                        providerFilter = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                case "--max-tokens":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt) && mt > 0)
                        maxTokens = mt;
                    break;
                case "--timeout":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out var ts) && ts > 0)
                        timeoutSec = ts;
                    break;
                case "--concurrency":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var c) && c > 0)
                        concurrency = c;
                    break;
                case "--context":
                    if (i + 1 < args.Length) context = args[++i];
                    break;
                case "--json":
                    emitJson = true;
                    break;
            }
        }

        var providers = ResolveProviders(providerFilter);
        if (providers.Count == 0)
        {
            Console.Error.WriteLine(
                "error: no trusted providers selected. Trusted: "
                + string.Join(", ", TrustedProviderIds));
            return 1;
        }

        var assignments = AssignRoundRobin(count, providers, tier);
        if (!emitJson)
        {
            var perProvider = string.Join(", ",
                providers.Select(p => $"{p}×{assignments.Count(a => a.ProviderId == p)}"));
            Console.WriteLine($"Polling {count} voter(s) at {tier} tier across {providers.Count} provider(s) [{perProvider}]...");
            Console.WriteLine();
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec + 5) };
        var client     = new LegionClient(http, LegionClientOptions.NoResilience);

        var systemPrompt = options.Count > 0
            ? "You are casting one vote among many. Pick exactly one of these options and reply with ONLY that option's exact text, nothing else: "
              + string.Join(", ", options.Select(o => $"\"{o}\""))
            : "You are casting one vote among many. Answer the user's question directly and concisely. No JSON, no preamble — just your answer.";

        var userMessage = string.IsNullOrWhiteSpace(context)
            ? $"QUESTION: {question}"
            : $"CONTEXT:\n{context}\n\nQUESTION: {question}";

        var outcomes = await FanOutAsync(
            client, assignments, systemPrompt, userMessage, options, maxTokens,
            TimeSpan.FromSeconds(timeoutSec), concurrency);

        if (emitJson) EmitJson(question, count, tier, providers, outcomes, options);
        else          EmitTable(outcomes, options);

        var ok = outcomes.Count(o => o.Ok);
        return ok > 0 ? 0 : 1;
    }

    /// <summary>
    /// Intersect the requested provider list with <see cref="TrustedProviderIds"/>
    /// (case-insensitive, deduped). Empty/null ⇒ full trusted set.
    /// </summary>
    internal static List<string> ResolveProviders(IEnumerable<string>? requested)
    {
        var trusted = new HashSet<string>(TrustedProviderIds, StringComparer.OrdinalIgnoreCase);
        var list    = requested?.Where(s => !string.IsNullOrWhiteSpace(s))
                                .Select(s => s.Trim().ToLowerInvariant())
                                .ToList();
        if (list is null || list.Count == 0)
            return TrustedProviderIds.ToList();
        return list.Where(id => trusted.Contains(id)).Distinct().ToList();
    }

    /// <summary>
    /// One assigned voter — index, provider, resolved tier model.
    /// </summary>
    internal sealed record VoterAssignment(int Index, string ProviderId, string Model);

    /// <summary>
    /// Round-robin <paramref name="count"/> voters over <paramref name="providers"/>:
    /// voter <c>i</c> gets <c>providers[i % providers.Count]</c>. The tier is
    /// resolved per-assignment via <see cref="LlmProviderCatalog.GetTieredModel"/>;
    /// providers without a mapping at the requested tier walk down to the next
    /// available tier. Returns one assignment per voter even if some
    /// providers can't resolve a model — failures surface in fan-out, not here.
    /// </summary>
    internal static List<VoterAssignment> AssignRoundRobin(
        int count, IReadOnlyList<string> providers, ModelTier tier)
    {
        var assignments = new List<VoterAssignment>(count);
        for (var i = 0; i < count; i++)
        {
            var providerId = providers[i % providers.Count];
            var model      = LlmProviderCatalog.GetTieredModel(providerId, tier) ?? "(none)";
            assignments.Add(new VoterAssignment(i, providerId, model));
        }
        return assignments;
    }

    /// <summary>
    /// Fan out every assignment in parallel under a concurrency cap, awaiting
    /// each call inside a <see cref="SemaphoreSlim"/> so we don't burst 100
    /// requests at once. Returns one <see cref="VoterOutcome"/> per assignment
    /// in the same order as the input — never throws.
    /// </summary>
    internal static async Task<List<VoterOutcome>> FanOutAsync(
        LegionClient client,
        IReadOnlyList<VoterAssignment> assignments,
        string systemPrompt,
        string userMessage,
        IReadOnlyList<string> options,
        int maxTokens,
        TimeSpan perCallTimeout,
        int concurrency)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = assignments.Select(a => RunOneAsync(a)).ToArray();
        var done  = await Task.WhenAll(tasks);
        return done.OrderBy(o => o.Index).ToList();

        async Task<VoterOutcome> RunOneAsync(VoterAssignment a)
        {
            await gate.WaitAsync();
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(perCallTimeout);
                var raw = await client.CallAsync(
                    providerId:    a.ProviderId,
                    systemPrompt:  systemPrompt,
                    userMessage:   userMessage,
                    maxTokens:     maxTokens,
                    temperature:   0.7,
                    modelOverride: a.Model,
                    ct:            cts.Token);
                sw.Stop();
                var answer = (raw ?? "").Trim();
                if (options.Count > 0)
                {
                    var snapped = SnapToOption(answer, options);
                    if (snapped is null)
                    {
                        return new VoterOutcome(a.Index, a.ProviderId, a.Model,
                            Ok: false, sw.ElapsedMilliseconds,
                            Answer: null,
                            Error: $"off-ballot reply: {Truncate(answer, 60)}");
                    }
                    answer = snapped;
                }
                return new VoterOutcome(a.Index, a.ProviderId, a.Model,
                    Ok: true, sw.ElapsedMilliseconds, answer, Error: null);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new VoterOutcome(a.Index, a.ProviderId, a.Model,
                    Ok: false, sw.ElapsedMilliseconds, Answer: null,
                    Error: ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>
    /// Snap a free-form answer to one of <paramref name="options"/> for
    /// choice mode. Mirrors the contract of <see cref="AskCommand.SnapToOption"/>:
    /// exact match wins, then the longest contained option, otherwise null
    /// (treated as off-ballot by the caller).
    /// </summary>
    internal static string? SnapToOption(string answer, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(answer) || options is null || options.Count == 0)
            return null;

        var trimmed = answer.Trim();
        var exact   = options.FirstOrDefault(o => trimmed.Equals(o, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Whole-token contains match (longest wins). Using a token boundary rather
        // than raw substring stops a short option ("No", "A", "cat") from matching
        // inside an unrelated word ("Notify", "communicate") and skewing the poll.
        return options
            .Where(o => !string.IsNullOrWhiteSpace(o) && ContainsWholeToken(trimmed, o))
            .OrderByDescending(o => o.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// True when <paramref name="option"/> occurs in <paramref name="haystack"/>
    /// as a whole token — not embedded inside a larger alphanumeric word.
    /// Mirrors <see cref="AskCommand"/>'s matching contract. Case-insensitive.
    /// </summary>
    private static bool ContainsWholeToken(string haystack, string option)
    {
        var idx = 0;
        while ((idx = haystack.IndexOf(option, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var after    = idx + option.Length;
            var afterOk  = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);
            if (beforeOk && afterOk) return true;
            idx++;
        }
        return false;
    }

    /// <summary>
    /// Aggregate successful outcomes into a count-sorted distribution.
    /// Case-insensitive grouping for free-form answers (so "yes" and "Yes"
    /// merge); choice-mode answers are already canonicalized by SnapToOption.
    /// </summary>
    internal static List<(string Answer, int Count)> Aggregate(IEnumerable<VoterOutcome> outcomes)
    {
        return outcomes
            .Where(o => o.Ok && !string.IsNullOrWhiteSpace(o.Answer))
            .GroupBy(o => o.Answer!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Answer: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();
    }

    private static void EmitTable(IReadOnlyList<VoterOutcome> outcomes, IReadOnlyList<string> options)
    {
        var dist = Aggregate(outcomes);
        var ok   = outcomes.Count(o => o.Ok);
        var fail = outcomes.Count - ok;

        Console.WriteLine($"Successful: {ok}/{outcomes.Count} ({fail} errored)");
        Console.WriteLine();

        if (dist.Count == 0)
        {
            Console.WriteLine("(no successful voters — no distribution to report)");
            return;
        }

        var winner    = dist[0].Answer;
        var maxCount  = dist[0].Count;
        Console.WriteLine("DISTRIBUTION:");
        foreach (var (answer, c) in dist)
        {
            var pct  = ok == 0 ? 0.0 : (double)c / ok;
            var bars = new string('█', Math.Min(40, (int)Math.Round(40.0 * c / maxCount)));
            Console.WriteLine($"  {c,4}  {pct,6:P1}  {Truncate(answer, 40),-42} {bars}");
        }
        Console.WriteLine();
        Console.WriteLine($"most-voted: {winner}");

        if (fail > 0)
        {
            Console.WriteLine();
            Console.WriteLine("ERRORS (sample):");
            foreach (var e in outcomes.Where(o => !o.Ok).Take(3))
                Console.WriteLine($"  voter#{e.Index} {e.ProviderId}: {Truncate(e.Error ?? "", 80)}");
            if (fail > 3) Console.WriteLine($"  ...and {fail - 3} more");
        }
    }

    private static void EmitJson(
        string question, int count, ModelTier tier,
        IReadOnlyList<string> providers,
        IReadOnlyList<VoterOutcome> outcomes,
        IReadOnlyList<string> options)
    {
        var dist = Aggregate(outcomes);
        var ok   = outcomes.Count(o => o.Ok);

        var json = JsonSerializer.Serialize(new
        {
            question,
            count,
            tier   = tier.ToString(),
            options,
            successful = ok,
            errors     = outcomes.Count - ok,
            winner     = dist.Count > 0 ? dist[0].Answer : null,
            winner_pct = dist.Count > 0 && ok > 0 ? (double)dist[0].Count / ok : 0.0,
            distribution = dist.Select(d => new
            {
                answer = d.Answer,
                count  = d.Count,
                pct    = ok == 0 ? 0.0 : (double)d.Count / ok,
            }),
            providers_used = providers.Select(p => new
            {
                id      = p,
                model   = LlmProviderCatalog.GetTieredModel(p, tier),
                voters  = outcomes.Count(o => o.ProviderId == p),
                errored = outcomes.Count(o => o.ProviderId == p && !o.Ok),
            }),
            voters = outcomes.Select(o => new
            {
                index    = o.Index,
                provider = o.ProviderId,
                model    = o.Model,
                ok       = o.Ok,
                elapsed_ms = o.ElapsedMs,
                answer   = o.Answer,
                error    = o.Error,
            }),
        }, new JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        Console.WriteLine(json);
    }

    /// <summary>Truncate a single-line snippet for table rendering.</summary>
    internal static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var oneLine = s.Replace('\n', ' ').Replace('\r', ' ');
        return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1) + "…";
    }

    internal static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    private static void PrintUsage()
    {
        Console.WriteLine("legion poll <question> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Bulk-vote across N independent voters round-robined over the trusted");
        Console.WriteLine("  providers, all on a single tier. Outputs a count-sorted distribution");
        Console.WriteLine("  and the plurality winner. No quorum concept.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --count N          Total voters (default 10).");
        Console.WriteLine("  --tier <t>         low | medium | high | higher | highest (default low).");
        Console.WriteLine("  --options A,B,C    Force choice mode; off-ballot replies count as errors.");
        Console.WriteLine("  --providers a,b,c  Narrow within trusted set (claude, openai, gemini, deepseek).");
        Console.WriteLine("  --context <text>   Extra context appended to every voter's prompt.");
        Console.WriteLine("  --max-tokens N     Per-voter cap (default 200).");
        Console.WriteLine("  --timeout S        Per-voter timeout in seconds (default 30).");
        Console.WriteLine("  --concurrency N    In-flight call cap (default 8).");
        Console.WriteLine("  --json             Emit full poll record as JSON instead of a table.");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 if at least one voter replied; 1 otherwise.");
    }
}
