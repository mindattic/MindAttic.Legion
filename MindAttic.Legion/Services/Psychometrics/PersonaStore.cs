using System.Text.Json;

namespace MindAttic.Legion;

/// <summary>
/// File-backed store for personas and their psychometric profiles: one faithful,
/// human-readable JSON document per persona under <c>personas/</c>, plus a small
/// <c>runs.json</c> index of assessment runs. No database — a persona is fully
/// reconstructable from its single file.
///
/// <para>The administering LLM is a per-assessment lens, not a persona attribute:
/// a persona can carry several variant profiles (one per administering model),
/// distinguished by <see cref="PsychometricProfile.AdministeredByProvider"/>.
/// The latest/history queries accept an optional provider filter so those
/// variants stay separable.</para>
///
/// <para>Single-writer by design (the CLI scores one run at a time); writes are
/// atomic (temp file + move) so a crash can't leave a half-written persona.</para>
/// </summary>
public sealed class PersonaStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string personasDir;
    private readonly string runsPath;

    public PersonaStore(string? storeDir = null)
    {
        RootDirectory = LegionStorePaths.Resolve(storeDir);
        personasDir = Path.Combine(RootDirectory, "personas");
        runsPath = Path.Combine(RootDirectory, "runs.json");
    }

    /// <summary>The resolved store root directory.</summary>
    public string RootDirectory { get; }

    // ── personas ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensure a document exists for every persona in <see cref="PersonaLibrary"/>,
    /// refreshing identity/traits while preserving any recorded assessments.
    /// Returns the number of files created or updated.
    /// </summary>
    public int SyncFromLibrary()
    {
        Directory.CreateDirectory(personasDir);
        var detailsById = PersonaLibrary.AllDetails.ToDictionary(d => d.Id);
        var changed = 0;

        foreach (var p in PersonaLibrary.All)
        {
            detailsById.TryGetValue(p.Id, out var detail);
            var path = PersonaPath(p.Id);

            if (File.Exists(path))
            {
                var existing = ReadDoc(path);
                var updated = existing with
                {
                    Name = p.Name,
                    PersonalityMarkdown = p.PersonalityMarkdown,
                    Traits = detail ?? existing.Traits,
                };
                if (updated != existing) { WriteJson(path, updated); changed++; }
            }
            else
            {
                var traits = detail ?? new PersonaDetail(p.Id, null, null, null, null, null, null, false, null);
                WriteJson(path, new PersonaDocument(p.Id, p.Name, p.PersonalityMarkdown, traits, new List<StoredAssessment>()));
                changed++;
            }
        }
        return changed;
    }

    /// <summary>Number of persona documents in the store.</summary>
    public int Count() =>
        Directory.Exists(personasDir) ? Directory.GetFiles(personasDir, "*.json").Length : 0;

    /// <summary>Load one persona document, or null if it doesn't exist.</summary>
    public PersonaDocument? Get(string personaId)
    {
        var path = PersonaPath(personaId);
        return File.Exists(path) ? ReadDoc(path) : null;
    }

    /// <summary>All persona ids in the store, ascending.</summary>
    public IReadOnlyList<string> AllIds() =>
        Directory.Exists(personasDir)
            ? Directory.GetFiles(personasDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : new List<string>();

    // ── assessments ──────────────────────────────────────────────────────────────

    /// <summary>Append a scored assessment to a persona document.</summary>
    public void SaveAssessment(
        string personaId, int runId, PsychometricProfile profile,
        Dictionary<string, Dictionary<int, int>>? rawAnswers = null)
    {
        var path = PersonaPath(personaId);
        var doc = File.Exists(path)
            ? ReadDoc(path)
            : new PersonaDocument(personaId, personaId, "", new PersonaDetail(personaId, null, null, null, null, null, null, false, null), new());
        doc.Assessments.Add(new StoredAssessment(runId, profile, rawAnswers));
        WriteJson(path, doc);
    }

    /// <summary>The most recent profile for a persona (optionally for one administering provider/lens).</summary>
    public PsychometricProfile? LatestProfile(string personaId, string? administeredByProvider = null)
    {
        var doc = Get(personaId);
        if (doc is null) return null;
        return doc.Assessments
            .Where(a => administeredByProvider is null
                     || string.Equals(a.Profile.AdministeredByProvider, administeredByProvider, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.RunId)
            .FirstOrDefault()?.Profile;
    }

    /// <summary>Every assessment for a persona, oldest run first — the trend history.</summary>
    public IReadOnlyList<StoredAssessment> History(string personaId) =>
        Get(personaId)?.Assessments.OrderBy(a => a.RunId).ToList() ?? new List<StoredAssessment>();

    /// <summary>All profiles recorded under one run, keyed by persona id.</summary>
    public IReadOnlyDictionary<string, PsychometricProfile> ProfilesByRun(int runId)
    {
        var result = new Dictionary<string, PsychometricProfile>(StringComparer.Ordinal);
        foreach (var doc in AllDocs())
        {
            var match = doc.Assessments.FirstOrDefault(a => a.RunId == runId);
            if (match is not null) result[doc.Id] = match.Profile;
        }
        return result;
    }

    /// <summary>The latest profile per persona (optionally per provider/lens) — the current snapshot for stats.</summary>
    public IReadOnlyList<PsychometricProfile> LatestPerPersona(string? administeredByProvider = null)
    {
        var result = new List<PsychometricProfile>();
        foreach (var doc in AllDocs())
        {
            var latest = doc.Assessments
                .Where(a => administeredByProvider is null
                         || string.Equals(a.Profile.AdministeredByProvider, administeredByProvider, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.RunId)
                .FirstOrDefault();
            if (latest is not null) result.Add(latest.Profile);
        }
        return result;
    }

    /// <summary>
    /// Persona ids that already have a profile at <paramref name="instrumentSetVersion"/>
    /// (optionally restricted to one administering provider). Drives resume.
    /// </summary>
    public HashSet<string> PersonaIdsScored(string? instrumentSetVersion = null, string? administeredByProvider = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var doc in AllDocs())
            if (doc.Assessments.Any(a =>
                    (instrumentSetVersion is null || a.Profile.InstrumentSetVersion == instrumentSetVersion) &&
                    (administeredByProvider is null ||
                        string.Equals(a.Profile.AdministeredByProvider, administeredByProvider, StringComparison.OrdinalIgnoreCase))))
                ids.Add(doc.Id);
        return ids;
    }

    // ── runs ──────────────────────────────────────────────────────────────────

    /// <summary>Open a new run and return it with its assigned id.</summary>
    public RunRecord StartRun(
        string provider, string model, string tier, string instrumentSetVersion,
        int personaCount, DateTime startedUtc, string? notes)
    {
        var runs = ReadRuns();
        var id = runs.Count > 0 ? runs.Max(r => r.Id) + 1 : 1;
        var run = new RunRecord(id, startedUtc, null, provider, model, tier, instrumentSetVersion, personaCount, 0, notes);
        runs.Add(run);
        WriteRuns(runs);
        return run;
    }

    /// <summary>Upsert a run record by id (used when importing runs with pre-assigned ids).</summary>
    public void ImportRun(RunRecord run)
    {
        var runs = ReadRuns();
        var idx = runs.FindIndex(r => r.Id == run.Id);
        if (idx >= 0) runs[idx] = run; else runs.Add(run);
        WriteRuns(runs);
    }

    /// <summary>Update a run's progress counter.</summary>
    public void SetRunProgress(int runId, int completedCount) =>
        MutateRun(runId, r => r with { CompletedCount = completedCount });

    /// <summary>Mark a run finished.</summary>
    public void CompleteRun(int runId, DateTime completedUtc) =>
        MutateRun(runId, r => r with { CompletedUtc = completedUtc });

    /// <summary>Fetch a run by id, or null.</summary>
    public RunRecord? GetRun(int runId) => ReadRuns().FirstOrDefault(r => r.Id == runId);

    /// <summary>Most recent runs, newest first.</summary>
    public IReadOnlyList<RunRecord> RecentRuns(int take = 20) =>
        ReadRuns().OrderByDescending(r => r.Id).Take(take).ToList();

    // ── internals ──────────────────────────────────────────────────────────────

    private string PersonaPath(string personaId) => Path.Combine(personasDir, personaId + ".json");

    private IEnumerable<PersonaDocument> AllDocs()
    {
        if (!Directory.Exists(personasDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(personasDir, "*.json"))
            yield return ReadDoc(f);
    }

    private static PersonaDocument ReadDoc(string path) =>
        JsonSerializer.Deserialize<PersonaDocument>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Could not parse persona document: {path}");

    private List<RunRecord> ReadRuns() =>
        File.Exists(runsPath)
            ? JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(runsPath), Json) ?? new()
            : new();

    private void WriteRuns(List<RunRecord> runs) => WriteJson(runsPath, runs);

    private void MutateRun(int runId, Func<RunRecord, RunRecord> mutate)
    {
        var runs = ReadRuns();
        var idx = runs.FindIndex(r => r.Id == runId);
        if (idx < 0) return;
        runs[idx] = mutate(runs[idx]);
        WriteRuns(runs);
    }

    /// <summary>Serialize atomically: write a temp file in the same directory, then move over the target.</summary>
    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
