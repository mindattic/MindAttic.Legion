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
            DefaultModel: "claude-sonnet-4-6",
            DashboardUrl: "https://console.anthropic.com/",
            KeysUrl: "https://console.anthropic.com/settings/keys",
            AvailableModels: new[]
            {
                "claude-opus-4-7",
                "claude-opus-4-7[1m]",
                "claude-opus-4-6",
                "claude-sonnet-4-6",
                "claude-haiku-4-5-20251001",
                "claude-3-5-sonnet-20241022",
                "claude-3-5-haiku-20241022",
                "claude-3-opus-20240229",
            },
            ModelsApiEndpoint: "https://api.anthropic.com/v1/models"),

        new("openai", "ChatGPT", "OpenAI",
            DefaultModel: "gpt-4.1-mini",
            DashboardUrl: "https://platform.openai.com/usage",
            KeysUrl: "https://platform.openai.com/api-keys",
            AvailableModels: new[]
            {
                "gpt-4.1",
                "gpt-4.1-mini",
                "gpt-4.1-nano",
                "gpt-4o",
                "gpt-4o-mini",
                "o3-mini",
                "o1",
                "o1-mini",
                "gpt-3.5-turbo",
            },
            ModelsApiEndpoint: "https://api.openai.com/v1/models"),

        new("gemini", "Gemini", "Google",
            DefaultModel: "gemini-2.0-flash",
            DashboardUrl: "https://aistudio.google.com/",
            KeysUrl: "https://aistudio.google.com/app/apikey",
            AvailableModels: new[]
            {
                "gemini-2.5-flash",
                "gemini-2.5-pro",
                "gemini-2.0-flash",
                "gemini-2.0-flash-thinking-exp",
                "gemini-1.5-pro",
                "gemini-1.5-flash",
            },
            ModelsApiEndpoint: "https://generativelanguage.googleapis.com/v1beta/models"),

        new("deepseek", "DeepSeek", "DeepSeek AI",
            DefaultModel: "deepseek-chat",
            DashboardUrl: "https://platform.deepseek.com/usage",
            KeysUrl: "https://platform.deepseek.com/api_keys",
            AvailableModels: new[]
            {
                "deepseek-chat",
                "deepseek-reasoner",
            },
            ModelsApiEndpoint: "https://api.deepseek.com/models"),

        new("mistral", "Mistral", "Mistral AI",
            DefaultModel: "mistral-large-latest",
            DashboardUrl: "https://console.mistral.ai/",
            KeysUrl: "https://console.mistral.ai/api-keys/",
            AvailableModels: new[]
            {
                "mistral-large-latest",
                "mistral-medium-latest",
                "mistral-small-latest",
                "ministral-3b-latest",
                "ministral-8b-latest",
                "codestral-latest",
                "open-mixtral-8x22b",
            },
            ModelsApiEndpoint: "https://api.mistral.ai/v1/models"),

        new("xai", "Grok", "xAI",
            DefaultModel: "grok-3-mini-fast",
            DashboardUrl: "https://console.x.ai/",
            KeysUrl: "https://console.x.ai/team/default/api-keys",
            AvailableModels: new[]
            {
                "grok-3",
                "grok-3-mini",
                "grok-3-mini-fast",
                "grok-2-1212",
                "grok-2-vision-1212",
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
                "mixtral-8x7b-32768",
                "gemma2-9b-it",
            },
            ModelsApiEndpoint: "https://api.groq.com/openai/v1/models"),

        new("together", "Together", "Together AI",
            DefaultModel: "meta-llama/Llama-3-70b-chat-hf",
            DashboardUrl: "https://api.together.xyz/",
            KeysUrl: "https://api.together.xyz/settings/api-keys",
            AvailableModels: new[]
            {
                "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                "meta-llama/Llama-3-70b-chat-hf",
                "meta-llama/Llama-4-Scout-17B-16E-Instruct",
                "mistralai/Mixtral-8x7B-Instruct-v0.1",
                "Qwen/Qwen2.5-72B-Instruct-Turbo",
                "deepseek-ai/DeepSeek-V3",
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
            DefaultModel: "accounts/fireworks/models/llama-v3p1-70b-instruct",
            DashboardUrl: "https://app.fireworks.ai/",
            KeysUrl: "https://app.fireworks.ai/settings/users/api-keys",
            AvailableModels: new[]
            {
                "accounts/fireworks/models/llama-v3p3-70b-instruct",
                "accounts/fireworks/models/llama-v3p1-70b-instruct",
                "accounts/fireworks/models/llama-v3p1-8b-instruct",
                "accounts/fireworks/models/qwen2p5-72b-instruct",
                "accounts/fireworks/models/deepseek-v3",
                "accounts/fireworks/models/mixtral-8x22b-instruct",
            },
            ModelsApiEndpoint: "https://api.fireworks.ai/inference/v1/models"),

        new("cohere", "Cohere", "Cohere",
            DefaultModel: "command-r-plus",
            DashboardUrl: "https://dashboard.cohere.com/",
            KeysUrl: "https://dashboard.cohere.com/api-keys",
            AvailableModels: new[]
            {
                "command-a-03-2025",
                "command-r-plus",
                "command-r",
                "command",
                "command-light",
            },
            ModelsApiEndpoint: "https://api.cohere.com/v1/models"),
    };

    /// <summary>Every supported provider in canonical order.</summary>
    public static IReadOnlyList<LlmProviderInfo> All => providers;

    /// <summary>Provider IDs only (lowercase).</summary>
    public static IEnumerable<string> AllIds => providers.Select(p => p.Id);

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
}
