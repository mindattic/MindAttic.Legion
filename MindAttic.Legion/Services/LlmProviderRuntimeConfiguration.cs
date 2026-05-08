using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Runtime configuration resolved for one provider from the shared credential
/// store and the provider catalog.
/// </summary>
public sealed record LlmProviderRuntimeConfiguration(
    string ProviderId,
    string? ApiKey,
    string? Type,
    string? Model,
    int? MaxTokens)
{
    /// <summary>True when an API key is present and non-blank.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Reads provider runtime settings from providers.json without requiring callers
/// to parse that file themselves. The supported optional fields are:
/// <c>apiKey</c>, <c>type</c>, <c>model</c>, and <c>maxTokens</c>.
/// </summary>
public static class LlmProviderRuntimeConfigurationResolver
{
    /// <summary>Resolve stored configuration for a provider.</summary>
    public static LlmProviderRuntimeConfiguration Get(string providerId)
    {
        var id = providerId?.Trim().ToLowerInvariant() ?? "";

        string? type = null;
        string? model = null;
        int? maxTokens = null;

        var raw = MindAtticCredentialStore.LoadAllRaw();
        if (raw.TryGetValue(id, out var json))
            ReadRawProviderJson(json, out type, out model, out maxTokens);

        return new LlmProviderRuntimeConfiguration(
            ProviderId: id,
            ApiKey: MindAtticCredentialStore.GetKey(id),
            Type: type,
            Model: model,
            MaxTokens: maxTokens);
    }

    /// <summary>Returns a configured model override, if providers.json has one.</summary>
    public static string? GetModel(string providerId) => Get(providerId).Model;

    /// <summary>
    /// Parse one provider's raw JSON string and surface the optional fields.
    /// Bad JSON is intentionally swallowed so a malformed providers.json
    /// can't take the whole status renderer down — connectivity probes will
    /// flag the real problem with a clearer error.
    /// </summary>
    private static void ReadRawProviderJson(
        string json,
        out string? type,
        out string? model,
        out int? maxTokens)
    {
        type = null;
        model = null;
        maxTokens = null;

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

            if (root.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number)
                maxTokens = mt.GetInt32();
        }
        catch
        {
            // Bad config should be reported by the caller's connectivity checks,
            // not by throwing during metadata rendering.
        }
    }

    /// <summary>Read a string-valued property, or null when missing/non-string.</summary>
    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
