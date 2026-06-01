using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MindAttic.Legion.Data;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can build the context
/// without a running host. Uses <see cref="LegionConnectionString.Resolve()"/>,
/// so the EF tools talk to the same database the app would.
/// </summary>
public sealed class LegionDbContextFactory : IDesignTimeDbContextFactory<LegionDbContext>
{
    public LegionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LegionDbContext>()
            .UseSqlServer(LegionConnectionString.Resolve())
            .Options;
        return new LegionDbContext(options);
    }
}
