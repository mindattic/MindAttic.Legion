namespace MindAttic.Legion.Data;

/// <summary>
/// A persisted row mirroring one entry in <see cref="MindAttic.Legion.PersonaLibrary"/>,
/// keyed by the library's stable persona id. Stores the prompt plus the
/// structured cube axes so personas can be queried by archetype/worldview/
/// background. Seeded/synced by the CLI's <c>psychometrics db init</c>.
/// </summary>
public class PersonaEntity
{
    /// <summary>Stable persona id, e.g. "persona-0042" or "default-claude" (primary key).</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string PersonalityMarkdown { get; set; } = "";

    /// <summary>Vocational archetype (null for default per-provider personas).</summary>
    public string? Archetype { get; set; }
    public string? Worldview { get; set; }
    public string? Background { get; set; }
    public int? Age { get; set; }
    public string? Pronouns { get; set; }
    public string? Quirk { get; set; }

    /// <summary>True for the raw per-provider "default" personas (empty prompt).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Provider id for default personas (e.g. "claude"); null for enriched.</summary>
    public string? ProviderId { get; set; }

    /// <summary>All psychometric profiles recorded for this persona, across runs.</summary>
    public List<PsychometricProfileEntity> Profiles { get; set; } = new();
}
