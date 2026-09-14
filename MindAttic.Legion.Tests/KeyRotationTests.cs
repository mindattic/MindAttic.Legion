using System.Net;
using System.Text;
using MindAttic.Legion.Tests.TestSupport;
using MindAttic.Vault.Credentials;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Pins down the multi-key "sticky failover" behaviour of the shared-credential
/// overloads (<see cref="LegionClient.CallAsync(string,string,string,int,double,string?,CancellationToken,string?,bool)"/>,
/// <see cref="LegionClient.CallChatAsync(string,IEnumerable{ChatTurn},string?,int,double,string?,CancellationToken)"/>)
/// when a provider has more than one key configured in the shared credential store.
/// </summary>
[TestFixture]
public class KeyRotationTests
{
    [SetUp]
    public void SetUp() => CircuitBreaker.ResetAll();

    [TearDown]
    public void TearDown() => CircuitBreaker.ResetAll();

    private static LegionClientOptions FastOptions() => new()
    {
        MaxRetries = 0,
        InitialBackoff = TimeSpan.FromMilliseconds(1),
        BackoffMultiplier = 1.0,
        CircuitBreakerThreshold = 100, // disable — this suite is about key selection, not the breaker threshold
        CircuitBreakerCooldown = TimeSpan.FromSeconds(60),
    };

    private static void WriteKeyPool(string directory, string providerId, params string[] keys)
    {
        var store = new LlmCredentialStore(directory);
        store.SetKeys(providerId, keys.Select(k => new CredentialPoolEntry(k)).ToList());
    }

    [Test]
    public async Task FirstKeyUnauthorized_FailsOverToSecondKey()
    {
        using var scope = new TempCredentialScope();
        WriteKeyPool(scope.Directory, "claude", "sk-bad", "sk-good");

        var handler = new KeyAwareHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["sk-bad"] = (HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody),
            ["sk-good"] = (HttpStatusCode.OK, Bodies.ClaudeOk),
        });
        var client = new LegionClient(new HttpClient(handler), FastOptions());

        var reply = await client.CallAsync("claude", "s", "u");

        Assert.That(reply, Is.EqualTo("Hello World!"));
        Assert.That(handler.CallCountFor("sk-bad"), Is.EqualTo(1));
        Assert.That(handler.CallCountFor("sk-good"), Is.EqualTo(1));
    }

    [Test]
    public async Task FirstKeyRateLimited_FailsOverToSecondKey_ChatOverload()
    {
        using var scope = new TempCredentialScope();
        WriteKeyPool(scope.Directory, "claude", "sk-limited", "sk-good");

        var handler = new KeyAwareHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["sk-limited"] = (HttpStatusCode.TooManyRequests, "rate limited"),
            ["sk-good"] = (HttpStatusCode.OK, Bodies.ClaudeOk),
        });
        var client = new LegionClient(new HttpClient(handler), FastOptions());

        var reply = await client.CallChatAsync("claude", new[] { new ChatTurn("user", "hi") });

        Assert.That(reply, Is.EqualTo("Hello World!"));
        Assert.That(handler.CallCountFor("sk-limited"), Is.EqualTo(1));
        Assert.That(handler.CallCountFor("sk-good"), Is.EqualTo(1));
    }

    [Test]
    public void EveryKeyFails_ThrowsTheLastKeysException()
    {
        using var scope = new TempCredentialScope();
        WriteKeyPool(scope.Directory, "claude", "sk-a", "sk-b");

        var handler = new KeyAwareHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["sk-a"] = (HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody),
            ["sk-b"] = (HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody),
        });
        var client = new LegionClient(new HttpClient(handler), FastOptions());

        Assert.ThrowsAsync<HttpRequestException>(() => client.CallAsync("claude", "s", "u"));
        Assert.That(handler.CallCountFor("sk-a"), Is.EqualTo(1));
        Assert.That(handler.CallCountFor("sk-b"), Is.EqualTo(1));
    }

    [Test]
    public async Task SingleKey_UsesPlainProviderIdBreakerBucket_UnchangedFromBeforeThisFeature()
    {
        // A provider with exactly one key must not gain a per-key breaker bucket —
        // existing health-check/diagnostic code pre-trips or inspects the breaker
        // by the plain provider id.
        using var scope = new TempCredentialScope();
        scope.WriteKey("claude", "sk-only");
        CircuitBreaker.RecordFailure("claude", threshold: 1, cooldown: TimeSpan.FromMinutes(5));

        var handler = new KeyAwareHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["sk-only"] = (HttpStatusCode.OK, Bodies.ClaudeOk),
        });
        var client = new LegionClient(new HttpClient(handler), FastOptions());

        Assert.ThrowsAsync<CircuitBreakerOpenException>(() => client.CallAsync("claude", "s", "u"));
        Assert.That(handler.CallCountFor("sk-only"), Is.EqualTo(0), "the open provider-level breaker must fast-fail before any HTTP call");
    }

    [Test]
    public async Task OneBadKeyTrippingItsOwnBreaker_DoesNotBlockTheOtherKey()
    {
        using var scope = new TempCredentialScope();
        WriteKeyPool(scope.Directory, "claude", "sk-bad", "sk-good");

        var handler = new KeyAwareHandler(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["sk-bad"] = (HttpStatusCode.InternalServerError, Bodies.ServerErrorBody),
            ["sk-good"] = (HttpStatusCode.OK, Bodies.ClaudeOk),
        });
        var options = new LegionClientOptions
        {
            MaxRetries = 0,
            CircuitBreakerThreshold = 1,
            CircuitBreakerCooldown = TimeSpan.FromMinutes(5),
        };
        var client = new LegionClient(new HttpClient(handler), options);

        // Two calls in a row: each one fails over from sk-bad to sk-good and succeeds.
        // sk-bad's own breaker bucket trips after the first failure, but sk-good keeps working.
        for (var i = 0; i < 2; i++)
        {
            var reply = await client.CallAsync("claude", "s", "u");
            Assert.That(reply, Is.EqualTo("Hello World!"));
        }

        // sk-bad was actually dispatched only once — its second attempt should have been
        // fast-failed by its own (now-open) per-key breaker bucket rather than hitting HTTP,
        // while sk-good's bucket never tripped and kept succeeding both times.
        Assert.That(handler.CallCountFor("sk-bad"), Is.EqualTo(1));
        Assert.That(handler.CallCountFor("sk-good"), Is.EqualTo(2));
    }

    /// <summary>Routes responses by the incoming <c>x-api-key</c> header value; counts calls per key.</summary>
    private sealed class KeyAwareHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Code, string Body)> byKey;
        private readonly Dictionary<string, int> counts = new();

        public KeyAwareHandler(Dictionary<string, (HttpStatusCode, string)> byKey) => this.byKey = byKey;

        public int CallCountFor(string key) => counts.GetValueOrDefault(key, 0);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = request.Headers.TryGetValues("x-api-key", out var values) ? values.First() : "";
            counts[key] = counts.GetValueOrDefault(key, 0) + 1;

            var (code, body) = byKey.TryGetValue(key, out var response)
                ? response
                : (HttpStatusCode.InternalServerError, "unrecognized key in test handler");
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
