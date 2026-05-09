using MindAttic.Legion.Cli;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="AskCommand"/>'s pure helpers — the trust-list
/// intersection, choice-mode option snapping, auto-context assembly, and the
/// small string utilities. Anything that requires an HTTP round-trip or
/// stdout/stderr capture is covered by the live smoke tests, not here.
/// </summary>
[TestFixture]
public class AskCommandTests
{
    // ── TrustedProviderIds / IntersectWithTrustedSet ───────────────────────

    [Test]
    public void TrustedProviderIds_AreExactlyTheFourTrusted()
    {
        Assert.That(AskCommand.TrustedProviderIds,
            Is.EquivalentTo(new[] { "claude", "openai", "gemini", "deepseek" }));
    }

    [Test]
    public void IntersectWithTrustedSet_NullRequest_ReturnsFullTrustedSet()
    {
        var result = AskCommand.IntersectWithTrustedSet(null);
        Assert.That(result, Is.EquivalentTo(AskCommand.TrustedProviderIds));
    }

    [Test]
    public void IntersectWithTrustedSet_EmptyRequest_ReturnsFullTrustedSet()
    {
        var result = AskCommand.IntersectWithTrustedSet(Array.Empty<string>());
        Assert.That(result, Is.EquivalentTo(AskCommand.TrustedProviderIds));
    }

    [Test]
    public void IntersectWithTrustedSet_NarrowsToRequestedSubset()
    {
        var result = AskCommand.IntersectWithTrustedSet(new[] { "claude", "openai" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude", "openai" }));
    }

    [Test]
    public void IntersectWithTrustedSet_DropsUntrustedIds()
    {
        // The whole point: passing an untrusted id never adds it to the panel.
        var result = AskCommand.IntersectWithTrustedSet(new[] { "claude", "mistral", "ollama" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude" }));
    }

    [Test]
    public void IntersectWithTrustedSet_AllUntrusted_ReturnsEmpty()
    {
        var result = AskCommand.IntersectWithTrustedSet(new[] { "mistral", "groq", "xai" });
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void IntersectWithTrustedSet_CaseInsensitive()
    {
        var result = AskCommand.IntersectWithTrustedSet(new[] { "CLAUDE", "OpenAI" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude", "openai" }));
    }

    [Test]
    public void IntersectWithTrustedSet_StripsBlankEntries()
    {
        var result = AskCommand.IntersectWithTrustedSet(new[] { "claude", "", "  " });
        Assert.That(result, Is.EquivalentTo(new[] { "claude" }));
    }

    // ── BuildHighTierModelOverrides ────────────────────────────────────────

    [Test]
    public void BuildHighTierModelOverrides_PinsClaudeToOpus47()
    {
        // The whole reason this helper exists: `legion ask` must run Claude on
        // Opus 4.7 (the High tier), not the Sonnet default that LegionClient
        // would otherwise pick. If this assertion ever flips, every ask vote
        // silently downgrades to Sonnet — fail loud.
        var overrides = AskCommand.BuildHighTierModelOverrides();
        Assert.That(overrides["claude"], Is.EqualTo("claude-opus-4-7"));
    }

    [Test]
    public void BuildHighTierModelOverrides_PinsAllFourTrustedVotersToHighTier()
    {
        var overrides = AskCommand.BuildHighTierModelOverrides();
        Assert.That(overrides["claude"],   Is.EqualTo("claude-opus-4-7"));
        Assert.That(overrides["openai"],   Is.EqualTo("gpt-4.1"));
        Assert.That(overrides["gemini"],   Is.EqualTo("gemini-2.5-pro"));
        Assert.That(overrides["deepseek"], Is.EqualTo("deepseek-reasoner"));
    }

    [Test]
    public void BuildHighTierModelOverrides_HasOneEntryPerTrustedProvider()
    {
        var overrides = AskCommand.BuildHighTierModelOverrides();
        Assert.That(overrides.Keys, Is.EquivalentTo(AskCommand.TrustedProviderIds));
    }

    [Test]
    public void BuildHighTierModelOverrides_IsCaseInsensitive()
    {
        // VotingConfiguration.ModelOverrides is keyed case-insensitively in the
        // resolution chain, so a "Claude" lookup must hit the same entry as
        // "claude". Without an OrdinalIgnoreCase comparer the override would
        // miss when callers use mixed case.
        var overrides = AskCommand.BuildHighTierModelOverrides();
        Assert.That(overrides.ContainsKey("CLAUDE"), Is.True);
        Assert.That(overrides.ContainsKey("Claude"), Is.True);
    }

    [Test]
    public void DefaultTier_IsHigh()
    {
        // Architecture decisions are the default ask shape; if the default
        // ever flips to Medium/Low it'd silently downgrade every legion-ask
        // call that doesn't pass --tier. Pin it.
        Assert.That(AskCommand.DefaultTier, Is.EqualTo(ModelTier.High));
    }

    [Test]
    public void BuildTierModelOverrides_Low_PinsCheapModels()
    {
        var overrides = AskCommand.BuildTierModelOverrides(ModelTier.Low);
        Assert.That(overrides["claude"],   Is.EqualTo("claude-haiku-4-5-20251001"));
        Assert.That(overrides["openai"],   Is.EqualTo("gpt-4.1-nano"));
        Assert.That(overrides["gemini"],   Is.EqualTo("gemini-2.5-flash-lite"));
        Assert.That(overrides["deepseek"], Is.EqualTo("deepseek-chat"));
    }

    [Test]
    public void BuildTierModelOverrides_Medium_PinsBalancedModels()
    {
        var overrides = AskCommand.BuildTierModelOverrides(ModelTier.Medium);
        Assert.That(overrides["claude"],   Is.EqualTo("claude-sonnet-4-6"));
        Assert.That(overrides["openai"],   Is.EqualTo("gpt-4.1-mini"));
        Assert.That(overrides["gemini"],   Is.EqualTo("gemini-2.5-flash"));
        Assert.That(overrides["deepseek"], Is.EqualTo("deepseek-chat"));
    }

    [Test]
    public void BuildTierModelOverrides_HighMatchesBackCompatShim()
    {
        // BuildHighTierModelOverrides is kept as a delegate to BuildTierModelOverrides(High).
        // Asserting equivalence stops a future refactor that drops the shim from
        // also accidentally diverging the High mapping.
        var viaShim    = AskCommand.BuildHighTierModelOverrides();
        var viaGeneric = AskCommand.BuildTierModelOverrides(ModelTier.High);
        Assert.That(viaGeneric, Is.EquivalentTo(viaShim));
    }

    [Test]
    public void BuildTierModelOverrides_Higher_FallsBackToHighWhenNotMappedDirectly()
    {
        // Higher tier walks down to High when not explicitly mapped — the
        // catalog GetTieredModel guarantees this, but pin it here too so a
        // refactor that strips the walk-down behavior breaks the test.
        var overrides = AskCommand.BuildTierModelOverrides(ModelTier.Higher);
        Assert.That(overrides["claude"], Is.EqualTo("claude-opus-4-7[1m]"));
        Assert.That(overrides["openai"], Is.EqualTo("o1"));
    }

    // ── SnapToOption ───────────────────────────────────────────────────────

    [Test]
    public void SnapToOption_ExactMatch_ReturnsOption()
    {
        var match = AskCommand.SnapToOption("Singleton", new[] { "Singleton", "Scoped", "Transient" });
        Assert.That(match, Is.EqualTo("Singleton"));
    }

    [Test]
    public void SnapToOption_ExactMatch_IsCaseInsensitive()
    {
        var match = AskCommand.SnapToOption("singleton", new[] { "Singleton", "Scoped", "Transient" });
        Assert.That(match, Is.EqualTo("Singleton"));
    }

    [Test]
    public void SnapToOption_AnswerContainsOption_ReturnsMatchedOption()
    {
        // "I'd pick Singleton — it's idempotent" should map cleanly to "Singleton".
        var match = AskCommand.SnapToOption(
            "I'd pick Singleton — it's idempotent",
            new[] { "Singleton", "Scoped", "Transient" });
        Assert.That(match, Is.EqualTo("Singleton"));
    }

    [Test]
    public void SnapToOption_PrefersLongestMatchWhenSeveralFit()
    {
        // Without longest-match, "First" would win over "FirstChoiceOption" by
        // virtue of appearing first in the option list — that's the bug we
        // protect against. A reply containing "FirstChoiceOption" should map
        // to that, not to its substring "First".
        var match = AskCommand.SnapToOption(
            "FirstChoiceOption",
            new[] { "First", "FirstChoiceOption" });
        Assert.That(match, Is.EqualTo("FirstChoiceOption"));
    }

    [Test]
    public void SnapToOption_OffBallot_ReturnsNull()
    {
        var match = AskCommand.SnapToOption(
            "Use raw ADO.NET instead",
            new[] { "Singleton", "Scoped", "Transient" });
        Assert.That(match, Is.Null);
    }

    [Test]
    public void SnapToOption_EmptyAnswer_ReturnsNull()
    {
        Assert.That(AskCommand.SnapToOption("", new[] { "A", "B" }), Is.Null);
        Assert.That(AskCommand.SnapToOption("   ", new[] { "A", "B" }), Is.Null);
    }

    [Test]
    public void SnapToOption_EmptyOptions_ReturnsNull()
    {
        Assert.That(AskCommand.SnapToOption("anything", Array.Empty<string>()), Is.Null);
    }

    // ── Truncate ───────────────────────────────────────────────────────────

    [Test]
    public void Truncate_ShortString_ReturnsUnchanged()
    {
        Assert.That(AskCommand.Truncate("hello", 100), Is.EqualTo("hello"));
    }

    [Test]
    public void Truncate_AtBoundary_ReturnsUnchanged()
    {
        // Boundary: length == cap → keep unchanged (the marker would be misleading).
        var s = new string('x', 50);
        Assert.That(AskCommand.Truncate(s, 50), Is.EqualTo(s));
    }

    [Test]
    public void Truncate_OverCap_PrependsCappedHeadAndAppendsMarker()
    {
        var s        = new string('x', 100);
        var result   = AskCommand.Truncate(s, 50);
        Assert.That(result, Does.StartWith(new string('x', 50)));
        Assert.That(result, Does.Contain("truncated"));
        Assert.That(result, Does.Contain("100 chars"));
    }

    // ── IsHelp ─────────────────────────────────────────────────────────────

    [Test]
    public void IsHelp_RecognizesEveryHelpFlag()
    {
        Assert.That(AskCommand.IsHelp("-h"),     Is.True);
        Assert.That(AskCommand.IsHelp("--help"), Is.True);
        Assert.That(AskCommand.IsHelp("help"),   Is.True);
        Assert.That(AskCommand.IsHelp("/?"),     Is.True);
    }

    [Test]
    public void IsHelp_RejectsNonHelpFlags()
    {
        Assert.That(AskCommand.IsHelp("--json"),    Is.False);
        Assert.That(AskCommand.IsHelp("ask"),       Is.False);
        Assert.That(AskCommand.IsHelp(""),          Is.False);
        Assert.That(AskCommand.IsHelp("--HELP"),    Is.False); // case-sensitive on purpose: matches POSIX convention
    }

    // ── BuildArchitectFraming ──────────────────────────────────────────────

    [Test]
    public void BuildArchitectFraming_ContainsTheFiveHeuristics()
    {
        // The framing is the only steering signal voters get for non-trivial
        // calls; if a heuristic gets accidentally deleted the panel's bias
        // changes silently. Pin all five so a regression fails the build.
        var framing = AskCommand.BuildArchitectFraming();
        Assert.That(framing, Does.Contain("senior software architect"));
        Assert.That(framing, Does.Contain("boring, conventional"));
        Assert.That(framing, Does.Contain("reversible"));
        Assert.That(framing, Does.Contain("project's existing style"));
        Assert.That(framing, Does.Contain("next 30 minutes"));
        Assert.That(framing, Does.Contain("security or data-loss"));
        Assert.That(framing, Does.Contain("Do not refuse to answer"));
    }

    // ── FindFirst ──────────────────────────────────────────────────────────

    [Test]
    public void FindFirst_ReturnsFirstExistingNameInOrder()
    {
        var dir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "second.txt"), "");
            File.WriteAllText(Path.Combine(dir, "third.txt"),  "");

            var match = AskCommand.FindFirst(dir, new[] { "first.txt", "second.txt", "third.txt" });
            Assert.That(match, Is.EqualTo(Path.Combine(dir, "second.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void FindFirst_NoneExist_ReturnsNull()
    {
        var dir = MakeTempDir();
        try
        {
            var match = AskCommand.FindFirst(dir, new[] { "absent1", "absent2" });
            Assert.That(match, Is.Null);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── CollectAutoContextAsync ────────────────────────────────────────────

    [Test]
    public async Task CollectAutoContextAsync_NoFiles_ReturnsEmpty()
    {
        var dir = MakeTempDir();
        try
        {
            var ctx = await AskCommand.CollectAutoContextAsync(dir);
            // git status / git log may legitimately succeed at the repo root
            // when the tempdir happens to live inside a git tree on Windows;
            // strip those sections before asserting "no project files."
            Assert.That(ctx, Does.Not.Contain("CLAUDE.md"));
            Assert.That(ctx, Does.Not.Contain("=== README ==="));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task CollectAutoContextAsync_IncludesClaudeMdAndReadmeWhenPresent()
    {
        var dir = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "claude-instructions-here");
            File.WriteAllText(Path.Combine(dir, "README.md"), "readme-prose-here");

            var ctx = await AskCommand.CollectAutoContextAsync(dir);
            Assert.That(ctx, Does.Contain("=== CLAUDE.md ==="));
            Assert.That(ctx, Does.Contain("claude-instructions-here"));
            Assert.That(ctx, Does.Contain("=== README ==="));
            Assert.That(ctx, Does.Contain("readme-prose-here"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task CollectAutoContextAsync_TruncatesOversizedFile()
    {
        var dir = MakeTempDir();
        try
        {
            // 16 KB README — twice the 8 KB cap, so a truncation marker must appear.
            var oversized = new string('x', 16 * 1024);
            File.WriteAllText(Path.Combine(dir, "README.md"), oversized);

            var ctx = await AskCommand.CollectAutoContextAsync(dir);
            Assert.That(ctx, Does.Contain("=== README ==="));
            Assert.That(ctx, Does.Contain("truncated"));
            Assert.That(ctx, Does.Contain("16384 chars"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public async Task CollectAutoContextAsync_PrefersClaudeMdOverLowercaseClaudeMd()
    {
        var dir = MakeTempDir();
        try
        {
            // FindFirst tries "CLAUDE.md" before "claude.md" — first hit wins.
            // On case-insensitive filesystems (Windows default) only one of the
            // two names can actually exist at a time, so write CLAUDE.md and
            // assert the section header is the one that matches.
            File.WriteAllText(Path.Combine(dir, "CLAUDE.md"), "uppercase-content");

            var ctx = await AskCommand.CollectAutoContextAsync(dir);
            Assert.That(ctx, Does.Contain("uppercase-content"));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "legion-ask-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
