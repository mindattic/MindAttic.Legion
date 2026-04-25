namespace MindAttic.Legion;

/// <summary>
/// One voter persona — a name plus the personality prompt an LLM adopts when
/// speaking as that voter. Pull from <see cref="PersonaLibrary"/> for the
/// built-in 1000-persona pool, or construct your own.
/// </summary>
public sealed record Persona(string Id, string Name, string PersonalityMarkdown);
