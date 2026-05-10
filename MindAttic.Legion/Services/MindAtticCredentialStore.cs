using Microsoft.Extensions.Configuration;
using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;

namespace MindAttic.Legion;

/// <summary>
/// Backward-compatible facade. The implementation now lives in
/// <see cref="LlmCredentialStore"/> from <c>MindAttic.Vault</c>; this static
/// class preserves every public symbol Legion and downstream apps were already
/// calling so the swap to Vault is invisible to consumers.
///
/// <para>Each method constructs a fresh credential store bound to the directory
/// currently named by the <c>MINDATTIC_LLM_CREDENTIALS</c> env var (or
/// <c>%APPDATA%\MindAttic\LLM</c> when unset). This matches the pre-Vault
/// behaviour where the env var was re-evaluated on every property access —
/// required for Legion's tests that redirect credentials per-test
/// (e.g. <c>LlmVotingServiceTests</c>, <c>ResilienceTests</c>).</para>
///
/// <para>When a host has registered an <see cref="IConfiguration"/> via
/// <see cref="UseConfiguration"/>, reads consult
/// <see cref="VaultConfigurationKeys.LlmSection"/> first (User Secrets,
/// App Service Application Settings, Azure Key Vault — whichever the host
/// composed) and fall back to the file-backed store. Writes always land in
/// the writable file store.</para>
///
/// <para>New code may inject <see cref="LlmCredentialStore"/> (file-only) or
/// <see cref="LlmCredentialResolver"/> (cloud-native composite) via DI
/// instead of calling this static facade. See <c>MindAttic.Vault</c> README
/// for the recommended wiring.</para>
/// </summary>
public static class MindAtticCredentialStore
{
    private static IConfiguration? configuration;

    /// <summary>
    /// Register an <see cref="IConfiguration"/> as the highest-priority credential
    /// source. Pass <c>null</c> to clear (e.g. between tests). Idempotent and
    /// safe to call from a host's startup (<c>Program.cs</c>, <c>LegionCli</c>, etc.).
    /// </summary>
    public static void UseConfiguration(IConfiguration? configuration)
    {
        MindAtticCredentialStore.configuration = configuration;
    }

    private static ICredentialStore Resolve()
    {
        var fileStore = new LlmCredentialStore(
            Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar)
            ?? VaultPaths.RoamingBucket(LlmCredentialStore.Bucket));

        var cfg = configuration;
        return cfg is null
            ? fileStore
            : new CompositeCredentialStore(
                ConfigurationCredentialStore.ForLlm(cfg),
                fileStore);
    }

    /// <summary>Full path to the writable credential directory. Re-evaluates the env var on every access.</summary>
    public static string CredentialDirectory  => Resolve().Directory;

    /// <summary>Full path to providers.json inside the writable credential directory.</summary>
    public static string ProvidersFilePath    => Resolve().ProvidersFilePath;

    /// <summary>True if providers.json exists in any registered store.</summary>
    public static bool   ProvidersFileExists() => Resolve().ProvidersFileExists();

    /// <summary>Returns the key for a provider, or null if no credential is registered.</summary>
    public static string?                    GetKey(string providerId)             => Resolve().GetKey(providerId);

    /// <summary>Writes a key for a provider, preserving any existing type/model/maxTokens fields. Lands in the writable file store.</summary>
    public static void                       SetKey(string providerId, string key) => Resolve().SetKey(providerId, key);

    /// <summary>Loads every credential as a flat dictionary (providerId → apiKey), merged across registered stores.</summary>
    public static Dictionary<string, string> LoadAll()                              => Resolve().LoadAll();

    /// <summary>Provider IDs that currently have a non-empty credential in any registered store.</summary>
    public static List<string>               ListProviders()                        => Resolve().ListProviders();

    /// <summary>Returns providers.json as a map of providerId → raw per-provider JSON object string.</summary>
    public static Dictionary<string, string> LoadAllRaw()                           => Resolve().LoadAllRaw();

    /// <summary>Replaces the entire providers.json with the supplied map.</summary>
    public static void                       SaveAllRaw(IDictionary<string, string> providers) => Resolve().SaveAllRaw(providers);

    /// <summary>Upserts a single provider's raw per-provider JSON, preserving every other entry.</summary>
    public static void                       SaveRaw(string providerId, string raw) => Resolve().SaveRaw(providerId, raw);
}
