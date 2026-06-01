namespace MindAttic.Legion;

/// <summary>
/// One voter persona — a name plus the personality prompt an LLM adopts when
/// speaking as that voter. Pull from <see cref="PersonaLibrary"/> for the
/// built-in 1024-persona pool, or construct your own.
/// </summary>
/// <param name="Id">Stable identifier (e.g. <c>"persona-0042"</c> or <c>"default-claude"</c>).</param>
/// <param name="Name">Display name shown in results.</param>
/// <param name="PersonalityMarkdown">Markdown system prompt that shapes the voter's voice. Empty = no overlay.</param>
public sealed record Persona(string Id, string Name, string PersonalityMarkdown);
