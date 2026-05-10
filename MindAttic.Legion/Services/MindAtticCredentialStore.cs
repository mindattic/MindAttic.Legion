using MindAttic.Vault.Credentials;
using MindAttic.Vault.Paths;

namespace MindAttic.Legion;

/// <summary>
/// Backward-compatible facade. The implementation now lives in
/// <see cref="LlmCredentialStore"/> from <c>MindAttic.Vault</c>; this static
/// class preserves every public symbol Legion and downstream apps were already
/// calling so the swap to Vault is invisible to consumers.
///
/// <para>Each method constructs a fresh <see cref="LlmCredentialStore"/> bound
/// to the directory currently named by the <c>MINDATTIC_LLM_CREDENTIALS</c>
/// env var (or <c>%APPDATA%\MindAttic\LLM</c> when unset). This matches the
/// pre-Vault behaviour where the env var was re-evaluated on every property
/// access — required for Legion's tests that redirect credentials per-test
/// (e.g. <c>LlmVotingServiceTests</c>, <c>ResilienceTests</c>).</para>
///
/// <para>New code should inject <see cref="LlmCredentialStore"/> (file-only)
/// or <see cref="LlmCredentialResolver"/> (cloud-native composite over
/// <c>IConfiguration</c>) via DI rather than calling this static facade.
/// See <c>MindAttic.Vault</c> README for the recommended wiring.</para>
/// </summary>
public static class MindAtticCredentialStore
{
    private static LlmCredentialStore Resolve() =>
        new LlmCredentialStore(
            Environment.GetEnvironmentVariable(LlmCredentialStore.DirectoryEnvVar)
            ?? VaultPaths.RoamingBucket(LlmCredentialStore.Bucket));

    /// <summary>Full path to the shared credential directory. Re-evaluates the env var on every access.</summary>
    public static string CredentialDirectory  => Resolve().Directory;

    /// <summary>Full path to providers.json inside the shared credential directory.</summary>
    public static string ProvidersFilePath    => Resolve().ProvidersFilePath;

    /// <summary>True if providers.json exists at the canonical location.</summary>
    public static bool   ProvidersFileExists() => Resolve().ProvidersFileExists();

    /// <summary>Returns the key for a provider, or null if no credential is on disk.</summary>
    public static string?                    GetKey(string providerId)             => Resolve().GetKey(providerId);

    /// <summary>Writes a key for a provider, preserving any existing type/model/maxTokens fields.</summary>
    public static void                       SetKey(string providerId, string key) => Resolve().SetKey(providerId, key);

    /// <summary>Loads every credential as a flat dictionary (providerId → apiKey).</summary>
    public static Dictionary<string, string> LoadAll()                              => Resolve().LoadAll();

    /// <summary>Provider IDs that currently have a non-empty credential on disk.</summary>
    public static List<string>               ListProviders()                        => Resolve().ListProviders();

    /// <summary>Returns providers.json as a map of providerId → raw per-provider JSON object string.</summary>
    public static Dictionary<string, string> LoadAllRaw()                           => Resolve().LoadAllRaw();

    /// <summary>Replaces the entire providers.json with the supplied map.</summary>
    public static void                       SaveAllRaw(IDictionary<string, string> providers) => Resolve().SaveAllRaw(providers);

    /// <summary>Upserts a single provider's raw per-provider JSON, preserving every other entry.</summary>
    public static void                       SaveRaw(string providerId, string raw) => Resolve().SaveRaw(providerId, raw);
}
