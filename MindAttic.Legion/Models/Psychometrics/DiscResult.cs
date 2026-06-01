namespace MindAttic.Legion;

/// <summary>
/// A DISC-style behavioural profile: a 0–100 score on each of the four
/// dimensions plus the dominant <see cref="PrimaryStyle"/>. Derived from an
/// open adjective/forced-choice item set the persona answered in-character;
/// no proprietary DISC® assessment is used.
/// </summary>
/// <param name="Dominance">Directness, assertiveness, results-focus.</param>
/// <param name="Influence">Sociability, persuasion, optimism.</param>
/// <param name="Steadiness">Patience, cooperation, consistency.</param>
/// <param name="Conscientiousness">Precision, caution, focus on accuracy and rules.</param>
/// <param name="PrimaryStyle">The highest-scoring dimension as a single letter: "D", "I", "S", or "C".</param>
public sealed record DiscResult(
    double Dominance,
    double Influence,
    double Steadiness,
    double Conscientiousness,
    string PrimaryStyle)
{
    /// <summary>Compact "D62 I40 S55 C48" fingerprint for logs and CLI output.</summary>
    public string ShortCode() =>
        $"D{Dominance:0} I{Influence:0} S{Steadiness:0} C{Conscientiousness:0}";
}
