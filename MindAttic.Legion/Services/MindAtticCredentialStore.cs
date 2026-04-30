using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Shared credential store for all MindAttic applications.
///
/// LLM API keys live in a per-user folder so every MindAttic app (LLMVoting,
/// LLMThinkTank, StreetSamurai, etc.) reads the same keys without each app
/// configuring them independently. The folder is user-scoped on Windows
/// (%APPDATA%) so access is already limited to the current user.
///
/// Resolution order when looking up a provider key:
///   1. &lt;providerId&gt;.key  — per-provider override file, content is the raw key (trimmed)
///   2. providers.json     — canonical rich format: { providerId: { type, apiKey, model, maxTokens } }
///                           This is what LLMThinkTank's settings UI writes to, so any keys
///                           configured there are visible to every other MindAttic app.
///   3. credentials.json   — legacy flat map: { providerId: "key" }
///
/// Per-provider .key files win over providers.json when both exist for the
/// same provider, so a developer can drop a file to override the shared store.
///
/// Platform paths (via <see cref="Environment.SpecialFolder.ApplicationData"/>):
///   Windows: %APPDATA%\MindAttic\LLM\
///   macOS:   ~/.config/MindAttic/LLM/     (via $XDG_CONFIG_HOME fallback)
///   Linux:   ~/.config/MindAttic/LLM/
/// </summary>
public static class MindAtticCredentialStore
{
    private const string ProvidersJsonFile   = "providers.json";
    private const string CredentialsJsonFile = "credentials.json";
    private const string KeyFileExtension    = ".key";

    private static readonly object writeLock = new();

    /// <summary>
    /// Full path to the shared credential directory. Does not create the directory.
    /// Override with the MINDATTIC_LLM_CREDENTIALS environment variable (useful for tests).
    /// </summary>
    public static string CredentialDirectory =>
        Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MindAttic", "LLM");

    /// <summary>Path to the canonical rich-format credentials file.</summary>
    public static string ProvidersFilePath => Path.Combine(CredentialDirectory, ProvidersJsonFile);

    /// <summary>Returns the key for a provider, or null if no credential is on disk.</summary>
    public static string? GetKey(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        var dir = CredentialDirectory;
        if (!Directory.Exists(dir)) return null;

        // 1. Per-provider .key file (highest priority — manual override)
        var keyFile = Path.Combine(dir, providerId + KeyFileExtension);
        if (File.Exists(keyFile))
        {
            var raw = ReadFileSafe(keyFile);
            if (!string.IsNullOrWhiteSpace(raw)) return raw.Trim();
        }

        // 2. providers.json (canonical rich format)
        var fromProviders = TryReadProvidersJsonKey(providerId);
        if (!string.IsNullOrWhiteSpace(fromProviders)) return fromProviders.Trim();

        // 3. credentials.json (legacy flat format)
        var jsonFile = Path.Combine(dir, CredentialsJsonFile);
        if (File.Exists(jsonFile))
        {
            var all = ParseFlatJsonSafe(jsonFile);
            if (all.TryGetValue(providerId, out var key) && !string.IsNullOrWhiteSpace(key))
                return key.Trim();
        }

        return null;
    }

    /// <summary>
    /// Writes a key for the given provider into providers.json (canonical rich format),
    /// preserving any existing type/model/maxTokens fields for that provider.
    /// Creates the directory if needed. After this call, every MindAttic app reading the
    /// shared store sees the new key.
    /// </summary>
    public static void SetKey(string providerId, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider ID is required.", nameof(providerId));

        var trimmed = apiKey?.Trim() ?? "";

        lock (writeLock)
        {
            Directory.CreateDirectory(CredentialDirectory);

            var providers = LoadProvidersRawSafe();
            providers[providerId] = MergeApiKeyIntoProviderJson(
                existingJson: providers.TryGetValue(providerId, out var existing) ? existing : null,
                providerId: providerId,
                apiKey: trimmed);

            WriteProvidersJson(providers);
        }
    }

    /// <summary>
    /// Loads every credential as a flat dictionary (providerId → apiKey). Merges
    /// credentials.json + providers.json + .key files. Per-provider .key files win
    /// on collision; providers.json beats credentials.json.
    /// </summary>
    public static Dictionary<string, string> LoadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dir = CredentialDirectory;
        if (!Directory.Exists(dir)) return result;

        // Start with credentials.json (lowest priority)
        var jsonFile = Path.Combine(dir, CredentialsJsonFile);
        if (File.Exists(jsonFile))
        {
            foreach (var kv in ParseFlatJsonSafe(jsonFile))
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    result[kv.Key] = kv.Value.Trim();
        }

        // Layer providers.json on top
        foreach (var kv in LoadProvidersRawSafe())
        {
            var key = ExtractApiKeyFromProviderJson(kv.Value);
            if (!string.IsNullOrWhiteSpace(key))
                result[kv.Key] = key.Trim();
        }

        // Layer .key files (highest priority)
        foreach (var file in Directory.EnumerateFiles(dir, "*" + KeyFileExtension))
        {
            var providerId = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(providerId)) continue;
            var raw = ReadFileSafe(file);
            if (!string.IsNullOrWhiteSpace(raw))
                result[providerId] = raw.Trim();
        }

        return result;
    }

    /// <summary>Provider IDs that currently have a non-empty credential on disk.</summary>
    public static List<string> ListProviders() => LoadAll().Keys.ToList();

    /// <summary>True if providers.json exists at the canonical location.</summary>
    public static bool ProvidersFileExists() => File.Exists(ProvidersFilePath);

    /// <summary>
    /// Returns providers.json as a map of providerId → the per-provider rich-JSON
    /// object string (i.e. <c>{ "type": "...", "apiKey": "...", "model": "...", "maxTokens": ... }</c>).
    /// Empty if the file is missing or unparseable. Use this when you need to read
    /// or persist the full per-provider auth payload (model, maxTokens, …) — the
    /// flat <see cref="LoadAll"/> only surfaces the apiKey.
    /// </summary>
    public static Dictionary<string, string> LoadAllRaw() => LoadProvidersRawSafe();

    /// <summary>
    /// Replaces the entire providers.json with the supplied map of
    /// providerId → raw per-provider JSON object string. Does a single
    /// pretty-printed atomic write under a lock.
    /// </summary>
    public static void SaveAllRaw(IDictionary<string, string> providers)
    {
        if (providers is null) return;
        lock (writeLock)
        {
            Directory.CreateDirectory(CredentialDirectory);
            WriteProvidersJson(providers);
        }
    }

    /// <summary>
    /// Upserts a single provider's raw per-provider JSON in providers.json,
    /// preserving every other provider's entry. Use when the caller already
    /// knows the full payload (type/apiKey/model/maxTokens) it wants written.
    /// </summary>
    public static void SaveRaw(string providerId, string rawProviderJson)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return;
        lock (writeLock)
        {
            var providers = LoadProvidersRawSafe();
            providers[providerId] = string.IsNullOrWhiteSpace(rawProviderJson) ? "{}" : rawProviderJson;
            Directory.CreateDirectory(CredentialDirectory);
            WriteProvidersJson(providers);
        }
    }

    // ── providers.json helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Loads providers.json as a map of providerId → raw JSON object string.
    /// Returns an empty dictionary if the file is missing or unparseable.
    /// </summary>
    private static Dictionary<string, string> LoadProvidersRawSafe()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(ProvidersFilePath)) return result;

            var raw = File.ReadAllText(ProvidersFilePath);
            if (string.IsNullOrWhiteSpace(raw)) return result;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                result[prop.Name] = prop.Value.GetRawText();
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// Reads providers.json and returns the <c>apiKey</c> for
    /// <paramref name="providerId"/>, or <c>null</c> if absent.
    /// </summary>
    private static string? TryReadProvidersJsonKey(string providerId)
    {
        var providers = LoadProvidersRawSafe();
        return providers.TryGetValue(providerId, out var json)
            ? ExtractApiKeyFromProviderJson(json)
            : null;
    }

    /// <summary>
    /// Pulls the <c>apiKey</c> string out of one provider's per-provider JSON
    /// object. Returns <c>null</c> on missing field, malformed JSON, or any
    /// other read failure.
    /// </summary>
    private static string? ExtractApiKeyFromProviderJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("apiKey", out var apiKey)
                && apiKey.ValueKind == JsonValueKind.String)
            {
                return apiKey.GetString();
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Builds the per-provider JSON object for <see cref="SetKey"/>: replaces
    /// only the <c>apiKey</c>, preserves any existing <c>type</c> /
    /// <c>model</c> / <c>maxTokens</c>. When no <c>type</c> is set, infers it
    /// from the provider id (anthropic / google / bearer).
    /// </summary>
    private static string MergeApiKeyIntoProviderJson(string? existingJson, string providerId, string apiKey)
    {
        // Preserve type/model/maxTokens; replace only apiKey.
        string? type = null;
        string? model = null;
        int? maxTokens = null;

        if (!string.IsNullOrWhiteSpace(existingJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(existingJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("type", out var t)  && t.ValueKind == JsonValueKind.String) type = t.GetString();
                    if (doc.RootElement.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) model = m.GetString();
                    if (doc.RootElement.TryGetProperty("maxTokens", out var mt) && mt.ValueKind == JsonValueKind.Number) maxTokens = mt.GetInt32();
                }
            }
            catch { }
        }

        type ??= providerId.Equals("claude", StringComparison.OrdinalIgnoreCase) ? "anthropic"
              :  providerId.Equals("gemini", StringComparison.OrdinalIgnoreCase) ? "google"
              :  "bearer";

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteString("apiKey", apiKey);
            if (!string.IsNullOrWhiteSpace(model))
                w.WriteString("model", model);
            if (maxTokens.HasValue)
                w.WriteNumber("maxTokens", maxTokens.Value);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Writes the supplied map of providerId → raw per-provider JSON to
    /// providers.json, sorted alphabetically and pretty-printed. Reuses each
    /// entry's existing JSON when parseable; substitutes <c>{}</c> when not.
    /// </summary>
    private static void WriteProvidersJson(IDictionary<string, string> providers)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var (providerId, json) in providers.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                w.WritePropertyName(providerId);
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                    doc.RootElement.WriteTo(w);
                }
                catch
                {
                    w.WriteStartObject();
                    w.WriteEndObject();
                }
            }
            w.WriteEndObject();
        }
        File.WriteAllBytes(ProvidersFilePath, ms.ToArray());
    }

    // ── small helpers ───────────────────────────────────────────────────────────

    /// <summary>Reads a text file, swallowing any IO error and returning <c>null</c>.</summary>
    private static string? ReadFileSafe(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return null; }
    }

    /// <summary>
    /// Parses the legacy flat <c>credentials.json</c> format (a top-level
    /// providerId → key map) into a dictionary. Returns an empty dictionary on
    /// any read or parse error.
    /// </summary>
    private static Dictionary<string, string> ParseFlatJsonSafe(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
