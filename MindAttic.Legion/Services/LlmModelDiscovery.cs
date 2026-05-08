using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Result of querying one provider's model inventory endpoint.
/// </summary>
public sealed record LlmModelDiscoveryResult(
    LlmProviderInfo Provider,
    IReadOnlyList<string> KnownModels,
    IReadOnlyList<string> LiveModels,
    string? ConfiguredModel,
    string EffectiveModel,
    string? ModelsEndpoint,
    bool HasCredential,
    bool CanQueryLiveModels,
    bool LiveModelQuerySucceeded,
    long ElapsedMilliseconds,
    LlmHealthDiagnosis Diagnosis,
    int? HttpStatusCode,
    string? ErrorMessage)
{
    /// <summary>
    /// The model list callers should show first. Live models win when the
    /// provider exposed them; otherwise Legion falls back to its static catalog.
    /// </summary>
    public IReadOnlyList<string> AvailableModels =>
        LiveModels.Count > 0 ? LiveModels : KnownModels;

    /// <summary>True when the configured/effective model came from live discovery.</summary>
    public bool UsesLiveModel => LiveModels.Count > 0 && LiveModels.Contains(EffectiveModel, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Queries cloud model-list endpoints and normalizes their response shapes into
/// simple model ids. This is intentionally independent from
/// <see cref="LegionClient"/> so apps can build settings/status screens without
/// sending a prompt to every provider.
/// </summary>
public sealed class LlmModelDiscovery
{
    private readonly HttpClient http;

    public LlmModelDiscovery(HttpClient http)
    {
        this.http = http;
    }

    /// <summary>Discover model inventories for every provider in the catalog.</summary>
    public Task<IReadOnlyList<LlmModelDiscoveryResult>> DiscoverAllAsync(
        TimeSpan? timeoutPerProvider = null,
        CancellationToken ct = default)
        => DiscoverAsync(LlmProviderCatalog.AllIds, timeoutPerProvider, ct);

    /// <summary>Discover model inventories for the supplied providers.</summary>
    public async Task<IReadOnlyList<LlmModelDiscoveryResult>> DiscoverAsync(
        IEnumerable<string> providerIds,
        TimeSpan? timeoutPerProvider = null,
        CancellationToken ct = default)
    {
        var ids = providerIds?.Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList() ?? new List<string>();

        if (ids.Count == 0)
            return Array.Empty<LlmModelDiscoveryResult>();

        var tasks = ids.Select(id => DiscoverOneAsync(id, timeoutPerProvider, ct));
        return await Task.WhenAll(tasks);
    }

    /// <summary>Discover the model inventory for one provider.</summary>
    public async Task<LlmModelDiscoveryResult> DiscoverOneAsync(
        string providerId,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var info = LlmProviderCatalog.Get(providerId)
                   ?? new LlmProviderInfo(providerId, providerId, "unknown",
                       DefaultModel: "", DashboardUrl: "", KeysUrl: "",
                       AvailableModels: Array.Empty<string>());

        var runtime = LlmProviderRuntimeConfigurationResolver.Get(info.Id);
        var hasCredential = !string.IsNullOrWhiteSpace(runtime.ApiKey);
        var knownModels = info.AvailableModels.ToArray();
        var endpoint = info.ModelsApiEndpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return CreateResult(info, knownModels, Array.Empty<string>(), runtime, endpoint,
                hasCredential, canQuery: false, succeeded: false, elapsedMs: 0,
                diagnosis: LlmHealthDiagnosis.Unknown, statusCode: null,
                error: "No model-list endpoint is configured.");
        }

        if (string.IsNullOrWhiteSpace(runtime.ApiKey))
        {
            return CreateResult(info, knownModels, Array.Empty<string>(), runtime, endpoint,
                hasCredential: false, canQuery: false, succeeded: false, elapsedMs: 0,
                diagnosis: LlmHealthDiagnosis.MissingCredential, statusCode: null,
                error: "No API key configured.");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            if (cts is not null)
                cts.CancelAfter(timeout!.Value);
            var token = cts?.Token ?? ct;

            using var req = new HttpRequestMessage(HttpMethod.Get, AddApiKeyToEndpoint(info, endpoint, runtime.ApiKey));
            AddAuthHeaders(req, info, runtime.ApiKey);

            var res = await http.SendAsync(req, token);
            await EnsureSuccessAsync(res, token);
            var json = await res.Content.ReadAsStringAsync(token);
            var liveModels = ExtractModelIds(info.Id, json);
            sw.Stop();

            return CreateResult(info, knownModels, liveModels, runtime, endpoint,
                hasCredential, canQuery: true, succeeded: true, elapsedMs: sw.ElapsedMilliseconds,
                diagnosis: LlmHealthDiagnosis.Healthy, statusCode: 200, error: null);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var (diagnosis, statusCode) = LlmHealthDiagnoser.ClassifyException(ex, ct);
            return CreateResult(info, knownModels, Array.Empty<string>(), runtime, endpoint,
                hasCredential, canQuery: true, succeeded: false, elapsedMs: sw.ElapsedMilliseconds,
                diagnosis: diagnosis, statusCode: statusCode, error: ex.Message);
        }
    }

    internal static IReadOnlyList<string> ExtractModelIds(string providerId, string json)
    {
        var models = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(json);
            CollectModelIds(doc.RootElement, providerId, models, seen);
        }
        catch
        {
            return Array.Empty<string>();
        }

        return models;
    }

    private static LlmModelDiscoveryResult CreateResult(
        LlmProviderInfo info,
        IReadOnlyList<string> knownModels,
        IReadOnlyList<string> liveModels,
        LlmProviderRuntimeConfiguration runtime,
        string? endpoint,
        bool hasCredential,
        bool canQuery,
        bool succeeded,
        long elapsedMs,
        LlmHealthDiagnosis diagnosis,
        int? statusCode,
        string? error)
    {
        var effectiveModel = !string.IsNullOrWhiteSpace(runtime.Model) ? runtime.Model!
            : liveModels.Count > 0 ? liveModels[0]
            : info.DefaultModel;

        return new LlmModelDiscoveryResult(
            Provider: info,
            KnownModels: knownModels,
            LiveModels: liveModels,
            ConfiguredModel: runtime.Model,
            EffectiveModel: effectiveModel,
            ModelsEndpoint: endpoint,
            HasCredential: hasCredential,
            CanQueryLiveModels: canQuery,
            LiveModelQuerySucceeded: succeeded,
            ElapsedMilliseconds: elapsedMs,
            Diagnosis: diagnosis,
            HttpStatusCode: statusCode,
            ErrorMessage: error);
    }

    private static void AddAuthHeaders(HttpRequestMessage req, LlmProviderInfo info, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        if (info.Id.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            return;
        }

        if (!info.Id.Equals("gemini", StringComparison.OrdinalIgnoreCase))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static string AddApiKeyToEndpoint(LlmProviderInfo info, string endpoint, string? apiKey)
    {
        if (!info.Id.Equals("gemini", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(apiKey))
            return endpoint;

        var separator = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return endpoint + separator + "key=" + Uri.EscapeDataString(apiKey);
    }

    private static void CollectModelIds(JsonElement element, string providerId, List<string> models, HashSet<string> seen)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectModelIds(item, providerId, models, seen);
                break;

            case JsonValueKind.Object:
                if (TryReadModelId(element, providerId, out var id))
                    AddModel(id, models, seen);

                if (element.TryGetProperty("data", out var data))
                    CollectModelIds(data, providerId, models, seen);
                if (element.TryGetProperty("models", out var nestedModels))
                    CollectModelIds(nestedModels, providerId, models, seen);
                break;

            case JsonValueKind.String:
                AddModel(element.GetString(), models, seen);
                break;
        }
    }

    private static bool TryReadModelId(JsonElement element, string providerId, out string? id)
    {
        id = ReadString(element, "id")
             ?? ReadString(element, "name")
             ?? ReadString(element, "model")
             ?? ReadString(element, "model_id");

        if (string.IsNullOrWhiteSpace(id))
            return false;

        id = NormalizeModelId(providerId, id);
        return !string.IsNullOrWhiteSpace(id);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string NormalizeModelId(string providerId, string? id)
    {
        var normalized = id?.Trim() ?? "";
        if (providerId.Equals("gemini", StringComparison.OrdinalIgnoreCase)
            && normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["models/".Length..];
        }

        return normalized;
    }

    private static void AddModel(string? modelId, List<string> models, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        var trimmed = modelId.Trim();
        if (seen.Add(trimmed))
            models.Add(trimmed);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode)
            return;

        string body = "";
        try { body = await res.Content.ReadAsStringAsync(ct); } catch { }
        if (body.Length > 2048)
            body = body[..2048];

        var msg = string.IsNullOrEmpty(body)
            ? $"{(int)res.StatusCode} {res.ReasonPhrase}"
            : $"{(int)res.StatusCode} {res.ReasonPhrase}: {body}";
        throw new HttpRequestException(msg, inner: null, statusCode: res.StatusCode);
    }
}
