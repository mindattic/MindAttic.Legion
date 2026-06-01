namespace MindAttic.Legion;

/// <summary>
/// The complete on-disk representation of one persona — everything needed to
/// reconstruct it faithfully from a single JSON file: identity, the structured
/// cube traits, and the full history of psychometric assessments (one entry per
/// run). This is the storage aggregate for <see cref="PersonaStore"/>.
/// </summary>
/// <param name="Id">Stable persona id (matches <see cref="Persona.Id"/>).</param>
/// <param name="Name">Display name.</param>
/// <param name="PersonalityMarkdown">The system-prompt overlay.</param>
/// <param name="Traits">Structured cube axes + enrichments (null axes for default personas).</param>
/// <param name="Assessments">Every recorded assessment, newest run last.</param>
public sealed record PersonaDocument(
    string Id,
    string Name,
    string PersonalityMarkdown,
    PersonaDetail Traits,
    List<StoredAssessment> Assessments);

/// <summary>
/// One psychometric assessment of a persona, tagged with the run it belongs to
/// and optionally the raw per-item answers that produced it.
/// </summary>
/// <param name="RunId">The <see cref="RunRecord.Id"/> this assessment was part of.</param>
/// <param name="Profile">The scored profile (carries its own provenance + instrument-set version).</param>
/// <param name="RawAnswers">Optional raw item answers (instrument key → item id → 1–5), stored only when requested.</param>
public sealed record StoredAssessment(
    int RunId,
    PsychometricProfile Profile,
    Dictionary<string, Dictionary<int, int>>? RawAnswers = null);

/// <summary>
/// Metadata for one versioned scoring batch. The lightweight equivalent of the
/// old assessment-run table — kept in a single <c>runs.json</c> index so drift
/// across runs can still be queried without a database.
/// </summary>
public sealed record RunRecord(
    int Id,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    string Provider,
    string Model,
    string Tier,
    string InstrumentSetVersion,
    int PersonaCount,
    int CompletedCount,
    string? Notes);
