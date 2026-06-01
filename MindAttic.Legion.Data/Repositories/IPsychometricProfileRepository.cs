using MindAttic.Legion;

namespace MindAttic.Legion.Data;

/// <summary>Persists and queries scored psychometric profiles.</summary>
public interface IPsychometricProfileRepository
{
    /// <summary>
    /// Save one scored profile under <paramref name="runId"/>, optionally storing
    /// the raw per-item answers (instrument key → item id → value) as an audit
    /// trail. Returns the persisted entity.
    /// </summary>
    Task<PsychometricProfileEntity> SaveAsync(
        PsychometricProfile profile,
        int runId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>>? rawAnswers = null,
        CancellationToken ct = default);

    /// <summary>The most recent profile for a persona (latest run), or null.</summary>
    Task<PsychometricProfileEntity?> GetLatestAsync(string personaId, CancellationToken ct = default);

    /// <summary>Every profile for a persona, oldest run first — the trend history.</summary>
    Task<List<PsychometricProfileEntity>> HistoryAsync(string personaId, CancellationToken ct = default);

    /// <summary>All profiles recorded under one run.</summary>
    Task<List<PsychometricProfileEntity>> ByRunAsync(int runId, CancellationToken ct = default);

    /// <summary>The latest profile per persona across all runs — the current snapshot for stats.</summary>
    Task<List<PsychometricProfileEntity>> LatestPerPersonaAsync(CancellationToken ct = default);

    /// <summary>Persona ids already scored in a specific run (for resuming that run).</summary>
    Task<HashSet<string>> PersonaIdsInRunAsync(int runId, CancellationToken ct = default);

    /// <summary>
    /// Persona ids that already have any profile, optionally restricted to a
    /// given instrument-set version (for skipping already-scored personas).
    /// </summary>
    Task<HashSet<string>> PersonaIdsScoredAsync(string? instrumentSetVersion = null, CancellationToken ct = default);
}
