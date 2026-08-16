using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Static checks on <see cref="LlmProviderCatalog"/>: provider count, lookup
/// case sensitivity, every provider carries the metadata Legion needs (default
/// model in the available list, https URLs, distinct dashboard/keys URLs), and
/// model recognition is case-insensitive.
/// </summary>
[TestFixture]
public class LlmProviderCatalogTests
{
    [Test]
    public void All_HasExpectedProviderCount()
    {
        Assert.That(LlmProviderCatalog.All, Has.Count.EqualTo(14));
    }

    [Test]
    public void All_KnownIds()
    {
        var expected = new[]
        {
            "claude-api","claude-team","openai","gemini","deepseek","mistral",
            "xai","groq","together","openrouter","fireworks","cohere","kimi","perplexity",
        };
        var actual = LlmProviderCatalog.AllIds.ToArray();
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public void Get_ByLowercaseId_ReturnsProvider()
    {
        Assert.That(LlmProviderCatalog.Get("claude-api"), Is.Not.Null);
    }

    [Test]
    public void Get_IsCaseInsensitive()
    {
        Assert.That(LlmProviderCatalog.Get("Claude-Api")?.Id, Is.EqualTo("claude-api"));
        Assert.That(LlmProviderCatalog.Get("CLAUDE-API")?.Id, Is.EqualTo("claude-api"));
    }

    [Test]
    public void Get_TrimsWhitespace()
    {
        Assert.That(LlmProviderCatalog.Get("  openai  ")?.Id, Is.EqualTo("openai"));
    }

    [Test]
    public void Get_UnknownReturnsNull()
    {
        Assert.That(LlmProviderCatalog.Get("madeup"), Is.Null);
        Assert.That(LlmProviderCatalog.Get(""), Is.Null);
        Assert.That(LlmProviderCatalog.Get(null!), Is.Null);
    }

    [Test]
    public void IsSupported_TruthTable()
    {
        Assert.That(LlmProviderCatalog.IsSupported("claude-api"), Is.True);
        Assert.That(LlmProviderCatalog.IsSupported("madeup"), Is.False);
    }

    [Test]
    public void EveryProvider_HasNonEmptyMetadata()
    {
        foreach (var p in LlmProviderCatalog.All)
        {
            Assert.That(p.Id,           Is.Not.Empty, $"{p.Id}.Id");
            Assert.That(p.DisplayName,  Is.Not.Empty, $"{p.Id}.DisplayName");
            Assert.That(p.Vendor,       Is.Not.Empty, $"{p.Id}.Vendor");
            Assert.That(p.DefaultModel, Is.Not.Empty, $"{p.Id}.DefaultModel");
            Assert.That(p.DashboardUrl, Does.StartWith("https://"), $"{p.Id}.DashboardUrl");
            // claude-team is OAuth-only — it has no key-creation URL by design.
            if (!string.Equals(p.Id, "claude-team", StringComparison.OrdinalIgnoreCase))
                Assert.That(p.KeysUrl, Does.StartWith("https://"), $"{p.Id}.KeysUrl");
            Assert.That(p.AvailableModels, Is.Not.Empty, $"{p.Id}.AvailableModels");
        }
    }

    [Test]
    public void EveryProvider_DefaultModelIsListedAsAvailable()
    {
        foreach (var p in LlmProviderCatalog.All)
            Assert.That(p.AvailableModels, Does.Contain(p.DefaultModel),
                $"{p.Id}: DefaultModel '{p.DefaultModel}' must be in AvailableModels");
    }

    [Test]
    public void IsKnownModel_RecognizesCatalogModels()
    {
        Assert.That(LlmProviderCatalog.IsKnownModel("claude-api", "claude-sonnet-4-6"), Is.True);
        Assert.That(LlmProviderCatalog.IsKnownModel("openai", "gpt-4.1-mini"), Is.True);
        Assert.That(LlmProviderCatalog.IsKnownModel("openai", "GPT-4.1-MINI"), Is.True); // case-insensitive
    }

    [Test]
    public void IsKnownModel_RejectsUnknown()
    {
        Assert.That(LlmProviderCatalog.IsKnownModel("claude-api", "made-up-model"), Is.False);
        Assert.That(LlmProviderCatalog.IsKnownModel("madeup", "anything"), Is.False);
        Assert.That(LlmProviderCatalog.IsKnownModel("claude-api", ""), Is.False);
    }

    [Test]
    public void Models_AreAllUniqueWithinAProvider()
    {
        foreach (var p in LlmProviderCatalog.All)
        {
            var unique = p.AvailableModels.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            Assert.That(unique, Is.EqualTo(p.AvailableModels.Count),
                $"{p.Id}: AvailableModels has duplicates");
        }
    }

    [Test]
    public void DashboardAndKeysUrls_AreDistinct()
    {
        // Distinct URLs ensures the catalog can route users to monitoring vs. key-creation
        // pages independently. (Some providers happen to use the same root path for both,
        // but the configured URLs in the catalog should be distinct query targets.)
        foreach (var p in LlmProviderCatalog.All)
            Assert.That(p.DashboardUrl, Is.Not.EqualTo(p.KeysUrl), $"{p.Id}: dashboard and keys URLs collide");
    }
}
