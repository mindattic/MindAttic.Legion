using System.Net;
using System.Text;
using MindAttic.Legion.Tests.TestSupport;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// LAMP (M.LEGN-T44): Legion resolves keys from MindAttic Vault's shared named lists (the host app's assignment,
/// aliases merged, disabled/failed keys skipped) and reports each key's outcome to Vault's shared key health.
/// </summary>
[TestFixture]
[NonParallelizable]
public class LampKeyTests
{
    private string root = "";
    private readonly Dictionary<string, string?> saved = new();
    private NamedKeyStore Keys => NamedKeyStore.ForRoamingRoot();
    private KeyHealthStore Health => KeyHealthStore.ForLocalRoot();

    [SetUp]
    public void SetUp()
    {
        CircuitBreaker.ResetAll();
        root = Path.Combine(Path.GetTempPath(), "legion-lamp-" + Guid.NewGuid().ToString("N"));
        foreach (var v in new[] { VaultPaths.RoamingRootEnvVar, VaultPaths.LocalRootEnvVar, LlmCredentialStore.DirectoryEnvVar })
            saved[v] = Environment.GetEnvironmentVariable(v);
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Path.Combine(root, "roaming"));
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(root, "local"));
        Environment.SetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar, null);
        MindAtticCredentialStore.AppId = null;
        MindAtticCredentialStore.UseConfiguration(null);
    }

    [TearDown]
    public void TearDown()
    {
        CircuitBreaker.ResetAll();
        MindAtticCredentialStore.AppId = null;
        foreach (var (k, v) in saved) Environment.SetEnvironmentVariable(k, v);
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private static LegionClientOptions FastOptions() => new()
    {
        MaxRetries = 0,
        InitialBackoff = TimeSpan.FromMilliseconds(1),
        BackoffMultiplier = 1.0,
        CircuitBreakerThreshold = 100,
        CircuitBreakerCooldown = TimeSpan.FromSeconds(60),
    };

    [Test]
    public void KeysFollowTheHostAppsAssignment_ElseTheSharedOrder()
    {
        Keys.Add("claude", "shared", "sk-shared");
        Keys.Add("claude", "thinktank", "sk-tt");
        Keys.Assignments.Set("ThinkTank", "claude", ["thinktank"]);
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("sk-shared"));
        MindAtticCredentialStore.AppId = "ThinkTank";
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("sk-tt"));
        Assert.That(MindAtticCredentialStore.GetKeys("claude").Select(k => k.Label), Is.EqualTo(new[] { "thinktank" }));
    }

    [Test]
    public void AliasesMergeIntoOneList()
    {
        var llm = new LlmCredentialStore(Path.Combine(root, "roaming", "LLM"));
        llm.SetKey("xai", "x-1");
        Assert.That(MindAtticCredentialStore.GetKey("grok"), Is.EqualTo("x-1"));
        Assert.That(MindAtticCredentialStore.GetKey("xai"), Is.EqualTo("x-1"));
    }

    [Test]
    public void DisabledAndFailedKeysAreSkipped()
    {
        Keys.Add("openai", "a", "sk-a");
        Keys.Add("openai", "b", "sk-b");
        Keys.Add("openai", "c", "sk-c");
        Keys.SetDisabled("openai", "a", true);
        Health.RecordFailure("openai", "b", KeyFailureKind.Auth, DateTimeOffset.UtcNow);
        Assert.That(MindAtticCredentialStore.GetKeys("openai").Select(k => k.Key), Is.EqualTo(new[] { "sk-c" }));
    }

    [Test]
    public async Task AFailingKeyFailsOverAndIsMarkedFailedForEveryApp()
    {
        Keys.Add("claude", "bad", "sk-bad");
        Keys.Add("claude", "good", "sk-good");
        var handler = new KeyAwareHandler(new()
        {
            ["sk-bad"] = (HttpStatusCode.Unauthorized, Bodies.AuthInvalidBody),
            ["sk-good"] = (HttpStatusCode.OK, Bodies.ClaudeOk),
        });
        var reply = await new LegionClient(new HttpClient(handler), FastOptions()).CallAsync("claude", "s", "u");

        Assert.That(reply, Is.EqualTo("Hello World!"));
        Assert.That(Health.Get("claude", "bad").Failed, Is.True);
        Assert.That(Health.Get("claude", "good").StatusAt(DateTimeOffset.UtcNow, false), Is.EqualTo(KeyStatus.Healthy));
        Assert.That(MindAtticCredentialStore.GetKeys("claude").Select(k => k.Key), Is.EqualTo(new[] { "sk-good" }), "the failed key is skipped next time");
    }

    [Test]
    public async Task ARateLimitedKeyCoolsDownInsteadOfFailing()
    {
        Keys.Add("claude", "busy", "sk-busy");
        Keys.Add("claude", "good", "sk-good");
        var handler = new KeyAwareHandler(new()
        {
            ["sk-busy"] = (HttpStatusCode.TooManyRequests, "rate limited"),
            ["sk-good"] = (HttpStatusCode.OK, Bodies.ClaudeOk),
        });
        await new LegionClient(new HttpClient(handler), FastOptions()).CallAsync("claude", "s", "u");
        var h = Health.Get("claude", "busy");
        Assert.That((h.Failed, h.CoolDownUntilUtc > DateTimeOffset.UtcNow), Is.EqualTo((false, true)));
    }

    [Test]
    public void TheTestOverrideFolderStillWorks()
    {
        using var scope = new TempCredentialScope();
        new LlmCredentialStore(scope.Directory).SetKey("claude", "sk-folder");
        Assert.That(MindAtticCredentialStore.GetKey("claude"), Is.EqualTo("sk-folder"));
    }

    private sealed class KeyAwareHandler(Dictionary<string, (HttpStatusCode Code, string Body)> byKey) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = request.Headers.TryGetValues("x-api-key", out var values) ? values.First() : "";
            var (code, body) = byKey.TryGetValue(key, out var r) ? r : (HttpStatusCode.InternalServerError, "unknown key");
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
