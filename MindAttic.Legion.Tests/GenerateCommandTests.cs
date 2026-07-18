using MindAttic.Legion;
using MindAttic.Legion.Cli;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Unit tests for <see cref="GenerateCommand"/>'s pure helpers — provider
/// resolution, count splitting, list-marker stripping, quote stripping,
/// item extraction from raw replies, and dedup. The live HTTP fan-out
/// (<see cref="GenerateCommand.FanOutAsync"/>) is exercised end-to-end
/// when a developer runs <c>legion generate</c>; live behavior is also
/// covered by the explicit live-API integration fixture.
/// </summary>
[TestFixture]
public class GenerateCommandTests
{
    [Test]
    public void TrustedProviderIds_MatchAskCommandTrustList()
    {
        Assert.That(GenerateCommand.TrustedProviderIds,
            Is.EquivalentTo(AskCommand.TrustedProviderIds));
    }

    [Test]
    public void DefaultTier_IsMedium()
    {
        // Medium = creative balance. Low produces flat output for creative
        // bulk; High burns budget on flagship reasoning that doesn't help
        // for "list 25 names". Pin Medium so a refactor doesn't drift.
        Assert.That(GenerateCommand.DefaultTier, Is.EqualTo(ModelTier.Medium));
    }

    [Test]
    public void DefaultCount_IsTen()
    {
        Assert.That(GenerateCommand.DefaultCount, Is.EqualTo(10));
    }

    // ── ResolveProviders ───────────────────────────────────────────────────

    [Test]
    public void ResolveProviders_NullRequest_ReturnsFullTrustedSet()
    {
        Assert.That(GenerateCommand.ResolveProviders(null),
            Is.EquivalentTo(GenerateCommand.TrustedProviderIds));
    }

    [Test]
    public void ResolveProviders_DropsUntrustedAndDeduplicates()
    {
        var result = GenerateCommand.ResolveProviders(new[] { "Claude-Api", "claude-api", "mistral" });
        Assert.That(result, Is.EquivalentTo(new[] { "claude-api" }));
    }

    // ── SplitCount ─────────────────────────────────────────────────────────

    [Test]
    public void SplitCount_EvenDivision()
    {
        Assert.That(GenerateCommand.SplitCount(100, 4), Is.EqualTo(new[] { 25, 25, 25, 25 }));
    }

    [Test]
    public void SplitCount_UnevenDivision_RemainderToFront()
    {
        // 10 across 4 → [3, 3, 2, 2]: front buckets get the remainder so
        // the totals still sum to N exactly.
        Assert.That(GenerateCommand.SplitCount(10, 4), Is.EqualTo(new[] { 3, 3, 2, 2 }));
    }

    [Test]
    public void SplitCount_FewerThanBuckets_AssignsToFront()
    {
        // 3 items / 4 buckets → [1, 1, 1, 0]. The trailing zero gets
        // filtered out by the assignment loop so we don't make pointless
        // empty calls.
        Assert.That(GenerateCommand.SplitCount(3, 4), Is.EqualTo(new[] { 1, 1, 1, 0 }));
    }

    [Test]
    public void SplitCount_TotalsToInputN()
    {
        // Property: regardless of bucket count, the slots must sum to N.
        for (var n = 0; n < 200; n += 17)
        for (var b = 1; b <= 8; b++)
            Assert.That(GenerateCommand.SplitCount(n, b).Sum(), Is.EqualTo(n),
                $"split({n},{b}) lost items");
    }

    [Test]
    public void SplitCount_ZeroBuckets_ReturnsEmpty()
    {
        Assert.That(GenerateCommand.SplitCount(10, 0), Is.Empty);
    }

    // ── StripListMarker ────────────────────────────────────────────────────

    [Test]
    public void StripListMarker_NumberedDot()
    {
        Assert.That(GenerateCommand.StripListMarker("1. Aragorn"),  Is.EqualTo("Aragorn"));
        Assert.That(GenerateCommand.StripListMarker("12. Aragorn"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripListMarker_NumberedParen()
    {
        Assert.That(GenerateCommand.StripListMarker("1) Aragorn"),  Is.EqualTo("Aragorn"));
        Assert.That(GenerateCommand.StripListMarker("99) Aragorn"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripListMarker_DashBullet()
    {
        Assert.That(GenerateCommand.StripListMarker("- Aragorn"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripListMarker_StarBullet()
    {
        Assert.That(GenerateCommand.StripListMarker("* Aragorn"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripListMarker_UnicodeBullet()
    {
        Assert.That(GenerateCommand.StripListMarker("• Aragorn"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripListMarker_NoMarker_ReturnsUnchanged()
    {
        Assert.That(GenerateCommand.StripListMarker("Aragorn"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripListMarker_NumberWithoutSeparator_ReturnsUnchanged()
    {
        // "12 Aragorn" looks like content (a quantity), not a list marker.
        // Don't lossy-strip just because it starts with digits.
        Assert.That(GenerateCommand.StripListMarker("12 Aragorn"), Is.EqualTo("12 Aragorn"));
    }

    // ── StripWrappingQuotes ────────────────────────────────────────────────

    [Test]
    public void StripWrappingQuotes_StraightDouble()
    {
        Assert.That(GenerateCommand.StripWrappingQuotes("\"Aragorn\""), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripWrappingQuotes_StraightSingle()
    {
        Assert.That(GenerateCommand.StripWrappingQuotes("'Aragorn'"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripWrappingQuotes_CurlyDouble()
    {
        Assert.That(GenerateCommand.StripWrappingQuotes("“Aragorn”"), Is.EqualTo("Aragorn"));
    }

    [Test]
    public void StripWrappingQuotes_MismatchedPair_ReturnsUnchanged()
    {
        // "Aragorn' — only the trailing side closes — is data, not wrap.
        Assert.That(GenerateCommand.StripWrappingQuotes("\"Aragorn'"),
            Is.EqualTo("\"Aragorn'"));
    }

    [Test]
    public void StripWrappingQuotes_NoQuote_ReturnsUnchanged()
    {
        Assert.That(GenerateCommand.StripWrappingQuotes("Aragorn"), Is.EqualTo("Aragorn"));
    }

    // ── ExtractItems ───────────────────────────────────────────────────────

    [Test]
    public void ExtractItems_PlainNewlineSeparated()
    {
        var result = GenerateCommand.ExtractItems("Aragorn\nBoromir\nGimli");
        Assert.That(result, Is.EqualTo(new[] { "Aragorn", "Boromir", "Gimli" }));
    }

    [Test]
    public void ExtractItems_StripsNumberedAndBulletedAndQuoted()
    {
        // The wild — models may ignore "no markers" instructions: numbered,
        // bulleted, quoted, or any combination. Extraction should produce
        // clean items regardless.
        var raw = "1. \"Aragorn\"\n2) Boromir\n- Gimli\n* Legolas\n• “Frodo”";
        var result = GenerateCommand.ExtractItems(raw);
        Assert.That(result, Is.EqualTo(new[] { "Aragorn", "Boromir", "Gimli", "Legolas", "Frodo" }));
    }

    [Test]
    public void ExtractItems_DropsEmptyLines()
    {
        var raw = "Aragorn\n\n\nBoromir\n   \nGimli";
        var result = GenerateCommand.ExtractItems(raw);
        Assert.That(result, Is.EqualTo(new[] { "Aragorn", "Boromir", "Gimli" }));
    }

    [Test]
    public void ExtractItems_PreservesOrder()
    {
        // Item order matters when the user pipes into `head -N` to sample
        // the first M of N — preserving model output order keeps that
        // intuition. Sorting would silently break it.
        var result = GenerateCommand.ExtractItems("zeta\nalpha\nbeta");
        Assert.That(result, Is.EqualTo(new[] { "zeta", "alpha", "beta" }));
    }

    // ── Deduplicate ────────────────────────────────────────────────────────

    [Test]
    public void Deduplicate_RemovesRepeatsCaseInsensitively()
    {
        var result = GenerateCommand.Deduplicate(new[] { "Aragorn", "aragorn", "Boromir", "ARAGORN" });
        Assert.That(result, Is.EqualTo(new[] { "Aragorn", "Boromir" }));
    }

    [Test]
    public void Deduplicate_PreservesFirstSeenOrder()
    {
        // First-seen-wins so a model's casing/preference isn't randomly
        // overwritten. With "alpha" appearing first, that's what survives —
        // not a later "Alpha" or "ALPHA".
        var result = GenerateCommand.Deduplicate(new[] { "alpha", "beta", "Alpha", "gamma", "ALPHA" });
        Assert.That(result, Is.EqualTo(new[] { "alpha", "beta", "gamma" }));
    }

    [Test]
    public void Deduplicate_DropsBlankAndWhitespaceOnly()
    {
        var result = GenerateCommand.Deduplicate(new[] { "alpha", "", "  ", "beta", "alpha" });
        Assert.That(result, Is.EqualTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public void Deduplicate_EmptyInput_ReturnsEmpty()
    {
        Assert.That(GenerateCommand.Deduplicate(Array.Empty<string>()), Is.Empty);
    }

    // ── IsHelp ─────────────────────────────────────────────────────────────

    [Test]
    public void IsHelp_RecognizesEveryHelpFlag()
    {
        Assert.That(GenerateCommand.IsHelp("-h"),     Is.True);
        Assert.That(GenerateCommand.IsHelp("--help"), Is.True);
        Assert.That(GenerateCommand.IsHelp("help"),   Is.True);
        Assert.That(GenerateCommand.IsHelp("/?"),     Is.True);
        Assert.That(GenerateCommand.IsHelp("--json"), Is.False);
    }
}
