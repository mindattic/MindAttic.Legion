namespace MindAttic.Legion.Providers;

/// <summary>
/// Voting-aware provider wrapper. Resolves API keys and models per the
/// <see cref="VotingConfiguration"/>, then delegates the actual HTTP call to
/// <see cref="LegionClient"/>. This keeps the wire-level scaffolding in one
/// place and lets the voting layer focus on per-voter overrides + key resolution.
/// </summary>
public class LlmVotingProvider
{
    private readonly LegionClient client;
    private readonly VotingConfiguration config;

    /// <summary>
    /// Constructs the voting provider. Wraps <paramref name="http"/> in a
    /// <see cref="LegionClient"/>; <see cref="VotingConfiguration.ProviderTimeout"/>
    /// is applied per call via a linked <see cref="CancellationTokenSource"/> so
    /// we don't mutate a shared HttpClient (its lifetime is owned by
    /// <see cref="IHttpClientFactory"/>).
    /// </summary>
    public LlmVotingProvider(HttpClient http, VotingConfiguration config)
    {
        this.client = new LegionClient(http);
        this.config = config;
    }

    /// <summary>
    /// Call a provider with a system prompt + user message.
    /// Uses per-voter API key and model overrides if set on the profile.
    /// </summary>
    public async Task<string> CallAsync(
        string providerId,
        string systemPrompt,
        string userMessage,
        int maxTokens,
        double temperature,
        VoterProfile? voterOverrides = null,
        CancellationToken ct = default)
    {
        var key   = voterOverrides?.ApiKeyOverride ?? GetApiKey(providerId);
        var model = voterOverrides?.ModelOverride
            ?? config.ModelOverrides.GetValueOrDefault(providerId)
            ?? LlmProviderRuntimeConfigurationResolver.GetModel(providerId)
            ?? LegionClient.DefaultModels.GetValueOrDefault(providerId, "");

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key configured for provider '{providerId}'.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(config.ProviderTimeout);
        return await client.CallAsync(providerId, key, model, systemPrompt, userMessage, maxTokens, temperature, cts.Token);
    }

    /// <summary>
    /// Resolves the API key for a provider. Checks <see cref="VotingConfiguration.ApiKeys"/>
    /// first (explicit config wins), then falls back to the shared MindAttic credential
    /// store when <see cref="VotingConfiguration.UseSharedCredentials"/> is enabled.
    /// </summary>
    public string? GetApiKey(string providerId)
    {
        // OAuth providers must always be resolved fresh — the token in ApiKeys is a
        // startup snapshot that expires mid-session.  ClaudeCodeOAuthSource auto-
        // refreshes when within 60 s of expiry, matching LegionClient.ResolveKey.
        if (string.Equals(providerId, "claude-team", StringComparison.OrdinalIgnoreCase))
            return ClaudeCodeOAuthSource.GetAccessToken();

        if (config.ApiKeys.TryGetValue(providerId, out var explicitKey)
            && !string.IsNullOrWhiteSpace(explicitKey))
            return explicitKey;

        if (config.UseSharedCredentials)
            return MindAtticCredentialStore.GetKey(providerId);

        return null;
    }
}
