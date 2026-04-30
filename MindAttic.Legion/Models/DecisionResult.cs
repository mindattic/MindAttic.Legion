namespace MindAttic.Legion;

/// <summary>
/// Result of a <see cref="LLMVotingService.DecideAsync"/> call —
/// Legion's panel picks one option from a fixed list, with reasoning and
/// confidence. A thin distillation of <see cref="VotingResult"/> tailored
/// for "give me a decision" callers (e.g. an automated workflow that needs
/// to pick a field, route a request, resolve a tie, etc.).
/// </summary>
public class DecisionResult
{
    /// <summary>The original question Legion was asked to decide.</summary>
    public string Question { get; init; } = "";

    /// <summary>The full list of options that were on the ballot.</summary>
    public List<string> Options { get; init; } = [];

    /// <summary>The chosen option (exact match from <see cref="Options"/>). Empty if quorum failed.</summary>
    public string Choice { get; init; } = "";

    /// <summary>
    /// Plain-English explanation of why this option won, synthesized
    /// across the voters' rationales.
    /// </summary>
    public string Reasoning { get; init; } = "";

    /// <summary>
    /// Fraction of voters who picked <see cref="Choice"/> (0.0–1.0).
    /// 1.0 = unanimous; 0.5 = split decision among two options.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>Quorum threshold that was applied.</summary>
    public Quorum QuorumType { get; init; }

    /// <summary>
    /// True if the chosen option met the quorum threshold. False when the panel
    /// was too divided — caller should treat <see cref="Choice"/> as an opinion,
    /// not a verdict.
    /// </summary>
    public bool QuorumReached { get; init; }

    /// <summary>Each voter's individual decision and reasoning.</summary>
    public List<VoteResult> IndividualVotes { get; init; } = [];

    /// <summary>Reasons given by voters who picked something other than <see cref="Choice"/>.</summary>
    public List<string> DissenterReasons { get; init; } = [];

    /// <summary>How long the decision took to resolve.</summary>
    public TimeSpan Duration { get; init; }
}
