namespace MindAttic.Legion.Cli;

using System.Diagnostics;
using System.Text.Json;
using MindAttic.Legion;

/// <summary>
/// <c>legion generate</c> — bulk creative generation across the trusted
/// providers. Distinct from <c>poll</c> (which counts votes) and <c>ask</c>
/// (which seeks one decision): generate produces N <em>distinct creative
/// items</em> on a single prompt — e.g. "100 hero-vibe names",
/// "20 plausible product taglines", "50 alternative function names".
///
/// <para><b>Strategy.</b></para>
/// One HTTP call per provider produces a batch of items, not one item per
/// call. With four trusted providers and N=100, generate fans out 4 calls
/// (one per provider) each asking for 25 items, yielding ~100 candidates
/// in roughly the time of one round-trip rather than 100. Each provider
/// is asked for an exact target count split round-robin from N. Items are
/// extracted from each reply via simple line-splitting (numbered or bullet
/// prefixes are stripped), deduped case-insensitively across all providers
/// by default, and emitted newline-separated to stdout for pipe-friendly
/// scripting (Unix convention).
///
/// <para><b>Why these defaults.</b></para>
/// <list type="bullet">
///   <item>Tier=Medium — creative balance; flagship reasoning is overkill
///         for "list 25 names" but Low can produce flat output.</item>
///   <item>Round-robin across providers — variety beats stylistic uniformity
///         for bulk creative; if you want one voice, pass <c>--providers</c>.</item>
///   <item>Dedup on — bulk creative collisions are common (multiple providers
///         reach for similar tropes); the user almost always wants unique.</item>
///   <item>Newline stdout — composes with <c>head</c>, <c>shuf</c>, <c>grep</c>,
///         file redirection. JSON via <c>--json</c> for structured callers.</item>
/// </list>
/// </summary>
public static class GenerateCommand
{
    /// <summary>Trust list — same as ask/poll/tiers; never widens here.</summary>
    internal static readonly string[] TrustedProviderIds =
        { "claude", "openai", "gemini", "deepseek" };

    /// <summary>
    /// Default tier when <c>--tier</c> isn't passed. Medium (sonnet-class /
    /// flash / mini / chat) gives creative balance — better diversity than
    /// Low's tiny models, far cheaper than High's flagship reasoners.
    /// </summary>
    internal const ModelTier DefaultTier = ModelTier.Medium;

    /// <summary>Default number of items requested when <c>--count</c> is omitted.</summary>
    internal const int DefaultCount = 10;

    /// <summary>Default per-call max tokens — enough for ~50 short items.</summary>
    internal const int DefaultMaxTokens = 1500;

    /// <summary>One provider's batch reply — pure data, used by both emitters.</summary>
    internal sealed record ProviderBatch(
        string ProviderId,
        string Model,
        int Requested,
        bool Ok,
        long ElapsedMs,
        IReadOnlyList<string> Items,
        string? Error);

    /// <summary>
    /// Run the generation, emit results, return process exit code:
    /// 0 if at least one item was produced, 1 otherwise.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var prompt           = args[0];
        var count            = DefaultCount;
        var tier             = DefaultTier;
        var providerFilter   = new List<string>();
        var maxTokens        = DefaultMaxTokens;
        var timeoutSec       = 60.0;
        var emitJson         = false;
        var dedup            = true;
        var temperature      = 0.9; // higher than default — we want creative variance, not consensus.

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
                case "--temperature":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out var temp))
                        temperature = temp;
                    break;
                case "--no-dedup":
                    dedup = false;
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

        var split = SplitCount(count, providers.Count);
        var assignments = providers
            .Select((p, idx) => new BatchAssignment(
                p, LlmProviderCatalog.GetTieredModel(p, tier) ?? "(none)", split[idx]))
            .Where(a => a.Requested > 0)
            .ToList();

        if (!emitJson)
        {
            var perProvider = string.Join(", ", assignments.Select(a => $"{a.ProviderId}×{a.Requested}"));
            Console.Error.WriteLine($"Generating {count} item(s) at {tier} tier across {assignments.Count} provider(s) [{perProvider}]...");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSec + 5) };
        var client     = new LegionClient(http, LegionClientOptions.NoResilience);

        var batches = await FanOutAsync(client, assignments, prompt, maxTokens, temperature,
            TimeSpan.FromSeconds(timeoutSec));

        var allItems = batches.Where(b => b.Ok).SelectMany(b => b.Items).ToList();
        var finalItems = dedup ? Deduplicate(allItems) : allItems;

        if (emitJson) EmitJson(prompt, count, tier, dedup, batches, finalItems);
        else          EmitNewlineList(batches, finalItems);

        return finalItems.Count > 0 ? 0 : 1;
    }

    /// <summary>Same intersect-with-trust-list contract as ask/poll/tiers.</summary>
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
    /// Split <paramref name="total"/> across <paramref name="bucketCount"/>
    /// buckets as evenly as possible, with the remainder distributed to the
    /// front buckets. Example: 10 across 4 → [3, 3, 2, 2]; 100 across 4 →
    /// [25, 25, 25, 25]; 5 across 4 → [2, 1, 1, 1]. Used to assign per-provider
    /// item counts so the totals are exact, not approximate.
    /// </summary>
    internal static int[] SplitCount(int total, int bucketCount)
    {
        if (bucketCount <= 0) return Array.Empty<int>();
        var result = new int[bucketCount];
        var baseShare = total / bucketCount;
        var remainder = total % bucketCount;
        for (var i = 0; i < bucketCount; i++)
            result[i] = baseShare + (i < remainder ? 1 : 0);
        return result;
    }

    /// <summary>One provider's slot in the fan-out — provider, model, item count.</summary>
    internal sealed record BatchAssignment(string ProviderId, string Model, int Requested);

    /// <summary>
    /// Fan out one HTTP call per provider, each asking for that provider's
    /// share of the total. All calls run in parallel — there's no concurrency
    /// cap because the trusted set is at most four providers, well below any
    /// reasonable rate-limit budget. Returns one <see cref="ProviderBatch"/>
    /// per assignment in input order; never throws.
    /// </summary>
    internal static async Task<List<ProviderBatch>> FanOutAsync(
        LegionClient client,
        IReadOnlyList<BatchAssignment> assignments,
        string prompt,
        int maxTokens,
        double temperature,
        TimeSpan perCallTimeout)
    {
        var tasks = assignments.Select(a => RunOneAsync(a)).ToArray();
        var done  = await Task.WhenAll(tasks);
        return done.ToList();

        async Task<ProviderBatch> RunOneAsync(BatchAssignment a)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var systemPrompt =
                    $"Generate exactly {a.Requested} distinct items for the user's prompt. "
                  + "Reply with ONE item per line, plain text, no numbering, no bullets, no preamble, "
                  + "no quotes, no explanation. Just the items, one per line.";

                using var cts = new CancellationTokenSource(perCallTimeout);
                var raw = await client.CallAsync(
                    providerId:    a.ProviderId,
                    systemPrompt:  systemPrompt,
                    userMessage:   prompt,
                    maxTokens:     maxTokens,
                    temperature:   temperature,
                    modelOverride: a.Model,
                    ct:            cts.Token);
                sw.Stop();
                var items = ExtractItems(raw ?? "");
                return new ProviderBatch(a.ProviderId, a.Model, a.Requested,
                    Ok: true, sw.ElapsedMilliseconds, items, Error: null);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ProviderBatch(a.ProviderId, a.Model, a.Requested,
                    Ok: false, sw.ElapsedMilliseconds,
                    Items: Array.Empty<string>(), Error: ex.Message);
            }
        }
    }

    /// <summary>
    /// Parse a model reply into individual items. Splits on newlines, then
    /// strips common list-marker prefixes (numbered "1.", "1)", bullet
    /// "- ", "* ", quote pairs) so a model that ignored the "no numbering"
    /// instruction still yields clean items. Empty lines are dropped, but
    /// the order is preserved so callers can correlate items with their
    /// position. No semantic dedup here — that happens later, across all
    /// providers' items.
    /// </summary>
    internal static List<string> ExtractItems(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.None)
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0)
                       .ToList();
        var items = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var item = StripListMarker(line);
            item = StripWrappingQuotes(item);
            if (!string.IsNullOrWhiteSpace(item))
                items.Add(item.Trim());
        }
        return items;
    }

    /// <summary>
    /// Strip a leading "1.", "1)", "- ", "* ", "• " marker if present.
    /// Returns the original string when no marker matches so the caller
    /// doesn't lose content from a one-off prefix shape.
    /// </summary>
    internal static string StripListMarker(string line)
    {
        // Numbered: "1." / "12." / "1)" / "12)"
        var i = 0;
        while (i < line.Length && char.IsDigit(line[i])) i++;
        if (i > 0 && i < line.Length && (line[i] == '.' || line[i] == ')'))
        {
            var rest = line[(i + 1)..].TrimStart();
            if (rest.Length > 0) return rest;
        }
        // Bulleted: "- " / "* " / "• "
        if (line.Length >= 2 && (line[0] == '-' || line[0] == '*' || line[0] == '•')
            && line[1] == ' ')
            return line[2..].TrimStart();
        return line;
    }

    /// <summary>
    /// Drop a wrapping pair of straight or curly quotes if both sides match.
    /// Models love wrapping list items in quotes despite the instruction to
    /// not — strip them so dedup works.
    /// </summary>
    internal static string StripWrappingQuotes(string s)
    {
        if (s.Length < 2) return s;
        var first = s[0];
        var last  = s[^1];
        var pairs = new[] { ('"', '"'), ('\'', '\''), ('“', '”'), ('‘', '’') };
        foreach (var (a, b) in pairs)
            if (first == a && last == b) return s[1..^1];
        return s;
    }

    /// <summary>
    /// Case-insensitive dedup that preserves first-seen order. Uses
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> so "Aragorn" and
    /// "aragorn" merge to one entry; the first variant seen wins.
    /// </summary>
    internal static List<string> Deduplicate(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            if (seen.Add(item)) unique.Add(item);
        }
        return unique;
    }

    private static void EmitNewlineList(IReadOnlyList<ProviderBatch> batches, IReadOnlyList<string> items)
    {
        foreach (var item in items)
            Console.WriteLine(item);

        // Diagnostics on stderr so stdout stays clean for piping.
        var ok       = batches.Count(b => b.Ok);
        var fail     = batches.Count - ok;
        var rawTotal = batches.Sum(b => b.Items.Count);
        var dropped  = rawTotal - items.Count;
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"summary: {items.Count} unique item(s) from {ok}/{batches.Count} provider(s)"
            + (dropped > 0 ? $" ({dropped} dup/empty trimmed)" : "")
            + (fail > 0 ? $", {fail} errored" : ""));
        if (fail > 0)
        {
            foreach (var b in batches.Where(b => !b.Ok).Take(3))
                Console.Error.WriteLine($"  {b.ProviderId}: {Truncate(b.Error ?? "", 80)}");
        }
    }

    private static void EmitJson(
        string prompt, int count, ModelTier tier, bool dedup,
        IReadOnlyList<ProviderBatch> batches, IReadOnlyList<string> items)
    {
        var json = JsonSerializer.Serialize(new
        {
            prompt,
            requested  = count,
            tier       = tier.ToString(),
            dedup,
            returned   = items.Count,
            items,
            batches = batches.Select(b => new
            {
                provider   = b.ProviderId,
                model      = b.Model,
                requested  = b.Requested,
                returned   = b.Items.Count,
                ok         = b.Ok,
                elapsed_ms = b.ElapsedMs,
                error      = b.Error,
            }),
        }, new JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        Console.WriteLine(json);
    }

    /// <summary>Truncate a single-line snippet for diagnostic output.</summary>
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
        Console.WriteLine("legion generate <prompt> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Bulk creative generation: produces N distinct items by fanning out one");
        Console.WriteLine("  batched call per trusted provider, then merging+deduping the lines.");
        Console.WriteLine("  Stdout = newline-separated items (pipe into head/shuf/grep/etc.).");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --count N          Total distinct items (default 10).");
        Console.WriteLine("  --tier <t>         low | medium | high | higher | highest (default medium).");
        Console.WriteLine("  --providers a,b,c  Narrow within trusted set (claude, openai, gemini, deepseek).");
        Console.WriteLine("  --max-tokens N     Per-batch cap (default 1500 ≈ 50 short items).");
        Console.WriteLine("  --timeout S        Per-call timeout in seconds (default 60).");
        Console.WriteLine("  --temperature T    Sampling temperature (default 0.9 — favors creative variance).");
        Console.WriteLine("  --no-dedup         Emit duplicates from across providers; default is dedup.");
        Console.WriteLine("  --json             Emit JSON record instead of newline list.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  legion generate \"100 hero-vibe character names\" --count 100");
        Console.WriteLine("  legion generate \"product taglines for a tea brand\" --count 30 --tier high");
        Console.WriteLine("  legion generate \"function names for a queue.dequeue helper\" --providers claude --count 20");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 if at least one item was produced; 1 otherwise.");
    }
}
