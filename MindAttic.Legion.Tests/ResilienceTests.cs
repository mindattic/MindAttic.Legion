using System.Net;
using System.Net.Http;
using System.Text;
using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Pins down the retry, circuit-breaker, and fallback-chain behaviour of
/// <see cref="LegionClient"/>. Covers transient-vs-non-transient
/// classification, retry exhaustion, breaker opening / resetting on success,
/// the "no resilience" preset, and multi-provider fallback.
/// </summary>
[TestFixture]
public class ResilienceTests
{
    [SetUp]
    public void SetUp() => CircuitBreaker.ResetAll();

    [TearDown]
    public void TearDown() => CircuitBreaker.ResetAll();

    private static LegionClientOptions FastRetries(int max) => new()
    {
        MaxRetries = max,
        InitialBackoff = TimeSpan.FromMilliseconds(1),
        BackoffMultiplier = 1.0,
        CircuitBreakerThreshold = 100,  // disable for retry tests
        CircuitBreakerCooldown = TimeSpan.FromSeconds(60),
    };

    [Test]
    public async Task TransientErrorThenSuccess_RetriesAndRecovers()
    {
        // First call: 503; second call: success
        var handler = new ScriptedHandler(
            (HttpStatusCode.ServiceUnavailable, "down"),
            (HttpStatusCode.OK, """{"content":[{"text":"finally"}]}"""));
        var client = new LegionClient(new HttpClient(handler), FastRetries(2));
        var reply = await client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u");
        Assert.That(reply, Is.EqualTo("finally"));
        Assert.That(handler.CallCount, Is.EqualTo(2));
    }

    [Test]
    public void TransientErrorExhausted_Throws()
    {
        var handler = new RepeatingHandler(HttpStatusCode.InternalServerError);
        var client  = new LegionClient(new HttpClient(handler), FastRetries(2));
        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("openai", "k", "gpt-4.1-mini", "s", "u"));
        Assert.That(handler.CallCount, Is.EqualTo(3)); // 1 + 2 retries
    }

    [Test]
    public void NonTransientError_DoesNotRetry()
    {
        var handler = new RepeatingHandler(HttpStatusCode.Unauthorized);
        var client  = new LegionClient(new HttpClient(handler), FastRetries(5));
        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));
        Assert.That(handler.CallCount, Is.EqualTo(1)); // no retries on auth errors
    }

    [Test]
    public void RateLimit429_IsTreatedAsTransient()
    {
        var handler = new RepeatingHandler(HttpStatusCode.TooManyRequests);
        var client  = new LegionClient(new HttpClient(handler), FastRetries(3));
        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("openai", "k", "gpt-4.1-mini", "s", "u"));
        Assert.That(handler.CallCount, Is.EqualTo(4)); // 1 + 3 retries
    }

    [Test]
    public void NoResilienceOption_RetriesNothing()
    {
        var handler = new RepeatingHandler(HttpStatusCode.InternalServerError);
        var client  = new LegionClient(new HttpClient(handler), LegionClientOptions.NoResilience);
        Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CallAsync("openai", "k", "gpt-4.1-mini", "s", "u"));
        Assert.That(handler.CallCount, Is.EqualTo(1));
    }

    [Test]
    public void CircuitBreaker_OpensAfterThreshold()
    {
        var handler = new RepeatingHandler(HttpStatusCode.InternalServerError);
        var options = new LegionClientOptions
        {
            MaxRetries = 0,
            CircuitBreakerThreshold = 3,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(5),
        };
        var client = new LegionClient(new HttpClient(handler), options);

        for (int i = 0; i < 3; i++)
            Assert.ThrowsAsync<HttpRequestException>(() =>
                client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));

        // 4th call: breaker is open, should fast-fail without hitting HTTP
        var ex = Assert.ThrowsAsync<CircuitBreakerOpenException>(() =>
            client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));
        Assert.That(ex!.ProviderId, Is.EqualTo("claude"));
        Assert.That(handler.CallCount, Is.EqualTo(3)); // 4th call never reached HTTP
    }

    [Test]
    public async Task CircuitBreaker_ResetsOnSuccess()
    {
        // Two failures, then a success, then another failure — should NOT trip
        // because consecutive count was reset by the success.
        var handler = new ScriptedHandler(
            (HttpStatusCode.InternalServerError, "fail1"),
            (HttpStatusCode.InternalServerError, "fail2"),
            (HttpStatusCode.OK, """{"content":[{"text":"recovered"}]}"""),
            (HttpStatusCode.InternalServerError, "fail3"));
        var options = new LegionClientOptions
        {
            MaxRetries = 0,
            CircuitBreakerThreshold = 3,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(5),
        };
        var client = new LegionClient(new HttpClient(handler), options);

        Assert.ThrowsAsync<HttpRequestException>(() => client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));
        Assert.ThrowsAsync<HttpRequestException>(() => client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));
        var reply = await client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u");
        Assert.That(reply, Is.EqualTo("recovered"));
        Assert.ThrowsAsync<HttpRequestException>(() => client.CallAsync("claude", "k", "claude-sonnet-4-6", "s", "u"));
        // Breaker should still be closed (fewer than 3 consecutive)
        Assert.That(CircuitBreaker.IsOpen("claude"), Is.False);
    }

    [Test]
    public async Task FallbackChain_FirstFails_SecondSucceeds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "legion-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prev = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "claude.key"), "k1");
            File.WriteAllText(Path.Combine(dir, "openai.key"), "k2");

            var handler = new ProviderAwareHandler();
            handler.SetForUri("api.anthropic.com", HttpStatusCode.InternalServerError, "down");
            handler.SetForUri("api.openai.com", HttpStatusCode.OK, """{"choices":[{"message":{"content":"backup ok"}}]}""");

            var client = new LegionClient(new HttpClient(handler), LegionClientOptions.NoResilience);
            var (id, reply) = await client.CallWithFallbackAsync(
                new[] { "claude", "openai" },
                systemPrompt: "s", userMessage: "u");

            Assert.That(id, Is.EqualTo("openai"));
            Assert.That(reply, Is.EqualTo("backup ok"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prev);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Test]
    public void FallbackChain_AllFail_ThrowsAggregate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "legion-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prev = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "claude.key"), "k");
            File.WriteAllText(Path.Combine(dir, "openai.key"), "k");

            var handler = new RepeatingHandler(HttpStatusCode.Unauthorized);
            var client  = new LegionClient(new HttpClient(handler), LegionClientOptions.NoResilience);

            var ex = Assert.ThrowsAsync<AggregateException>(() =>
                client.CallWithFallbackAsync(new[] { "claude", "openai" }, "s", "u"));
            Assert.That(ex!.InnerExceptions, Has.Count.EqualTo(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prev);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // ── handlers ────────────────────────────────────────────────────────────────

    /// <summary>Always replies with the same status code; counts every call.</summary>
    internal sealed class RepeatingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode code;
        /// <summary>Number of requests this handler has received.</summary>
        public int CallCount;
        /// <summary>Constructs a handler that always returns <paramref name="code"/>.</summary>
        public RepeatingHandler(HttpStatusCode code) => this.code = code;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("err") });
        }
    }

    /// <summary>
    /// Returns each scripted (status, body) step in order, then keeps replaying
    /// the last step (with body "exhausted" if no steps were provided).
    /// </summary>
    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Code, string Body)> script;
        /// <summary>Number of requests this handler has received.</summary>
        public int CallCount;
        /// <summary>Constructs a handler with the supplied response sequence.</summary>
        public ScriptedHandler(params (HttpStatusCode Code, string Body)[] steps) =>
            script = new Queue<(HttpStatusCode Code, string Body)>(steps);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Interlocked.Increment(ref CallCount);
            (HttpStatusCode Code, string Body) step = script.Count > 0
                ? script.Dequeue()
                : (HttpStatusCode.InternalServerError, "exhausted");
            return Task.FromResult(new HttpResponseMessage(step.Code)
            {
                Content = new StringContent(step.Body, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// Routes responses by URL substring match — lets one HttpClient simulate
    /// multiple providers behaving differently in the same fallback chain.
    /// </summary>
    internal sealed class ProviderAwareHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Code, string Body)> map = new();
        /// <summary>Configure the response when the request URI contains <paramref name="contains"/>.</summary>
        public void SetForUri(string contains, HttpStatusCode code, string body) =>
            map[contains] = (code, body);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            var hit = map.FirstOrDefault(kv => r.RequestUri!.ToString().Contains(kv.Key));
            (HttpStatusCode Code, string Body) chosen = hit.Key is null ? (HttpStatusCode.OK, "{}") : hit.Value;
            return Task.FromResult(new HttpResponseMessage(chosen.Code)
            {
                Content = new StringContent(chosen.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
