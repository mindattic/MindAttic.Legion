using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Universal LLM client. The single point of contact every MindAttic application
/// uses to call any LLM provider — Claude, OpenAI, Gemini, DeepSeek, Mistral, Grok,
/// Groq, Together, OpenRouter, Fireworks, Cohere.
///
/// Legion owns the connection scaffolding: endpoint URLs, auth headers, request
/// shape, response parsing, model defaults, and credential resolution. Apps keep
/// their own prompts, parsing of structured replies, and business logic — they
/// just delegate the wire-level "send prompt, get text" work to this client.
///
/// Usage with shared credentials (most common):
/// <code>
///   var text = await legion.CallAsync("claude", systemPrompt, userMessage);
/// </code>
///
/// Usage with explicit key (when the app holds the key itself):
/// <code>
///   var text = await legion.CallAsync("openai", apiKey: myKey, model: "gpt-4.1-mini",
///                                     systemPrompt, userMessage);
/// </code>
/// </summary>
public class LegionClient
{
    private readonly HttpClient http;

    public LegionClient(HttpClient http)
    {
        this.http = http;
    }

    /// <summary>
    /// Default model per provider. Used when no <c>model</c> override is supplied
    /// and the providers.json store has no model recorded.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultModels { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"]     = "claude-sonnet-4-6",
        ["openai"]     = "gpt-4.1-mini",
        ["gemini"]     = "gemini-2.0-flash",
        ["deepseek"]   = "deepseek-chat",
        ["mistral"]    = "mistral-large-latest",
        ["xai"]        = "grok-3-mini-fast",
        ["groq"]       = "llama-3.3-70b-versatile",
        ["together"]   = "meta-llama/Llama-3-70b-chat-hf",
        ["openrouter"] = "meta-llama/llama-3.1-8b-instruct:free",
        ["fireworks"]  = "accounts/fireworks/models/llama-v3p1-70b-instruct",
        ["cohere"]     = "command-r-plus",
    };

    private static readonly Dictionary<string, string> Endpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"]     = "https://api.anthropic.com/v1/messages",
        ["openai"]     = "https://api.openai.com/v1/chat/completions",
        ["gemini"]     = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={key}",
        ["deepseek"]   = "https://api.deepseek.com/chat/completions",
        ["mistral"]    = "https://api.mistral.ai/v1/chat/completions",
        ["xai"]        = "https://api.x.ai/v1/chat/completions",
        ["groq"]       = "https://api.groq.com/openai/v1/chat/completions",
        ["together"]   = "https://api.together.xyz/v1/chat/completions",
        ["openrouter"] = "https://openrouter.ai/api/v1/chat/completions",
        ["fireworks"]  = "https://api.fireworks.ai/inference/v1/chat/completions",
        ["cohere"]     = "https://api.cohere.com/v2/chat",
    };

    /// <summary>True if Legion knows how to talk to this provider.</summary>
    public static bool IsSupported(string providerId) =>
        !string.IsNullOrWhiteSpace(providerId) && Endpoints.ContainsKey(providerId);

    /// <summary>
    /// Calls the provider with explicit credentials. Use this when the calling app
    /// already holds the key (e.g. multi-tenant SaaS). Returns the response text.
    /// </summary>
    public Task<string> CallAsync(
        string providerId,
        string apiKey,
        string model,
        string systemPrompt,
        string userMessage,
        int maxTokens = 2048,
        double temperature = 0.7,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key supplied for provider '{providerId}'.");

        var resolvedModel = string.IsNullOrWhiteSpace(model)
            ? DefaultModels.GetValueOrDefault(providerId, "")
            : model;

        return providerId.ToLowerInvariant() switch
        {
            "claude" => CallClaudeAsync(apiKey, resolvedModel, systemPrompt, userMessage, maxTokens, temperature, ct),
            "gemini" => CallGeminiAsync(apiKey, resolvedModel, systemPrompt, userMessage, maxTokens, temperature, ct),
            "cohere" => CallCohereAsync(apiKey, resolvedModel, systemPrompt, userMessage, maxTokens, temperature, ct),
            _        => CallOpenAiCompatibleAsync(providerId, apiKey, resolvedModel, systemPrompt, userMessage, maxTokens, temperature, ct),
        };
    }

    /// <summary>
    /// Calls the provider, resolving the API key from the shared MindAttic credential
    /// store at <c>%APPDATA%/MindAttic/LLM/</c>. The model is taken from
    /// <paramref name="modelOverride"/>, then providers.json, then <see cref="DefaultModels"/>.
    /// Throws if no key is configured for <paramref name="providerId"/>.
    /// </summary>
    public Task<string> CallAsync(
        string providerId,
        string systemPrompt,
        string userMessage,
        int maxTokens = 2048,
        double temperature = 0.7,
        string? modelOverride = null,
        CancellationToken ct = default)
    {
        var key = MindAtticCredentialStore.GetKey(providerId);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key configured for provider '{providerId}' in shared store.");

        var model = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride
                  : ResolveModelFromStore(providerId)
                  ?? DefaultModels.GetValueOrDefault(providerId, "");

        return CallAsync(providerId, key, model!, systemPrompt, userMessage, maxTokens, temperature, ct);
    }

    private static string? ResolveModelFromStore(string providerId)
    {
        try
        {
            var path = MindAtticCredentialStore.ProvidersFilePath;
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.NameEquals(providerId)) continue;
                if (prop.Value.ValueKind != JsonValueKind.Object) return null;
                if (prop.Value.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String)
                    return m.GetString();
                return null;
            }
        }
        catch { }
        return null;
    }

    // ── Provider-specific dispatch ──────────────────────────────────────────────

    private async Task<string> CallClaudeAsync(
        string key, string model, string system, string user,
        int maxTokens, double temperature, CancellationToken ct)
    {
        var payload = new
        {
            model,
            max_tokens = maxTokens,
            temperature,
            system,
            messages = new[] { new { role = "user", content = user } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoints["claude"]);
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallGeminiAsync(
        string key, string model, string system, string user,
        int maxTokens, double temperature, CancellationToken ct)
    {
        var url = Endpoints["gemini"].Replace("{model}", model).Replace("{key}", key);
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { parts = new[] { new { text = user } } } },
            generationConfig = new { maxOutputTokens = maxTokens, temperature }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallCohereAsync(
        string key, string model, string system, string user,
        int maxTokens, double temperature, CancellationToken ct)
    {
        var payload = new
        {
            model,
            max_tokens = maxTokens,
            temperature,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoints["cohere"]);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("message").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallOpenAiCompatibleAsync(
        string providerId, string key, string model, string system, string user,
        int maxTokens, double temperature, CancellationToken ct)
    {
        if (!Endpoints.TryGetValue(providerId, out var endpoint))
            throw new ArgumentException($"Unknown provider: {providerId}");

        var payload = new
        {
            model,
            max_tokens = maxTokens,
            temperature,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
