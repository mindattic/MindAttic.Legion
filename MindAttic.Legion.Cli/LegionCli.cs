namespace MindAttic.Legion.Cli;

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Legion.Providers;

/// <summary>
/// Command-line entry point for the <c>legion</c> tool. Dispatches the first
/// argument to a subcommand (<c>health</c>, <c>ping</c>, <c>providers</c>,
/// <c>models</c>, <c>personas</c>, <c>panel</c>) and reads API keys from the
/// shared MindAttic credential store at <c>%APPDATA%/MindAttic/LLM/</c>.
/// </summary>
public class LegionCli
{
    /// <summary>
    /// Parses <paramref name="args"/>, runs the matching subcommand, and returns
    /// a process exit code: <c>0</c> on success, <c>1</c> for usage errors or a
    /// failed health check, <c>2</c> when an exception bubbled out.
    /// </summary>
    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "health"    => await HealthAsync(args.Skip(1).ToArray()),
                "ping"      => await PingAsync(args.Skip(1).ToArray()),
                "models"    => Models(args.Skip(1).ToArray()),
                "providers" => Providers(),
                "personas"  => Personas(args.Skip(1).ToArray()),
                "panel"     => Panel(args.Skip(1).ToArray()),
                "vote"      => await VoteAsync(args.Skip(1).ToArray()),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    /// <summary>True when the argument is one of the help flags.</summary>
    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    /// <summary>Print "unknown command" + usage and return exit code 1.</summary>
    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"unknown command: {cmd}");
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    /// <summary>Write the top-level usage banner to stdout.</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("legion — MindAttic.Legion CLI");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  health                       Probe every provider with a 'Hello World!' test");
        Console.WriteLine("  ping <provider>              Probe a single provider");
        Console.WriteLine("  providers                    List supported providers + dashboard URLs");
        Console.WriteLine("  models <provider>            Show known models for a provider");
        Console.WriteLine("  personas <count>             Sample N personas from the 1000-persona library");
        Console.WriteLine("  panel <count> [provider...]  Build a voter panel: spread across providers, backfill claude");
        Console.WriteLine("  vote <question> [opts]       Multi-LLM consensus vote on a question; outputs JSON.");
        Console.WriteLine("                               Opts: --context <text>, --quorum plurality|simplemajority|twothirds|unanimous,");
        Console.WriteLine("                                     --options A,B,C, --max-tokens N, --no-narrative");
        Console.WriteLine();
        Console.WriteLine("All commands read keys from the shared store at %APPDATA%/MindAttic/LLM/.");
    }

    // ── health ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Probe every supported provider (or the supplied subset) with the
    /// "Hello World!" health prompt and print a one-line summary per provider.
    /// Returns 0 if at least one provider replied correctly, 1 otherwise.
    /// </summary>
    private static async Task<int> HealthAsync(string[] args)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new LegionClient(http, LegionClientOptions.NoResilience);
        var hc = new LlmHealthCheck(client);

        var providerIds = args.Length > 0 ? args : LlmProviderCatalog.AllIds.ToArray();
        Console.WriteLine($"Probing {providerIds.Length} provider(s)...");
        Console.WriteLine();

        var results = await hc.CheckAsync(providerIds, timeoutPerProvider: TimeSpan.FromSeconds(20));

        // Header
        Console.WriteLine($"{"PROVIDER",-12} {"STATUS",-14} {"TIME",-8} {"DETAIL"}");
        Console.WriteLine(new string('─', 100));
        foreach (var r in results)
        {
            var detail = r.HasCredential
                ? (r.IsHealthy ? (r.Response ?? "").Trim() : (r.ErrorMessage ?? ""))
                : $"add a key at {r.KeysUrl}";
            Console.WriteLine($"{r.DisplayName,-12} {r.Status,-14} {r.ElapsedMilliseconds + "ms",-8} {Truncate(detail, 60)}");
        }
        Console.WriteLine();

        var ok      = results.Count(r => r.RespondedCorrectly);
        var missing = results.Count(r => !r.HasCredential);
        var errors  = results.Count(r => r.HasCredential && !r.IsHealthy);
        var wrong   = results.Count(r => r.HasCredential && r.IsHealthy && !r.RespondedCorrectly);
        Console.WriteLine($"summary: {ok} ok / {missing} missing-key / {errors} errored / {wrong} wrong-reply");

        // Exit code: 0 if at least one provider healthy, 1 otherwise.
        return ok > 0 ? 0 : 1;
    }

    // ── ping ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Probe a single provider and print its status, reply (or error), and
    /// dashboard URL. Returns 0 if the reply matched the probe expectation.
    /// </summary>
    private static async Task<int> PingAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: legion ping <provider>");
            return 1;
        }
        var providerId = args[0];
        var http   = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new LegionClient(http, LegionClientOptions.NoResilience);
        var hc     = new LlmHealthCheck(client);
        var r      = await hc.CheckOneAsync(providerId, timeout: TimeSpan.FromSeconds(20));

        Console.WriteLine($"{r.DisplayName} ({r.ProviderId})  {r.Status}  {r.ElapsedMilliseconds}ms");
        if (!r.HasCredential)
            Console.WriteLine($"  add a key:  {r.KeysUrl}");
        else if (r.IsHealthy)
            Console.WriteLine($"  reply: {(r.Response ?? "").Trim()}");
        else
            Console.WriteLine($"  error: {r.ErrorMessage}");
        Console.WriteLine($"  dashboard:  {r.DashboardUrl}");

        return r.RespondedCorrectly ? 0 : 1;
    }

    // ── providers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Print a table of every provider Legion knows about, with id, name,
    /// vendor, default model, and dashboard URL.
    /// </summary>
    private static int Providers()
    {
        Console.WriteLine($"{"ID",-12} {"NAME",-12} {"VENDOR",-14} {"DEFAULT MODEL",-46} {"DASHBOARD"}");
        Console.WriteLine(new string('─', 130));
        foreach (var p in LlmProviderCatalog.All)
            Console.WriteLine($"{p.Id,-12} {p.DisplayName,-12} {p.Vendor,-14} {Truncate(p.DefaultModel, 44),-46} {p.DashboardUrl}");
        return 0;
    }

    // ── models ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Print the catalog of known models for a provider, marking the default
    /// model and pointing at the live <c>/v1/models</c> endpoint when known.
    /// </summary>
    private static int Models(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: legion models <provider>");
            return 1;
        }
        var p = LlmProviderCatalog.Get(args[0]);
        if (p is null)
        {
            Console.Error.WriteLine($"unknown provider: {args[0]}");
            Console.Error.WriteLine($"known: {string.Join(", ", LlmProviderCatalog.AllIds)}");
            return 1;
        }
        Console.WriteLine($"{p.DisplayName} ({p.Vendor})  default: {p.DefaultModel}");
        Console.WriteLine($"  dashboard: {p.DashboardUrl}");
        Console.WriteLine($"  keys:      {p.KeysUrl}");
        if (!string.IsNullOrEmpty(p.ModelsApiEndpoint))
            Console.WriteLine($"  live:      {p.ModelsApiEndpoint}");
        Console.WriteLine();
        Console.WriteLine($"Known models ({p.AvailableModels.Count}):");
        foreach (var m in p.AvailableModels)
            Console.WriteLine($"  - {m}{(m == p.DefaultModel ? "  (default)" : "")}");
        return 0;
    }

    // ── personas ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sample <c>count</c> personas (default 10) from <see cref="PersonaLibrary"/>
    /// without replacement, and print id + name + the first line of each persona's
    /// system prompt as a fingerprint.
    /// </summary>
    private static int Personas(string[] args)
    {
        var count = 10;
        if (args.Length > 0 && !int.TryParse(args[0], out count))
        {
            Console.Error.WriteLine("usage: legion personas <count>");
            return 1;
        }
        var sample = PersonaLibrary.Sample(count);
        Console.WriteLine($"Sampled {sample.Count} of {PersonaLibrary.Count} personas:");
        Console.WriteLine();
        foreach (var p in sample)
        {
            Console.WriteLine($"[{p.Id}] {p.Name}");
            // First line of the personality prompt is the most distinctive part.
            var firstLine = p.PersonalityMarkdown.Split('\n').FirstOrDefault()?.Trim() ?? "";
            Console.WriteLine($"   {firstLine}");
        }
        return 0;
    }

    // ── panel ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a voter panel via <see cref="VoterFactory.GenerateUniqueVoters"/>:
    /// spread voters across the supplied providers (or every provider with a
    /// configured key) and backfill with <c>claude</c>. Prints provider, name,
    /// and a one-line persona preview per voter.
    /// </summary>
    private static int Panel(string[] args)
    {
        if (args.Length == 0 || !int.TryParse(args[0], out var count))
        {
            Console.Error.WriteLine("usage: legion panel <count> [provider1 provider2 ...]");
            return 1;
        }
        var providers = args.Skip(1).ToArray();
        if (providers.Length == 0)
            providers = LlmProviderCatalog.AllIds
                .Where(id => !string.IsNullOrEmpty(MindAtticCredentialStore.GetKey(id)))
                .ToArray();

        var voters = VoterFactory.GenerateUniqueVoters(count, providers, fallbackProviderId: "claude");
        Console.WriteLine($"Built panel of {voters.Count}; available providers: [{string.Join(", ", providers)}]");
        Console.WriteLine();
        Console.WriteLine($"{"PROVIDER",-12} {"NAME",-14} {"PERSONA"}");
        Console.WriteLine(new string('─', 100));
        foreach (var v in voters)
        {
            var firstLine = v.PersonalityMarkdown.Split('\n').FirstOrDefault()?.Trim() ?? "";
            Console.WriteLine($"{v.ProviderId,-12} {v.Name,-14} {Truncate(firstLine, 70)}");
        }
        return 0;
    }

    /// <summary>
    /// Trim <paramref name="s"/> to at most <paramref name="max"/> characters,
    /// appending an ellipsis when truncated. Returns "" for null/empty input.
    /// </summary>
    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");

    // ── vote ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Run a multi-LLM consensus vote on the supplied question. Constructs the
    /// voting service from the shared <see cref="MindAtticCredentialStore"/>,
    /// dispatches to every configured provider in parallel, aggregates the
    /// result, and emits a JSON document on stdout. Exit code 0 if quorum was
    /// reached, 1 otherwise (also 1 on usage error).
    ///
    /// Usage:
    ///   legion vote "Should Kyle take the contract?"
    ///   legion vote "Pick the best option" --options "A,B,C" --quorum twothirds
    ///   legion vote "Rate this scene" --context "..." --no-narrative
    ///
    /// Designed for autonomous Claude Code sessions that need a second opinion
    /// on tonal or canon decisions — invoke from Bash, parse the JSON, act on
    /// the consensus.
    /// </summary>
    private static async Task<int> VoteAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            Console.Error.WriteLine("usage: legion vote <question> [--context <text>] [--context-file <path>] [--quorum plurality|simplemajority|twothirds|unanimous] [--options A,B,C] [--max-tokens N] [--no-narrative]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Outputs the vote result as JSON on stdout. Exit code 0 if quorum was reached, 1 otherwise.");
            return args.Length == 0 ? 1 : 0;
        }

        var question            = args[0];
        var context             = "";
        var quorum              = Quorum.SimpleMajority;
        var options             = new List<string>();
        var maxTokens           = 2048;
        var synthesizeNarrative = true;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--context":
                    if (i + 1 < args.Length) context = args[++i];
                    break;
                case "--context-file":
                    if (i + 1 < args.Length)
                    {
                        var ctxPath = args[++i];
                        try
                        {
                            context = await File.ReadAllTextAsync(ctxPath);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"error: failed to read --context-file '{ctxPath}': {ex.Message}");
                            return 1;
                        }
                    }
                    break;
                case "--quorum":
                    if (i + 1 < args.Length && Enum.TryParse<Quorum>(args[++i], ignoreCase: true, out var q))
                        quorum = q;
                    break;
                case "--options":
                    if (i + 1 < args.Length)
                        options = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                case "--max-tokens":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt))
                        maxTokens = mt;
                    break;
                case "--no-narrative":
                    synthesizeNarrative = false;
                    break;
            }
        }

        var config       = new VotingConfiguration();
        var activeIds    = config.ActiveProviderIds;
        if (activeIds.Count == 0)
        {
            Console.Error.WriteLine("error: no providers have a configured API key — add keys at %APPDATA%/MindAttic/LLM/ or set ApiKeys explicitly");
            return 1;
        }

        using var http = new HttpClient { Timeout = config.ProviderTimeout };
        var provider   = new LlmVotingProvider(http, config);
        var service    = new LLMVotingService(provider, config, NullLogger<LLMVotingService>.Instance);

        var request = new VoteRequest
        {
            Question            = question,
            Context             = context,
            Options             = options,
            MaxTokens           = maxTokens,
            SynthesizeNarrative = synthesizeNarrative,
        };

        var result = await service.VoteAsync(request, quorum);

        var output = new
        {
            question           = result.Question,
            consensus          = result.Consensus,
            consensus_strength = result.ConsensusStrength,
            quorum_type        = result.QuorumType.ToString(),
            quorum_reached     = result.QuorumReached,
            successful_voters  = result.SuccessfulVoters,
            total_voters       = result.IndividualVotes.Count,
            narrative          = result.NarrativeSummary,
            duration_ms        = (int)result.Duration.TotalMilliseconds,
            votes = result.IndividualVotes.Select(v => new
            {
                voter      = v.VoterName,
                provider   = v.ProviderId,
                decision   = v.Decision,
                reasoning  = v.Reasoning,
                confidence = v.Confidence,
                error      = v.IsError ? v.ErrorMessage : null,
            }),
            dissenters = result.DissenterReasons,
        };

        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        }));

        return result.QuorumReached ? 0 : 1;
    }
}
