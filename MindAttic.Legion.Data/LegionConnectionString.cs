namespace MindAttic.Legion.Data;

/// <summary>
/// Resolves the SQL Server connection string for the Legion store. Precedence:
/// an explicit argument, then the <c>MINDATTIC_LEGION_DB</c> environment
/// variable, then a local <c>(localdb)\MSSQLLocalDB</c> database named
/// <c>MindAtticLegion</c> — zero-config for development on Windows.
/// </summary>
public static class LegionConnectionString
{
    /// <summary>Environment variable that overrides the connection string.</summary>
    public const string EnvVar = "MINDATTIC_LEGION_DB";

    /// <summary>Default LocalDB connection used when nothing else is configured.</summary>
    public const string Default =
        @"Server=(localdb)\MSSQLLocalDB;Database=MindAtticLegion;Trusted_Connection=True;TrustServerCertificate=True;";

    /// <summary>
    /// Pick the connection string: <paramref name="explicit"/> if non-empty,
    /// else the <see cref="EnvVar"/> environment variable, else <see cref="Default"/>.
    /// </summary>
    public static string Resolve(string? @explicit = null)
    {
        if (!string.IsNullOrWhiteSpace(@explicit)) return @explicit;
        var env = Environment.GetEnvironmentVariable(EnvVar);
        return !string.IsNullOrWhiteSpace(env) ? env : Default;
    }
}
