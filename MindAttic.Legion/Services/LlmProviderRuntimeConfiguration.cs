using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Runtime configuration resolved for one provider from the shared credential
/// store, environment variables, and the provider catalog.
/// </summary>
public sealed record LlmProviderRuntimeConfiguration(
    string ProviderId,
    string? ApiKey,
    string? Type,
    string? Model,
    int? MaxTokens,
    string? BaseUrl,
    string? BaseUrlSource)
{
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Reads provider runtime settings from providers.json without requiring callers
/// to parse that file themselves. The supported optional fields are:
/// <c>apiKey</c>, <c>type</c>, <c>model</c>, <c>maxTokens</c>, and
/// <c>baseUrl</c>. Local providers also honor their catalog-defined environment
/// variable before falling back to the default local URL.
/// </summary>
public static class LlmProviderRuntimeConfigurationResolver
{
    /// <summary>Resolve stored and environment-backed configuration for a provider.</summary>
    public static LlmProviderRuntimeConfiguration Get(string providerId)
    {
        var id = providerId?.Trim().ToLowerInvariant() ?? "";
        var info = LlmProviderCatalog.Get(id);

        string? type = null;
        string? model = null;
        string? baseUrl = null;
        int? maxTokens = null;

        var raw = MindAtticCredentialStore.LoadAllRaw();
        if (raw.TryGetValue(id, out var json))
            ReadRawProviderJson(json, out type, out model, out maxTokens, out baseUrl);

        var baseUrlSource = string.IsNullOrWhiteSpace(baseUrl) ? null : "providers.json";
        if (!string.IsNullOrWhiteSpace(info?.BaseUrlEnvironmentVariable))
        {
            var envBaseUrl = Environment.GetEnvironmentVariable(info.BaseUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(envBaseUrl))
            {
                baseUrl = envBaseUrl.Trim();
                baseUrlSource = info.BaseUrlEnvironmentVariable;
            }
        }

        if (string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(info?.DefaultBaseUrl))
        {
            baseUrl = info.DefaultBaseUrl;
            baseUrlSource = "default";
        }

        return new LlmProviderRuntimeConfiguration(
            ProviderId: id,
            ApiKey: MindAtticCredentialStore.GetKey(id),
            Type: type,
            Model: model,
            MaxTokens: maxTokens,
            BaseUrl: NormalizeBaseUrl(baseUrl),
            BaseUrlSource: baseUrlSource);
    }

    /// <summary>Returns a configured model override, if providers.json has one.</summary>
    public static string? GetModel(string providerId) => Get(providerId).Model;

    /// <summary>Resolve the live-model endpoint for a provider.</summary>
    public static string? GetModelsEndpoint(LlmProviderInfo provider)
    {
        if (!provider.IsLocal)
            return provider.ModelsApiEndpoint;

        var baseUrl = Get(provider.Id).BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return provider.ModelsApiEndpoint;

        return provider.Id switch
        {
            "ollama" => CombineUrl(baseUrl, "api/tags"),
            "lmstudio" => CombineUrl(EnsureOpenAiBaseUrl(baseUrl), "models"),
            _ => provider.ModelsApiEndpoint,
        };
    }

    /// <summary>Resolve the chat-completions endpoint for a provider.</summary>
    public static string? GetChatEndpoint(LlmProviderInfo provider)
    {
        if (!provider.IsLocal)
            return provider.ChatCompletionsEndpoint;

        var baseUrl = Get(provider.Id).BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return provider.ChatCompletionsEndpoint;

        return provider.Id switch
        {
            "ollama" => CombineUrl(baseUrl, "v1/chat/completions"),
            "lmstudio" => CombineUrl(EnsureOpenAiBaseUrl(baseUrl), "chat/completions"),
            _ => provider.ChatCompletionsEndpoint,
        };
    }

    internal static string CombineUrl(string baseUrl, string relativePath)
    {
        var left = NormalizeBaseUrl(baseUrl) ?? "";
        var right = relativePath.TrimStart('/');
        return left.EndsWith("/", StringComparison.Ordinal)
            ? left + right
            : left + "/" + right;
    }

    private static void ReadRawProviderJson(
        string json,
        out string? type,
        out string? model,
        out int? maxTokens,
        out string? baseUrl)
    {
        type = null;
        model = null;
        maxTokens = null;
        baseUrl = null;

        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            type = ReadString(root, "type");
            model = ReadString(root, "model");
            baseUrl = ReadString(root, "baseUrl") ?? ReadString(root, "baseURL");

            if (root.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                maxTokens = mt.GetInt32();
        }
        catch
        {
            // Bad config should be reported by the caller's connectivity checks,
            // not by throwing during metadata rendering.
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var trimmed = baseUrl.Trim();
        while (trimmed.EndsWith("/", StringComparison.Ordinal))
            trimmed = trimmed[..^1];
        return trimmed;
    }

    private static string EnsureOpenAiBaseUrl(string baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl) ?? "";
        return normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : CombineUrl(normalized, "v1");
    }
}
