namespace MindAttic.LLMVoting;

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
    /// </summary>
    public static double Threshold(this Quorum quorum) => quorum switch
    {
        Quorum.Plurality      => 0.0,
        Quorum.SimpleMajority => 0.50,
        Quorum.TwoThirds      => 0.67,
        Quorum.Unanimous      => 1.00,
        _                     => 0.50,
    };
}
