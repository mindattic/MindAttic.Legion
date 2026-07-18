using Microsoft.Extensions.Configuration;
using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for the Phase B IConfiguration hook on
/// <see cref="MindAtticCredentialStore"/>. Pins down precedence between the
/// IConfiguration source (User Secrets / App Service Application Settings /
/// Azure Key Vault when a host has composed them) and the legacy file-backed
/// store at <c>%APPDATA%/MindAttic/LLM/providers.json</c>.
///
/// <para>Each test redirects the file-store directory via
/// <c>MINDATTIC_LLM_CREDENTIALS</c> to a fresh temp dir AND clears the static
/// IConfiguration registration in <see cref="TearDown"/> so leakage between
/// tests is impossible.</para>
/// </summary>
[TestFixture]
public class MindAtticCredentialStoreConfigurationTests
{
    private string tempDir = "";
    private string? prevEnv;

    [SetUp]
    public void SetUp()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "legion-credstore-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        prevEnv = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        // Critical: clear the static registration so the next test starts clean.
        MindAtticCredentialStore.UseConfiguration(null);
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prevEnv);
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    private static IConfiguration BuildConfig(params (string key, string value)[] entries)
    {
        var dict = entries.ToDictionary(e => e.key, e => (string?)e.value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    // ── No-config fallback (legacy parity) ────────────────────────────────────

    [Test]
    public void NoConfigRegistered_GetKey_ReadsFromFileStore()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-key");
        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("file-key"));
    }

    [Test]
    public void NoConfigRegistered_GetKey_ReturnsNullWhenAbsent()
    {
        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.Null);
    }

    // ── Config-first precedence ───────────────────────────────────────────────

    [Test]
    public void ConfigRegistered_WinsOverFileStore()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-key");
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "config-key")));

        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("config-key"));
    }

    [Test]
    public void ConfigRegistered_FallsThroughToFile_WhenKeyAbsentInConfig()
    {
        MindAtticCredentialStore.SetKey("openai", "file-only");
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "config-key")));

        Assert.That(MindAtticCredentialStore.GetKey("openai"), Is.EqualTo("file-only"));
    }

    [Test]
    public void ConfigRegistered_FallsThroughToFile_WhenConfigValueIsWhitespace()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-key");
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "   ")));

        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("file-key"));
    }

    [Test]
    public void ConfigRegistered_TrimsConfigValue()
    {
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "  trimmed-key  ")));

        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("trimmed-key"));
    }

    // ── Reset semantics ───────────────────────────────────────────────────────

    [Test]
    public void UseConfigurationNull_RevertsToFileOnly()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-key");
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "config-key")));
        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("config-key"));

        MindAtticCredentialStore.UseConfiguration(null);
        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("file-key"));
    }

    [Test]
    public void UseConfiguration_IsIdempotent_AndLatestWins()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-key");
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "first-config")));
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "second-config")));

        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("second-config"));
    }

    // ── Env-var redirection still re-evaluated per call ───────────────────────

    [Test]
    public void EnvVarRedirection_StillReevaluated_WithConfigRegistered()
    {
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:openai:apiKey", "config-openai")));

        // Initial dir has a claude file key, no openai.
        MindAtticCredentialStore.SetKey("claude-api", "first-dir-claude");
        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("first-dir-claude"));

        // Swap to a fresh dir mid-test — the facade must pick it up on next call.
        var second = Path.Combine(Path.GetTempPath(), "legion-credstore-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(second);
        try
        {
            Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", second);

            // claude no longer in the file store (new dir is empty), and not in config either.
            Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.Null);

            // openai still wins from config regardless of which dir is active.
            Assert.That(MindAtticCredentialStore.GetKey("openai"), Is.EqualTo("config-openai"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", tempDir);
            try { Directory.Delete(second, recursive: true); } catch { }
        }
    }

    // ── Writes always land in the file store ──────────────────────────────────

    [Test]
    public void SetKey_WithConfigRegistered_WritesToFileStore_NotConfig()
    {
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "config-key")));

        // Write through the facade. Config view is read-only, so this lands on disk.
        MindAtticCredentialStore.SetKey("openai", "newly-written");

        var expectedPath = Path.Combine(tempDir, "providers.json");
        Assert.That(File.Exists(expectedPath), Is.True, "providers.json should have been created in the writable file store");
        var raw = File.ReadAllText(expectedPath);
        Assert.That(raw, Does.Contain("openai"));
        Assert.That(raw, Does.Contain("newly-written"));

        // And reads still observe both layers correctly.
        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("config-key"),    "config still wins for claude");
        Assert.That(MindAtticCredentialStore.GetKey("openai"), Is.EqualTo("newly-written"), "file holds the new openai key");
    }

    [Test]
    public void SetKey_WithConfigRegistered_DoesNotMutateConfig()
    {
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "config-key")));

        // Writing claude through the facade should hit the file store but
        // NOT shadow or overwrite the config view; reads must still show
        // the config value (config wins).
        MindAtticCredentialStore.SetKey("claude-api", "file-attempt");

        Assert.That(MindAtticCredentialStore.GetKey("claude-api"), Is.EqualTo("config-key"));
    }

    // ── Aggregate views (LoadAll / ListProviders / LoadAllRaw) ────────────────

    [Test]
    public void LoadAll_MergesAcrossLayers_ConfigWins()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-claude");
        MindAtticCredentialStore.SetKey("gemini", "file-gemini");
        MindAtticCredentialStore.UseConfiguration(BuildConfig(
            ("MindAttic:Vault:LLM:claude-api:apiKey", "config-claude"),
            ("MindAttic:Vault:LLM:openai:apiKey", "config-openai")));

        var all = MindAtticCredentialStore.LoadAll();
        Assert.Multiple(() =>
        {
            Assert.That(all["claude-api"], Is.EqualTo("config-claude"), "config overrides file for claude");
            Assert.That(all["gemini"], Is.EqualTo("file-gemini"),   "file-only providers survive");
            Assert.That(all["openai"], Is.EqualTo("config-openai"), "config-only providers surface");
        });
    }

    [Test]
    public void ListProviders_IncludesUnionAcrossLayers()
    {
        MindAtticCredentialStore.SetKey("gemini", "file-gemini");
        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:openai:apiKey", "config-openai")));

        Assert.That(
            MindAtticCredentialStore.ListProviders(),
            Is.EquivalentTo(new[] { "gemini", "openai" }));
    }

    [Test]
    public void ProvidersFileExists_TrueWhenConfigHasChildren_EvenIfFileMissing()
    {
        // tempDir is empty — no providers.json on disk.
        Assert.That(MindAtticCredentialStore.ProvidersFileExists(), Is.False, "sanity: no file yet");

        MindAtticCredentialStore.UseConfiguration(
            BuildConfig(("MindAttic:Vault:LLM:claude-api:apiKey", "config-key")));

        Assert.That(MindAtticCredentialStore.ProvidersFileExists(), Is.True);
    }

    [Test]
    public void LoadAllRaw_MergesShapedJson_ConfigWins()
    {
        MindAtticCredentialStore.SetKey("claude-api", "file-claude");
        MindAtticCredentialStore.UseConfiguration(BuildConfig(
            ("MindAttic:Vault:LLM:claude-api:type",      "anthropic"),
            ("MindAttic:Vault:LLM:claude-api:apiKey",    "config-claude"),
            ("MindAttic:Vault:LLM:claude-api:model",     "claude-sonnet-4-6"),
            ("MindAttic:Vault:LLM:claude-api:maxTokens", "2048")));

        var raw = MindAtticCredentialStore.LoadAllRaw();
        Assert.That(raw.ContainsKey("claude-api"), Is.True);

        var json = raw["claude-api"];
        // Config view serialises children as a nested JSON object.
        Assert.That(json, Does.Contain("config-claude"));
        Assert.That(json, Does.Contain("claude-sonnet-4-6"));
        Assert.That(json, Does.Not.Contain("file-claude"), "config layer wins over file in the merged raw view");
    }

    // ── No leakage: a test that does NOT register config sees pristine state ──

    [Test]
    public void TearDown_ClearsConfigRegistration_NoCrossTestLeakage()
    {
        // This test relies on TearDown of preceding tests having cleared config.
        // If state leaked, file-only behaviour would be silently overridden.
        MindAtticCredentialStore.SetKey("xai", "leak-canary");
        Assert.That(MindAtticCredentialStore.GetKey("xai"), Is.EqualTo("leak-canary"));
    }
}
