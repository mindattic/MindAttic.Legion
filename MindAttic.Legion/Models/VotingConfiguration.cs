namespace MindAttic.Legion;

/// <summary>
/// Configuration for the LLMVotingService.
/// Pass this at startup — the service reads API keys and model preferences from here.
///
/// Example usage in StreetSamurai:
/// <code>
///   var config = new VotingConfiguration
///   {
///       ApiKeys = {
///           ["claude"]   = settings.ApiKey,
///           ["openai"]   = settings.OpenAiApiKey,
///           ["gemini"]   = settings.GeminiApiKey,
///       }
///   };
/// </code>
/// </summary>
public class VotingConfiguration
{
    /// <summary>
    /// Map of provider ID → API key. Explicit entries here win over the shared
    /// credential store (useful for tests or app-specific overrides).
    /// Supported provider IDs: claude, openai, gemini, deepseek, mistral,
    ///   xai, groq, together, openrouter, fireworks, cohere.
    /// </summary>
    public Dictionary<string, string> ApiKeys { get; set; } = new();

    /// <summary>
    /// When true, missing keys are resolved from the shared MindAttic credential
    /// store at <see cref="MindAtticCredentialStore.CredentialDirectory"/>.
    /// Set to false to sandbox an app to only the keys passed in ApiKeys.
    /// </summary>
    public bool UseSharedCredentials { get; set; } = true;

    /// <summary>
    /// Optional model overrides per provider (e.g., "claude" → "claude-opus-4-6").
    /// Falls back to each provider's default when not set.
    /// </summary>
    public Dictionary<string, string> ModelOverrides { get; set; } = new();

    /// <summary>Timeout per individual provider call.</summary>
    public TimeSpan ProviderTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Default max tokens per voter response.</summary>
    public int DefaultMaxTokens { get; set; } = 2048;

    /// <summary>
    /// The provider to use as a "judge" when synthesizing consensus from
    /// free-form votes. Defaults to "claude". Falls back to first available.
    /// </summary>
    public string JudgeProviderId { get; set; } = "claude";

    /// <summary>
    /// Global voter persona applied to all votes unless overridden per-request.
    /// Empty = no default persona (raw LLM calls).
    /// </summary>
    public string DefaultPersonalityMarkdown { get; set; } = "";

    /// <summary>
    /// Returns provider IDs that have a resolvable API key — either explicit in
    /// <see cref="ApiKeys"/> or present in the shared credential store when
    /// <see cref="UseSharedCredentials"/> is enabled.
    /// </summary>
    public List<string> ActiveProviderIds
    {
        get
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ApiKeys)
                if (!string.IsNullOrWhiteSpace(kv.Value)) ids.Add(kv.Key);
            if (UseSharedCredentials)
                foreach (var id in MindAtticCredentialStore.ListProviders()) ids.Add(id);
            return ids.ToList();
        }
    }
}
