using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MindAttic.Legion.Data;

/// <summary>DI wiring for the Legion SQL Server store.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="LegionDbContext"/> (SQL Server) and the persona,
    /// assessment-run, and profile repositories. The connection string is
    /// resolved via <see cref="LegionConnectionString.Resolve(string?)"/>:
    /// explicit argument → <c>MINDATTIC_LEGION_DB</c> env var → LocalDB default.
    /// </summary>
    public static IServiceCollection AddLegionData(this IServiceCollection services, string? connectionString = null)
    {
        var cs = LegionConnectionString.Resolve(connectionString);
        services.AddDbContext<LegionDbContext>(o => o.UseSqlServer(cs));
        services.AddScoped<IPersonaRepository, PersonaRepository>();
        services.AddScoped<IAssessmentRunRepository, AssessmentRunRepository>();
        services.AddScoped<IPsychometricProfileRepository, PsychometricProfileRepository>();
        return services;
    }
}
