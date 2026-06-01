namespace MindAttic.Legion;

/// <summary>
/// A 4-letter Jungian type ("MBTI-style") plus the strength of each dichotomy.
/// Derived from the open OEJTS instrument the persona answered in-character;
/// the trademarked MBTI® questionnaire is never used. Each *Pct field is the
/// percentage lean toward the FIRST pole of its axis (0–100); the letter in
/// <see cref="Type"/> is that pole when the value is ≥ 50.
/// </summary>
/// <param name="Type">The composite type, e.g. "INTJ".</param>
/// <param name="ExtraversionPct">% lean toward Extraversion (vs Introversion).</param>
/// <param name="SensingPct">% lean toward Sensing (vs iNtuition).</param>
/// <param name="ThinkingPct">% lean toward Thinking (vs Feeling).</param>
/// <param name="JudgingPct">% lean toward Judging (vs Perceiving).</param>
public sealed record MbtiResult(
    string Type,
    double ExtraversionPct,
    double SensingPct,
    double ThinkingPct,
    double JudgingPct);
