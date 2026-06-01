namespace MindAttic.Legion;

/// <summary>
/// HEXACO domain scores, each a 0–100 percentile-style value. HEXACO extends
/// the Big Five with an explicit <see cref="HonestyHumility"/> factor and
/// recasts neuroticism as <see cref="Emotionality"/>. Produced by scoring an
/// IPIP-HEXACO item set the persona answered in-character.
/// </summary>
/// <param name="HonestyHumility">Sincerity, fairness, greed-avoidance, modesty — the factor unique to HEXACO.</param>
/// <param name="Emotionality">Fearfulness, anxiety, sentimentality, need for emotional support.</param>
/// <param name="Extraversion">Social self-esteem, boldness, sociability, liveliness.</param>
/// <param name="Agreeableness">Forgiveness, gentleness, flexibility, patience.</param>
/// <param name="Conscientiousness">Organisation, diligence, perfectionism, prudence.</param>
/// <param name="Openness">Aesthetic appreciation, inquisitiveness, creativity, unconventionality.</param>
public sealed record HexacoScores(
    double HonestyHumility,
    double Emotionality,
    double Extraversion,
    double Agreeableness,
    double Conscientiousness,
    double Openness)
{
    /// <summary>Compact "H64 E50 X48 A55 C71 O60" fingerprint for logs and CLI output.</summary>
    public string ShortCode() =>
        $"H{HonestyHumility:0} E{Emotionality:0} X{Extraversion:0} A{Agreeableness:0} C{Conscientiousness:0} O{Openness:0}";
}
