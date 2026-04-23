using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MindAttic.LLMVoting.Providers;

namespace MindAttic.LLMVoting;

/// <summary>
/// Extension methods for registering LLMVoting services in an IServiceCollection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="LLMVotingService"/> as a singleton with the supplied configuration.
    ///
    /// Usage:
    /// <code>
    ///   services.AddLLMVoting(new VotingConfiguration
    ///   {
    ///       ApiKeys = {
    ///           ["claude"] = settings.ApiKey,
    ///           ["openai"] = settings.OpenAiApiKey,
    ///       }
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
