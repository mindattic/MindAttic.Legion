using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;

namespace MindAttic.Legion.Data;

/// <inheritdoc />
public sealed class PsychometricProfileRepository : IPsychometricProfileRepository
{
    private readonly LegionDbContext db;
    public PsychometricProfileRepository(LegionDbContext db) => this.db = db;

    /// <inheritdoc />
    public async Task<PsychometricProfileEntity> SaveAsync(
        PsychometricProfile profile,
        int runId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>? rawAnswers = null,
        CancellationToken ct = default)
    {
        var entity = PsychometricProfileEntity.FromDomain(profile, runId);
        db.PsychometricProfiles.Add(entity);

        if (rawAnswers is not null)
        {
            foreach (var (instrument, answers) in rawAnswers)
            foreach (var (itemId, value) in answers)
                db.ItemResponses.Add(new AssessmentItemResponseEntity
                {
                    AssessmentRunId = runId,
                    PersonaId = profile.PersonaId,
                    Instrument = instrument,
                    ItemId = itemId,
                    Value = value,
                });
        }

        await db.SaveChangesAsync(ct);
        return entity;
    }

    /// <inheritdoc />
    public Task<PsychometricProfileEntity?> GetLatestAsync(string personaId, CancellationToken ct = default) =>
        db.PsychometricProfiles.AsNoTracking()
            .Where(p => p.PersonaId == personaId)
            .OrderByDescending(p => p.AssessmentRunId)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<List<PsychometricProfileEntity>> HistoryAsync(string personaId, CancellationToken ct = default) =>
        db.PsychometricProfiles.AsNoTracking()
            .Where(p => p.PersonaId == personaId)
            .OrderBy(p => p.AssessmentRunId)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<List<PsychometricProfileEntity>> ByRunAsync(int runId, CancellationToken ct = default) =>
        db.PsychometricProfiles.AsNoTracking()
            .Where(p => p.AssessmentRunId == runId)
            .OrderBy(p => p.PersonaId)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<List<PsychometricProfileEntity>> LatestPerPersonaAsync(CancellationToken ct = default)
    {
        // Owned columns live in the same table, so a single load brings them
        // along; grouping in memory keeps the query trivially translatable and
        // is cheap at library scale (~1k rows per run).
        var all = await db.PsychometricProfiles.AsNoTracking().ToListAsync(ct);
        return all
            .GroupBy(p => p.PersonaId)
            .Select(g => g.OrderByDescending(p => p.AssessmentRunId).First())
            .OrderBy(p => p.PersonaId)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> PersonaIdsInRunAsync(int runId, CancellationToken ct = default)
    {
        var ids = await db.PsychometricProfiles
            .Where(p => p.AssessmentRunId == runId)
            .Select(p => p.PersonaId)
            .ToListAsync(ct);
        return new HashSet<string>(ids, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> PersonaIdsScoredAsync(string? instrumentSetVersion = null, CancellationToken ct = default)
    {
        var query = db.PsychometricProfiles.AsQueryable();
        if (!string.IsNullOrWhiteSpace(instrumentSetVersion))
            query = query.Where(p => p.InstrumentSetVersion == instrumentSetVersion);
        var ids = await query.Select(p => p.PersonaId).Distinct().ToListAsync(ct);
        return new HashSet<string>(ids, StringComparer.Ordinal);
    }
}
