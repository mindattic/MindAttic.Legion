namespace MindAttic.Legion;

/// <summary>
/// A single voter's response to a vote request.
/// </summary>
public class VoteResult
{
    /// <summary>Stable voter identifier from <see cref="VoterProfile.VoterId"/>.</summary>
    public string VoterId { get; init; } = "";

    /// <summary>Display name of the voter.</summary>
    public string VoterName { get; init; } = "";

    /// <summary>Provider that generated this vote (e.g., "claude", "openai").</summary>
    public string ProviderId { get; init; } = "";

    /// <summary>
    /// The voter's decision:
    ///   - Choice vote: the selected option (exact match from VoteRequest.Options)
    ///   - Free-form vote: the voter's own answer text
    ///   - Scored vote: comma-separated "DIMENSION:score" pairs
    /// </summary>
    public string Decision { get; init; } = "";

    /// <summary>The voter's reasoning — why they voted this way.</summary>
    public string Reasoning { get; init; } = "";

    /// <summary>Optional dimension scores (1-10) for scored vote requests.</summary>
    public Dictionary<string, int> Scores { get; init; } = new();

    /// <summary>
    /// Optional specific clichés, failures, or strong points identified.
    /// Used by quality evaluation to feed back into the story generation pipeline.
    /// </summary>
    public List<string> Flags { get; init; } = [];

    /// <summary>The strongest moment identified in the evaluated content. Populated by scored votes.</summary>
    public string BestMoment { get; init; } = "";

    /// <summary>The weakest moment identified in the evaluated content. Populated by scored votes.</summary>
    public string WorstMoment { get; init; } = "";

    /// <summary>How confident the voter is in their decision (1-10). Self-reported.</summary>
    public int Confidence { get; init; } = 5;

    /// <summary>True if this voter's call failed (network error, API error, etc.).</summary>
    public bool IsError { get; init; }

    /// <summary>Error message if IsError is true.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Aggregated result of a voting session.
/// </summary>
public class VotingResult
{
    /// <summary>The original question that was voted on.</summary>
    public string Question { get; init; } = "";

    /// <summary>Quorum requirement that was applied.</summary>
    public Quorum QuorumType { get; init; }

    /// <summary>True if enough voters agreed to meet the quorum threshold.</summary>
    public bool QuorumReached { get; init; }

    /// <summary>
    /// The winning decision. For choice votes: the selected option.
    /// For free-form: the synthesized consensus or most common answer.
    /// Empty if quorum was not reached.
    /// </summary>
    public string Consensus { get; init; } = "";

    /// <summary>
    /// Fraction of voters who voted for the consensus (0.0–1.0).
    /// E.g., 0.75 = 3 of 4 voters agreed.
    /// </summary>
    public double ConsensusStrength { get; init; }

    /// <summary>All individual voter results, including errors.</summary>
    public List<VoteResult> IndividualVotes { get; init; } = [];

    /// <summary>
    /// Reasons given by dissenters (voters who voted differently from consensus).
    /// Empty on unanimous decisions.
    /// </summary>
    public List<string> DissenterReasons { get; init; } = [];

    /// <summary>
    /// Plain-English narrative summary of the vote, its outcome, and key arguments.
    /// Populated if VoteRequest.SynthesizeNarrative = true.
    /// </summary>
    public string NarrativeSummary { get; init; } = "";

    /// <summary>How long the vote took in total.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Number of voters that responded successfully (not IsError).</summary>
    public int SuccessfulVoters => IndividualVotes.Count(v => !v.IsError);
}

/// <summary>
/// Aggregated result of a multi-dimensional scoring vote.
/// </summary>
public class ScoredVotingResult : VotingResult
{
    /// <summary>Average score per dimension across all successful voters.</summary>
    public Dictionary<string, double> AggregateScores { get; init; } = new();

    /// <summary>Dimensions that scored below the failure threshold.</summary>
    public List<string> FailingDimensions { get; init; } = [];

    /// <summary>Common positive patterns identified by multiple voters.</summary>
    public List<string> ConsensusStrengths { get; init; } = [];

    /// <summary>Common failure patterns identified by multiple voters.</summary>
    public List<string> ConsensusFailures { get; init; } = [];

    /// <summary>
    /// Specific improvement directives synthesized from voter feedback.
    /// Ready to inject into a future system prompt.
    /// </summary>
    public List<string> ImprovementDirectives { get; init; } = [];

    /// <summary>Dimension with the lowest average score.</summary>
    public string WeakestDimension { get; init; } = "";
}
