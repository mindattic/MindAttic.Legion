namespace MindAttic.Legion.Cli;

using System.Text.Json;
using Microsoft.Data.SqlClient;
using MindAttic.Legion;
using MindAttic.Legion.Data;
using MindAttic.Legion.Providers;

/// <summary>
/// <c>legion psychometrics</c> — score the persona library on five instruments
/// (Big Five/OCEAN, HEXACO, MBTI-style, Enneagram-style, DISC-style) and persist
/// the results to SQL Server. A single trusted model administers every test in a
/// run, so all personas are measured on the same yardstick; each run is versioned
/// so drift can be tracked over time.
///
/// Subcommands:
///   db init                         create/upgrade the database and seed personas
///   score   [opts]                  score personas missing a current-version profile (resumable)
///   rescore [opts]                  force a fresh full run (for drift tracking)
///   show    &lt;persona-id&gt; [opts]      print a persona's latest profile
///   stats   [opts]                  distribution summary across the library
///   history &lt;persona-id&gt; [opts]      a persona's profiles across runs
///   diff    &lt;runA&gt; &lt;runB&gt; [opts]      per-framework drift between two runs
/// </summary>
public static class PsychometricsCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || AskCommand.IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var rest = args.Skip(1).ToArray();
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "db"      => await DbAsync(rest),
                "score"   => await ScoreAsync(rest, rescore: false),
                "rescore" => await ScoreAsync(rest, rescore: true),
                "show"    => await ShowAsync(rest),
                "stats"   => await StatsAsync(rest),
                "history" => await HistoryAsync(rest),
                "diff"    => await DiffAsync(rest),
                _         => Unknown(args[0]),
            };
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine($"error: database call failed: {ex.Message}");
            Console.Error.WriteLine($"  connection: {Redact(LegionConnectionString.Resolve(LastConnectionOverride))}");
            Console.Error.WriteLine($"  set {LegionConnectionString.EnvVar} to point at your SQL Server, or run 'legion psychometrics db init' first.");
            return 2;
        }
    }

    // Remembered so the SqlException handler can report the connection actually used.
    private static string? LastConnectionOverride;

    // ── db init ──────────────────────────────────────────────────────────────

    private static async Task<int> DbAsync(string[] args)
    {
        if (args.Length == 0 || args[0].ToLowerInvariant() != "init")
        {
            Console.Error.WriteLine("usage: legion psychometrics db init [--connection <cs>]");
            return 1;
        }
        var connection = ParseConnection(args);
        await using var db = LegionData.CreateContext(connection);
        await LegionData.MigrateAsync(db);

        var personas = new PersonaRepository(db);
        var changed = await personas.SyncFromLibraryAsync();
        var count = await personas.CountAsync();

        Console.WriteLine($"Database ready: {Redact(LegionConnectionString.Resolve(connection))}");
        Console.WriteLine($"Personas synced: {count} total ({changed} inserted/updated).");
        Console.WriteLine($"Instruments: {PsychometricInstruments.All.Count} ({PsychometricInstruments.TotalItemCount} items), set version {PsychometricInstruments.SetVersion}.");
        return 0;
    }

    // ── score / rescore ────────────────────────────────────────────────────────

    private static async Task<int> ScoreAsync(string[] args, bool rescore)
    {
        var connection = ParseConnection(args);
        var providerId = "claude";
        var tier = ModelTier.High;
        var limit = int.MaxValue;
        var concurrency = 4;
        var timeoutSeconds = 120.0;
        var storeRaw = false;
        string? notes = rescore ? "rescore" : null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--provider": if (i + 1 < args.Length) providerId = args[++i].ToLowerInvariant(); break;
                case "--tier": if (i + 1 < args.Length && Enum.TryParse<ModelTier>(args[++i], true, out var t)) tier = t; break;
                case "--limit": if (i + 1 < args.Length && int.TryParse(args[++i], out var l) && l > 0) limit = l; break;
                case "--concurrency": if (i + 1 < args.Length && int.TryParse(args[++i], out var c) && c > 0) concurrency = c; break;
                case "--timeout": if (i + 1 < args.Length && double.TryParse(args[++i], out var s) && s > 0) timeoutSeconds = s; break;
                case "--store-raw": storeRaw = true; break;
                case "--notes": if (i + 1 < args.Length) notes = args[++i]; break;
            }
        }

        // Only trusted providers may administer an autonomous assessment.
        if (!AskCommand.IntersectWithTrustedSet(new[] { providerId }).Contains(providerId))
        {
            Console.Error.WriteLine($"error: '{providerId}' is not a trusted provider. Trusted: {string.Join(", ", AskCommand.TrustedProviderIds)}.");
            return 1;
        }

        var config = new VotingConfiguration
        {
            ProviderTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            UseSharedCredentials = true,
            AllowedProviderIds = AskCommand.IntersectWithTrustedSet(new[] { providerId }),
            ModelOverrides = AskCommand.BuildTierModelOverrides(tier),
        };

        using var http = new HttpClient { Timeout = config.ProviderTimeout + TimeSpan.FromSeconds(15) };
        var provider = new LlmVotingProvider(http, config);
        if (string.IsNullOrWhiteSpace(provider.GetApiKey(providerId)))
        {
            Console.Error.WriteLine($"error: no API key for '{providerId}'. Add one at %APPDATA%/MindAttic/LLM/.");
            return 1;
        }

        var assessor = new LlmPsychometricAssessor(provider, providerId, tier);

        await using var db = LegionData.CreateContext(connection);
        await LegionData.MigrateAsync(db);
        var personaRepo = new PersonaRepository(db);
        var runRepo = new AssessmentRunRepository(db);
        var profileRepo = new PsychometricProfileRepository(db);
        await personaRepo.SyncFromLibraryAsync();

        // Decide who to score. `score` skips personas already scored at the
        // current instrument version (resumable); `rescore` always re-scores.
        var byId = PersonaLibrary.All.ToDictionary(p => p.Id);
        var ordered = PersonaLibrary.All.Select(p => p.Id).ToList();
        var skip = rescore
            ? new HashSet<string>(StringComparer.Ordinal)
            : await profileRepo.PersonaIdsScoredAsync(PsychometricInstruments.SetVersion);
        var toScore = ordered.Where(id => !skip.Contains(id)).Take(limit).Select(id => byId[id]).ToList();

        if (toScore.Count == 0)
        {
            Console.WriteLine(rescore
                ? "Nothing to score."
                : $"All {ordered.Count} personas already scored at instrument set {PsychometricInstruments.SetVersion}. Use 'rescore' to force a fresh run.");
            return 0;
        }

        Console.WriteLine($"Scoring {toScore.Count} persona(s) on {PsychometricInstruments.All.Count} instruments " +
                          $"via {providerId} ({assessor.ModelId}, tier {tier}), concurrency {concurrency}.");
        Console.WriteLine($"  ≈ {toScore.Count * PsychometricInstruments.All.Count} model calls. Ctrl+C saves progress and exits (resume with 'score').");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var run = await runRepo.StartAsync(
            providerId, assessor.ModelId, tier.ToString(), PsychometricInstruments.SetVersion,
            toScore.Count, DateTime.UtcNow, notes, cts.Token);

        int done = 0, failed = 0;
        try
        {
            for (var start = 0; start < toScore.Count; start += concurrency)
            {
                cts.Token.ThrowIfCancellationRequested();
                var batch = toScore.Skip(start).Take(concurrency).ToList();
                var tasks = batch.Select(async p =>
                {
                    try { return (persona: p, assessment: await assessor.AssessAsync(p, DateTime.UtcNow, cts.Token), error: (string?)null); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { return (persona: p, assessment: (PsychometricAssessment?)null, error: ex.Message); }
                });
                foreach (var r in await Task.WhenAll(tasks))
                {
                    if (r.assessment is null)
                    {
                        failed++;
                        Console.Error.WriteLine($"  ! {r.persona.Id} failed: {r.error}");
                        continue;
                    }
                    await profileRepo.SaveAsync(r.assessment.Profile, run.Id, storeRaw ? r.assessment.RawAnswers : null, cts.Token);
                    done++;
                    Console.WriteLine($"  [{done}/{toScore.Count}] {r.persona.Id}  {r.assessment.Profile.Summary()}");
                }
                await runRepo.SetProgressAsync(run.Id, done, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"cancelled — {done} saved, {failed} failed. Resume with 'legion psychometrics score'.");
        }

        await runRepo.CompleteAsync(run.Id, DateTime.UtcNow, CancellationToken.None);
        Console.WriteLine($"Run #{run.Id} done: {done} scored, {failed} failed.");
        return failed > 0 && done == 0 ? 1 : 0;
    }

    // ── show ──────────────────────────────────────────────────────────────────

    private static async Task<int> ShowAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--"))
        {
            Console.Error.WriteLine("usage: legion psychometrics show <persona-id> [--json] [--connection <cs>]");
            return 1;
        }
        var personaId = args[0];
        var json = args.Contains("--json");
        var connection = ParseConnection(args);

        await using var db = LegionData.CreateContext(connection);
        var profile = await new PsychometricProfileRepository(db).GetLatestAsync(personaId);
        var persona = await new PersonaRepository(db).GetAsync(personaId);
        if (profile is null)
        {
            Console.Error.WriteLine($"no profile for '{personaId}'. Has it been scored? (run 'score')");
            return 1;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(ToDto(profile, persona),
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"{personaId} — {persona?.Name ?? "(unknown)"}");
        if (persona is { IsDefault: false })
            Console.WriteLine($"  {persona.Worldview} {persona.Archetype}, {persona.Background}, age {persona.Age} ({persona.Pronouns})");
        Console.WriteLine($"  scored {profile.ScoredAtUtc:u} by {profile.AdministeredByProvider}/{profile.AdministeredByModel} (set {profile.InstrumentSetVersion})");
        Console.WriteLine();
        Console.WriteLine($"  MBTI:       {profile.Mbti.Type}  (E/I {profile.Mbti.ExtraversionPct:0} · S/N {profile.Mbti.SensingPct:0} · T/F {profile.Mbti.ThinkingPct:0} · J/P {profile.Mbti.JudgingPct:0})");
        Console.WriteLine($"  Enneagram:  {profile.Enneagram.Notation()}  ({profile.Enneagram.Triad})");
        Console.WriteLine($"  DISC:       {profile.Disc.PrimaryStyle}  [{profile.Disc.ShortCode()}]");
        Console.WriteLine($"  OCEAN:      {profile.Ocean.ShortCode()}");
        Console.WriteLine($"  HEXACO:     {profile.Hexaco.ShortCode()}");
        return 0;
    }

    // ── stats ──────────────────────────────────────────────────────────────────

    private static async Task<int> StatsAsync(string[] args)
    {
        var json = args.Contains("--json");
        var connection = ParseConnection(args);
        await using var db = LegionData.CreateContext(connection);
        var latest = await new PsychometricProfileRepository(db).LatestPerPersonaAsync();

        if (latest.Count == 0)
        {
            Console.Error.WriteLine("no profiles yet. Run 'legion psychometrics score'.");
            return 1;
        }

        var mbti = latest.GroupBy(p => p.Mbti.Type).ToDictionary(g => g.Key, g => g.Count());
        var disc = latest.GroupBy(p => p.Disc.PrimaryStyle).ToDictionary(g => g.Key, g => g.Count());
        var enn = latest.GroupBy(p => p.Enneagram.Type).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var meanOcean = new OceanScores(
            latest.Average(p => p.Ocean.Openness), latest.Average(p => p.Ocean.Conscientiousness),
            latest.Average(p => p.Ocean.Extraversion), latest.Average(p => p.Ocean.Agreeableness),
            latest.Average(p => p.Ocean.Neuroticism));
        var meanHexaco = new HexacoScores(
            latest.Average(p => p.Hexaco.HonestyHumility), latest.Average(p => p.Hexaco.Emotionality),
            latest.Average(p => p.Hexaco.Extraversion), latest.Average(p => p.Hexaco.Agreeableness),
            latest.Average(p => p.Hexaco.Conscientiousness), latest.Average(p => p.Hexaco.Openness));

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                profiles = latest.Count,
                mbti, disc, enneagram = enn,
                meanOcean, meanHexaco,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"Profiles (latest per persona): {latest.Count}");
        Console.WriteLine();
        Console.WriteLine("MBTI types:");
        foreach (var kv in mbti.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key,-5} {kv.Value}");
        Console.WriteLine("DISC primary:");
        foreach (var kv in disc.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key,-5} {kv.Value}");
        Console.WriteLine("Enneagram types:");
        foreach (var kv in enn) Console.WriteLine($"  {kv.Key,-5} {kv.Value}");
        Console.WriteLine($"Mean OCEAN:  {meanOcean.ShortCode()}");
        Console.WriteLine($"Mean HEXACO: {meanHexaco.ShortCode()}");
        return 0;
    }

    // ── history ─────────────────────────────────────────────────────────────────

    private static async Task<int> HistoryAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--"))
        {
            Console.Error.WriteLine("usage: legion psychometrics history <persona-id> [--connection <cs>]");
            return 1;
        }
        var personaId = args[0];
        var connection = ParseConnection(args);
        await using var db = LegionData.CreateContext(connection);
        var rows = await new PsychometricProfileRepository(db).HistoryAsync(personaId);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"no profiles for '{personaId}'.");
            return 1;
        }
        Console.WriteLine($"{personaId} — {rows.Count} run(s):");
        foreach (var p in rows)
            Console.WriteLine($"  run #{p.AssessmentRunId,-4} {p.ScoredAtUtc:u}  {p.ToDomain().Summary()}");
        return 0;
    }

    // ── diff ──────────────────────────────────────────────────────────────────

    private static async Task<int> DiffAsync(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var runA) || !int.TryParse(args[1], out var runB))
        {
            Console.Error.WriteLine("usage: legion psychometrics diff <runA> <runB> [--connection <cs>]");
            return 1;
        }
        var connection = ParseConnection(args);
        await using var db = LegionData.CreateContext(connection);
        var repo = new PsychometricProfileRepository(db);
        var a = (await repo.ByRunAsync(runA)).ToDictionary(p => p.PersonaId);
        var b = (await repo.ByRunAsync(runB)).ToDictionary(p => p.PersonaId);
        var shared = a.Keys.Intersect(b.Keys).OrderBy(x => x).ToList();
        if (shared.Count == 0)
        {
            Console.Error.WriteLine($"runs #{runA} and #{runB} share no personas (or don't exist).");
            return 1;
        }

        double oceanDrift = 0, hexacoDrift = 0, discDrift = 0;
        int mbtiChanged = 0, ennChanged = 0;
        foreach (var id in shared)
        {
            var x = a[id]; var y = b[id];
            oceanDrift += MeanAbs(
                x.Ocean.Openness - y.Ocean.Openness, x.Ocean.Conscientiousness - y.Ocean.Conscientiousness,
                x.Ocean.Extraversion - y.Ocean.Extraversion, x.Ocean.Agreeableness - y.Ocean.Agreeableness,
                x.Ocean.Neuroticism - y.Ocean.Neuroticism);
            hexacoDrift += MeanAbs(
                x.Hexaco.HonestyHumility - y.Hexaco.HonestyHumility, x.Hexaco.Emotionality - y.Hexaco.Emotionality,
                x.Hexaco.Extraversion - y.Hexaco.Extraversion, x.Hexaco.Agreeableness - y.Hexaco.Agreeableness,
                x.Hexaco.Conscientiousness - y.Hexaco.Conscientiousness, x.Hexaco.Openness - y.Hexaco.Openness);
            discDrift += MeanAbs(
                x.Disc.Dominance - y.Disc.Dominance, x.Disc.Influence - y.Disc.Influence,
                x.Disc.Steadiness - y.Disc.Steadiness, x.Disc.Conscientiousness - y.Disc.Conscientiousness);
            if (x.Mbti.Type != y.Mbti.Type) mbtiChanged++;
            if (x.Enneagram.Type != y.Enneagram.Type) ennChanged++;
        }
        var n = shared.Count;
        Console.WriteLine($"diff run #{runA} → #{runB}  ({n} shared personas)");
        Console.WriteLine($"  OCEAN  mean |Δ| per dimension: {oceanDrift / n:0.0}");
        Console.WriteLine($"  HEXACO mean |Δ| per dimension: {hexacoDrift / n:0.0}");
        Console.WriteLine($"  DISC   mean |Δ| per dimension: {discDrift / n:0.0}");
        Console.WriteLine($"  MBTI type changed:      {mbtiChanged}/{n} ({100.0 * mbtiChanged / n:0.0}%)");
        Console.WriteLine($"  Enneagram type changed: {ennChanged}/{n} ({100.0 * ennChanged / n:0.0}%)");
        return 0;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static double MeanAbs(params double[] deltas) => deltas.Select(Math.Abs).Average();

    private static object ToDto(PsychometricProfileEntity p, PersonaEntity? persona) => new
    {
        personaId = p.PersonaId,
        name = persona?.Name,
        scoredAtUtc = p.ScoredAtUtc,
        administeredBy = new { provider = p.AdministeredByProvider, model = p.AdministeredByModel },
        instrumentSetVersion = p.InstrumentSetVersion,
        ocean = p.Ocean,
        hexaco = p.Hexaco,
        mbti = p.Mbti,
        enneagram = new { p.Enneagram.Type, p.Enneagram.Wing, p.Enneagram.Triad, notation = p.Enneagram.Notation() },
        disc = p.Disc,
    };

    private static string? ParseConnection(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--connection", StringComparison.OrdinalIgnoreCase))
            {
                LastConnectionOverride = args[i + 1];
                return args[i + 1];
            }
        return null;
    }

    /// <summary>Show only server + database from a connection string (never credentials).</summary>
    private static string Redact(string connectionString)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            return $"{b.DataSource}/{b.InitialCatalog}";
        }
        catch { return "(configured connection)"; }
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"unknown psychometrics subcommand: {sub}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("legion psychometrics <subcommand> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Score the persona library on OCEAN, HEXACO, MBTI, Enneagram, and DISC, and");
        Console.WriteLine("  persist versioned profiles to SQL Server.");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  db init                      Create/upgrade the database and seed personas.");
        Console.WriteLine("  score [opts]                 Score personas missing a current-version profile (resumable).");
        Console.WriteLine("  rescore [opts]               Force a fresh full run (drift tracking).");
        Console.WriteLine("  show <persona-id> [--json]   Print a persona's latest profile.");
        Console.WriteLine("  stats [--json]               Distribution summary across the library.");
        Console.WriteLine("  history <persona-id>         A persona's profiles across runs.");
        Console.WriteLine("  diff <runA> <runB>           Per-framework drift between two runs.");
        Console.WriteLine();
        Console.WriteLine("score/rescore opts:");
        Console.WriteLine("  --provider <id>   Trusted administrator (default claude).");
        Console.WriteLine("  --tier <t>        low|medium|high|higher|highest (default high = Opus class).");
        Console.WriteLine("  --limit N         Score at most N personas (pilot runs).");
        Console.WriteLine("  --concurrency N   Personas assessed in parallel (default 4).");
        Console.WriteLine("  --timeout S       Per-provider timeout in seconds (default 120).");
        Console.WriteLine("  --store-raw       Persist raw per-item answers for audit.");
        Console.WriteLine("  --notes <text>    Free-form note on the run.");
        Console.WriteLine();
        Console.WriteLine("All subcommands accept --connection <cs>; otherwise the MINDATTIC_LEGION_DB env var");
        Console.WriteLine("or a local (localdb)\\MSSQLLocalDB database is used.");
    }
}
