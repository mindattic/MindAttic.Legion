using System.Diagnostics;

namespace MindAttic.Legion;

/// <summary>
/// Status of one provider after a health probe.
/// </summary>
public sealed record LlmHealthResult(
    string ProviderId,
    string DisplayName,
    bool HasCredential,
    bool IsHealthy,
    bool RespondedCorrectly,
    long ElapsedMilliseconds,
    string? Response,
    string? ErrorMessage,
    string DashboardUrl,
    string KeysUrl)
{
    /// <summary>
    /// Human-readable status: "OK", "MISSING KEY", "WRONG REPLY", "ERROR: ...".
    /// </summary>
    public string Status =>
        !HasCredential        ? "MISSING KEY" :
        !IsHealthy            ? $"ERROR: {ErrorMessage}" :
        !RespondedCorrectly   ? $"WRONG REPLY: {Response}" :
                                "OK";
}

/// <summary>
/// Sends a small "Say 'Hello World!' literally and nothing else" prompt to every
/// configured provider and reports who's reachable, who needs a new key, and who
/// is misbehaving. Apps should use this on startup or surface it in a settings
/// page so users know whether to top up tokens at the dashboard URL.
/// </summary>
public class LlmHealthCheck
{
    /// <summary>The exact prompt sent to every provider during a health probe.</summary>
    public const string ProbePrompt = "Reply with exactly the two words: Hello World! Nothing else, no punctuation differences, no quotes.";

    /// <summary>Substrings considered a "correct" reply (case-insensitive).</summary>
    private static readonly string[] AcceptableReplies = { "hello world" };

    private readonly LegionClient client;

    public LlmHealthCheck(LegionClient client)
    {
        this.client = client;
    }

    /// <summary>
    /// Probes every supported provider in <see cref="LlmProviderCatalog"/>.
    /// Skips providers without a credential in the shared store (their result still
    /// appears in the output with <see cref="LlmHealthResult.HasCredential"/> = false
    /// so the caller can prompt the user to add a key).
    /// Probes run in parallel — total wall time ≈ slowest provider, not their sum.
    /// </summary>
    public Task<IReadOnlyList<LlmHealthResult>> CheckAllAsync(
        TimeSpan? timeoutPerProvider = null,
        CancellationToken ct = default)
        => CheckAsync(LlmProviderCatalog.AllIds, timeoutPerProvider, ct);

    /// <summary>
    /// Probes the supplied subset of providers in parallel.
    /// </summary>
    public async Task<IReadOnlyList<LlmHealthResult>> CheckAsync(
        IEnumerable<string> providerIds,
        TimeSpan? timeoutPerProvider = null,
        CancellationToken ct = default)
    {
        var ids = providerIds?.Select(p => p.Trim().ToLowerInvariant())
                              .Distinct()
                              .ToList()
                  ?? new List<string>();
        if (ids.Count == 0) return Array.Empty<LlmHealthResult>();

        var tasks = ids.Select(id => CheckOneAsync(id, timeoutPerProvider, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>Probe a single provider.</summary>
    public async Task<LlmHealthResult> CheckOneAsync(
        string providerId,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var info = LlmProviderCatalog.Get(providerId)
                   ?? new LlmProviderInfo(providerId, providerId, "unknown",
                          DefaultModel: "", DashboardUrl: "", KeysUrl: "",
                          AvailableModels: Array.Empty<string>());

        var key = MindAtticCredentialStore.GetKey(providerId);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new LlmHealthResult(
                ProviderId: info.Id,
                DisplayName: info.DisplayName,
                HasCredential: false,
                IsHealthy: false,
                RespondedCorrectly: false,
                ElapsedMilliseconds: 0,
                Response: null,
                ErrorMessage: "No API key configured",
                DashboardUrl: info.DashboardUrl,
                KeysUrl: info.KeysUrl);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            if (cts is not null) cts.CancelAfter(timeout!.Value);
            var token = cts?.Token ?? ct;

            var reply = await client.CallAsync(
                providerId: info.Id,
                systemPrompt: "You are a status-check responder. Reply only with what the user asks. No commentary.",
                userMessage: ProbePrompt,
                maxTokens: 32,
                temperature: 0.0,
                ct: token);

            sw.Stop();
            var normalized = (reply ?? "").Trim().ToLowerInvariant();
            var ok = AcceptableReplies.Any(r => normalized.Contains(r));
            return new LlmHealthResult(
                ProviderId: info.Id,
                DisplayName: info.DisplayName,
                HasCredential: true,
                IsHealthy: true,
                RespondedCorrectly: ok,
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                Response: reply,
                ErrorMessage: null,
                DashboardUrl: info.DashboardUrl,
                KeysUrl: info.KeysUrl);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new LlmHealthResult(
                ProviderId: info.Id,
                DisplayName: info.DisplayName,
                HasCredential: true,
                IsHealthy: false,
                RespondedCorrectly: false,
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                Response: null,
                ErrorMessage: ex.Message,
                DashboardUrl: info.DashboardUrl,
                KeysUrl: info.KeysUrl);
        }
    }
}
