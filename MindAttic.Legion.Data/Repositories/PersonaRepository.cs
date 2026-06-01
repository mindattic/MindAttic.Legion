using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;

namespace MindAttic.Legion.Data;

/// <inheritdoc />
public sealed class PersonaRepository : IPersonaRepository
{
    private readonly LegionDbContext db;
    public PersonaRepository(LegionDbContext db) => this.db = db;

    /// <inheritdoc />
    public async Task<int> SyncFromLibraryAsync(CancellationToken ct = default)
    {
        var existing = await db.Personas.ToDictionaryAsync(p => p.Id, ct);
        var detailsById = PersonaLibrary.AllDetails.ToDictionary(d => d.Id);
        var changed = 0;

        foreach (var persona in PersonaLibrary.All)
        {
            detailsById.TryGetValue(persona.Id, out var detail);
            if (!existing.TryGetValue(persona.Id, out var row))
            {
                row = new PersonaEntity { Id = persona.Id };
                db.Personas.Add(row);
                existing[persona.Id] = row;
            }

            var before = (row.Name, row.PersonalityMarkdown, row.Archetype, row.Worldview,
                row.Background, row.Age, row.Pronouns, row.Quirk, row.IsDefault, row.ProviderId);

            row.Name = persona.Name;
            row.PersonalityMarkdown = persona.PersonalityMarkdown;
            row.Archetype = detail?.Archetype;
            row.Worldview = detail?.Worldview;
            row.Background = detail?.Background;
            row.Age = detail?.Age;
            row.Pronouns = detail?.Pronouns;
            row.Quirk = detail?.Quirk;
            row.IsDefault = detail?.IsDefault ?? false;
            row.ProviderId = detail?.ProviderId;

            var after = (row.Name, row.PersonalityMarkdown, row.Archetype, row.Worldview,
                row.Background, row.Age, row.Pronouns, row.Quirk, row.IsDefault, row.ProviderId);

            if (db.Entry(row).State == EntityState.Added || before != after) changed++;
        }

        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken ct = default) => db.Personas.CountAsync(ct);

    /// <inheritdoc />
    public Task<PersonaEntity?> GetAsync(string personaId, CancellationToken ct = default) =>
        db.Personas.FirstOrDefaultAsync(p => p.Id == personaId, ct);

    /// <inheritdoc />
    public Task<List<string>> AllIdsAsync(CancellationToken ct = default) =>
        db.Personas.OrderBy(p => p.Id).Select(p => p.Id).ToListAsync(ct);
}
