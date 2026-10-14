namespace MindAttic.Legion;

/// <summary>
/// Metadata for one LLM provider Legion can talk to.
/// <see cref="DashboardUrl"/> is where users monitor usage; <see cref="KeysUrl"/>
/// is where they mint new API keys; <see cref="AvailableModels"/> lists the models
/// Legion knows about for that provider, with <see cref="DefaultModel"/> picked
/// when no override is supplied. <see cref="ModelsApiEndpoint"/>, when non-null,
/// is a runtime endpoint that returns the provider's live model catalog.
/// </summary>
public sealed record LlmProviderInfo(
    string Id,
    string DisplayName,
    string Vendor,
    string DefaultModel,
    string DashboardUrl,
    string KeysUrl,
    IReadOnlyList<string> AvailableModels,
    string? ModelsApiEndpoint = null);

/// <summary>
/// Static catalog of every LLM provider Legion supports — display name, vendor,
/// dashboard/keys URLs, default model, and the list of currently-known models
/// per provider. Use this to render settings UIs, point users at the right page
/// when their key is missing, or fetch live model lists at runtime.
/// </summary>
public static class LlmProviderCatalog
{
    private static readonly LlmProviderInfo[] providers =
    {
        new("claude", "Claude", "Anthropic",
            DefaultModel: "claude-sonnet-5-5",
            DashboardUrl: "https://console.anthropic.com/",
            KeysUrl: "https://console.anthropic.com/settings/keys",
            AvailableModels: new[]
            {
                "claude-fable-5-1",
                "claude-fable-5",
                "claude-opus-5-5",
                "claude-opus-5",
                "claude-sonnet-5-5",
                "claude-sonnet-5",
                "claude-opus-4-8",
                "claude-opus-4-7",
                "claude-opus-4-6",
                "claude-sonnet-4-6",
                "claude-haiku-5-5",
                "claude-haiku-4-5-20251001",
            },
            ModelsApiEndpoint: "https://api.anthropic.com/v1/models"),

        new("openai", "ChatGPT", "OpenAI",
            DefaultModel: "gpt-5.4-mini",
            DashboardUrl: "https://platform.openai.com/usage",
            KeysUrl: "https://platform.openai.com/api-keys",
            AvailableModels: new[]
            {
                "gpt-6.1-sol",
                "gpt-6-sol",
                "gpt-6-astra",
                "gpt-6-luna",
                "gpt-5.6-sol",
                "gpt-5.6-terra",
                "gpt-5.6-luna",
                "gpt-5.5",
                "gpt-5.4",
                "gpt-5.4-mini",
                "gpt-5.4-nano",
                "gpt-4.1",
                "gpt-4.1-mini",
                "gpt-4.1-nano",
                "gpt-4o",
                "gpt-4o-mini",
                "o4-mini",
                "o3",
                "o3-mini",
                "o1",
            },
            ModelsApiEndpoint: "https://api.openai.com/v1/models"),

        new("gemini", "Gemini", "Google",
            DefaultModel: "gemini-2.5-flash",
            DashboardUrl: "https://aistudio.google.com/",
            KeysUrl: "https://aistudio.google.com/app/apikey",
            AvailableModels: new[]
            {
                "gemini-3.8-flash",
                "gemini-3.5-flash",
                "gemini-3.5-flash-lite",
                "gemini-3.1-flash-lite",
                "gemini-2.5-pro",
                "gemini-2.5-flash",
                "gemini-2.5-flash-lite",
            },
            ModelsApiEndpoint: "https://generativelanguage.googleapis.com/v1beta/models"),

        new("deepseek", "DeepSeek", "DeepSeek AI",
            DefaultModel: "deepseek-flash",
            DashboardUrl: "https://platform.deepseek.com/usage",
            KeysUrl: "https://platform.deepseek.com/api_keys",
            AvailableModels: new[]
            {
                "deepseek-v4-pro",
                "deepseek-flash",
            },
            ModelsApiEndpoint: "https://api.deepseek.com/models"),

        new("mistral", "Mistral", "Mistral AI",
            DefaultModel: "mistral-medium-latest",
            DashboardUrl: "https://console.mistral.ai/",
            KeysUrl: "https://console.mistral.ai/api-keys/",
            AvailableModels: new[]
            {
                "magistral-medium-latest",
                "magistral-small-latest",
                "mistral-medium-latest",
                "mistral-small-latest",
                "ministral-14b-latest",
                "ministral-8b-latest",
                "ministral-3b-latest",
                "codestral-latest",
            },
            ModelsApiEndpoint: "https://api.mistral.ai/v1/models"),

        new("xai", "Grok", "xAI",
            DefaultModel: "grok-4.3",
            DashboardUrl: "https://console.x.ai/",
            KeysUrl: "https://console.x.ai/team/default/api-keys",
            AvailableModels: new[]
            {
                "grok-4.7",
                "grok-4.6",
                "grok-4.5",
                "grok-4.3",
                "grok-4.20-multi-agent-0309",
                "grok-4.20-0309-reasoning",
                "grok-4.20-0309-non-reasoning",
            },
            ModelsApiEndpoint: "https://api.x.ai/v1/models"),

        new("groq", "Groq", "Groq",
            DefaultModel: "llama-3.3-70b-versatile",
            DashboardUrl: "https://console.groq.com/",
            KeysUrl: "https://console.groq.com/keys",
            AvailableModels: new[]
            {
                "llama-3.3-70b-versatile",
                "llama-3.1-8b-instant",
                "llama-4-scout-17b-16e-instruct",
            },
            ModelsApiEndpoint: "https://api.groq.com/openai/v1/models"),

        new("together", "Together", "Together AI",
            DefaultModel: "meta-llama/Llama-3.3-70B-Instruct-Turbo",
            DashboardUrl: "https://api.together.xyz/",
            KeysUrl: "https://api.together.xyz/settings/api-keys",
            AvailableModels: new[]
            {
                "deepseek-ai/DeepSeek-V4-Pro-0813",
                "deepseek-ai/DeepSeek-V4.1-Flash",
                "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                "meta-llama/Llama-4-Scout-17B-16E-Instruct",
                "Qwen/Qwen2.5-72B-Instruct-Turbo",
                "Qwen/Qwen3.7-Max",
                "mistralai/Mixtral-8x7B-Instruct-v0.1",
            },
            ModelsApiEndpoint: "https://api.together.xyz/v1/models"),

        new("openrouter", "OpenRouter", "OpenRouter",
            DefaultModel: "meta-llama/llama-3.1-8b-instruct:free",
            DashboardUrl: "https://openrouter.ai/activity",
            KeysUrl: "https://openrouter.ai/keys",
            AvailableModels: new[]
            {
                "anthropic/claude-3.5-sonnet",
                "openai/gpt-4o",
                "openai/gpt-4o-mini",
                "google/gemini-pro-1.5",
                "meta-llama/llama-3.1-405b-instruct",
                "meta-llama/llama-3.1-70b-instruct",
                "meta-llama/llama-3.1-8b-instruct",
                "meta-llama/llama-3.1-8b-instruct:free",
                "mistralai/mistral-large",
                "deepseek/deepseek-chat",
            },
            ModelsApiEndpoint: "https://openrouter.ai/api/v1/models"),

        new("fireworks", "Fireworks", "Fireworks AI",
            DefaultModel: "accounts/fireworks/models/glm-5p3-flash",
            DashboardUrl: "https://app.fireworks.ai/",
            KeysUrl: "https://app.fireworks.ai/settings/users/api-keys",
            AvailableModels: new[]
            {
                "accounts/fireworks/models/qwen3p8-max",
                "accounts/fireworks/models/glm-5p3",
                "accounts/fireworks/models/glm-5p3-flash",
                "accounts/fireworks/models/deepseek-v4p1-flash",
                "accounts/fireworks/models/kimi-k3",
                "accounts/fireworks/models/gpt-oss-120b",
            },
            ModelsApiEndpoint: "https://api.fireworks.ai/inference/v1/models"),

        new("cohere", "Cohere", "Cohere",
            DefaultModel: "command-a-03-2025",
            DashboardUrl: "https://dashboard.cohere.com/",
            KeysUrl: "https://dashboard.cohere.com/api-keys",
            AvailableModels: new[]
            {
                "command-a-plus-05-2026",
                "command-a-reasoning-08-2025",
                "command-a-03-2025",
                "command-r-plus-08-2024",
                "command-r-08-2024",
                "command-r7b-12-2024",
            },
            ModelsApiEndpoint: "https://api.cohere.com/v1/models"),

        new("kimi", "Kimi", "Moonshot AI",
            DefaultModel: "kimi-k3",
            DashboardUrl: "https://platform.moonshot.cn/",
            KeysUrl: "https://platform.moonshot.cn/console/api-keys",
            AvailableModels: new[]
            {
                "kimi-k3",
                "kimi-k2.7-code",
                "kimi-k2.7-code-highspeed",
                "kimi-k2.6",
                "kimi-k2.5",
                "kimi-k2",
                "moonshot-v1-128k-vision-preview",
                "moonshot-v1-32k-vision-preview",
                "moonshot-v1-8k-vision-preview",
                "moonshot-v1-128k",
                "moonshot-v1-32k",
                "moonshot-v1-8k",
            },
            ModelsApiEndpoint: "https://api.moonshot.cn/v1/models"),

        new("perplexity", "Perplexity", "Perplexity AI",
            DefaultModel: "sonar",
            DashboardUrl: "https://www.perplexity.ai/account/api/group",
            KeysUrl: "https://www.perplexity.ai/settings/api",
            AvailableModels: new[]
            {
                "sonar",
                "sonar-pro",
                "sonar-reasoning",
                "sonar-reasoning-pro",
                "sonar-deep-research",
            }),
    };

    /// <summary>Every supported provider in canonical order.</summary>
    public static IReadOnlyList<LlmProviderInfo> All => providers;

    /// <summary>Provider IDs only (lowercase).</summary>
    public static IEnumerable<string> AllIds => providers.Select(p => p.Id);

    private static readonly string[] defaultIds =
        { "claude", "openai", "deepseek", "gemini", "xai", "cohere", "mistral", "together", "fireworks" };

    /// <summary>
    /// First-party provider set surfaced in app UIs and voted on by default —
    /// every provider with a currently-working key and catalog entry. Groq,
    /// OpenRouter, and Kimi live in <see cref="All"/> but are excluded until
    /// their stored credentials are refreshed (the keys on file are rejected
    /// by the vendor); Perplexity is excluded because no key is configured.
    /// Callers can still opt any of those into a panel explicitly.
    /// </summary>
    public static IReadOnlyList<LlmProviderInfo> Default
        => defaultIds.Select(id => providers.First(p => p.Id == id)).ToArray();

    /// <summary>Default provider IDs only (lowercase). See <see cref="Default"/>.</summary>
    public static IEnumerable<string> DefaultIds => defaultIds;

    /// <summary>True if <paramref name="providerId"/> is in the default first-party set.</summary>
    public static bool IsDefault(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return false;
        var id = providerId.Trim().ToLowerInvariant();
        return defaultIds.Contains(id);
    }

    /// <summary>
    /// Look up a provider by its canonical id. Returns null if Legion doesn't
    /// know this provider.
    /// </summary>
    public static LlmProviderInfo? Get(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        var id = providerId.Trim().ToLowerInvariant();
        return providers.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>True if Legion knows about this provider.</summary>
    public static bool IsSupported(string providerId) => Get(providerId) is not null;

    /// <summary>True if Legion knows about this model id within the supplied provider.</summary>
    public static bool IsKnownModel(string providerId, string modelId)
    {
        var p = Get(providerId);
        if (p is null || string.IsNullOrWhiteSpace(modelId)) return false;
        return p.AvailableModels.Any(m => string.Equals(m, modelId, StringComparison.OrdinalIgnoreCase));
    }

    // ── Tiered model selection ──────────────────────────────────────────────

    /// <summary>
    /// Per-provider tier → model assignments. When a provider isn't listed,
    /// <see cref="GetTieredModel"/> falls back to the provider's DefaultModel.
    /// When a tier isn't listed for a provider, GetTieredModel walks down to
    /// the closest available tier (Highest → Higher → High → Medium → Low).
    ///
    /// The default (trusted) nine are explicitly mapped because they're the
    /// ones every production call uses. Other providers can be added
    /// incrementally once their credentials are live.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<ModelTier, string>> tieredModels =
        new Dictionary<string, IReadOnlyDictionary<ModelTier, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "claude-haiku-5-5",
                [ModelTier.Medium]  = "claude-sonnet-5-5",
                [ModelTier.High]    = "claude-opus-4-7",
                [ModelTier.Higher]  = "claude-opus-5-5",
                [ModelTier.Highest] = "claude-fable-5-1",
            },
            ["openai"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "gpt-4.1-nano",
                [ModelTier.Medium]  = "gpt-5.4-mini",
                [ModelTier.High]    = "gpt-5.4",
                [ModelTier.Higher]  = "gpt-5.5",
                [ModelTier.Highest] = "gpt-6.1-sol",
            },
            ["gemini"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "gemini-2.5-flash-lite",
                [ModelTier.Medium]  = "gemini-2.5-flash",
                [ModelTier.High]    = "gemini-2.5-pro",
                [ModelTier.Higher]  = "gemini-3.5-flash",
                [ModelTier.Highest] = "gemini-3.8-flash",
            },
            ["deepseek"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "deepseek-flash",
                [ModelTier.Medium]  = "deepseek-flash",
                [ModelTier.High]    = "deepseek-v4-pro",
                [ModelTier.Higher]  = "deepseek-v4-pro",
                [ModelTier.Highest] = "deepseek-v4-pro",
            },
            ["xai"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "grok-4.20-0309-non-reasoning",
                [ModelTier.Medium]  = "grok-4.3",
                [ModelTier.High]    = "grok-4.5",
                [ModelTier.Higher]  = "grok-4.6",
                [ModelTier.Highest] = "grok-4.7",
            },
            ["cohere"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "command-r7b-12-2024",
                [ModelTier.Medium]  = "command-r-plus-08-2024",
                [ModelTier.High]    = "command-a-03-2025",
                [ModelTier.Higher]  = "command-a-reasoning-08-2025",
                [ModelTier.Highest] = "command-a-plus-05-2026",
            },
            ["mistral"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "ministral-3b-latest",
                [ModelTier.Medium]  = "mistral-small-latest",
                [ModelTier.High]    = "mistral-medium-latest",
                [ModelTier.Higher]  = "magistral-medium-latest",
            },
            ["together"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                [ModelTier.Medium]  = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                [ModelTier.High]    = "Qwen/Qwen2.5-72B-Instruct-Turbo",
                [ModelTier.Higher]  = "deepseek-ai/DeepSeek-V4.1-Flash",
                [ModelTier.Highest] = "deepseek-ai/DeepSeek-V4-Pro-0813",
            },
            ["fireworks"] = new Dictionary<ModelTier, string>
            {
                [ModelTier.Low]     = "accounts/fireworks/models/glm-5p3-flash",
                [ModelTier.Medium]  = "accounts/fireworks/models/glm-5p3-flash",
                [ModelTier.High]    = "accounts/fireworks/models/glm-5p3",
                [ModelTier.Higher]  = "accounts/fireworks/models/deepseek-v4p1-flash",
                [ModelTier.Highest] = "accounts/fireworks/models/qwen3p8-max",
            },
        };

    /// <summary>
    /// Resolve a tier-appropriate model for the given provider. Falls back from
    /// the requested tier downward when that exact tier isn't mapped — so a
    /// caller asking for <see cref="ModelTier.Highest"/> against a provider
    /// that only registers Low/Medium/High will receive the High model. When
    /// the provider has no tier mapping at all, returns the provider's
    /// DefaultModel; when the provider is unknown, returns null.
    /// </summary>
    public static string? GetTieredModel(string providerId, ModelTier tier)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        var info = Get(providerId);
        if (info is null) return null;

        if (tieredModels.TryGetValue(providerId, out var tiers))
        {
            // Walk down from the requested tier — Highest → Higher → ... → Low.
            for (int t = (int)tier; t >= 0; t--)
                if (tiers.TryGetValue((ModelTier)t, out var model) && !string.IsNullOrWhiteSpace(model))
                    return model;
            // Walk back up if the requested tier was somehow below every entry.
            for (int t = (int)tier + 1; t <= (int)ModelTier.Highest; t++)
                if (tiers.TryGetValue((ModelTier)t, out var model) && !string.IsNullOrWhiteSpace(model))
                    return model;
        }

        return info.DefaultModel;
    }
}
