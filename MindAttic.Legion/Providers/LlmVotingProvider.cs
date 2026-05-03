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
    /// Constructs the voting provider. Sets the underlying
    /// <see cref="HttpClient"/>'s timeout to <see cref="VotingConfiguration.ProviderTimeout"/>
    /// and wraps the client in a <see cref="LegionClient"/>.
    /// </summary>
    public LlmVotingProvider(HttpClient http, VotingConfiguration config)
    {
        http.Timeout = config.ProviderTimeout;
        this.client = new LegionClient(http);
        this.config = config;
    }

    /// <summary>
    /// Call a provider with a system prompt + user message.
    /// Uses per-voter API key and model overrides if set on the profile.
    /// </summary>
    public Task<string> CallAsync(
        string providerId,
        string systemPrompt,
        string userMessage,
        int maxTokens,
        double temperature,
        VoterProfile? voterOverrides = null,
        CancellationToken ct = default)
    {
        var info  = LlmProviderCatalog.Get(providerId);
        var key   = voterOverrides?.ApiKeyOverride ?? GetApiKey(providerId);
        var model = voterOverrides?.ModelOverride
            ?? config.ModelOverrides.GetValueOrDefault(providerId)
            ?? LlmProviderRuntimeConfigurationResolver.GetModel(providerId)
            ?? LegionClient.DefaultModels.GetValueOrDefault(providerId, "");

        if (info?.RequiresApiKey != false && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException($"No API key configured for provider '{providerId}'.");

        return client.CallAsync(providerId, key ?? "", model, systemPrompt, userMessage, maxTokens, temperature, ct);
    }

    /// <summary>
    /// Resolves the API key for a provider. Checks <see cref="VotingConfiguration.ApiKeys"/>
    /// first (explicit config wins), then falls back to the shared MindAttic credential
    /// store when <see cref="VotingConfiguration.UseSharedCredentials"/> is enabled.
    /// </summary>
    public string? GetApiKey(string providerId)
    {
        if (config.ApiKeys.TryGetValue(providerId, out var explicitKey)
            && !string.IsNullOrWhiteSpace(explicitKey))
            return explicitKey;

        if (config.UseSharedCredentials)
            return MindAtticCredentialStore.GetKey(providerId);

        return null;
    }
}
