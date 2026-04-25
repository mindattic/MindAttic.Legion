using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

[TestFixture]
public class LegionClientTests
{
    [Test]
    public void IsSupported_KnowsAllCatalogProviders()
    {
        foreach (var id in LlmProviderCatalog.AllIds)
            Assert.That(LegionClient.IsSupported(id), Is.True, $"expected {id} to be supported");
    }

    [Test]
    public void IsSupported_RejectsUnknown()
    {
        Assert.That(LegionClient.IsSupported(""), Is.False);
        Assert.That(LegionClient.IsSupported("not-a-real-provider"), Is.False);
        Assert.That(LegionClient.IsSupported(null!), Is.False);
    }

    [Test]
    public void DefaultModels_HaveEntryForEverySupportedProvider()
    {
        foreach (var id in LlmProviderCatalog.AllIds)
            Assert.That(LegionClient.DefaultModels.ContainsKey(id), Is.True, $"expected default model for {id}");
    }

    [Test]
    public async Task CallAsync_ExplicitKey_DispatchesClaudeShape()
    {
        var stubBody = """{"content":[{"text":"hi from claude"}]}""";
        var capture = new CapturingHandler(stubBody);
        var client  = new LegionClient(new HttpClient(capture));

        var reply = await client.CallAsync("claude",
            apiKey: "sk-ant-test",
            model: "claude-sonnet-4-6",
            systemPrompt: "be brief",
            userMessage: "hi",
            maxTokens: 64,
            temperature: 0.0);

        Assert.That(reply, Is.EqualTo("hi from claude"));
        Assert.That(capture.LastUri!.ToString(), Is.EqualTo("https://api.anthropic.com/v1/messages"));
        Assert.That(capture.LastHeaders!["x-api-key"], Is.EqualTo("sk-ant-test"));
        Assert.That(capture.LastHeaders!["anthropic-version"], Is.EqualTo("2023-06-01"));
    }

    [Test]
    public async Task CallAsync_ExplicitKey_DispatchesOpenAiShape()
    {
        var stubBody = """{"choices":[{"message":{"content":"hi from openai"}}]}""";
        var capture = new CapturingHandler(stubBody);
        var client  = new LegionClient(new HttpClient(capture));

        var reply = await client.CallAsync("openai",
            apiKey: "sk-test",
            model: "gpt-4.1-mini",
            systemPrompt: "be brief",
            userMessage: "hi");

        Assert.That(reply, Is.EqualTo("hi from openai"));
        Assert.That(capture.LastUri!.ToString(), Is.EqualTo("https://api.openai.com/v1/chat/completions"));
        Assert.That(capture.LastAuthScheme, Is.EqualTo("Bearer"));
        Assert.That(capture.LastAuthValue,  Is.EqualTo("sk-test"));
    }

    [Test]
    public async Task CallAsync_ExplicitKey_DispatchesGeminiShape()
    {
        var stubBody = """{"candidates":[{"content":{"parts":[{"text":"hi from gemini"}]}}]}""";
        var capture = new CapturingHandler(stubBody);
        var client  = new LegionClient(new HttpClient(capture));

        var reply = await client.CallAsync("gemini",
            apiKey: "google-key",
            model: "gemini-2.0-flash",
            systemPrompt: "be brief",
            userMessage: "hi");

        Assert.That(reply, Is.EqualTo("hi from gemini"));
        Assert.That(capture.LastUri!.ToString(), Does.Contain("models/gemini-2.0-flash:generateContent"));
        Assert.That(capture.LastUri!.Query, Does.Contain("key=google-key"));
    }

    [Test]
    public async Task CallAsync_ExplicitKey_DispatchesCohereShape()
    {
        var stubBody = """{"message":{"content":[{"text":"hi from cohere"}]}}""";
        var capture = new CapturingHandler(stubBody);
        var client  = new LegionClient(new HttpClient(capture));

        var reply = await client.CallAsync("cohere",
            apiKey: "cohere-key",
            model: "command-r-plus",
            systemPrompt: "be brief",
            userMessage: "hi");

        Assert.That(reply, Is.EqualTo("hi from cohere"));
        Assert.That(capture.LastUri!.ToString(), Is.EqualTo("https://api.cohere.com/v2/chat"));
    }

    [Test]
    public void CallAsync_MissingKey_Throws()
    {
        var client = new LegionClient(new HttpClient(new CapturingHandler("{}")));
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallAsync("claude", apiKey: "", model: "x", systemPrompt: "s", userMessage: "u"));
    }

    [Test]
    public void CallAsync_UnknownProvider_Throws()
    {
        var client = new LegionClient(new HttpClient(new CapturingHandler("{}")));
        Assert.ThrowsAsync<ArgumentException>(() =>
            client.CallAsync("madeup", apiKey: "k", model: "m", systemPrompt: "s", userMessage: "u"));
    }

    [Test]
    public async Task CallAsync_BlankModel_FallsBackToDefault()
    {
        var stubBody = """{"choices":[{"message":{"content":"ok"}}]}""";
        var capture = new CapturingHandler(stubBody);
        var client  = new LegionClient(new HttpClient(capture));

        await client.CallAsync("openai",
            apiKey: "k",
            model: "",  // blank → default
            systemPrompt: "s",
            userMessage: "u");

        Assert.That(capture.LastBody, Does.Contain("\"model\":\"gpt-4.1-mini\""));
    }

    [Test]
    public void CallAsync_HttpError_Propagates()
    {
        var failing = new HttpClient(new ErrorHandler(HttpStatusCode.Unauthorized));
        var client  = new LegionClient(failing);
        var ex = Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("claude", "k", "m", "s", "u"));
        Assert.That(ex!.Message, Does.Contain("401").Or.Contains("Unauthorized"));
    }
}

// ── shared HTTP test doubles ────────────────────────────────────────────────

internal sealed class CapturingHandler : HttpMessageHandler
{
    private readonly string body;
    public Uri? LastUri { get; private set; }
    public string? LastBody { get; private set; }
    public IDictionary<string, string>? LastHeaders { get; private set; }
    public string? LastAuthScheme { get; private set; }
    public string? LastAuthValue  { get; private set; }

    public CapturingHandler(string responseBody) => body = responseBody;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        LastHeaders = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        LastAuthScheme = request.Headers.Authorization?.Scheme;
        LastAuthValue  = request.Headers.Authorization?.Parameter;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ErrorHandler : HttpMessageHandler
{
    private readonly HttpStatusCode code;
    public ErrorHandler(HttpStatusCode code) => this.code = code;
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("err") });
}
