namespace MindAttic.Legion;

/// <summary>
/// Provider-reported token usage for one successful LLM call. Token counts come straight from the
/// provider's own response (<c>usage</c> / <c>usageMetadata</c>), never from a character estimate.
/// <see cref="InputTokens"/> is always the NON-cached input; cached input is reported separately
/// so the buckets never overlap and can be priced independently.
/// </summary>
/// <param name="Provider">Provider id, e.g. "claude", "openai".</param>
/// <param name="Model">Model id the call was sent to.</param>
/// <param name="InputTokens">Non-cached prompt tokens.</param>
/// <param name="OutputTokens">Completion tokens (for Gemini this includes thinking tokens).</param>
/// <param name="CacheReadTokens">Prompt tokens served from the provider's cache.</param>
/// <param name="CacheWriteTokens">Prompt tokens written to the provider's cache (Claude only).</param>
/// <param name="EstimatedCostUsd">Cost from the host-supplied <see cref="LlmPriceTable"/>; null when no price is known.</param>
/// <param name="Tag">Host-supplied label from <see cref="LegionUsage.Track"/> (e.g. a tenant id).</param>
/// <param name="Operation">Which call path produced it: "chat" or "document".</param>
public sealed record LlmUsage(
    string Provider,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    decimal? EstimatedCostUsd,
    string? Tag,
    string Operation)
{
    /// <summary>Sum of every token bucket.</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
}

/// <summary>Receives a <see cref="LlmUsage"/> for every metered provider call inside a <see cref="LegionUsage.Track"/> scope.</summary>
public interface IUsageSink
{
    /// <summary>Called once per successful provider call. Exceptions thrown here are swallowed.</summary>
    void Record(LlmUsage usage);
}

/// <summary><see cref="IUsageSink"/> that forwards to a delegate.</summary>
public sealed class DelegateUsageSink : IUsageSink
{
    private readonly Action<LlmUsage> record;

    /// <summary>Wraps <paramref name="record"/>.</summary>
    public DelegateUsageSink(Action<LlmUsage> record) =>
        this.record = record ?? throw new ArgumentNullException(nameof(record));

    /// <inheritdoc />
    public void Record(LlmUsage usage) => record(usage);
}

/// <summary>Per-million-token USD prices for one model. Cache prices default to the input price when null.</summary>
public sealed record ModelPrice(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal? CacheReadPerMillion = null,
    decimal? CacheWritePerMillion = null);

/// <summary>
/// Host-supplied price list. Legion ships NO prices (they drift and are the host's business
/// decision); with no entry for a model the usage's <see cref="LlmUsage.EstimatedCostUsd"/> is null.
/// Lookup is exact first, then the longest registered model-id prefix for the same provider
/// (so a "claude-sonnet-5" entry also prices a dated snapshot).
/// </summary>
public sealed class LlmPriceTable
{
    private readonly Dictionary<(string Provider, string Model), ModelPrice> prices = new();

    /// <summary>Registers or replaces a price. Returns this table for chaining.</summary>
    public LlmPriceTable Set(string provider, string model, ModelPrice price)
    {
        prices[(provider.ToLowerInvariant(), model.ToLowerInvariant())] = price;
        return this;
    }

    /// <summary>Finds the price for a model, or false when unknown.</summary>
    public bool TryGet(string provider, string model, out ModelPrice price)
    {
        var p = (provider ?? "").ToLowerInvariant();
        var m = (model ?? "").ToLowerInvariant();
        if (prices.TryGetValue((p, m), out price!)) return true;

        ModelPrice? best = null;
        var bestLen = -1;
        foreach (var kv in prices)
        {
            if (kv.Key.Provider != p) continue;
            if (m.StartsWith(kv.Key.Model, StringComparison.Ordinal) && kv.Key.Model.Length > bestLen)
            {
                best = kv.Value;
                bestLen = kv.Key.Model.Length;
            }
        }
        price = best!;
        return best is not null;
    }

    /// <summary>Estimated USD cost of the given token counts, or null when the model has no price.</summary>
    public decimal? Estimate(string provider, string model, long input, long output, long cacheRead, long cacheWrite)
    {
        if (!TryGet(provider, model, out var pr)) return null;
        const decimal million = 1_000_000m;
        return input * pr.InputPerMillion / million
             + output * pr.OutputPerMillion / million
             + cacheRead * (pr.CacheReadPerMillion ?? pr.InputPerMillion) / million
             + cacheWrite * (pr.CacheWritePerMillion ?? pr.InputPerMillion) / million;
    }
}
