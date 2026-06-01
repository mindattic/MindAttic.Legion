using MindAttic.Vault.Paths;

namespace MindAttic.Legion;

/// <summary>
/// Resolves the on-disk root for the persona/psychometric store. Precedence: an
/// explicit path, then the <c>MINDATTIC_LEGION_STORE</c> environment variable,
/// then the roaming MindAttic bucket (<c>%APPDATA%/MindAttic/Legion</c> on
/// Windows). Personas live as one JSON file each under <c>personas/</c>, with a
/// small <c>runs.json</c> index alongside.
/// </summary>
public static class LegionStorePaths
{
    /// <summary>Environment variable that overrides the store directory.</summary>
    public const string EnvVar = "MINDATTIC_LEGION_STORE";

    /// <summary>Resolve the store root directory (not guaranteed to exist yet).</summary>
    public static string Resolve(string? @explicit = null)
    {
        if (!string.IsNullOrWhiteSpace(@explicit)) return @explicit;
        var env = Environment.GetEnvironmentVariable(EnvVar);
        return !string.IsNullOrWhiteSpace(env) ? env : VaultPaths.RoamingBucket("Legion");
    }
}
