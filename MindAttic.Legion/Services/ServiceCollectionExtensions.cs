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
        services.AddLegionClient();
        // Replace the typed-client LegionClient registration with one that
        // also consults VotingConfiguration.ApiKeys, so direct LegionClient
        // consumers and the voting layer see the same key set.
        services.AddTransient<LegionClient>(sp =>
        {
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LegionClient));
            return new LegionClient(http, options: null, keyResolver: BuildVotingKeyResolver(config));
        });
        return services;
    }

    /// <summary>
    /// Register <see cref="LegionClient"/> as the universal LLM-call entry point.
    /// Apps that don't need voting can call this directly to get the same connection
    /// scaffolding (endpoints, auth headers, request/response shape, model defaults,
    /// shared-credential lookup) without pulling in the voting machinery.
    ///
    /// Usage in any MindAttic app:
    /// <code>
    ///   services.AddLegionClient();
    ///   // ...
    ///   public class MyService(LegionClient legion) {
    ///       public Task&lt;string&gt; AskClaude(string prompt) =&gt;
    ///           legion.CallAsync("claude", systemPrompt: "...", userMessage: prompt);
    ///   }
    /// </code>
    /// </summary>
    public static IServiceCollection AddLegionClient(this IServiceCollection services)
    {
        services.AddHttpClient<LegionClient>();
        // LlmHealthCheck is transient on purpose: AddHttpClient<LegionClient>()
        // registers LegionClient as transient (typed-client default), and a
        // singleton LlmHealthCheck would capture the first LegionClient ever
        // resolved -- pinning its underlying HttpMessageHandler indefinitely
        // and bypassing IHttpClientFactory's handler-rotation policy.
        services.AddTransient<LlmHealthCheck>();
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
        // Mirror the eager-config overload: any consumer who injects
        // LegionClient or LlmHealthCheck alongside the voting service should
        // be able to resolve them without a second registration call.
        services.AddLegionClient();
        services.AddTransient<LegionClient>(sp =>
        {
            var cfg  = sp.GetRequiredService<VotingConfiguration>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(LegionClient));
            return new LegionClient(http, options: null, keyResolver: BuildVotingKeyResolver(cfg));
        });
        return services;
    }

    /// <summary>
    /// Builds the LegionClient key-resolver used when voting is registered:
    /// VotingConfiguration.ApiKeys wins, then the shared store (when
    /// UseSharedCredentials is on), then null. Mirrors LlmVotingProvider's
    /// own key-resolution order so direct LegionClient consumers and the
    /// voting layer agree on which key applies to a given provider.
    /// </summary>
    private static Func<string, string?> BuildVotingKeyResolver(VotingConfiguration config) => providerId =>
    {
        if (config.ApiKeys.TryGetValue(providerId, out var k) && !string.IsNullOrWhiteSpace(k))
            return k;
        return config.UseSharedCredentials ? MindAtticCredentialStore.GetKey(providerId) : null;
    };
}
