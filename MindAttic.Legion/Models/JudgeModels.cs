namespace MindAttic.Legion;

/// <summary>One rubric criterion for <see cref="LlmVotingService.JudgeAsync"/>.</summary>
/// <param name="Name">Unique criterion name (case-insensitive); the key in every result.</param>
/// <param name="Description">What a high score on this criterion means.</param>
/// <param name="Weight">Relative weight in the overall score (must be positive; default 1).</param>
public sealed record JudgeCriterion(string Name, string Description, double Weight = 1.0);

/// <summary>A numbered/identified piece of evidence the answer is judged against.</summary>
/// <param name="Id">Caller-chosen id the judges cite (e.g. "1" or "p7"). Must be unique.</param>
/// <param name="Text">The passage text.</param>
public sealed record EvidencePassage(string Id, string Text);

/// <summary>Input to grounded judging.</summary>
public class JudgeRequest
{
    /// <summary>Rubric criteria. At least one; names must be unique.</summary>
    public List<JudgeCriterion> Criteria { get; init; } = [];

    /// <summary>Evidence passages. May be empty (judging is then ungrounded and no ids can be cited).</summary>
    public List<EvidencePassage> Evidence { get; init; } = [];

    /// <summary>The candidate answer being judged.</summary>
    public string Answer { get; init; } = "";

    /// <summary>Optional question the answer responds to.</summary>
    public string Question { get; init; } = "";

    /// <summary>Optional prior context (earlier turns, earlier answers) the judges should take into account.</summary>
    public string PriorContext { get; init; } = "";

    /// <summary>Optional domain framing replacing the generic "strict evaluator" intro.</summary>
    public string EvaluatorContext { get; init; } = "";

    /// <summary>Max tokens per judge reply.</summary>
    public int MaxTokens { get; init; } = 2048;

    /// <summary>Temperature for judge calls (low: judging should be repeatable).</summary>
    public double Temperature { get; init; } = 0.2;
}

/// <summary>One voter's judgment of one criterion.</summary>
/// <param name="Score">Normalized 0..1 score, (raw - 1) / 9.</param>
/// <param name="RawScore">The 1..10 integer the voter gave.</param>
/// <param name="Rationale">The voter's justification.</param>
/// <param name="CitedPassageIds">Evidence ids the voter cited for this criterion (validated against the request).</param>
public sealed record CriterionVerdict(double Score, int RawScore, string Rationale, IReadOnlyList<string> CitedPassageIds);

/// <summary>One voter's full, validated verdict.</summary>
public sealed class JudgeVoterVerdict
{
    /// <summary>Voter id.</summary>
    public string VoterId { get; init; } = "";
    /// <summary>Voter display name.</summary>
    public string VoterName { get; init; } = "";
    /// <summary>Provider that produced the verdict.</summary>
    public string ProviderId { get; init; } = "";
    /// <summary>Per-criterion verdicts keyed by criterion name.</summary>
    public IReadOnlyDictionary<string, CriterionVerdict> Criteria { get; init; } = new Dictionary<string, CriterionVerdict>();
    /// <summary>Weighted overall 0..1 for this voter.</summary>
    public double Overall { get; init; }
    /// <summary>Model calls it took to get valid JSON (1, or 2 when the retry was needed).</summary>
    public int Attempts { get; init; }
}

/// <summary>A voter that produced no valid verdict.</summary>
/// <param name="VoterId">Voter id.</param>
/// <param name="VoterName">Voter name.</param>
/// <param name="ProviderId">Provider id.</param>
/// <param name="Error">Why: a provider error, or the validation failure after the retry.</param>
public sealed record JudgeVoterFailure(string VoterId, string VoterName, string ProviderId, string Error);

/// <summary>Panel-level result for one criterion.</summary>
public sealed class CriterionJudgment
{
    /// <summary>Criterion name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Criterion weight.</summary>
    public double Weight { get; init; }
    /// <summary>Mean normalized 0..1 score across the voters that succeeded.</summary>
    public double Score { get; init; }
    /// <summary>Mean raw 1..10 score.</summary>
    public double RawMean { get; init; }
    /// <summary>Population standard deviation of the normalized scores (0 = identical).</summary>
    public double Dispersion { get; init; }
    /// <summary>Rationale from the voter whose score is closest to the panel mean.</summary>
    public string Rationale { get; init; } = "";
    /// <summary>Evidence ids cited by at least one voter, most-cited first.</summary>
    public IReadOnlyList<string> CitedPassageIds { get; init; } = [];
}

/// <summary>Result of grounded judging.</summary>
public sealed class JudgeResult
{
    /// <summary>True when at least one voter returned a valid verdict.</summary>
    public bool Succeeded { get; init; }
    /// <summary>Weighted overall 0..1 score (0 when not <see cref="Succeeded"/>).</summary>
    public double Overall { get; init; }
    /// <summary>
    /// Agreement 0..1 across voters: 1 minus twice the mean per-criterion dispersion, clamped.
    /// 1.0 when every voter scored identically, and by convention when only one voter succeeded.
    /// </summary>
    public double Agreement { get; init; }
    /// <summary>Per-criterion panel results, in request order.</summary>
    public IReadOnlyList<CriterionJudgment> Criteria { get; init; } = [];
    /// <summary>Every successful voter's own verdict.</summary>
    public IReadOnlyList<JudgeVoterVerdict> Verdicts { get; init; } = [];
    /// <summary>Voters that failed (reported, never silently dropped).</summary>
    public IReadOnlyList<JudgeVoterFailure> FailedVoters { get; init; } = [];
    /// <summary>Wall-clock duration of the whole judging.</summary>
    public TimeSpan Duration { get; init; }
}
