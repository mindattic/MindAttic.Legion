using MindAttic.Legion;
using MindAttic.Legion.Cli;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="TiersCommand"/>'s pure helpers — provider
/// resolution, truncation, help-flag detection. The live probe matrix
/// (<see cref="TiersCommand.ProbeMatrixAsync"/>) is exercised end-to-end
/// when a developer runs <c>legion tiers</c>; pinning it here would
/// require an HTTP mock harness that doesn't add value over the wire-
/// shape tests in <see cref="LegionClientWireTests"/>.
/// </summary>
[TestFixture]
public class TiersCommandTests
{
    [Test]
    public void TrustedProviderIds_MatchAskCommandTrustList()
    {
        // The two commands must agree on the trust list — the user's stated
        // rule is that legion ask and legion tiers both probe exactly the
        // same four providers, never widening on either side.
        Assert.That(TiersCommand.TrustedProviderIds,
            Is.EquivalentTo(AskCommand.TrustedProviderIds));
    }

    [Test]
    public void DefaultTiers_AreLowMediumHigh()
    {
        // Higher/Highest are excluded from the default sweep because they
        // collapse onto High in every trusted provider's catalog mapping;
        // probing them adds noise without information. --all-tiers opts in
        // when a developer wants every tier exercised explicitly.
        Assert.That(TiersCommand.DefaultTiers,
            Is.EquivalentTo(new[] { ModelTier.Low, ModelTier.Medium, ModelTier.High }));
    }

    [Test]
    public void ResolveProviders_NullRequest_ReturnsFullTrustedSet()
    {
        var result = TiersCommand.ResolveProviders(null);
        Assert.That(result, Is.EquivalentTo(TiersCommand.TrustedProviderIds));
    }

    [Test]
    public void ResolveProviders_EmptyRequest_ReturnsFullTrustedSet()
    {
        var result = TiersCommand.ResolveProviders(Array.Empty<string>());
        Assert.That(result, Is.EquivalentTo(TiersCommand.TrustedProviderIds));
    }

    [Test]
    public void ResolveProviders_NarrowsToRequestedSubset()
    {
        var result = TiersCommand.ResolveProviders(new[] { "claude", "openai" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude", "openai" }));
    }

    [Test]
    public void ResolveProviders_DropsUntrustedIds()
    {
        // Same security model as AskCommand: untrusted ids are silently
        // dropped, never widening the probe set.
        var result = TiersCommand.ResolveProviders(new[] { "claude", "mistral", "ollama" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void ResolveProviders_AllUntrusted_ReturnsEmpty()
    {
        var result = TiersCommand.ResolveProviders(new[] { "mistral", "groq", "xai" });
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ResolveProviders_CaseInsensitive()
    {
        var result = TiersCommand.ResolveProviders(new[] { "CLAUDE", "OpenAI" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude", "openai" }));
    }

    [Test]
    public void ResolveProviders_DeduplicatesRepeats()
    {
        // A user passing --providers claude,claude,CLAUDE shouldn't get
        // three identical probes back — that's just wasted API spend.
        var result = TiersCommand.ResolveProviders(new[] { "claude", "claude", "CLAUDE" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void Truncate_ShortString_ReturnsUnchanged()
    {
        Assert.That(TiersCommand.Truncate("OK", 50), Is.EqualTo("OK"));
    }

    [Test]
    public void Truncate_OverCap_AppendsEllipsis()
    {
        var s = new string('x', 100);
        var result = TiersCommand.Truncate(s, 50);
        Assert.That(result.Length, Is.EqualTo(50));
        Assert.That(result, Does.EndWith("…"));
    }

    [Test]
    public void Truncate_FlattensNewlines()
    {
        // Probe replies and error messages occasionally span lines; the
        // table cell expects a single line, so newlines are flattened
        // before length is measured.
        Assert.That(TiersCommand.Truncate("line1\nline2", 50), Is.EqualTo("line1 line2"));
        Assert.That(TiersCommand.Truncate("line1\r\nline2", 50), Is.EqualTo("line1  line2"));
    }

    [Test]
    public void IsHelp_RecognizesEveryHelpFlag()
    {
        Assert.That(TiersCommand.IsHelp("-h"),     Is.True);
        Assert.That(TiersCommand.IsHelp("--help"), Is.True);
        Assert.That(TiersCommand.IsHelp("help"),   Is.True);
        Assert.That(TiersCommand.IsHelp("/?"),     Is.True);
    }

    [Test]
    public void IsHelp_RejectsNonHelpFlags()
    {
        Assert.That(TiersCommand.IsHelp("--json"),    Is.False);
        Assert.That(TiersCommand.IsHelp("tiers"),     Is.False);
        Assert.That(TiersCommand.IsHelp(""),          Is.False);
    }
}
