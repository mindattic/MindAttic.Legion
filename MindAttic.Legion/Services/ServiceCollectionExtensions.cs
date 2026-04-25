using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindAttic.Legion.Providers;

namespace MindAttic.Legion;

/// <summary>
/// Extension methods for registering LLMVoting services in an IServiceCollection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="LLMVotingService"/> as a singleton with the supplied configuration.
    ///
    /// API keys are resolved in this order per provider:
    ///   1. <see cref="VoterProfile.ApiKeyOverride"/> (per-voter)
    ///   2. <see cref="VotingConfiguration.ApiKeys"/> (explicit config)
    ///   3. Shared <see cref="MindAtticCredentialStore"/> folder — the default source
    ///      shared across all MindAttic apps (%APPDATA%/MindAttic/LLM on Windows).
    ///
    /// Apps can leave <see cref="VotingConfiguration.ApiKeys"/> empty and rely
    /// entirely on the shared store — set UseSharedCredentials=false to sandbox.
    ///
    /// Usage:
    /// <code>
    ///   // Zero-config: reads all keys from %APPDATA%/MindAttic/LLM/
    ///   services.AddLLMVoting(new VotingConfiguration());
    ///
    ///   // Or mix explicit + shared:
    ///   services.AddLLMVoting(new VotingConfiguration
    ///   {
    ///       ApiKeys = { ["claude"] = settings.ApiKey } // overrides shared store
    ///   });
    /// </code>
    /// </summary>
    public static IServiceCollection AddLLMVoting(
        this IServiceCollection services,
        VotingConfiguration config)
    {
        services.AddSingleton(config);
        services.AddHttpClient<LlmVotingProvider>();
        services.AddSingleton<LlmVotingProvider>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LlmVotingProvider));
            return new LlmVotingProvider(http, config);
        });
        services.AddSingleton<LLMVotingService>();
        return services;
    }

    /// <summary>
    /// Register <see cref="LLMVotingService"/> with configuration resolved from the DI container at runtime.
    /// Useful when the configuration is built dynamically from another service (e.g., SettingsService).
    /// </summary>
    public static IServiceCollection AddLLMVoting(
        this IServiceCollection services,
        Func<IServiceProvider, VotingConfiguration> configFactory)
    {
        services.AddSingleton(configFactory);
        services.AddHttpClient<LlmVotingProvider>();
        services.AddSingleton<LlmVotingProvider>(sp =>
        {
            var cfg  = sp.GetRequiredService<VotingConfiguration>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LlmVotingProvider));
            return new LlmVotingProvider(http, cfg);
        });
        services.AddSingleton<LLMVotingService>();
        return services;
    }
}
