namespace MindAttic.Legion.Cli;

using System.Text.Json;
using MindAttic.Legion;
using MindAttic.Legion.Providers;

/// <summary>
/// <c>legion psychometrics</c> — score the persona library on five instruments
/// (Big Five/OCEAN, HEXACO, MBTI-style, Enneagram-style, DISC-style) and persist
/// the results as one faithful JSON file per persona (see <see cref="PersonaStore"/>).
/// A single trusted model administers every test in a run; the administering
/// model is a per-assessment lens, so re-scoring through a different provider
/// records a new <em>variant</em> of the same persona rather than overwriting it.
///
/// Subcommands:
///   init                            create the store and seed persona files
///   score   [opts]                  score personas missing a current-version profile for this lens (resumable)
///   rescore [opts]                  force a fresh full run (drift / new lens)
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
        return args[0].ToLowerInvariant() switch
        {
            "init"    => Init(rest),
            "score"   => await ScoreAsync(rest, rescore: false),
            "rescore" => await ScoreAsync(rest, rescore: true),
            "show"    => Show(rest),
            "stats"   => Stats(rest),
            "history" => History(rest),
            "diff"    => Diff(rest),
            _         => Unknown(args[0]),
        };
    }

    // ── init ──────────────────────────────────────────────────────────────────

    private static int Init(string[] args)
    {
        var store = new PersonaStore(ParseStore(args));
        var changed = store.SyncFromLibrary();
        Console.WriteLine($"Store ready: {store.RootDirectory}");
        Console.WriteLine($"Personas synced: {store.Count()} total ({changed} written).");
        Console.WriteLine($"Instruments: {PsychometricInstruments.All.Count} ({PsychometricInstruments.TotalItemCount} items), set version {PsychometricInstruments.SetVersion}.");
        return 0;
    }

    // ── score / rescore ────────────────────────────────────────────────────────

    private static async Task<int> ScoreAsync(string[] args, bool rescore)
    {
        var store = new PersonaStore(ParseStore(args));
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
        store.SyncFromLibrary();

        // `score` skips personas already scored at the current instrument version
        // *through this provider/lens* (resumable; a different provider yields a
        // new variant). `rescore` always re-scores.
        var byId = PersonaLibrary.All.ToDictionary(p => p.Id);
        var ordered = PersonaLibrary.All.Select(p => p.Id).ToList();
        var skip = rescore
            ? new HashSet<string>(StringComparer.Ordinal)
            : store.PersonaIdsScored(PsychometricInstruments.SetVersion, providerId);
        var toScore = ordered.Where(id => !skip.Contains(id)).Take(limit).Select(id => byId[id]).ToList();

        if (toScore.Count == 0)
        {
            Console.WriteLine(rescore
                ? "Nothing to score."
                : $"All {ordered.Count} personas already scored at set {PsychometricInstruments.SetVersion} via {providerId}. Use 'rescore' or a different --provider for a new variant.");
            return 0;
        }

        Console.WriteLine($"Scoring {toScore.Count} persona(s) on {PsychometricInstruments.All.Count} instruments " +
                          $"via {providerId} ({assessor.ModelId}, tier {tier}), concurrency {concurrency}.");
        Console.WriteLine($"  ≈ {toScore.Count * PsychometricInstruments.All.Count} model calls. Resumable with 'score'.");

        using var cts = new CancellationTokenSource();
        if (!Console.IsInputRedirected)
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var run = store.StartRun(providerId, assessor.ModelId, tier.ToString(),
            PsychometricInstruments.SetVersion, toScore.Count, DateTime.UtcNow, notes);

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
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
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
                    store.SaveAssessment(r.persona.Id, run.Id, r.assessment.Profile, storeRaw ? ToMutable(r.assessment.RawAnswers) : null);
                    done++;
                    Console.WriteLine($"  [{done}/{toScore.Count}] {r.persona.Id}  {r.assessment.Profile.Summary()}");
                }
                store.SetRunProgress(run.Id, done);
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"cancelled — {done} saved, {failed} failed. Resume with 'legion psychometrics score'.");
        }

        store.CompleteRun(run.Id, DateTime.UtcNow);
        Console.WriteLine($"Run #{run.Id} done: {done} scored, {failed} failed.");
        return failed > 0 && done == 0 ? 1 : 0;
    }

    private static Dictionary<string, Dictionary<int, int>> ToMutable(
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> raw) =>
        raw.ToDictionary(kv => kv.Key, kv => kv.Value.ToDictionary(x => x.Key, x => x.Value));

    // ── show ──────────────────────────────────────────────────────────────────

    private static int Show(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--"))
        {
            Console.Error.WriteLine("usage: legion psychometrics show <persona-id> [--json] [--store <dir>]");
            return 1;
        }
        var personaId = args[0];
        var json = args.Contains("--json");
        var store = new PersonaStore(ParseStore(args));

        var doc = store.Get(personaId);
        var profile = store.LatestProfile(personaId);
        if (doc is null || profile is null)
        {
            Console.Error.WriteLine($"no profile for '{personaId}'. Has it been scored? (run 'score')");
            return 1;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"{personaId} — {doc.Name}");
        if (doc.Traits is { IsDefault: false, Archetype: not null })
            Console.WriteLine($"  {doc.Traits.Worldview} {doc.Traits.Archetype}, {doc.Traits.Background}, age {doc.Traits.Age} ({doc.Traits.Pronouns})");
        Console.WriteLine($"  scored {profile.ScoredAtUtc:u} through {profile.AdministeredByProvider}/{profile.AdministeredByModel} (set {profile.InstrumentSetVersion})");
        Console.WriteLine();
        Console.WriteLine($"  MBTI:       {profile.Mbti.Type}  (E/I {profile.Mbti.ExtraversionPct:0} · S/N {profile.Mbti.SensingPct:0} · T/F {profile.Mbti.ThinkingPct:0} · J/P {profile.Mbti.JudgingPct:0})");
        Console.WriteLine($"  Enneagram:  {profile.Enneagram.Notation()}  ({profile.Enneagram.Triad})");
        Console.WriteLine($"  DISC:       {profile.Disc.PrimaryStyle}  [{profile.Disc.ShortCode()}]");
        Console.WriteLine($"  OCEAN:      {profile.Ocean.ShortCode()}");
        Console.WriteLine($"  HEXACO:     {profile.Hexaco.ShortCode()}");
        return 0;
    }

    // ── stats ──────────────────────────────────────────────────────────────────

    private static int Stats(string[] args)
    {
        var json = args.Contains("--json");
        var store = new PersonaStore(ParseStore(args));
        var latest = store.LatestPerPersona();

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
                profiles = latest.Count, mbti, disc, enneagram = enn, meanOcean, meanHexaco,
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

    private static int History(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith("--"))
        {
            Console.Error.WriteLine("usage: legion psychometrics history <persona-id> [--store <dir>]");
            return 1;
        }
        var personaId = args[0];
        var store = new PersonaStore(ParseStore(args));
        var rows = store.History(personaId);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine($"no profiles for '{personaId}'.");
            return 1;
        }
        Console.WriteLine($"{personaId} — {rows.Count} assessment(s):");
        foreach (var a in rows)
            Console.WriteLine($"  run #{a.RunId,-4} {a.Profile.ScoredAtUtc:u}  via {a.Profile.AdministeredByProvider,-8} {a.Profile.Summary()}");
        return 0;
    }

    // ── diff ──────────────────────────────────────────────────────────────────

    private static int Diff(string[] args)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out var runA) || !int.TryParse(args[1], out var runB))
        {
            Console.Error.WriteLine("usage: legion psychometrics diff <runA> <runB> [--store <dir>]");
            return 1;
        }
        var store = new PersonaStore(ParseStore(args));
        var a = store.ProfilesByRun(runA);
        var b = store.ProfilesByRun(runB);
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

    private static string? ParseStore(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--store", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
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
        Console.WriteLine("  persist one JSON file per persona (no database).");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  init                         Create the store and seed persona files.");
        Console.WriteLine("  score [opts]                 Score personas missing a current-version profile for this lens (resumable).");
        Console.WriteLine("  rescore [opts]               Force a fresh full run (drift / new lens).");
        Console.WriteLine("  show <persona-id> [--json]   Print a persona's latest profile.");
        Console.WriteLine("  stats [--json]               Distribution summary across the library.");
        Console.WriteLine("  history <persona-id>         A persona's profiles across runs.");
        Console.WriteLine("  diff <runA> <runB>           Per-framework drift between two runs.");
        Console.WriteLine();
        Console.WriteLine("score/rescore opts:");
        Console.WriteLine("  --provider <id>   Trusted administering lens (default claude).");
        Console.WriteLine("  --tier <t>        low|medium|high|higher|highest (default high = Opus class).");
        Console.WriteLine("  --limit N         Score at most N personas (pilot runs).");
        Console.WriteLine("  --concurrency N   Personas assessed in parallel (default 4).");
        Console.WriteLine("  --timeout S       Per-provider timeout in seconds (default 120).");
        Console.WriteLine("  --store-raw       Persist raw per-item answers for audit.");
        Console.WriteLine("  --notes <text>    Free-form note on the run.");
        Console.WriteLine();
        Console.WriteLine("All subcommands accept --store <dir>; otherwise MINDATTIC_LEGION_STORE or the");
        Console.WriteLine("roaming MindAttic bucket (%APPDATA%/MindAttic/Legion) is used.");
    }
}
