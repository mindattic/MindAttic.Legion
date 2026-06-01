namespace MindAttic.Legion;

/// <summary>
/// Deterministic scoring for the bundled <see cref="PsychometricInstruments"/>.
/// Takes the raw Likert answers a persona gave (item id → 1–5) and produces the
/// framework result. Pure and side-effect-free: the same answers always yield
/// the same scores, so this is fully unit-testable without any LLM. Missing or
/// out-of-range answers fall back to the scale midpoint so a dropped item never
/// throws.
/// </summary>
public static class PsychometricScorer
{
    /// <summary>Score the Big Five / OCEAN domains from Mini-IPIP answers.</summary>
    public static OceanScores ScoreBigFive(IReadOnlyDictionary<int, int> answers)
    {
        var s = ScaleScores(PsychometricInstruments.BigFive, answers);
        return new OceanScores(s["O"], s["C"], s["E"], s["A"], s["N"]);
    }

    /// <summary>Score the six HEXACO factors from the IPIP-derived answers.</summary>
    public static HexacoScores ScoreHexaco(IReadOnlyDictionary<int, int> answers)
    {
        var s = ScaleScores(PsychometricInstruments.Hexaco, answers);
        return new HexacoScores(s["H"], s["E"], s["X"], s["A"], s["C"], s["O"]);
    }

    /// <summary>Resolve the four Jungian dichotomies and composite type from OEJTS-style answers.</summary>
    public static MbtiResult ScoreMbti(IReadOnlyDictionary<int, int> answers)
    {
        // Each axis score is the % lean toward its FIRST pole (E, S, T, J).
        var s = ScaleScores(PsychometricInstruments.Mbti, answers);
        double e = s["EI"], sn = s["SN"], tf = s["TF"], jp = s["JP"];
        var type = string.Concat(
            e  >= 50 ? "E" : "I",
            sn >= 50 ? "S" : "N",
            tf >= 50 ? "T" : "F",
            jp >= 50 ? "J" : "P");
        return new MbtiResult(type, e, sn, tf, jp);
    }

    /// <summary>Score the four DISC dimensions and pick the dominant style.</summary>
    public static DiscResult ScoreDisc(IReadOnlyDictionary<int, int> answers)
    {
        var s = ScaleScores(PsychometricInstruments.Disc, answers);
        double d = s["D"], i = s["I"], st = s["S"], c = s["C"];
        // Deterministic tie-break: D > I > S > C (catalog order).
        var primary = "D";
        var best = d;
        if (i > best) { best = i; primary = "I"; }
        if (st > best) { best = st; primary = "S"; }
        if (c > best) { best = c; primary = "C"; }
        return new DiscResult(d, i, st, c, primary);
    }

    /// <summary>Determine the dominant Enneagram type, its wing, and its triad.</summary>
    public static EnneagramResult ScoreEnneagram(IReadOnlyDictionary<int, int> answers)
    {
        var s = ScaleScores(PsychometricInstruments.Enneagram, answers);
        var byType = new double[10]; // 1..9
        for (var t = 1; t <= 9; t++) byType[t] = s.TryGetValue(t.ToString(), out var v) ? v : 50.0;

        // Dominant: highest score; tie → lowest type number (deterministic).
        var dominant = 1;
        for (var t = 2; t <= 9; t++)
            if (byType[t] > byType[dominant]) dominant = t;

        // Wing: the higher-scoring of the two circle neighbours; tie → lower number.
        var left = dominant == 1 ? 9 : dominant - 1;
        var right = dominant == 9 ? 1 : dominant + 1;
        int wing = byType[left] > byType[right] ? left
                 : byType[right] > byType[left] ? right
                 : Math.Min(left, right); // tie → lower number

        return new EnneagramResult(dominant, wing, Triad(dominant));
    }

    /// <summary>Score every instrument at once into a complete <see cref="PsychometricProfile"/>.</summary>
    public static PsychometricProfile ScoreAll(
        string personaId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> answersByInstrument,
        string administeredByProvider,
        string administeredByModel,
        DateTime scoredAtUtc,
        string instrumentSetVersion = PsychometricInstruments.SetVersion)
    {
        IReadOnlyDictionary<int, int> A(string key) =>
            answersByInstrument.TryGetValue(key, out var a) ? a : EmptyAnswers;

        return new PsychometricProfile(
            personaId,
            ScoreBigFive(A("bigfive")),
            ScoreHexaco(A("hexaco")),
            ScoreMbti(A("mbti")),
            ScoreEnneagram(A("enneagram")),
            ScoreDisc(A("disc")),
            administeredByProvider,
            administeredByModel,
            instrumentSetVersion,
            scoredAtUtc);
    }

    private static readonly IReadOnlyDictionary<int, int> EmptyAnswers = new Dictionary<int, int>();

    /// <summary>
    /// Compute a 0–100 score per scale for an instrument: reverse-key each item,
    /// average per scale, and normalize the response range onto 0–100.
    /// </summary>
    private static Dictionary<string, double> ScaleScores(
        PsychometricInstrument inst,
        IReadOnlyDictionary<int, int> answers)
    {
        var sums = new Dictionary<string, double>();
        var counts = new Dictionary<string, int>();
        var midpoint = (inst.Min + inst.Max) / 2;

        foreach (var item in inst.Items)
        {
            var raw = answers.TryGetValue(item.Id, out var v)
                ? Math.Clamp(v, inst.Min, inst.Max)
                : midpoint;
            // Reverse-keyed: agreement counts against the scale (and, for MBTI
            // axes, toward the second pole). "Toward first pole" contribution.
            var adjusted = item.Reverse ? (inst.Min + inst.Max - raw) : raw;
            sums[item.Scale] = sums.GetValueOrDefault(item.Scale) + adjusted;
            counts[item.Scale] = counts.GetValueOrDefault(item.Scale) + 1;
        }

        var result = new Dictionary<string, double>();
        var span = inst.Max - inst.Min;
        foreach (var scale in sums.Keys)
        {
            var n = counts[scale];
            var mean = sums[scale] / n;                  // in [Min, Max]
            var pct = (mean - inst.Min) / span * 100.0;  // → [0, 100]
            result[scale] = Math.Round(pct, 1);
        }
        return result;
    }

    /// <summary>The Enneagram centre-of-intelligence triad for a type.</summary>
    private static string Triad(int type) => type switch
    {
        8 or 9 or 1 => "Gut",
        2 or 3 or 4 => "Heart",
        5 or 6 or 7 => "Head",
        _ => "Unknown",
    };
}
