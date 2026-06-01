namespace MindAttic.Legion.Data;

/// <summary>Creates and tracks versioned assessment runs.</summary>
public interface IAssessmentRunRepository
{
    /// <summary>Open a new run row and return it (with its generated id).</summary>
    Task<AssessmentRunEntity> StartAsync(
        string provider, string model, string tier, string instrumentSetVersion,
        int personaCount, DateTime startedUtc, string? notes = null, CancellationToken ct = default);

    /// <summary>Update a run's progress counter (called as profiles are saved).</summary>
    Task SetProgressAsync(int runId, int completedCount, CancellationToken ct = default);

    /// <summary>Mark a run finished.</summary>
    Task CompleteAsync(int runId, DateTime completedUtc, CancellationToken ct = default);

    /// <summary>Fetch a run by id, or null.</summary>
    Task<AssessmentRunEntity?> GetAsync(int runId, CancellationToken ct = default);

    /// <summary>Most recent runs, newest first.</summary>
    Task<List<AssessmentRunEntity>> RecentAsync(int take = 20, CancellationToken ct = default);
}
