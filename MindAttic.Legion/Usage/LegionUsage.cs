using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Ambient usage metering. Wrap any Legion calls (including whole votes and judgings) in
/// <see cref="Track"/>; every successful provider call made inside the scope, on this async flow
/// and any tasks it spawns, reports its provider-reported token usage to the sink.
/// Scopes nest: the innermost wins, and disposing restores the outer one.
/// </summary>
public static class LegionUsage
{
    private sealed record Scope(IUsageSink Sink, LlmPriceTable? Prices, string? Tag);

    private static readonly AsyncLocal<Scope?> current = new();

    /// <summary>Starts a metering scope. Dispose the result to end it.</summary>
    public static IDisposable Track(IUsageSink sink, LlmPriceTable? prices = null, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        var previous = current.Value;
        current.Value = new Scope(sink, prices, tag);
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Scope? previous;
        private bool done;
        public Restore(Scope? previous) => this.previous = previous;
        public void Dispose()
        {
            if (done) return;
            done = true;
            current.Value = previous;
        }
    }

    /// <summary>True when a <see cref="Track"/> scope is active on this async flow.</summary>
    public static bool IsTracking => current.Value is not null;

    internal static void Report(string provider, string model, string operation,
        long input, long output, long cacheRead, long cacheWrite)
    {
        var scope = current.Value;
        if (scope is null) return;
        if (input < 0) input = 0;
        try
        {
            var cost = scope.Prices?.Estimate(provider, model, input, output, cacheRead, cacheWrite);
            scope.Sink.Record(new LlmUsage(provider, model, input, output, cacheRead, cacheWrite, cost, scope.Tag, operation));
        }
        catch
        {
            // A metering failure must never fail the LLM call.
        }
    }

    private static long Num(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var e)
        && e.ValueKind == JsonValueKind.Number
        && e.TryGetInt64(out var v) ? v : 0;

    private static bool Has(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out _);

    // Anthropic: input_tokens excludes cache reads/writes.
    internal static void ReportClaude(JsonElement root, string model, string operation)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
        Report("claude", model, operation,
            Num(u, "input_tokens"), Num(u, "output_tokens"),
            Num(u, "cache_read_input_tokens"), Num(u, "cache_creation_input_tokens"));
    }

    // Gemini: promptTokenCount includes cachedContentTokenCount; thinking tokens bill as output.
    internal static void ReportGemini(JsonElement root, string model, string operation)
    {
        if (!root.TryGetProperty("usageMetadata", out var u) || u.ValueKind != JsonValueKind.Object) return;
        var cached = Num(u, "cachedContentTokenCount");
        Report("gemini", model, operation,
            Num(u, "promptTokenCount") - cached,
            Num(u, "candidatesTokenCount") + Num(u, "thoughtsTokenCount"),
            cached, 0);
    }

    // Cohere v2: usage.tokens.{input_tokens,output_tokens}, falling back to usage.billed_units.
    internal static void ReportCohere(JsonElement root, string model, string operation)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
        JsonElement src = default;
        if (u.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Object) src = t;
        else if (u.TryGetProperty("billed_units", out var b) && b.ValueKind == JsonValueKind.Object) src = b;
        if (src.ValueKind != JsonValueKind.Object) return;
        Report("cohere", model, operation, Num(src, "input_tokens"), Num(src, "output_tokens"), 0, 0);
    }

    // OpenAI-compatible: prompt_tokens includes cached (prompt_tokens_details.cached_tokens);
    // DeepSeek reports prompt_cache_hit_tokens.
    internal static void ReportOpenAiCompatible(JsonElement root, string provider, string model, string operation)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object) return;
        long cached = 0;
        if (u.TryGetProperty("prompt_tokens_details", out var d)) cached = Num(d, "cached_tokens");
        if (cached == 0 && Has(u, "prompt_cache_hit_tokens")) cached = Num(u, "prompt_cache_hit_tokens");
        Report(provider, model, operation,
            Num(u, "prompt_tokens") - cached, Num(u, "completion_tokens"), cached, 0);
    }
}
