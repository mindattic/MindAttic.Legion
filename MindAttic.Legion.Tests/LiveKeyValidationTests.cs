using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Live validation that the <em>real</em> API key stored in the Vault
/// (<c>%APPDATA%\MindAttic\LLM\providers.json</c>) actually authenticates
/// against each provider's live endpoint.
///
/// <para>This is the suite you run after rotating a key — or on a schedule —
/// to catch the failure modes that <em>only</em> show up against the real API:
/// a revoked / expired key (401), a key flagged or disabled (403), an account
/// out of credits (402/429-quota), or a provider that has <b>diverged or
/// refined its wire shape</b> so the previously-OK request no longer parses
/// (BadResponse). LLM APIs change constantly; a mocked test can't see any of
/// that. This one can, and on failure it tells you exactly which category broke
/// via <see cref="LlmHealthDiagnoser"/> so the fix is obvious (rotate the key vs
/// top up the account vs update the request shape).</para>
///
/// <para><b>Data-driven over the Vault.</b> The per-provider cases are generated
/// from whatever keys are actually present in the credential store (intersected
/// with the providers Legion knows how to call), so adding or removing a key in
/// the Vault automatically adds or removes a test case — no edit here required.</para>
///
/// <para><b>How to run</b> (kept <c>[Explicit]</c> so normal <c>dotnet test</c>
/// and CI stay free and deterministic — these cost money and hit the network):</para>
/// <code>
///   # Validate every key in the Vault
///   dotnet test --filter "Category=LiveKeys"
///
///   # Validate one provider's key
///   dotnet test --filter "FullyQualifiedName~LiveKeyValidation.RealKey_Authenticates(deepseek)"
/// </code>
/// </summary>
[TestFixture]
[Category("LiveKeys")]
[Explicit("Hits real provider APIs with the live Vault keys — costs money and depends on network. Run on demand or on a schedule.")]
public class LiveKeyValidationTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Every provider that (a) has a non-empty key in the Vault and (b) Legion
    /// knows how to call. Evaluated at test-discovery time, so the live matrix
    /// always mirrors the current credential store.
    /// </summary>
    private static IEnumerable<string> KeyedProviders() =>
        MindAtticCredentialStore.ListProviders()
            .Where(LegionClient.IsSupported)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);

    // ── live: does the real key authenticate? ───────────────────────────────

    [TestCaseSource(nameof(KeyedProviders))]
    public async Task RealKey_Authenticates(string providerId)
    {
        using var http = new HttpClient { Timeout = ProbeTimeout + TimeSpan.FromSeconds(5) };
        // No-resilience so a 401/403 surfaces immediately as the real diagnosis
        // instead of being retried / masked by the circuit breaker.
        var client = new LegionClient(http, LegionClientOptions.NoResilience);
        var health = new LlmHealthCheck(client);

        var r = await health.CheckOneAsync(providerId, ProbeTimeout);

        // Hard gate: the call must complete against the live API. The diagnosis
        // says what actually broke — a dead/expired key (AuthInvalid/Forbidden),
        // an exhausted account (QuotaExhausted), OR an API divergence that is
        // NOT a key problem at all (NotFound = model deprecated, BadRequest =
        // request shape rejected). The message leads with the category so the
        // fix is unambiguous rather than blaming the key for a deprecated model.
        Assert.That(r.IsHealthy, Is.True,
            $"{providerId}: live check FAILED — {r.Diagnosis} (HTTP {r.HttpStatusCode?.ToString() ?? "n/a"}).\n" +
            $"  next step: {r.ActionableMessage}\n" +
            $"  raw error: {r.ErrorMessage}");

        // Soft signal: the key is good, but the model's reply didn't match the
        // probe. That's model/behaviour drift, not a credential problem — warn
        // rather than fail so a key-validation run isn't blocked by it.
        if (!r.RespondedCorrectly)
            Assert.Warn(
                $"{providerId}: key is VALID but the probe reply drifted ({r.Diagnosis}). " +
                $"Reply was: \"{(r.Response ?? "").Trim()}\". " +
                "Check whether the default model changed its instruction-following behaviour.");

        TestContext.WriteLine($"{providerId}: OK in {r.ElapsedMilliseconds}ms — \"{(r.Response ?? "").Trim()}\"");
    }

    // ── offline: is a key even present for the trusted panel? ────────────────

    /// <summary>
    /// Cheap, network-free guard (not <c>[Explicit]</c> via category but runs
    /// under the same fixture): every trusted-panel provider must have a
    /// non-empty key in the Vault. Catches a missed rotation or a key written
    /// to the wrong store <em>before</em> spending a live call — the exact class
    /// of failure that retiring User Secrets was meant to prevent.
    /// </summary>
    [Test]
    public void TrustedPanel_EveryProviderHasAKeyInTheVault()
    {
        var missing = LlmProviderCatalog.DefaultIds
            .Where(id => string.IsNullOrWhiteSpace(MindAtticCredentialStore.GetKey(id)))
            .ToList();

        Assert.That(missing, Is.Empty,
            "trusted-panel providers with NO key in the Vault " +
            $"({MindAtticCredentialStore.CredentialDirectory}): {string.Join(", ", missing)}. " +
            "Rotate / add the key there — the panel can't vote without it.");
    }

    // ── pre-commit gate: every trusted-panel key must work LIVE ──────────────

    /// <summary>
    /// The pre-commit gate. Probes the four trusted voting providers
    /// (claude / openai / gemini / deepseek) against their LIVE endpoints and
    /// fails if ANY key does not authenticate. Wired into the <c>pre-commit</c>
    /// hook for both Legion and Vault (<c>.githooks/pre-commit</c>) — there is no
    /// point committing when a trusted panel key is dead.
    ///
    /// <para>Tagged with its own <c>LiveKeysTrusted</c> category so the hook can
    /// run JUST this — four cheap parallel calls — without dragging in the
    /// non-trusted providers that may legitimately be stale.</para>
    /// </summary>
    [Test]
    [Category("LiveKeysTrusted")]
    public async Task TrustedPanel_EveryKeyAuthenticatesLive()
    {
        using var http = new HttpClient { Timeout = ProbeTimeout + TimeSpan.FromSeconds(5) };
        var client = new LegionClient(http, LegionClientOptions.NoResilience);
        var health = new LlmHealthCheck(client);

        var results = await health.CheckAsync(LlmProviderCatalog.DefaultIds, ProbeTimeout);

        var broken = results
            .Where(r => !r.IsHealthy)
            .Select(r => $"{r.ProviderId}: {r.Diagnosis} " +
                         $"(HTTP {r.HttpStatusCode?.ToString() ?? "n/a"}) — {r.ActionableMessage}")
            .ToList();

        foreach (var r in results)
            TestContext.WriteLine($"{r.ProviderId}: {(r.IsHealthy ? "OK" : "FAIL")} " +
                                  $"({r.Diagnosis}) in {r.ElapsedMilliseconds}ms");

        Assert.That(broken, Is.Empty,
            "trusted-panel keys that FAILED live validation — fix/rotate before committing:\n  - "
            + string.Join("\n  - ", broken));
    }
}
