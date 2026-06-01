using MindAttic.Legion;
using NUnit.Framework;

namespace MindAttic.Legion.Tests;

/// <summary>
/// Round-trip and query tests for the JSON-file <see cref="PersonaStore"/>: seeding,
/// owned-score serialization, run history, the variant (per-lens) separation, and
/// the resume query. Each test runs against a throwaway temp directory.
/// </summary>
[TestFixture]
public class PersonaStoreTests
{
    private string dir = "";

    [SetUp]
    public void MakeTempDir() => dir = Path.Combine(Path.GetTempPath(), "legion-store-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void RemoveTempDir() { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }

    private PersonaStore NewStore() => new(dir);

    private static PsychometricProfile Profile(string personaId, string provider = "claude", string model = "claude-opus-4-8") =>
        PsychometricScorer.ScoreAll(personaId, new Dictionary<string, IReadOnlyDictionary<int, int>>(),
            provider, model, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Test]
    public void SyncFromLibrary_SeedsEveryPersona_AndIsIdempotent()
    {
        var store = NewStore();
        Assert.That(store.SyncFromLibrary(), Is.EqualTo(PersonaLibrary.Count), "first sync writes everything");
        Assert.That(store.Count(), Is.EqualTo(PersonaLibrary.Count));
        Assert.That(store.SyncFromLibrary(), Is.EqualTo(0), "second sync changes nothing");
    }

    [Test]
    public void SaveAssessment_RoundTripsOwnedScoresThroughJson()
    {
        var store = NewStore();
        var run = store.StartRun("claude", "claude-opus-4-8", "High", PsychometricInstruments.SetVersion, 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0000", run.Id, Profile("persona-0000"));

        var latest = store.LatestProfile("persona-0000");
        Assert.That(latest, Is.Not.Null);
        Assert.That(latest!.Ocean.Openness, Is.EqualTo(50.0));
        Assert.That(latest.Mbti.Type, Is.EqualTo("ESTJ"));
        Assert.That(latest.Enneagram.Triad, Is.EqualTo("Gut"));
        Assert.That(latest, Is.EqualTo(Profile("persona-0000")), "full profile survives serialization by value");
    }

    [Test]
    public void Sync_PreservesAssessments()
    {
        var store = NewStore();
        store.SyncFromLibrary();
        var run = store.StartRun("claude", "m", "High", PsychometricInstruments.SetVersion, 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0000", run.Id, Profile("persona-0000"));

        store.SyncFromLibrary(); // re-sync must not wipe the recorded assessment
        Assert.That(store.LatestProfile("persona-0000"), Is.Not.Null);
    }

    [Test]
    public void Runs_IncrementIdsAndTrackProgress()
    {
        var store = NewStore();
        var r1 = store.StartRun("claude", "m", "High", "1.0.0", 2, DateTime.UtcNow, "first");
        var r2 = store.StartRun("claude", "m", "High", "1.0.0", 2, DateTime.UtcNow, "second");
        Assert.That(r1.Id, Is.EqualTo(1));
        Assert.That(r2.Id, Is.EqualTo(2));

        store.SetRunProgress(r2.Id, 5);
        store.CompleteRun(r2.Id, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var fetched = store.GetRun(r2.Id);
        Assert.That(fetched!.CompletedCount, Is.EqualTo(5));
        Assert.That(fetched.CompletedUtc, Is.Not.Null);
    }

    [Test]
    public void LatestPerPersona_PrefersNewerRun_AndByRunGroups()
    {
        var store = NewStore();
        var r1 = store.StartRun("claude", "old", "High", "1.0.0", 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0000", r1.Id, Profile("persona-0000", model: "old"));
        var r2 = store.StartRun("claude", "new", "High", "1.0.0", 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0000", r2.Id, Profile("persona-0000", model: "new"));

        Assert.That(store.History("persona-0000"), Has.Count.EqualTo(2));
        var latestPer = store.LatestPerPersona();
        Assert.That(latestPer, Has.Count.EqualTo(1));
        Assert.That(latestPer[0].AdministeredByModel, Is.EqualTo("new"));

        Assert.That(store.ProfilesByRun(r1.Id)["persona-0000"].AdministeredByModel, Is.EqualTo("old"));
    }

    [Test]
    public void Variants_AreSeparableByAdministeringProvider()
    {
        var store = NewStore();
        var rc = store.StartRun("claude", "claude-opus-4-8", "High", "1.0.0", 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0000", rc.Id, Profile("persona-0000", provider: "claude"));
        var ro = store.StartRun("openai", "gpt-4.1", "High", "1.0.0", 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0000", ro.Id, Profile("persona-0000", provider: "openai"));

        Assert.That(store.LatestProfile("persona-0000", "claude")!.AdministeredByProvider, Is.EqualTo("claude"));
        Assert.That(store.LatestProfile("persona-0000", "openai")!.AdministeredByProvider, Is.EqualTo("openai"));

        Assert.That(store.PersonaIdsScored("1.0.0", "openai"), Does.Contain("persona-0000"));
        Assert.That(store.PersonaIdsScored("1.0.0", "gemini"), Is.Empty, "no gemini variant yet");
    }

    [Test]
    public void PersonaIdsScored_GatesOnInstrumentVersion()
    {
        var store = NewStore();
        var run = store.StartRun("claude", "m", "High", PsychometricInstruments.SetVersion, 1, DateTime.UtcNow, null);
        store.SaveAssessment("persona-0001", run.Id, Profile("persona-0001"));

        Assert.That(store.PersonaIdsScored(PsychometricInstruments.SetVersion), Does.Contain("persona-0001"));
        Assert.That(store.PersonaIdsScored("9.9.9"), Is.Empty);
    }
}
