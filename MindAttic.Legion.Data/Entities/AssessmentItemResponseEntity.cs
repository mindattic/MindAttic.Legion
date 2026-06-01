namespace MindAttic.Legion.Data;

/// <summary>
/// One raw item answer captured during an assessment — the per-item audit trail
/// behind a scored profile. Only persisted when a run is started with the
/// <c>--store-raw</c> option; profiles are fully reproducible from these plus
/// the instrument-set version.
/// </summary>
public class AssessmentItemResponseEntity
{
    public long Id { get; set; }

    public int AssessmentRunId { get; set; }
    public AssessmentRunEntity? AssessmentRun { get; set; }

    public string PersonaId { get; set; } = "";

    /// <summary>Instrument key: "bigfive", "hexaco", "mbti", "disc", "enneagram".</summary>
    public string Instrument { get; set; } = "";

    /// <summary>The item id (1–110) within the instrument set.</summary>
    public int ItemId { get; set; }

    /// <summary>The 1–5 Likert value the persona gave.</summary>
    public int Value { get; set; }
}
