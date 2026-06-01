namespace MindAttic.Legion;

/// <summary>
/// The structured ingredients behind a <see cref="Persona"/> — the cube axes and
/// per-persona enrichments that <see cref="PersonaLibrary"/> bakes into the
/// prompt string. Exposed so persistence and analytics can query personas by
/// archetype/worldview/background instead of re-parsing the prompt. Default
/// (per-provider) personas carry only <see cref="IsDefault"/> + <see cref="ProviderId"/>;
/// their axis fields are null.
/// </summary>
/// <param name="Id">Matches <see cref="Persona.Id"/>.</param>
/// <param name="Archetype">Vocational archetype (enriched personas only).</param>
/// <param name="Worldview">Reasoning-style worldview (enriched personas only).</param>
/// <param name="Background">Cultural background (enriched personas only).</param>
/// <param name="Age">Deterministic age 22–78 (enriched personas only).</param>
/// <param name="Pronouns">Pronoun set (enriched personas only).</param>
/// <param name="Quirk">Signature quirk (enriched personas only).</param>
/// <param name="IsDefault">True for the raw per-provider "default" personas.</param>
/// <param name="ProviderId">Provider id for default personas (e.g. "claude"); null for enriched.</param>
public sealed record PersonaDetail(
    string Id,
    string? Archetype,
    string? Worldview,
    string? Background,
    int? Age,
    string? Pronouns,
    string? Quirk,
    bool IsDefault,
    string? ProviderId);
