namespace MindAttic.Legion;

/// <summary>
/// How many voters must agree before a decision is binding.
/// </summary>
public enum Quorum
{
    /// <summary>Most votes wins — even a single voter can decide if they're alone. Ties broken by first response.</summary>
    Plurality,

    /// <summary>More than half (>50%) must agree.</summary>
    SimpleMajority,

    /// <summary>At least two-thirds (≥67%) must agree.</summary>
    TwoThirds,

    /// <summary>Every configured voter must agree. One dissenter blocks consensus.</summary>
    Unanimous,
}

public static class QuorumExtensions
{
    /// <summary>
    /// Return the minimum fraction of votes required for the quorum to pass.
    /// <para>
    /// <see cref="Quorum.TwoThirds"/> is exactly 2/3 (≈0.6666…) so the canonical
    /// 2-of-3 case clears its own threshold. A rounded 0.67 would fail it.
    /// </para>
    /// </summary>
    public static double Threshold(this Quorum quorum) => quorum switch
    {
        Quorum.Plurality      => 0.0,
        Quorum.SimpleMajority => 0.50,
        Quorum.TwoThirds      => 2.0 / 3.0,
        Quorum.Unanimous      => 1.00,
        _                     => 0.50,
    };

    /// <summary>
    /// True when <paramref name="agree"/> of <paramref name="total"/> voters
    /// satisfy this quorum. Uses integer arithmetic so the boundaries are exact:
    /// <list type="bullet">
    ///   <item>Plurality — any agreement (≥1).</item>
    ///   <item>SimpleMajority — strictly MORE than half (so a 2-of-4 tie fails,
    ///     matching the documented "&gt;50%"; a naive <c>fraction &gt;= 0.50</c>
    ///     would wrongly admit the tie).</item>
    ///   <item>TwoThirds — at least two-thirds (2-of-3 clears it).</item>
    ///   <item>Unanimous — every voter agrees.</item>
    /// </list>
    /// </summary>
    public static bool IsSatisfiedBy(this Quorum quorum, int agree, int total) => total > 0 && quorum switch
    {
        Quorum.Plurality      => agree > 0,
        Quorum.SimpleMajority => agree * 2 > total,
        Quorum.TwoThirds      => agree * 3 >= total * 2,
        Quorum.Unanimous      => agree >= total,
        _                     => agree * 2 > total,
    };
}
