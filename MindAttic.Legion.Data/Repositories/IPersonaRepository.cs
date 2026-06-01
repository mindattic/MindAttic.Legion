namespace MindAttic.Legion.Data;

/// <summary>Reads and seeds the persisted persona library.</summary>
public interface IPersonaRepository
{
    /// <summary>
    /// Upsert every persona from <see cref="MindAttic.Legion.PersonaLibrary"/>
    /// (and its <see cref="MindAttic.Legion.PersonaDetail"/> metadata) into the
    /// store. Idempotent: re-running refreshes names/prompts/axes without
    /// touching profiles. Returns the number of rows inserted or updated.
    /// </summary>
    Task<int> SyncFromLibraryAsync(CancellationToken ct = default);

    /// <summary>Total personas in the store.</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Fetch one persona by id, or null if absent.</summary>
    Task<PersonaEntity?> GetAsync(string personaId, CancellationToken ct = default);

    /// <summary>All persona ids in the store (deterministic, ascending).</summary>
    Task<List<string>> AllIdsAsync(CancellationToken ct = default);
}
