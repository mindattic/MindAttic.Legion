namespace MindAttic.LLMVoting;

/// <summary>
/// A vote request. Covers four voting modes:
///   1. Binary (yes/no):      Options = ["Yes", "No"]
///   2. Choice:               Options = ["A", "B", "C"]
///   3. Free-form:            Options = [] (voters write their own answer)
///   4. Scored:               Dimensions = ["VOICE", "PACING", ...] (1-10 per dimension)
///
/// Modes can overlap — e.g., scored + choice-based options.
/// </summary>
public class VoteRequest
{
    /// <summary>The central question all voters must answer.</summary>
    public string Question { get; init; } = "";

    /// <summary>
    /// Background context shared with all voters before they decide.
    /// Can be as long as needed — this is injected into each voter's system context.
    /// </summary>
    public string Context { get; init; } = "";

    /// <summary>
    /// Optional fixed options to choose from.
    /// If empty, voters give a free-form answer.
    /// </summary>
    public List<string> Options { get; init; } = [];

    /// <summary>
    /// Optional scoring dimensions. If provided, each voter rates each dimension 1-10.
    /// The aggregated scores drive <see cref="ScoredVotingResult"/>.
    /// </summary>
    public List<string> Dimensions { get; init; } = [];

    /// <summary>Max tokens per voter response (default 2048).</summary>
    public int MaxTokens { get; init; } = 2048;

    /// <summary>LLM temperature for voter reasoning (default 0.3 — deliberate, not creative).</summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>
    /// If true, after all individual votes are in, a judge LLM synthesizes the
    /// final consensus in prose. Adds one extra LLM call but produces a readable
    /// narrative summary of the decision and why.
    /// </summary>
    public bool SynthesizeNarrative { get; init; } = true;
}

/// <summary>
/// Extended request for multi-dimensional scoring votes (e.g., quality evaluation).
/// </summary>
public class ScoredVoteRequest : VoteRequest
{
    /// <summary>
    /// Must be non-empty. Each voter scores every dimension 1-10.
    /// </summary>
    public new List<string> Dimensions { get; init; } = [];

    /// <summary>Score below this threshold marks a dimension as a "failure pattern".</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>
    /// Optional domain-specific evaluator framing. When set, this replaces the generic
    /// "You are a structured evaluator" intro in the system prompt. Use this to inject
    /// rubric details and expert context (e.g., story quality evaluation, game balance).
    /// The JSON output schema is always enforced by LLMVoting regardless of this value.
    /// </summary>
    public string EvaluatorContext { get; init; } = "";
}
