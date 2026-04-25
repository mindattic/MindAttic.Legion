using System.Net;
using System.Text;

namespace MindAttic.Legion.Tests.TestSupport;

/// <summary>
/// Shared HTTP-handler test doubles used across the diagnostic suite. Each
/// handler simulates a specific failure mode an LLM API can exhibit so we can
/// pin down exactly how Legion classifies it.
/// </summary>
internal static class Bodies
{
    public const string ClaudeOk      = """{"content":[{"text":"Hello World!"}]}""";
    public const string OpenAiOk      = """{"choices":[{"message":{"content":"Hello World!"}}]}""";
    public const string GeminiOk      = """{"candidates":[{"content":{"parts":[{"text":"Hello World!"}]}}]}""";
    public const string CohereOk      = """{"message":{"content":[{"text":"Hello World!"}]}}""";

    public const string ClaudeWrong   = """{"content":[{"text":"goodbye"}]}""";
    public const string OpenAiWrong   = """{"choices":[{"message":{"content":"farewell"}}]}""";

    /// <summary>Body OpenAI returns when the account is out of credit (insufficient_quota).</summary>
    public const string OpenAiQuota   = """{"error":{"message":"You exceeded your current quota, please check your plan and billing details.","type":"insufficient_quota","code":"insufficient_quota"}}""";

    /// <summary>Body Anthropic returns when credit balance is too low.</summary>
    public const string ClaudeCreditLow = """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits."}}""";

    public const string AuthInvalidBody = """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","code":"invalid_api_key"}}""";

    public const string ServerErrorBody = """{"error":{"message":"The server had an error while processing your request"}}""";

    public const string MalformedJson = "this is not json {{ at all";

    public const string EmptyResponse = "";

    public const string EmptyJsonObject = "{}";
}

/// <summary>
/// Always returns the same <see cref="HttpStatusCode"/> + body. Useful for
/// pinning down how a single failure mode classifies. Captures every request
/// for later assertion.
/// </summary>
internal sealed class FixedResponseHandler : HttpMessageHandler
{
    private readonly HttpStatusCode code;
    private readonly string body;

    public int CallCount;

    /// <summary>Snapshots of every captured request body — read these instead of
    /// touching <see cref="Requests"/>.Content, which gets disposed when the caller's
    /// <c>using var req</c> falls out of scope.</summary>
    public List<string> Bodies { get; } = new();

    /// <summary>Captured URI / method / headers per request.</summary>
    public List<RequestSnapshot> Requests { get; } = new();

    public FixedResponseHandler(HttpStatusCode code, string body)
    {
        this.code = code;
        this.body = body;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        var bodyText = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(bodyText);
        Requests.Add(RequestSnapshot.From(request));
        return new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Frozen snapshot of an outgoing request, safe to read after the request is disposed.</summary>
internal sealed record RequestSnapshot(
    Uri Uri,
    string Method,
    Dictionary<string, string> Headers,
    string? AuthScheme,
    string? AuthValue)
{
    public static RequestSnapshot From(HttpRequestMessage req)
    {
        var headers = req.Headers.ToDictionary(
            h => h.Key,
            h => string.Join(",", h.Value),
            StringComparer.OrdinalIgnoreCase);
        return new RequestSnapshot(
            req.RequestUri!,
            req.Method.Method,
            headers,
            req.Headers.Authorization?.Scheme,
            req.Headers.Authorization?.Parameter);
    }
}

/// <summary>
/// Handler that throws a raw <see cref="HttpRequestException"/> with no status
/// code — i.e. simulates DNS failure / connection refused / network down.
/// </summary>
internal sealed class NetworkFailureHandler : HttpMessageHandler
{
    private readonly string message;
    public int CallCount;

    public NetworkFailureHandler(string message = "No such host is known.") => this.message = message;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        // No StatusCode set — this matches what HttpClient does on real network failure
        throw new HttpRequestException(message);
    }
}

/// <summary>
/// Handler that hangs until the caller's <see cref="CancellationToken"/> fires.
/// Useful for testing both user-cancellation and per-request timeout paths.
/// </summary>
internal sealed class HangingHandler : HttpMessageHandler
{
    public int CallCount;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return await tcs.Task;
    }
}

/// <summary>
/// Plays back a scripted sequence of (status, body) responses one at a time.
/// Once the script is exhausted, repeats the last entry.
/// </summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly List<(HttpStatusCode Code, string Body)> steps;
    public int CallCount;
    public List<string> Bodies { get; } = new();
    public List<RequestSnapshot> Requests { get; } = new();

    public ScriptedHandler(params (HttpStatusCode Code, string Body)[] steps) =>
        this.steps = new List<(HttpStatusCode, string)>(steps);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var idx = Interlocked.Increment(ref CallCount) - 1;
        var bodyText = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Bodies.Add(bodyText);
        Requests.Add(RequestSnapshot.From(request));

        var step = idx < steps.Count ? steps[idx] : steps[^1];
        return new HttpResponseMessage(step.Code)
        {
            Content = new StringContent(step.Body, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>
/// Routes responses by URL substring. Lets one client hit multiple providers
/// in a fallback chain, returning a different response per provider.
/// </summary>
internal sealed class ProviderAwareHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Code, string Body)> map = new();
    public int CallCount;
    public List<Uri> CalledUris { get; } = new();

    public void SetForUri(string uriContains, HttpStatusCode code, string body) =>
        map[uriContains] = (code, body);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref CallCount);
        CalledUris.Add(request.RequestUri!);
        var hit = map.FirstOrDefault(kv => request.RequestUri!.ToString().Contains(kv.Key));
        var chosen = hit.Key is null
            ? (HttpStatusCode.OK, "{}")
            : hit.Value;
        return Task.FromResult(new HttpResponseMessage(chosen.Item1)
        {
            Content = new StringContent(chosen.Item2, Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>
/// Scoped credential-store helper: redirects the shared credential directory
/// to a temp folder for the duration of a test, restoring on dispose.
/// </summary>
internal sealed class TempCredentialScope : IDisposable
{
    public string Directory { get; }
    private readonly string? prev;

    public TempCredentialScope()
    {
        Directory = Path.Combine(Path.GetTempPath(), "legion-test-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        prev = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", Directory);
    }

    public void WriteKey(string providerId, string key) =>
        File.WriteAllText(Path.Combine(Directory, providerId + ".key"), key);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prev);
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Resilience options that complete instantly — no real backoff, no breaker.
/// Use in tests that only care about the success/failure path, not timing.
/// </summary>
internal static class TestOptions
{
    public static LegionClientOptions Instant(int retries = 0) => new()
    {
        MaxRetries = retries,
        InitialBackoff = TimeSpan.FromMilliseconds(1),
        BackoffMultiplier = 1.0,
        CircuitBreakerThreshold = int.MaxValue,
        CircuitBreakerCooldown = TimeSpan.FromSeconds(1),
    };
}
