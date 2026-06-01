namespace MindAttic.Legion.Data;

/// <summary>
/// One psychometric assessment batch — the unit of versioning. Every
/// <see cref="PsychometricProfileEntity"/> belongs to exactly one run, so the
/// latest run for a persona is its current profile and older runs form the
/// trend history. Records who administered it (provider/model/tier) and the
/// instrument-set version, so drift across re-runs is interpretable.
/// </summary>
public class AssessmentRunEntity
{
    public int Id { get; set; }

    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    /// <summary>Provider that administered the instruments (e.g. "claude").</summary>
    public string Provider { get; set; } = "";

    /// <summary>Concrete model id used (e.g. "claude-opus-4-8").</summary>
    public string Model { get; set; } = "";

    /// <summary>Capability tier requested (e.g. "High").</summary>
    public string Tier { get; set; } = "";

    /// <summary>Version of the bundled item banks used for this run.</summary>
    public string InstrumentSetVersion { get; set; } = "";

    /// <summary>Number of personas this run intended to score.</summary>
    public int PersonaCount { get; set; }

    /// <summary>Number of personas successfully scored so far.</summary>
    public int CompletedCount { get; set; }

    /// <summary>Free-form note (e.g. "pilot", "rescore --changed-only").</summary>
    public string? Notes { get; set; }

    public List<PsychometricProfileEntity> Profiles { get; set; } = new();
}
