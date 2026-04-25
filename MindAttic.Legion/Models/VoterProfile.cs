namespace MindAttic.Legion;

/// <summary>
/// Defines a single voter — an LLM provider with an optional personality overlay.
///
/// The personality is a markdown system prompt sent to the LLM before every vote.
/// Leave it empty to use the raw model without persona. Set it to a character
/// description to simulate how that character would decide.
///
/// Mirrors the ParticipantTemplate pattern from LLMThinkTank but is
/// agnostic to any specific application's settings system.
/// </summary>
public class VoterProfile
{
    /// <summary>Stable identifier for this voter profile (GUID hex).</summary>
    public string VoterId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name shown in results (e.g., "Claude", "Sable Chen", "The Skeptic").</summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Provider identifier. Built-in providers: claude, openai, gemini, deepseek,
    /// mistral, xai, groq, together, openrouter, fireworks, cohere.
    /// </summary>
    public string ProviderId { get; init; } = "";

    /// <summary>
    /// Optional markdown system prompt that shapes how this voter reasons.
    /// Empty = plain LLM call with no persona.
    /// Set to a character's psychology, behavioral patterns, and worldview
    /// to simulate how that character would vote.
    /// </summary>
    public string PersonalityMarkdown { get; init; } = "";

    /// <summary>Override the model for this specific voter (e.g., "claude-opus-4-6").</summary>
    public string? ModelOverride { get; init; }

    /// <summary>Override the API key for this voter (useful for A/B testing or rate limiting).</summary>
    public string? ApiKeyOverride { get; init; }

    /// <summary>Override max response tokens for this voter.</summary>
    public int? MaxTokensOverride { get; init; }

    /// <summary>
    /// Build a VoterProfile for character persona simulation.
    /// The LLM will reason and vote as this character would, given their psychology.
    /// </summary>
    public static VoterProfile ForCharacter(
        string characterName,
        string psychologyMarkdown,
        string providerId,
        string? apiKey = null,
        string? model = null) => new()
    {
        VoterId  = Guid.NewGuid().ToString("N"),
        Name     = characterName,
        ProviderId = providerId,
        PersonalityMarkdown = $"""
            You are simulating the psychology of **{characterName}** for decision-making purposes.
            You will reason and respond exactly as {characterName} would, based on their psychology,
            fears, desires, and behavioral patterns. Do NOT break character. Do NOT meta-comment
            on the exercise — just think and decide as this character would.

            CHARACTER PSYCHOLOGY:
            {psychologyMarkdown}
            """,
        ApiKeyOverride  = apiKey,
        ModelOverride   = model,
    };
}
