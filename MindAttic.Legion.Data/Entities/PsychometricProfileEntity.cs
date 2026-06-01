using MindAttic.Legion;

namespace MindAttic.Legion.Data;

/// <summary>
/// A persisted psychometric profile: one persona's results from a single
/// <see cref="AssessmentRunEntity"/>. The five framework results are stored as
/// EF owned types (flat, prefixed columns) reusing the domain records, and map
/// back to a <see cref="PsychometricProfile"/> via <see cref="ToDomain"/>.
/// </summary>
public class PsychometricProfileEntity
{
    public int Id { get; set; }

    public string PersonaId { get; set; } = "";
    public PersonaEntity? Persona { get; set; }

    public int AssessmentRunId { get; set; }
    public AssessmentRunEntity? AssessmentRun { get; set; }

    public string AdministeredByProvider { get; set; } = "";
    public string AdministeredByModel { get; set; } = "";
    public string InstrumentSetVersion { get; set; } = "";
    public DateTime ScoredAtUtc { get; set; }

    // Owned value objects — reuse the domain records; EF maps them to prefixed
    // columns (Ocean_Openness, Hexaco_HonestyHumility, Mbti_Type, ...).
    public OceanScores Ocean { get; set; } = default!;
    public HexacoScores Hexaco { get; set; } = default!;
    public MbtiResult Mbti { get; set; } = default!;
    public EnneagramResult Enneagram { get; set; } = default!;
    public DiscResult Disc { get; set; } = default!;

    /// <summary>Project this row back to the domain profile record.</summary>
    public PsychometricProfile ToDomain() => new(
        PersonaId, Ocean, Hexaco, Mbti, Enneagram, Disc,
        AdministeredByProvider, AdministeredByModel, InstrumentSetVersion, ScoredAtUtc);

    /// <summary>Build a persistable row from a scored domain profile within a run.</summary>
    public static PsychometricProfileEntity FromDomain(PsychometricProfile p, int assessmentRunId) => new()
    {
        PersonaId = p.PersonaId,
        AssessmentRunId = assessmentRunId,
        AdministeredByProvider = p.AdministeredByProvider,
        AdministeredByModel = p.AdministeredByModel,
        InstrumentSetVersion = p.InstrumentSetVersion,
        ScoredAtUtc = p.ScoredAtUtc,
        Ocean = p.Ocean,
        Hexaco = p.Hexaco,
        Mbti = p.Mbti,
        Enneagram = p.Enneagram,
        Disc = p.Disc,
    };
}
