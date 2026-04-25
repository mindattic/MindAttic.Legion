using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MindAttic.Legion.Providers;

/// <summary>
/// Handles the actual HTTP dispatch to each LLM provider.
/// Mirrors the multi-provider pattern from LLMThinkTank and StreetSamurai's MultiLlmService,
/// but is fully self-contained with no external dependencies.
/// </summary>
public class LlmVotingProvider
{
    private readonly HttpClient http;
    private readonly VotingConfiguration config;

    // Default models per provider — used when ModelOverrides is not set
    private static readonly Dictionary<string, string> DefaultModels = new()
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

    private static readonly Dictionary<string, string> Endpoints = new()
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

    public LlmVotingProvider(HttpClient http, VotingConfiguration config)
    {
        this.http   = http;
        this.config = config;
        http.Timeout = config.ProviderTimeout;
    }

    /// <summary>
    /// Call a provider with a system prompt + user message.
    /// Uses per-voter API key and model overrides if set on the profile.
    /// </summary>
    public async Task<string> CallAsync(
        string providerId,
        string systemPrompt,
        string userMessage,
        int maxTokens,
        double temperature,
        VoterProfile? voterOverrides = null,
        CancellationToken ct = default)
    {
        var key   = voterOverrides?.ApiKeyOverride ?? GetApiKey(providerId);
        var model = voterOverrides?.ModelOverride
            ?? config.ModelOverrides.GetValueOrDefault(providerId)
            ?? DefaultModels.GetValueOrDefault(providerId, "");

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key configured for provider '{providerId}'.");

        return providerId switch
        {
            "claude"     => await CallClaudeAsync(key, model, systemPrompt, userMessage, maxTokens, temperature, ct),
            "gemini"     => await CallGeminiAsync(key, model, systemPrompt, userMessage, maxTokens, temperature, ct),
            "cohere"     => await CallCohereAsync(key, model, systemPrompt, userMessage, maxTokens, temperature, ct),
            _            => await CallOpenAiCompatibleAsync(providerId, key, model, systemPrompt, userMessage, maxTokens, temperature, ct),
        };
    }

    /// <summary>
    /// Resolves the API key for a provider. Checks <see cref="VotingConfiguration.ApiKeys"/>
    /// first (explicit config wins), then falls back to the shared MindAttic credential
    /// store when <see cref="VotingConfiguration.UseSharedCredentials"/> is enabled.
    /// </summary>
    public string? GetApiKey(string providerId)
    {
        if (config.ApiKeys.TryGetValue(providerId, out var explicitKey)
            && !string.IsNullOrWhiteSpace(explicitKey))
            return explicitKey;

        if (config.UseSharedCredentials)
            return MindAtticCredentialStore.GetKey(providerId);

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
            messages = new[]
            {
                new { role = "user", content = user }
            }
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
