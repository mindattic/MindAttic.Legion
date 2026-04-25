using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// Universal LLM client. The single point of contact every MindAttic application
/// uses to call any LLM provider — Claude, OpenAI, Gemini, DeepSeek, Mistral, Grok,
/// Groq, Together, OpenRouter, Fireworks, Cohere.
///
/// Legion owns the wire-level scaffolding: endpoint URLs, auth headers, request
/// shape, response parsing, model defaults, credential resolution, retry policy,
/// and circuit breaking. Apps keep their own prompts, parsing of structured
/// replies, and business logic — they delegate the "send prompt, get text" work
/// to this client.
///
/// <para>Resilience: by default, transient failures (5xx / 429 / network errors)
/// are retried with exponential backoff. After repeated failures the per-provider
/// circuit breaker opens, so subsequent calls fail fast and apps can route to a
/// different provider. Use <see cref="CallWithFallbackAsync"/> to make Legion try
/// a chain of providers until one succeeds.</para>
/// </summary>
public class LegionClient
{
    private readonly HttpClient http;
    private readonly LegionClientOptions options;

    public LegionClient(HttpClient http, LegionClientOptions? options = null)
    {
        this.http = http;
        this.options = options ?? LegionClientOptions.Default;
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
    /// Calls the provider with explicit credentials. Wraps the call in retry +
    /// circuit-breaker logic per <see cref="LegionClientOptions"/>.
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

        return ExecuteWithResilienceAsync(providerId,
            () => DispatchAsync(providerId, apiKey, resolvedModel, systemPrompt, userMessage, maxTokens, temperature, ct),
            ct);
    }

    /// <summary>
    /// Calls the provider, resolving the API key from the shared MindAttic credential
    /// store at <c>%APPDATA%/MindAttic/LLM/</c>.
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

    /// <summary>
    /// Multi-turn chat call with explicit credentials. Each <see cref="ChatTurn"/>
    /// has Role = "user" or "assistant"; the optional <paramref name="systemPrompt"/>
    /// is routed to the right place per provider (separate parameter for Claude,
    /// prepended as a system message for OpenAI-compatible providers).
    /// </summary>
    public Task<string> CallChatAsync(
        string providerId,
        string apiKey,
        string model,
        IEnumerable<ChatTurn> messages,
        string? systemPrompt = null,
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

        var turns = (messages ?? Enumerable.Empty<ChatTurn>()).ToList();
        if (turns.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        return ExecuteWithResilienceAsync(providerId,
            () => DispatchChatAsync(providerId, apiKey, resolvedModel, turns, systemPrompt, maxTokens, temperature, ct),
            ct);
    }

    /// <summary>
    /// Multi-turn chat using shared-credential lookup. Mirrors
    /// <see cref="CallAsync(string, string, string, int, double, string?, CancellationToken)"/>
    /// but accepts a conversation history.
    /// </summary>
    public Task<string> CallChatAsync(
        string providerId,
        IEnumerable<ChatTurn> messages,
        string? systemPrompt = null,
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

        return CallChatAsync(providerId, key, model!, messages, systemPrompt, maxTokens, temperature, ct);
    }

    /// <summary>
    /// Generates embedding vectors for a batch of texts via the provider's
    /// /embeddings endpoint. Currently supports OpenAI-compatible providers
    /// (openai). Returns one float[] per input string, in input order.
    /// </summary>
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        string providerId,
        string apiKey,
        string model,
        IReadOnlyList<string> inputs,
        int? dimensions = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key supplied for provider '{providerId}'.");
        if (inputs is null || inputs.Count == 0)
            return Array.Empty<float[]>();

        return await ExecuteWithResilienceAsync(providerId, async () =>
        {
            object payload = dimensions.HasValue
                ? new { model, input = inputs, dimensions = dimensions.Value }
                : new { model, input = inputs };

            var endpoint = providerId.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? "https://api.openai.com/v1/embeddings"
                : throw new ArgumentException($"Embeddings not supported for provider '{providerId}'.");

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await http.SendAsync(req, ct);
            await EnsureSuccessAsync(res, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
            var result = new List<float[]>(data.GetArrayLength());
            foreach (var item in data.EnumerateArray())
            {
                var vec = item.GetProperty("embedding");
                var arr = new float[vec.GetArrayLength()];
                int i = 0;
                foreach (var v in vec.EnumerateArray())
                    arr[i++] = v.GetSingle();
                result.Add(arr);
            }
            return (IReadOnlyList<float[]>)result;
        }, ct);
    }

    /// <summary>
    /// Generates an image via the provider's image-generation endpoint and
    /// returns the raw bytes per result (using <c>response_format=b64_json</c>
    /// so the caller doesn't have to download from a temporary URL).
    /// Currently supports OpenAI (DALL·E).
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> GenerateImageBytesAsync(
        string providerId,
        string apiKey,
        string model,
        string prompt,
        string size = "1024x1024",
        string quality = "standard",
        int n = 1,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key supplied for provider '{providerId}'.");
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));

        return await ExecuteWithResilienceAsync(providerId, async () =>
        {
            var endpoint = providerId.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? "https://api.openai.com/v1/images/generations"
                : throw new ArgumentException($"Image generation not supported for provider '{providerId}'.");

            var payload = new { model, prompt, size, quality, n, response_format = "b64_json" };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await http.SendAsync(req, ct);
            await EnsureSuccessAsync(res, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
            var images = new List<byte[]>(data.GetArrayLength());
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("b64_json", out var b64) && b64.ValueKind == JsonValueKind.String)
                    images.Add(Convert.FromBase64String(b64.GetString() ?? ""));
            }
            return (IReadOnlyList<byte[]>)images;
        }, ct);
    }

    /// <summary>
    /// Generates an image via the provider's image-generation endpoint and
    /// returns the URL(s) the provider hosts the result at. Currently supports
    /// OpenAI-compatible providers (openai → DALL·E).
    /// </summary>
    public async Task<IReadOnlyList<string>> GenerateImageAsync(
        string providerId,
        string apiKey,
        string model,
        string prompt,
        string size = "1024x1024",
        int n = 1,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            throw new ArgumentException("Provider id is required.", nameof(providerId));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key supplied for provider '{providerId}'.");
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("Prompt is required.", nameof(prompt));

        return await ExecuteWithResilienceAsync(providerId, async () =>
        {
            var endpoint = providerId.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? "https://api.openai.com/v1/images/generations"
                : throw new ArgumentException($"Image generation not supported for provider '{providerId}'.");

            var payload = new { model, prompt, size, n };

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var res = await http.SendAsync(req, ct);
            await EnsureSuccessAsync(res, ct);
            var json = await res.Content.ReadAsStringAsync(ct);

            var data = JsonDocument.Parse(json).RootElement.GetProperty("data");
            var urls = new List<string>(data.GetArrayLength());
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                    urls.Add(url.GetString() ?? "");
            }
            return (IReadOnlyList<string>)urls;
        }, ct);
    }

    /// <summary>
    /// Tries each provider in <paramref name="fallbackChain"/> in order and returns
    /// the response from the first one that succeeds (skipping providers whose
    /// breaker is open or who have no credential). Throws
    /// <see cref="AggregateException"/> with every error if all providers fail.
    /// </summary>
    public async Task<(string ProviderId, string Response)> CallWithFallbackAsync(
        IEnumerable<string> fallbackChain,
        string systemPrompt,
        string userMessage,
        int maxTokens = 2048,
        double temperature = 0.7,
        CancellationToken ct = default)
    {
        var errors = new List<Exception>();
        foreach (var providerId in fallbackChain ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(providerId)) continue;
            try
            {
                var reply = await CallAsync(providerId, systemPrompt, userMessage,
                    maxTokens, temperature, modelOverride: null, ct: ct);
                return (providerId, reply);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors.Add(new Exception($"[{providerId}] {ex.Message}", ex));
            }
        }
        throw new AggregateException("All providers in fallback chain failed.", errors);
    }

    // ── Resilience wrapper ──────────────────────────────────────────────────────

    private async Task<T> ExecuteWithResilienceAsync<T>(
        string providerId,
        Func<Task<T>> action,
        CancellationToken ct)
    {
        CircuitBreaker.ThrowIfOpen(providerId);

        var attempt = 0;
        var delay = options.InitialBackoff;
        while (true)
        {
            try
            {
                var result = await action();
                CircuitBreaker.RecordSuccess(providerId);
                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (attempt >= options.MaxRetries)
                {
                    CircuitBreaker.RecordFailure(providerId, options.CircuitBreakerThreshold, options.CircuitBreakerCooldown);
                    throw;
                }
                attempt++;
                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { throw; }
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * options.BackoffMultiplier);
            }
            catch (Exception)
            {
                // Non-transient (e.g. 401 auth) — record and rethrow without retry
                CircuitBreaker.RecordFailure(providerId, options.CircuitBreakerThreshold, options.CircuitBreakerCooldown);
                throw;
            }
        }
    }

    private static bool IsTransient(Exception ex)
    {
        if (ex is HttpRequestException hre)
        {
            // Network errors (no status code) are transient.
            if (hre.StatusCode is null) return true;
            var code = (int)hre.StatusCode;
            return code == 408 || code == 429 || code >= 500;
        }
        if (ex is TaskCanceledException) return true; // request timeout (not user-cancel — handled above)
        return false;
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

    private Task<string> DispatchAsync(string providerId, string key, string model,
        string system, string user, int maxTokens, double temperature, CancellationToken ct)
        => DispatchChatAsync(providerId, key, model,
            new[] { new ChatTurn("user", user) },
            system, maxTokens, temperature, ct);

    private Task<string> DispatchChatAsync(string providerId, string key, string model,
        IReadOnlyList<ChatTurn> messages, string? systemPrompt,
        int maxTokens, double temperature, CancellationToken ct)
        => providerId.ToLowerInvariant() switch
        {
            "claude" => CallClaudeChatAsync(key, model, messages, systemPrompt, maxTokens, temperature, ct),
            "gemini" => CallGeminiChatAsync(key, model, messages, systemPrompt, maxTokens, temperature, ct),
            "cohere" => CallCohereChatAsync(key, model, messages, systemPrompt, maxTokens, temperature, ct),
            _        => CallOpenAiCompatibleChatAsync(providerId, key, model, messages, systemPrompt, maxTokens, temperature, ct),
        };

    private async Task<string> CallClaudeChatAsync(
        string key, string model, IReadOnlyList<ChatTurn> messages, string? systemPrompt,
        int maxTokens, double temperature, CancellationToken ct)
    {
        // Claude expects role = "user" or "assistant" only, with system as a separate field.
        var apiMessages = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { role = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user", content = m.Content })
            .ToArray();

        object payload = string.IsNullOrWhiteSpace(systemPrompt)
            ? new { model, max_tokens = maxTokens, temperature, messages = apiMessages }
            : new { model, max_tokens = maxTokens, temperature, system = systemPrompt, messages = apiMessages };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoints["claude"]);
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallGeminiChatAsync(
        string key, string model, IReadOnlyList<ChatTurn> messages, string? systemPrompt,
        int maxTokens, double temperature, CancellationToken ct)
    {
        var url = Endpoints["gemini"].Replace("{model}", model).Replace("{key}", key);
        // Gemini uses role="user"/"model"; convert "assistant"→"model".
        var contents = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new
            {
                role = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            })
            .ToArray();

        object payload = string.IsNullOrWhiteSpace(systemPrompt)
            ? new { contents, generationConfig = new { maxOutputTokens = maxTokens, temperature } }
            : new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents,
                generationConfig = new { maxOutputTokens = maxTokens, temperature }
            };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content").GetProperty("parts")[0]
            .GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallCohereChatAsync(
        string key, string model, IReadOnlyList<ChatTurn> messages, string? systemPrompt,
        int maxTokens, double temperature, CancellationToken ct)
    {
        var apiMessages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            apiMessages.Add(new { role = "system", content = systemPrompt });
        foreach (var m in messages.Where(x => !string.Equals(x.Role, "system", StringComparison.OrdinalIgnoreCase)))
            apiMessages.Add(new { role = m.Role.ToLowerInvariant(), content = m.Content });

        var payload = new { model, max_tokens = maxTokens, temperature, messages = apiMessages };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoints["cohere"]);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("message").GetProperty("content")[0]
            .GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallOpenAiCompatibleChatAsync(
        string providerId, string key, string model, IReadOnlyList<ChatTurn> messages,
        string? systemPrompt, int maxTokens, double temperature, CancellationToken ct)
    {
        if (!Endpoints.TryGetValue(providerId, out var endpoint))
            throw new ArgumentException($"Unknown provider: {providerId}");

        var apiMessages = new List<object>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            apiMessages.Add(new { role = "system", content = systemPrompt });
        foreach (var m in messages.Where(x => !string.Equals(x.Role, "system", StringComparison.OrdinalIgnoreCase)))
            apiMessages.Add(new { role = m.Role.ToLowerInvariant(), content = m.Content });

        var payload = new { model, max_tokens = maxTokens, temperature, messages = apiMessages };

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var res = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(res, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonDocument.Parse(json).RootElement
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    /// <summary>
    /// Throws <see cref="HttpRequestException"/> with the response body included
    /// in the message — so the diagnoser can spot quota / billing markers that
    /// providers return only in the response body. Replaces the framework's
    /// <c>EnsureSuccessStatusCode</c>, which discards the body.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;

        string body = "";
        try { body = await res.Content.ReadAsStringAsync(ct); } catch { /* best effort */ }
        if (body.Length > 2048) body = body[..2048];

        var msg = string.IsNullOrEmpty(body)
            ? $"{(int)res.StatusCode} {res.ReasonPhrase}"
            : $"{(int)res.StatusCode} {res.ReasonPhrase}: {body}";
        throw new HttpRequestException(msg, inner: null, statusCode: res.StatusCode);
    }

}
