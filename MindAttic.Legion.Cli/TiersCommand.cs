namespace MindAttic.Legion.Cli;

using System.Diagnostics;
using System.Text.Json;
using MindAttic.Legion;

/// <summary>
/// <c>legion tiers</c> — connectivity probe across the
/// <see cref="LlmProviderCatalog.GetTieredModel">tier mapping</see>.
///
/// Probes every (provider, tier) pair the catalog exposes for the trusted
/// four (claude, openai, gemini, deepseek) by default — Low / Medium / High
/// — using a tiny "reply OK" prompt. Use it to answer "is the panel
/// actually ready to vote on High?" without spinning up a real <c>ask</c>
/// or <c>vote</c>. Distinct from <c>legion health</c>, which only probes
/// the per-provider DefaultModel.
///
/// Defaults are chosen for human use (table output, 45s per-call timeout,
/// trusted four). All flags are optional:
///   <list type="bullet">
///     <item><c>--providers a,b,c</c> — narrow the provider set
///       (intersected with the trusted four).</item>
///     <item><c>--tiers low,medium,high,higher,highest</c> — narrow the
///       tier set. Default: low,medium,high.</item>
///     <item><c>--all-tiers</c> — shorthand for all five tiers.</item>
///     <item><c>--json</c> — emit a single JSON blob on stdout instead of a
///       table; quiet on stderr. Useful for scripting.</item>
///     <item><c>--timeout N</c> — per-probe timeout in seconds (default 45).</item>
///     <item><c>--max-tokens N</c> — token budget per probe (default 400 —
///       large enough for thinking models like gemini-2.5-pro to actually
///       emit text after reasoning).</item>
///   </list>
/// Exit code: 0 if every probe succeeded, 1 otherwise.
/// </summary>
public static class TiersCommand
{
    /// <summary>The same trust list as <see cref="AskCommand.TrustedProviderIds"/>.</summary>
    internal static readonly string[] TrustedProviderIds =
        { "claude", "openai", "gemini", "deepseek" };

    /// <summary>Default tier set probed when <c>--tiers</c> isn't passed.</summary>
    internal static readonly ModelTier[] DefaultTiers =
        { ModelTier.Low, ModelTier.Medium, ModelTier.High };

    /// <summary>
    /// Result of one (provider, tier) probe. Pure data — no formatting —
    /// so it's reusable by both the table and JSON emitters and easy to test.
    /// </summary>
    internal sealed record ProbeResult(
        string ProviderId,
        ModelTier Tier,
        string Model,
        bool Ok,
        long ElapsedMs,
        string? Reply,
        string? Error);

    /// <summary>
    /// Parse args, run the probe matrix, emit results, return process exit code:
    /// 0 if every probe succeeded, 1 if any failed (or usage error).
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        var providers   = new List<string>();
        var tiers       = new List<ModelTier>();
        var emitJson    = false;
        var timeoutSec  = 45.0;
        var maxTokens   = 400;
        var allTiers    = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--providers":
                    if (i + 1 < args.Length)
                        providers = args[++i].Split(',')
                            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                case "--tiers":
                    if (i + 1 < args.Length)
                    {
                        foreach (var raw in args[++i].Split(','))
                        {
                            if (Enum.TryParse<ModelTier>(raw.Trim(), ignoreCase: true, out var t))
                                tiers.Add(t);
                        }
                    }
                    break;
                case "--all-tiers":
                    allTiers = true;
                    break;
                case "--json":
                    emitJson = true;
                    break;
                case "--timeout":
                    if (i + 1 < args.Length && double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ts) && ts > 0)
                        timeoutSec = ts;
                    break;
                case "--max-tokens":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt) && mt > 0)
                        maxTokens = mt;
                    break;
            }
        }

        var resolvedProviders = ResolveProviders(providers);
        if (resolvedProviders.Count == 0)
        {
            Console.Error.WriteLine(
                "error: no trusted providers selected. Trusted set: "
                + string.Join(", ", TrustedProviderIds));
            return 1;
        }

        var resolvedTiers = allTiers
            ? new[] { ModelTier.Low, ModelTier.Medium, ModelTier.High, ModelTier.Higher, ModelTier.Highest }
            : (tiers.Count > 0 ? tiers.Distinct().ToArray() : DefaultTiers);

        if (!emitJson)
        {
            Console.WriteLine($"Probing {resolvedProviders.Count} provider(s) × {resolvedTiers.Length} tier(s) = {resolvedProviders.Count * resolvedTiers.Length} calls...");
            Console.WriteLine();
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec + 5) };
        var client     = new LegionClient(http, LegionClientOptions.NoResilience);

        var results = await ProbeMatrixAsync(
            client, resolvedProviders, resolvedTiers, maxTokens, TimeSpan.FromSeconds(timeoutSec));

        if (emitJson) EmitJson(results);
        else          EmitTable(results);

        return results.All(r => r.Ok) ? 0 : 1;
    }

    /// <summary>
    /// Intersects the requested provider list with <see cref="TrustedProviderIds"/>
    /// (case-insensitive). Empty/null input ⇒ full trusted set. Untrusted ids
    /// are silently dropped — the trust list is the source of truth for which
    /// providers ever get probed by <c>tiers</c>, mirroring <c>ask</c>'s
    /// security model.
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
    /// Run every (provider, tier) probe. Each probe is independent, but we
    /// run them sequentially so the table output orders predictably and
    /// per-provider rate limits don't trip with parallel bursts. Returns one
    /// <see cref="ProbeResult"/> per pair (never throws).
    /// </summary>
    internal static async Task<List<ProbeResult>> ProbeMatrixAsync(
        LegionClient client,
        IReadOnlyList<string> providers,
        IReadOnlyList<ModelTier> tiers,
        int maxTokens,
        TimeSpan perCallTimeout)
    {
        var results = new List<ProbeResult>(providers.Count * tiers.Count);
        foreach (var providerId in providers)
        {
            foreach (var tier in tiers)
            {
                var model = LlmProviderCatalog.GetTieredModel(providerId, tier) ?? "(none)";
                var sw    = Stopwatch.StartNew();
                using var cts = new CancellationTokenSource(perCallTimeout);
                try
                {
                    var reply = await client.CallAsync(
                        providerId:    providerId,
                        systemPrompt:  "Reply with exactly: OK",
                        userMessage:   "ping",
                        maxTokens:     maxTokens,
                        temperature:   0.0,
                        modelOverride: model,
                        ct:            cts.Token);
                    sw.Stop();
                    results.Add(new ProbeResult(
                        providerId, tier, model, Ok: true, sw.ElapsedMilliseconds,
                        Reply: (reply ?? "").Trim(), Error: null));
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    results.Add(new ProbeResult(
                        providerId, tier, model, Ok: false, sw.ElapsedMilliseconds,
                        Reply: null, Error: ex.Message));
                }
            }
        }
        return results;
    }

    private static void EmitTable(IReadOnlyList<ProbeResult> results)
    {
        Console.WriteLine($"{"PROVIDER",-10} {"TIER",-8} {"MODEL",-32} {"STATUS",-7} {"TIME",-8} {"DETAIL"}");
        Console.WriteLine(new string('─', 100));
        foreach (var r in results)
        {
            var status = r.Ok ? "OK" : "FAIL";
            var detail = r.Ok ? (r.Reply ?? "") : (r.Error ?? "");
            Console.WriteLine(
                $"{r.ProviderId,-10} {r.Tier,-8} {r.Model,-32} {status,-7} {(r.ElapsedMs + "ms"),-8} {Truncate(detail, 50)}");
        }
        Console.WriteLine();
        var ok   = results.Count(r => r.Ok);
        var fail = results.Count - ok;
        Console.WriteLine($"summary: {ok}/{results.Count} ok" + (fail > 0 ? $" ({fail} failed)" : ""));
    }

    private static void EmitJson(IReadOnlyList<ProbeResult> results)
    {
        var json = JsonSerializer.Serialize(new
        {
            total      = results.Count,
            ok         = results.Count(r => r.Ok),
            failed     = results.Count(r => !r.Ok),
            probes = results.Select(r => new
            {
                provider    = r.ProviderId,
                tier        = r.Tier.ToString(),
                model       = r.Model,
                ok          = r.Ok,
                elapsed_ms  = r.ElapsedMs,
                reply       = r.Reply,
                error       = r.Error,
            }),
        }, new JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        Console.WriteLine(json);
    }

    /// <summary>
    /// Truncate a long single-line string with a trailing ellipsis so the
    /// table doesn't wrap. Mirrors <see cref="AskCommand.Truncate"/> but with
    /// no character-count suffix — the table cell is too narrow for it.
    /// </summary>
    internal static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (max <= 0) return "";
        var oneLine = s.Replace('\n', ' ').Replace('\r', ' ');
        return oneLine.Length <= max ? oneLine : oneLine.Substring(0, max - 1) + "…";
    }

    internal static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    private static void PrintUsage()
    {
        Console.WriteLine("legion tiers — probe trusted providers × tier mapping for connectivity");
        Console.WriteLine();
        Console.WriteLine("usage: legion tiers [--providers a,b,c] [--tiers low,medium,high] [--all-tiers]");
        Console.WriteLine("                    [--json] [--timeout SECONDS] [--max-tokens N]");
        Console.WriteLine();
        Console.WriteLine("Defaults: probes claude/openai/gemini/deepseek × low,medium,high (12 calls).");
        Console.WriteLine("Exit 0 if every probe succeeded, 1 otherwise.");
    }
}
