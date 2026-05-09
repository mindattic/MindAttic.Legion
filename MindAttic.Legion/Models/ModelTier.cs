namespace MindAttic.Legion;

/// <summary>
/// Cost / capability tier for a model within a provider.
///
/// Each provider's <see cref="LlmProviderInfo.AvailableModels"/> list spans a
/// range from cheap-and-fast (Haiku, Mini, Flash) to flagship (Opus, GPT-4.1,
/// Gemini Pro). The catalog assigns each tier a specific model id per provider
/// so callers can request "the cheap one" or "the strong one" without naming
/// model versions that drift.
///
/// <para><b>Tier semantics.</b></para>
/// <list type="bullet">
///   <item><see cref="Low"/> — Haiku / Mini / Flash class. Fastest, cheapest.
///         Use for high-volume polling (100-persona scoring, repeated drafts).</item>
///   <item><see cref="Medium"/> — Sonnet / 4.1-mini / Flash-pro class. Balanced
///         everyday quality.</item>
///   <item><see cref="High"/> — Opus / GPT-4.1 / Gemini Pro class. Strong.
///         Use for prose generation, beat expansion, single-shot quality calls.</item>
///   <item><see cref="Higher"/> — long-context or reasoning-tuned variants
///         (Opus 1m, o1, etc). Reserved for jobs needing the extra horsepower.</item>
///   <item><see cref="Highest"/> — flagship-of-flagship for irreversible /
///         canonical decisions. Optional — not every provider exposes one.</item>
/// </list>
///
/// When a provider doesn't have a model registered for the requested tier,
/// <see cref="LlmProviderCatalog.GetTieredModel"/> returns the closest
/// available tier (climbing down). Asking for Highest on a 3-tier provider
/// returns High; asking for Low always returns *something* if the provider
/// has any models.
/// </summary>
public enum ModelTier
{
    Low,
    Medium,
    High,
    Higher,
    Highest,
}
