namespace MindAttic.Legion;

/// <summary>
/// Big Five / OCEAN domain scores, each expressed as a 0–100 percentile-style
/// value (higher = more of the trait). Produced by scoring an IPIP Big-Five
/// item set the persona answered in-character — see
/// <see cref="PsychometricScorer"/>.
/// </summary>
/// <param name="Openness">Openness to experience — curiosity, imagination, preference for novelty.</param>
/// <param name="Conscientiousness">Conscientiousness — organisation, diligence, impulse control.</param>
/// <param name="Extraversion">Extraversion — sociability, assertiveness, energy from others.</param>
/// <param name="Agreeableness">Agreeableness — compassion, cooperation, trust.</param>
/// <param name="Neuroticism">Neuroticism — proneness to anxiety, anger, and emotional volatility.</param>
public sealed record OceanScores(
    double Openness,
    double Conscientiousness,
    double Extraversion,
    double Agreeableness,
    double Neuroticism)
{
    /// <summary>Compact "O72 C58 E41 A66 N33" fingerprint for logs and CLI output.</summary>
    public string ShortCode() =>
        $"O{Openness:0} C{Conscientiousness:0} E{Extraversion:0} A{Agreeableness:0} N{Neuroticism:0}";
}
