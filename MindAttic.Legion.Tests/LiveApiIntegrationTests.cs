using MindAttic.Legion;
using MindAttic.Legion.Cli;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// End-to-end tests that hit the <em>real</em> trusted-provider APIs —
/// the .NET equivalent of a Cypress / Playwright integration suite for
/// this CLI. Marked <c>[Explicit]</c> so they do NOT run on normal
/// <c>dotnet test</c> invocations (which would spend money on every CI
/// run); developers run them on demand to verify wire-shape, tier
/// mapping, and end-to-end behavior across providers.
///
/// <para><b>How to run.</b></para>
/// <code>
///   # Whole live suite
///   dotnet test --filter "Category=LiveApi"
///
///   # One specific test
///   dotnet test --filter "FullyQualifiedName~LiveApi.Claude_Low"
/// </code>
///
/// <para><b>What each test asserts.</b></para>
/// <list type="bullet">
///   <item>Per-provider × per-tier connectivity (<see cref="LegionClient.CallAsync"/>
///         actually returns text). One test per (provider, tier) pair so a
///         failure points at a specific cell of the matrix.</item>
///   <item>The <c>BuildHighTierModelOverrides</c> map matches each provider's
///         catalog tier mapping — guards against a future tier-map drift
///         that isn't caught by unit tests because they only pin the
///         catalog itself.</item>
/// </list>
///
/// <para><b>Why these are explicit.</b></para>
/// Two reasons: cost (every run pays the API for ~12 small calls plus
/// whatever a smoke-test of the bulk commands would consume), and
/// flakiness (real APIs go down, deprecate models, or reject
/// previously-OK params). Explicit + opt-in keeps CI green while still
/// giving developers a single command to validate the whole panel before
/// shipping a tier-related change.
/// </summary>
[TestFixture]
[Category("LiveApi")]
[Explicit("Hits real provider APIs — costs money and depends on network. Run on demand.")]
public class LiveApiIntegrationTests
{
    private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(60);
    private const int    PerCallMaxTokens = 400;
    private const string ProbeSystemPrompt = "Reply with exactly: OK";
    private const string ProbeUserMessage  = "ping";

    // ── Per-(provider, tier) connectivity matrix ───────────────────────────

    [Test] public Task Claude_Low()    => ProbeAsync("claude",   ModelTier.Low);
    [Test] public Task Claude_Medium() => ProbeAsync("claude",   ModelTier.Medium);
    [Test] public Task Claude_High()   => ProbeAsync("claude",   ModelTier.High);
    [Test] public Task OpenAi_Low()    => ProbeAsync("openai",   ModelTier.Low);
    [Test] public Task OpenAi_Medium() => ProbeAsync("openai",   ModelTier.Medium);
    [Test] public Task OpenAi_High()   => ProbeAsync("openai",   ModelTier.High);
    [Test] public Task Gemini_Low()    => ProbeAsync("gemini",   ModelTier.Low);
    [Test] public Task Gemini_Medium() => ProbeAsync("gemini",   ModelTier.Medium);
    [Test] public Task Gemini_High()   => ProbeAsync("gemini",   ModelTier.High);
    [Test] public Task DeepSeek_Low()    => ProbeAsync("deepseek", ModelTier.Low);
    [Test] public Task DeepSeek_Medium() => ProbeAsync("deepseek", ModelTier.Medium);
    [Test] public Task DeepSeek_High()   => ProbeAsync("deepseek", ModelTier.High);

    // ── Whole-matrix sanity (use the TiersCommand probe machinery directly) ────

    [Test]
    public async Task TrustedFour_AllThreeDefaultTiers_AllRespond()
    {
        // The single test that says "the panel is healthy". If this passes,
        // legion ask / poll / generate are all viable on every default tier.
        using var http = new HttpClient { Timeout = PerCallTimeout + TimeSpan.FromSeconds(5) };
        var client     = new LegionClient(http, LegionClientOptions.NoResilience);

        var results = await TiersCommand.ProbeMatrixAsync(
            client,
            TiersCommand.TrustedProviderIds,
            TiersCommand.DefaultTiers,
            maxTokens: PerCallMaxTokens,
            perCallTimeout: PerCallTimeout);

        Assert.That(results, Has.Count.EqualTo(12),
            "expected 4 providers × 3 default tiers = 12 probes");
        var failures = results.Where(r => !r.Ok)
            .Select(r => $"{r.ProviderId}/{r.Tier}: {r.Error}")
            .ToList();
        Assert.That(failures, Is.Empty,
            $"some live probes failed:\n  - {string.Join("\n  - ", failures)}");
    }

    // ── Catalog ↔ overrides parity ─────────────────────────────────────────

    [Test]
    public void HighTierOverrides_MatchCatalogForEveryTrustedProvider()
    {
        // Not strictly "live" (no HTTP), but it lives here because it pins
        // the contract that drives the live behavior of legion ask: the
        // override map must always reflect the catalog at the High tier.
        // Marking [Explicit] (via fixture) keeps it grouped with the live
        // suite that exercises the same surface.
        var overrides = AskCommand.BuildHighTierModelOverrides();
        foreach (var id in AskCommand.TrustedProviderIds)
        {
            var catalogModel = LlmProviderCatalog.GetTieredModel(id, ModelTier.High);
            Assert.That(overrides[id], Is.EqualTo(catalogModel),
                $"override for {id} must match catalog GetTieredModel({id},High)");
        }
    }

    // ── End-to-end smoke of the public commands ───────────────────────────

    [Test]
    public async Task Ask_SmokeTest_ReturnsConsensusAtLowTier()
    {
        // Exercises the whole ask pipeline (config → voting service →
        // panel) with a tiny, deterministic question. Low tier so the
        // smoke test is cheap.
        var exit = await AskCommand.RunAsync(new[]
        {
            "Pick: A or B?", "--options", "A,B",
            "--tier",          "low",
            "--quorum",        "plurality",
            "--max-tokens",    "50",
            "--timeout",       "60",
            "--no-auto-context",
        });
        Assert.That(exit, Is.EqualTo(0), "ask should reach plurality on a binary question");
    }

    [Test]
    public async Task Poll_SmokeTest_ReportsDistributionAtLowTier()
    {
        // 8 voters round-robin to cover all four providers twice; shouldn't
        // hit rate limits anywhere.
        var exit = await PollCommand.RunAsync(new[]
        {
            "Pick: A or B?", "--options", "A,B",
            "--count",       "8",
            "--tier",        "low",
            "--max-tokens",  "30",
            "--timeout",     "30",
            "--concurrency", "4",
        });
        Assert.That(exit, Is.EqualTo(0), "poll should produce at least one successful voter");
    }

    [Test]
    public async Task Generate_SmokeTest_ReturnsAtLeastOneItem()
    {
        // Tiny request — 4 items across 4 providers means each batches just 1,
        // exercising the fan-out + extraction + dedup pipeline without
        // burning tokens.
        var exit = await GenerateCommand.RunAsync(new[]
        {
            "Single-word hero names",
            "--count",      "4",
            "--tier",       "low",
            "--max-tokens", "100",
            "--timeout",    "30",
        });
        Assert.That(exit, Is.EqualTo(0), "generate should produce at least one item");
    }

    // ── shared probe helper ────────────────────────────────────────────────

    private static async Task ProbeAsync(string providerId, ModelTier tier)
    {
        var model = LlmProviderCatalog.GetTieredModel(providerId, tier);
        Assert.That(model, Is.Not.Null.And.Not.Empty,
            $"catalog has no {tier} mapping for {providerId} — fix the catalog before this test can pass");

        using var http = new HttpClient { Timeout = PerCallTimeout + TimeSpan.FromSeconds(5) };
        var client     = new LegionClient(http, LegionClientOptions.NoResilience);

        using var cts = new CancellationTokenSource(PerCallTimeout);
        var reply = await client.CallAsync(
            providerId:    providerId,
            systemPrompt:  ProbeSystemPrompt,
            userMessage:   ProbeUserMessage,
            maxTokens:     PerCallMaxTokens,
            temperature:   0.0,
            modelOverride: model,
            ct:            cts.Token);

        Assert.That(reply, Is.Not.Null.And.Not.Empty,
            $"{providerId}/{tier} ({model}) returned an empty reply");
    }
}
