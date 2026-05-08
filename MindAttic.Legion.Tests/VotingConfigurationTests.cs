using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="VotingConfiguration.ActiveProviderIds"/> — the
/// gate that decides which providers actually generate voters. Mistakes here
/// silently change the panel composition (a non-trusted provider sneaking
/// in, or a trusted one failing to light up), so this surface deserves
/// dedicated coverage independent of the integration-level
/// <see cref="LlmVotingServiceTests"/> suite.
/// </summary>
[TestFixture]
public class VotingConfigurationTests
{
    private string tempDir = "";
    private string? prevEnv;

    [SetUp]
    public void SetUp()
    {
        tempDir  = Path.Combine(Path.GetTempPath(), "legion-cfg-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        prevEnv  = Environment.GetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS");
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("MINDATTIC_LLM_CREDENTIALS", prevEnv);
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    // ── ActiveProviderIds: explicit ApiKeys ────────────────────────────────

    [Test]
    public void ActiveProviderIds_ExplicitKeys_AreActiveWhenAllowedSetEmpty()
    {
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            AllowedProviderIds   = new(),
            ApiKeys              = { ["claude"] = "k1", ["openai"] = "k2" },
        };
        Assert.That(cfg.ActiveProviderIds, Is.EquivalentTo(new[] { "claude", "openai" }));
    }

    [Test]
    public void ActiveProviderIds_BlankKey_IsNotActive()
    {
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            AllowedProviderIds   = new(),
            ApiKeys              = { ["claude"] = "k1", ["openai"] = "  " },
        };
        Assert.That(cfg.ActiveProviderIds, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void ActiveProviderIds_NoKeys_ReturnsEmpty()
    {
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            AllowedProviderIds   = new(),
        };
        Assert.That(cfg.ActiveProviderIds, Is.Empty);
    }

    // ── ActiveProviderIds: AllowedProviderIds whitelist ────────────────────

    [Test]
    public void ActiveProviderIds_AllowedSet_NarrowsToWhitelist()
    {
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            AllowedProviderIds   = new(StringComparer.OrdinalIgnoreCase) { "claude" },
            ApiKeys              =
            {
                ["claude"]   = "k1",
                ["openai"]   = "k2",
                ["deepseek"] = "k3",
            },
        };
        Assert.That(cfg.ActiveProviderIds, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void ActiveProviderIds_DefaultAllowedSet_IsTheTrustedFour()
    {
        // The default whitelist should be exactly the four trusted providers.
        // A regression here would silently let untrusted providers join the
        // panel for any caller that doesn't override AllowedProviderIds.
        Assert.That(new VotingConfiguration().AllowedProviderIds,
            Is.EquivalentTo(new[] { "claude", "openai", "gemini", "deepseek" }));
    }

    [Test]
    public void ActiveProviderIds_KeyForUntrustedProvider_IsFilteredOut()
    {
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            // Default AllowedProviderIds = trusted four
            ApiKeys              =
            {
                ["claude"]  = "k1",
                ["mistral"] = "k2", // untrusted; should not become active
            },
        };
        Assert.That(cfg.ActiveProviderIds, Is.EquivalentTo(new[] { "claude" }));
    }

    // ── ActiveProviderIds: shared credentials + explicit keys interaction ──

    [Test]
    public void ActiveProviderIds_SharedCredentialsDisabled_OnlyExplicitKeysCount()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "from-store");
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = false,
            AllowedProviderIds   = new(),
            ApiKeys              = { ["openai"] = "explicit" },
        };
        Assert.That(cfg.ActiveProviderIds, Is.EquivalentTo(new[] { "openai" }));
    }

    [Test]
    public void ActiveProviderIds_SharedCredentialsEnabled_StoreContributes()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "from-store");
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = true,
            AllowedProviderIds   = new(),
            ApiKeys              = { ["openai"] = "explicit" },
        };
        Assert.That(cfg.ActiveProviderIds, Is.EquivalentTo(new[] { "claude", "openai" }));
    }

    [Test]
    public void ActiveProviderIds_DedupesProvidersDeclaredInBothSources()
    {
        File.WriteAllText(Path.Combine(tempDir, "claude.key"), "from-store");
        var cfg = new VotingConfiguration
        {
            UseSharedCredentials = true,
            AllowedProviderIds   = new(),
            ApiKeys              = { ["claude"] = "from-explicit" },
        };
        // Claude shouldn't appear twice just because both sources name it.
        Assert.That(cfg.ActiveProviderIds.Count(id => id == "claude"), Is.EqualTo(1));
    }

    // ── Defaults sanity ────────────────────────────────────────────────────

    [Test]
    public void Defaults_AreReasonable()
    {
        var cfg = new VotingConfiguration();
        Assert.That(cfg.UseSharedCredentials, Is.True,         "UseSharedCredentials default");
        Assert.That(cfg.JudgeProviderId,      Is.EqualTo("claude"), "JudgeProviderId default");
        Assert.That(cfg.ProviderTimeout,      Is.EqualTo(TimeSpan.FromMinutes(2)), "ProviderTimeout default");
        Assert.That(cfg.DefaultMaxTokens,     Is.EqualTo(2048),    "DefaultMaxTokens default");
        Assert.That(cfg.DefaultPersonalityMarkdown, Is.Empty,      "DefaultPersonalityMarkdown default");
    }
}
