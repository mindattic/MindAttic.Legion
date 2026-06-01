namespace MindAttic.Legion;

/// <summary>
/// The result of administering every bundled instrument to one persona: the
/// scored <see cref="PsychometricProfile"/> plus the raw per-item answers
/// (instrument key → item id → 1–5) so callers can persist an audit trail.
/// </summary>
public sealed record PsychometricAssessment(
    PsychometricProfile Profile,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> RawAnswers);

/// <summary>
/// Administers the bundled psychometric instruments to a <see cref="Persona"/>
/// and returns a scored profile. Implementations decide how items are answered
/// (e.g. by an LLM speaking in-character); scoring is always delegated to the
/// deterministic <see cref="PsychometricScorer"/>.
/// </summary>
public interface IPsychometricAssessor
{
    /// <summary>
    /// Administer all five instruments to <paramref name="persona"/> and score
    /// the responses. <paramref name="scoredAtUtc"/> is stamped onto the profile.
    /// </summary>
    Task<PsychometricAssessment> AssessAsync(Persona persona, DateTime scoredAtUtc, CancellationToken ct = default);
}
