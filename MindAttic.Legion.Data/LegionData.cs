using Microsoft.EntityFrameworkCore;

namespace MindAttic.Legion.Data;

/// <summary>
/// Convenience helpers for using the Legion store without a DI container — the
/// shape the CLI uses. Builds a context (and repositories over it) against a
/// resolved connection string.
/// </summary>
public static class LegionData
{
    /// <summary>Build a <see cref="LegionDbContext"/> against the resolved connection string.</summary>
    public static LegionDbContext CreateContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<LegionDbContext>()
            .UseSqlServer(LegionConnectionString.Resolve(connectionString))
            .Options);

    /// <summary>Ensure the database exists and all migrations are applied.</summary>
    public static async Task MigrateAsync(LegionDbContext db, CancellationToken ct = default) =>
        await db.Database.MigrateAsync(ct);
}
