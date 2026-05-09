using System.Net;
using System.Text.Json;
using MindAttic.Legion;
using MindAttic.Legion.Tests.TestSupport;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Pins down the wire-level behavior of <see cref="LegionClient"/> per provider
/// dispatch. These guard against silent regressions like:
///   • a provider's request body shape changing
///   • the wrong auth header being sent
///   • the system prompt being routed to the wrong place
///   • the multi-turn conversation order being dropped
///   • response-body content not being preserved into the error message
/// </summary>
[TestFixture]
public class LegionClientWireTests
{
    [SetUp]
    public void SetUp() => CircuitBreaker.ResetAll();

    [TearDown]
    public void TearDown() => CircuitBreaker.ResetAll();

    // ── Claude wire shape ───────────────────────────────────────────────────────

    [Test]
    public async Task Claude_SendsXApiKeyHeader_AndAnthropicVersion()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("claude", "sk-ant-key", "claude-sonnet-4-6", "be brief", "hi");

        var req = handler.Requests.Single();
        Assert.That(req.Headers["x-api-key"], Is.EqualTo("sk-ant-key"));
        Assert.That(req.Headers["anthropic-version"], Is.EqualTo("2023-06-01"));
        Assert.That(req.Uri.ToString(), Is.EqualTo("https://api.anthropic.com/v1/messages"));
    }

    [Test]
    public async Task Claude_SystemPrompt_PostedAsTopLevelSystemField()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("claude", "sk", "claude-sonnet-4-6", "You are Claude.", "hi");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.GetProperty("system").GetString(), Is.EqualTo("You are Claude."));
        Assert.That(doc.RootElement.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("user"));
    }

    [Test]
    public async Task Claude_BlankSystemPrompt_OmitsSystemField()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("claude", "sk", "claude-sonnet-4-6", "", "hi");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.TryGetProperty("system", out _), Is.False);
    }

    [Test]
    public async Task Claude_PassesMaxTokensAndTemperature()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("claude", "sk", "claude-sonnet-4-6", "s", "u",
            maxTokens: 1234, temperature: 0.42);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.GetProperty("max_tokens").GetInt32(), Is.EqualTo(1234));
        Assert.That(doc.RootElement.GetProperty("temperature").GetDouble(), Is.EqualTo(0.42).Within(0.0001));
    }

    [Test]
    public async Task Claude_Opus47_OmitsTemperatureFromPayload()
    {
        // Opus 4.7 returns 400 invalid_request_error if `temperature` is present
        // in the payload. The wire builder strips it for opus-4-7 family ids;
        // this test pins that behavior so a future "always include temperature"
        // refactor can't silently re-break every Opus call.
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("claude", "sk", "claude-opus-4-7", "s", "u",
            maxTokens: 1234, temperature: 0.42);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.GetProperty("max_tokens").GetInt32(), Is.EqualTo(1234));
        Assert.That(doc.RootElement.TryGetProperty("temperature", out _), Is.False,
            "Opus 4.7 deprecates temperature; payload must omit the field.");
    }

    [Test]
    public async Task Claude_Opus47LongContext_OmitsTemperatureFromPayload()
    {
        // The [1m] long-context Opus 4.7 variant has the same temperature
        // restriction as the base model — the StartsWith match in
        // ClaudeModelDeprecatesTemperature covers both ids.
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.ClaudeOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("claude", "sk", "claude-opus-4-7[1m]", "s", "u",
            maxTokens: 1234, temperature: 0.42);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.TryGetProperty("temperature", out _), Is.False);
    }

    [Test]
    public void ClaudeModelDeprecatesTemperature_PinsTheModelFamilyExactly()
    {
        // Belt-and-braces: assert the helper directly so a typo in the model
        // prefix is caught even if the wire test fixtures change. Older Claude
        // models still accept temperature and must NOT be stripped.
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature("claude-opus-4-7"),       Is.True);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature("claude-opus-4-7[1m]"),   Is.True);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature("CLAUDE-OPUS-4-7"),       Is.True);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature("claude-opus-4-6"),       Is.False);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature("claude-sonnet-4-6"),     Is.False);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature("claude-haiku-4-5-20251001"), Is.False);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature(""),                      Is.False);
        Assert.That(LegionClient.ClaudeModelDeprecatesTemperature(null),                    Is.False);
    }

    // ── OpenAI-compatible wire shape ────────────────────────────────────────────

    [Test]
    public async Task OpenAi_SendsBearerAuth()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.OpenAiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("openai", "sk-key", "gpt-4.1-mini", "s", "u");

        var req = handler.Requests.Single();
        Assert.That(req.AuthScheme, Is.EqualTo("Bearer"));
        Assert.That(req.AuthValue,  Is.EqualTo("sk-key"));
    }

    [Test]
    public async Task OpenAi_SystemPrompt_PrependedAsSystemMessage()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.OpenAiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("openai", "sk", "gpt-4.1-mini", "You help.", "hi");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var msgs = doc.RootElement.GetProperty("messages");
        Assert.That(msgs[0].GetProperty("role").GetString(),    Is.EqualTo("system"));
        Assert.That(msgs[0].GetProperty("content").GetString(), Is.EqualTo("You help."));
        Assert.That(msgs[1].GetProperty("role").GetString(),    Is.EqualTo("user"));
        Assert.That(msgs[1].GetProperty("content").GetString(), Is.EqualTo("hi"));
    }

    [TestCase("openai")]
    [TestCase("deepseek")]
    [TestCase("mistral")]
    [TestCase("xai")]
    [TestCase("groq")]
    [TestCase("together")]
    [TestCase("openrouter")]
    [TestCase("fireworks")]
    public async Task OpenAiCompatible_AllUseChatCompletionsShape(string providerId)
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.OpenAiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var reply = await client.CallAsync(providerId, "k", model: "", systemPrompt: "s", userMessage: "u");

        Assert.That(reply, Is.EqualTo("Hello World!"));
        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.TryGetProperty("messages", out _), Is.True);
        Assert.That(doc.RootElement.TryGetProperty("model", out _), Is.True);
    }

    // ── Gemini wire shape ──────────────────────────────────────────────────────

    [Test]
    public async Task Gemini_PutsApiKeyInQueryString_NotHeader()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.GeminiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("gemini", "AIzaXYZ", "gemini-2.0-flash", "s", "u");

        var req = handler.Requests.Single();
        Assert.That(req.AuthScheme, Is.Null);
        Assert.That(req.Uri.Query, Does.Contain("key=AIzaXYZ"));
        Assert.That(req.Uri.AbsoluteUri,
            Does.Contain("models/gemini-2.0-flash:generateContent"));
    }

    [Test]
    public async Task Gemini_SystemPrompt_GoesToSystemInstructionNotMessages()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.GeminiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("gemini", "k", "gemini-2.0-flash", "You are Gemini.", "hi");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(
            doc.RootElement.GetProperty("systemInstruction")
                .GetProperty("parts")[0].GetProperty("text").GetString(),
            Is.EqualTo("You are Gemini."));
        Assert.That(doc.RootElement.GetProperty("contents")[0].GetProperty("role").GetString(),
            Is.EqualTo("user"));
    }

    // ── Cohere wire shape ───────────────────────────────────────────────────────

    [Test]
    public async Task Cohere_HitsV2Endpoint_AndExtractsMessageContent()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.CohereOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var reply = await client.CallAsync("cohere", "k", "command-r-plus", "s", "u");

        Assert.That(reply, Is.EqualTo("Hello World!"));
        Assert.That(handler.Requests[0].Uri.ToString(),
            Is.EqualTo("https://api.cohere.com/v2/chat"));
    }

    // ── Multi-turn chat ─────────────────────────────────────────────────────────

    [Test]
    public async Task ChatTurn_OpenAi_PreservesUserAssistantOrder()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.OpenAiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallChatAsync("openai", "k", "gpt-4.1-mini", new[]
        {
            new ChatTurn("user",      "Q1"),
            new ChatTurn("assistant", "A1"),
            new ChatTurn("user",      "Q2"),
        }, systemPrompt: "S");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var msgs = doc.RootElement.GetProperty("messages");
        Assert.That(msgs.GetArrayLength(), Is.EqualTo(4));
        Assert.That(msgs[0].GetProperty("role").GetString(),    Is.EqualTo("system"));
        Assert.That(msgs[1].GetProperty("content").GetString(), Is.EqualTo("Q1"));
        Assert.That(msgs[2].GetProperty("role").GetString(),    Is.EqualTo("assistant"));
        Assert.That(msgs[2].GetProperty("content").GetString(), Is.EqualTo("A1"));
        Assert.That(msgs[3].GetProperty("content").GetString(), Is.EqualTo("Q2"));
    }

    [Test]
    public async Task ChatTurn_Gemini_RemapsAssistantToModel()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.GeminiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallChatAsync("gemini", "k", "gemini-2.0-flash", new[]
        {
            new ChatTurn("user",      "first"),
            new ChatTurn("assistant", "reply"),
            new ChatTurn("user",      "follow-up"),
        });

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        var contents = doc.RootElement.GetProperty("contents");
        Assert.That(contents[0].GetProperty("role").GetString(), Is.EqualTo("user"));
        Assert.That(contents[1].GetProperty("role").GetString(), Is.EqualTo("model"),
            "Gemini's role for assistant turns is 'model', not 'assistant'");
        Assert.That(contents[2].GetProperty("role").GetString(), Is.EqualTo("user"));
    }

    [Test]
    public void ChatTurn_EmptyConversation_Throws()
    {
        var client = new LegionClient(new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")));
        Assert.ThrowsAsync<ArgumentException>(() =>
            client.CallChatAsync("openai", "k", "gpt-4.1-mini", Array.Empty<ChatTurn>()));
    }

    [Test]
    public void ChatTurn_MissingApiKey_Throws()
    {
        var client = new LegionClient(new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallChatAsync("openai", "", "gpt-4.1-mini",
                new[] { new ChatTurn("user", "hi") }));
    }

    // ── Model fallback resolution ───────────────────────────────────────────────

    [Test]
    public async Task BlankModel_FallsBackToProviderDefaultModel()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.OpenAiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("openai", "k", model: "", systemPrompt: "s", userMessage: "u");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.GetProperty("model").GetString(),
            Is.EqualTo(LegionClient.DefaultModels["openai"]));
    }

    [Test]
    public async Task ExplicitModel_OverridesDefault()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, Bodies.OpenAiOk);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.CallAsync("openai", "k", "gpt-4o", "s", "u");

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("gpt-4o"));
    }

    // ── Error-body propagation ──────────────────────────────────────────────────

    [Test]
    public void HttpError_PreservesStatusCodeOnException()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var ex = Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));
        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public void HttpError_IncludesResponseBodyInMessage_ForQuotaDetection()
    {
        // Critical: the diagnoser scans the message for "insufficient_quota" /
        // "billing" markers to disambiguate 429-quota from 429-rate-limit. If the
        // body is dropped from the message, that classification breaks.
        var handler = new FixedResponseHandler(HttpStatusCode.TooManyRequests, Bodies.OpenAiQuota);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var ex = Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("openai", "k", "gpt-4.1-mini", "s", "u"));
        Assert.That(ex!.Message, Does.Contain("insufficient_quota"));
    }

    [Test]
    public void HttpError_StatusCode_FlowsThroughDiagnoser()
    {
        // 401 from the wire → AuthInvalid through the classifier
        var handler = new FixedResponseHandler(HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        HttpRequestException? thrown = null;
        try { client.CallAsync("openai", "k", "gpt-4.1-mini", "s", "u").GetAwaiter().GetResult(); }
        catch (HttpRequestException ex) { thrown = ex; }

        Assert.That(thrown, Is.Not.Null);
        var (d, code) = LlmHealthDiagnoser.ClassifyException(thrown!);
        Assert.That(d, Is.EqualTo(LlmHealthDiagnosis.AuthInvalid));
        Assert.That(code, Is.EqualTo(401));
    }

    // ── Cancellation ────────────────────────────────────────────────────────────

    [Test]
    public void UserCancellation_PropagatesAsOperationCanceled()
    {
        var handler = new HangingHandler();
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var cts = new CancellationTokenSource();
        var task = client.CallAsync("openai", "k", "gpt-4.1-mini", "s", "u", ct: cts.Token);
        cts.Cancel();

        // CatchAsync (not ThrowsAsync) so subclasses like TaskCanceledException qualify —
        // both indicate the call was aborted by the cancellation token.
        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    // ── Embeddings ──────────────────────────────────────────────────────────────

    [Test]
    public async Task EmbedAsync_ReturnsVectorsFromOpenAi()
    {
        var body = """{"data":[{"embedding":[0.1,0.2,0.3]},{"embedding":[0.4,0.5,0.6]}]}""";
        var handler = new FixedResponseHandler(HttpStatusCode.OK, body);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var vectors = await client.EmbedAsync("openai", "k", "text-embedding-3-small",
            new[] { "alpha", "beta" });

        Assert.That(vectors, Has.Count.EqualTo(2));
        Assert.That(vectors[0], Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }).Within(0.0001f));
        Assert.That(vectors[1], Is.EqualTo(new[] { 0.4f, 0.5f, 0.6f }).Within(0.0001f));
        Assert.That(handler.Requests[0].Uri.ToString(),
            Is.EqualTo("https://api.openai.com/v1/embeddings"));
    }

    [Test]
    public async Task EmbedAsync_EmptyInputs_ReturnsEmptyAndDoesNotCallApi()
    {
        var handler = new FixedResponseHandler(HttpStatusCode.OK, "{}");
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var vectors = await client.EmbedAsync("openai", "k", "model", Array.Empty<string>());

        Assert.That(vectors, Is.Empty);
        Assert.That(handler.CallCount, Is.EqualTo(0));
    }

    [Test]
    public void EmbedAsync_MissingKey_Throws()
    {
        var client = new LegionClient(new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.EmbedAsync("openai", "", "m", new[] { "x" }));
    }

    [Test]
    public void EmbedAsync_UnsupportedProvider_Throws()
    {
        var client = new LegionClient(new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")));
        Assert.ThrowsAsync<ArgumentException>(() =>
            client.EmbedAsync("claude", "k", "m", new[] { "x" }));
    }

    [Test]
    public async Task EmbedAsync_PassesDimensionsWhenSpecified()
    {
        var body = """{"data":[{"embedding":[0.1]}]}""";
        var handler = new FixedResponseHandler(HttpStatusCode.OK, body);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        await client.EmbedAsync("openai", "k", "text-embedding-3-small",
            new[] { "x" }, dimensions: 256);

        using var doc = JsonDocument.Parse(handler.Bodies[0]);
        Assert.That(doc.RootElement.GetProperty("dimensions").GetInt32(), Is.EqualTo(256));
    }

    // ── Image generation ────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateImageBytesAsync_DecodesB64Json()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var b64   = Convert.ToBase64String(bytes);
        var body  = $$"""{"data":[{"b64_json":"{{b64}}"}]}""";
        var handler = new FixedResponseHandler(HttpStatusCode.OK, body);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var images = await client.GenerateImageBytesAsync("openai", "k", "dall-e-3", "a sunset");

        Assert.That(images, Has.Count.EqualTo(1));
        Assert.That(images[0], Is.EqualTo(bytes));
    }

    [Test]
    public async Task GenerateImageAsync_ReturnsUrls()
    {
        var body = """{"data":[{"url":"https://cdn.example/img-1.png"},{"url":"https://cdn.example/img-2.png"}]}""";
        var handler = new FixedResponseHandler(HttpStatusCode.OK, body);
        var client  = new LegionClient(new HttpClient(handler), TestOptions.Instant());

        var urls = await client.GenerateImageAsync("openai", "k", "dall-e-3", "a sunset", n: 2);

        Assert.That(urls, Has.Count.EqualTo(2));
        Assert.That(urls[0], Is.EqualTo("https://cdn.example/img-1.png"));
    }

    [Test]
    public void GenerateImage_BlankPrompt_Throws()
    {
        var client = new LegionClient(new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")));
        Assert.ThrowsAsync<ArgumentException>(() =>
            client.GenerateImageAsync("openai", "k", "dall-e-3", ""));
        Assert.ThrowsAsync<ArgumentException>(() =>
            client.GenerateImageBytesAsync("openai", "k", "dall-e-3", "  "));
    }

    [Test]
    public void GenerateImage_UnsupportedProvider_Throws()
    {
        var client = new LegionClient(new HttpClient(new FixedResponseHandler(HttpStatusCode.OK, "{}")));
        Assert.ThrowsAsync<ArgumentException>(() =>
            client.GenerateImageAsync("claude", "k", "m", "prompt"));
    }
}
