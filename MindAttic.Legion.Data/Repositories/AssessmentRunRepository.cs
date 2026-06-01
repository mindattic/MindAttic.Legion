using Microsoft.EntityFrameworkCore;

namespace MindAttic.Legion.Data;

/// <inheritdoc />
public sealed class AssessmentRunRepository : IAssessmentRunRepository
{
    private readonly LegionDbContext db;
    public AssessmentRunRepository(LegionDbContext db) => this.db = db;

    /// <inheritdoc />
    public async Task<AssessmentRunEntity> StartAsync(
        string provider, string model, string tier, string instrumentSetVersion,
        int personaCount, DateTime startedUtc, string? notes = null, CancellationToken ct = default)
    {
        var run = new AssessmentRunEntity
        {
            Provider = provider,
            Model = model,
            Tier = tier,
            InstrumentSetVersion = instrumentSetVersion,
            PersonaCount = personaCount,
            CompletedCount = 0,
            StartedUtc = startedUtc,
            Notes = notes,
        };
        db.AssessmentRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    /// <inheritdoc />
    public async Task SetProgressAsync(int runId, int completedCount, CancellationToken ct = default)
    {
        var run = await db.AssessmentRuns.FindAsync([runId], ct);
        if (run is null) return;
        run.CompletedCount = completedCount;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(int runId, DateTime completedUtc, CancellationToken ct = default)
    {
        var run = await db.AssessmentRuns.FindAsync([runId], ct);
        if (run is null) return;
        run.CompletedUtc = completedUtc;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<AssessmentRunEntity?> GetAsync(int runId, CancellationToken ct = default) =>
        db.AssessmentRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);

    /// <inheritdoc />
    public Task<List<AssessmentRunEntity>> RecentAsync(int take = 20, CancellationToken ct = default) =>
        db.AssessmentRuns.OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
}
