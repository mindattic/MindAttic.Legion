namespace MindAttic.Legion;

/// <summary>
/// An Enneagram-style typing: the dominant type (1–9), its adjacent
/// <see cref="Wing"/>, and the centre-of-intelligence <see cref="Triad"/>.
/// Derived from an open forced-choice item set the persona answered
/// in-character; no proprietary Enneagram inventory is used.
/// </summary>
/// <param name="Type">Dominant type, 1–9.</param>
/// <param name="Wing">Adjacent secondary type (the higher-scoring neighbour of <see cref="Type"/>), or null if indistinct.</param>
/// <param name="Triad">Centre of intelligence: "Gut" (8/9/1), "Heart" (2/3/4), or "Head" (5/6/7).</param>
public sealed record EnneagramResult(
    int Type,
    int? Wing,
    string Triad)
{
    /// <summary>Conventional "9w1" / "4w5" notation (bare type when no wing).</summary>
    public string Notation() => Wing is null ? Type.ToString() : $"{Type}w{Wing}";
}
