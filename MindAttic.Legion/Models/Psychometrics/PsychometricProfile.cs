namespace MindAttic.Legion;

/// <summary>
/// The complete psychometric fingerprint of one <see cref="Persona"/>: results
/// from all five instruments (Big Five/OCEAN, HEXACO, MBTI-style, Enneagram-style,
/// DISC-style) plus provenance — which model administered the tests, the
/// instrument-set version, and when. One profile is the product of a single
/// assessment run; persisting many runs lets callers track drift over time.
/// </summary>
/// <param name="PersonaId">The <see cref="Persona.Id"/> this profile describes.</param>
/// <param name="Ocean">Big Five / OCEAN domain scores.</param>
/// <param name="Hexaco">HEXACO domain scores.</param>
/// <param name="Mbti">MBTI-style Jungian type.</param>
/// <param name="Enneagram">Enneagram-style typing.</param>
/// <param name="Disc">DISC-style behavioural profile.</param>
/// <param name="AdministeredByProvider">Provider id of the model that answered the instruments (e.g. "claude").</param>
/// <param name="AdministeredByModel">The concrete model id used (e.g. "claude-opus-4-8").</param>
/// <param name="InstrumentSetVersion">Version of the bundled item banks, so re-runs are comparable. See <see cref="PsychometricInstruments.SetVersion"/>.</param>
/// <param name="ScoredAtUtc">UTC timestamp the profile was produced (supplied by the caller, never <c>DateTime.Now</c>).</param>
public sealed record PsychometricProfile(
    string PersonaId,
    OceanScores Ocean,
    HexacoScores Hexaco,
    MbtiResult Mbti,
    EnneagramResult Enneagram,
    DiscResult Disc,
    string AdministeredByProvider,
    string AdministeredByModel,
    string InstrumentSetVersion,
    DateTime ScoredAtUtc)
{
    /// <summary>One-line summary across all five instruments for CLI/log output.</summary>
    public string Summary() =>
        $"{Mbti.Type} · {Enneagram.Notation()} ({Enneagram.Triad}) · DISC-{Disc.PrimaryStyle} · " +
        $"OCEAN[{Ocean.ShortCode()}] · HEXACO[{Hexaco.ShortCode()}]";
}
