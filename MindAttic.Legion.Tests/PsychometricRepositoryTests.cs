using Microsoft.EntityFrameworkCore;
using MindAttic.Legion;
using MindAttic.Legion.Data;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Round-trip tests for the Data layer over the EF Core in-memory provider:
/// seeding, saving versioned profiles, and the latest/history/resume queries.
/// (A SQL-Server-backed variant lives in <see cref="PsychometricLiveDbTests"/>.)
/// </summary>
[TestFixture]
public class PsychometricRepositoryTests
{
    private LegionDbContext NewContext() =>
        new(new DbContextOptionsBuilder<LegionDbContext>()
            .UseInMemoryDatabase($"legion-{Guid.NewGuid():N}")
            .Options);

    private static PsychometricProfile NeutralProfile(string personaId, string model = "claude-opus-4-8") =>
        PsychometricScorer.ScoreAll(
            personaId,
            new Dictionary<string, IReadOnlyDictionary<int, int>>(),
            "claude", model, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Test]
    public async Task SyncFromLibrary_SeedsEveryPersona()
    {
        await using var db = NewContext();
        var repo = new PersonaRepository(db);

        var changed = await repo.SyncFromLibraryAsync();
        var count = await repo.CountAsync();

        Assert.That(count, Is.EqualTo(PersonaLibrary.Count));
        Assert.That(changed, Is.EqualTo(PersonaLibrary.Count), "first sync inserts everything");

        // Idempotent: a second sync changes nothing.
        Assert.That(await repo.SyncFromLibraryAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task SyncFromLibrary_PopulatesAxisColumnsForEnriched()
    {
        await using var db = NewContext();
        await new PersonaRepository(db).SyncFromLibraryAsync();

        var p = await new PersonaRepository(db).GetAsync("persona-0000");
        Assert.That(p, Is.Not.Null);
        Assert.That(p!.Archetype, Is.Not.Null.And.Not.Empty);
        Assert.That(p.IsDefault, Is.False);
    }

    [Test]
    public async Task SaveAndGetLatest_RoundTripsOwnedScores()
    {
        await using var db = NewContext();
        var runs = new AssessmentRunRepository(db);
        var profiles = new PsychometricProfileRepository(db);

        var run = await runs.StartAsync("claude", "claude-opus-4-8", "High",
            PsychometricInstruments.SetVersion, 1, DateTime.UtcNow);
        await profiles.SaveAsync(NeutralProfile("persona-0000"), run.Id);

        var latest = await profiles.GetLatestAsync("persona-0000");
        Assert.That(latest, Is.Not.Null);
        Assert.That(latest!.Ocean.Openness, Is.EqualTo(50.0));
        Assert.That(latest.Mbti.Type, Is.EqualTo("ESTJ"));
        Assert.That(latest.Enneagram.Triad, Is.EqualTo("Gut"));
        Assert.That(latest.ToDomain().PersonaId, Is.EqualTo("persona-0000"));
    }

    [Test]
    public async Task StoreRaw_PersistsItemResponses()
    {
        await using var db = NewContext();
        var run = await new AssessmentRunRepository(db).StartAsync("claude", "m", "High",
            PsychometricInstruments.SetVersion, 1, DateTime.UtcNow);

        var raw = new Dictionary<string, IReadOnlyDictionary<int, int>>
        {
            ["bigfive"] = new Dictionary<int, int> { [1] = 4, [2] = 2 },
        };
        await new PsychometricProfileRepository(db).SaveAsync(NeutralProfile("persona-0001"), run.Id, raw);

        var stored = await db.ItemResponses.CountAsync();
        Assert.That(stored, Is.EqualTo(2));
    }

    [Test]
    public async Task LatestPerPersona_PrefersTheNewerRun()
    {
        await using var db = NewContext();
        var runs = new AssessmentRunRepository(db);
        var profiles = new PsychometricProfileRepository(db);

        var run1 = await runs.StartAsync("claude", "old-model", "High", "1.0.0", 1, DateTime.UtcNow);
        await profiles.SaveAsync(NeutralProfile("persona-0000", "old-model"), run1.Id);
        var run2 = await runs.StartAsync("claude", "new-model", "High", "1.0.0", 1, DateTime.UtcNow);
        await profiles.SaveAsync(NeutralProfile("persona-0000", "new-model"), run2.Id);

        var history = await profiles.HistoryAsync("persona-0000");
        Assert.That(history, Has.Count.EqualTo(2));

        var latestPerPersona = await profiles.LatestPerPersonaAsync();
        Assert.That(latestPerPersona, Has.Count.EqualTo(1));
        Assert.That(latestPerPersona[0].AdministeredByModel, Is.EqualTo("new-model"));
    }

    [Test]
    public async Task PersonaIdsScored_DrivesResumeSkipping()
    {
        await using var db = NewContext();
        var run = await new AssessmentRunRepository(db).StartAsync("claude", "m", "High",
            PsychometricInstruments.SetVersion, 2, DateTime.UtcNow);
        var profiles = new PsychometricProfileRepository(db);
        await profiles.SaveAsync(NeutralProfile("persona-0000"), run.Id);
        await profiles.SaveAsync(NeutralProfile("persona-0001"), run.Id);

        var scored = await profiles.PersonaIdsScoredAsync(PsychometricInstruments.SetVersion);
        Assert.That(scored, Does.Contain("persona-0000"));
        Assert.That(scored, Does.Contain("persona-0001"));
        Assert.That(scored, Does.Not.Contain("persona-0002"));

        // A different instrument version should not count as already-scored.
        Assert.That(await profiles.PersonaIdsScoredAsync("9.9.9"), Is.Empty);
    }
}
