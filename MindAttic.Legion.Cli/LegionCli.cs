namespace MindAttic.Legion.Cli;

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Legion.Providers;
using MindAttic.Vault.Configuration;

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

        // Wire the %APPDATA%/MindAttic credential files + env vars into the Vault
        // credential chain. Reads consult MindAttic:Vault:LLM:* in IConfiguration,
        // surfaced from the %APPDATA%/MindAttic/LLM/providers.json file.
        MindAtticCredentialStore.UseConfiguration(BuildConfiguration());

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "health"    => await HealthAsync(args.Skip(1).ToArray()),
                "ping"      => await PingAsync(args.Skip(1).ToArray()),
                "status"    => await StatusAsync(args.Skip(1).ToArray()),
                "models"    => Models(args.Skip(1).ToArray()),
                "providers" => Providers(),
                "personas"  => Personas(args.Skip(1).ToArray()),
                "panel"     => Panel(args.Skip(1).ToArray()),
                "vote"      => await VoteAsync(args.Skip(1).ToArray()),
                "ask"       => await AskCommand.RunAsync(args.Skip(1).ToArray()),
                "poll"      => await PollCommand.RunAsync(args.Skip(1).ToArray()),
                "generate"  => await GenerateCommand.RunAsync(args.Skip(1).ToArray()),
                "tiers"     => await TiersCommand.RunAsync(args.Skip(1).ToArray()),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Build the credential-source chain handed to <see cref="MindAtticCredentialStore.UseConfiguration"/>.
    /// The <c>%APPDATA%\MindAttic</c> credential files + environment variables (incl. the
    /// <c>MindAttic__Vault__LLM__claude__apiKey</c> form for App Service / containers).
    /// </summary>
    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddMindAtticVaultFiles()
            .AddEnvironmentVariables()
            .Build();

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
        Console.WriteLine("  status [opts] [provider...]  Show model inventory, config, and connectivity");
        Console.WriteLine("  providers                    List supported providers + dashboard URLs");
        Console.WriteLine("  models <provider>            Show known models for a provider");
        Console.WriteLine("  personas <count>             Sample N personas from the 1000-persona library");
        Console.WriteLine("  panel <count> [provider...]  Build a voter panel: spread across providers, backfill claude");
        Console.WriteLine("  vote <question> [opts]       Multi-LLM consensus vote on a question; outputs JSON.");
        Console.WriteLine("                               Opts: --context <text>, --quorum plurality|simplemajority|twothirds|unanimous,");
        Console.WriteLine("                                     --options A,B,C, --max-tokens N, --no-narrative");
        Console.WriteLine("  ask <question> [opts]        Architect-framed decision; stdout = bare answer (or --json).");
        Console.WriteLine("                               Auto-pulls CLAUDE.md/README/git as context. Built for piping");
        Console.WriteLine("                               back into a Claude Code or Codex CLI that's blocking on a prompt.");
        Console.WriteLine("                               Opts: --tier low|medium|high (default high), --options, --quorum, etc.");
        Console.WriteLine("  poll <question> [opts]       Bulk vote: N voters round-robined across trusted providers");
        Console.WriteLine("                               at a chosen tier. Outputs distribution + plurality winner.");
        Console.WriteLine("                               Opts: --count N (default 10), --tier (default low), --options,");
        Console.WriteLine("                                     --concurrency N, --timeout S, --json");
        Console.WriteLine("  generate <prompt> [opts]     Bulk creative output: N distinct items via one batched call");
        Console.WriteLine("                               per provider, deduped, newline-separated to stdout.");
        Console.WriteLine("                               Opts: --count N (default 10), --tier (default medium),");
        Console.WriteLine("                                     --temperature T, --no-dedup, --providers, --json");
        Console.WriteLine("  tiers [opts]                 Probe trusted providers × tier mapping (Low/Medium/High).");
        Console.WriteLine("                               Opts: --providers a,b,c, --tiers low,medium,high, --all-tiers,");
        Console.WriteLine("                                     --json, --timeout SECONDS, --max-tokens N");
        Console.WriteLine("  status opts: --no-probe, --json, --timeout N");
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

    // ── status ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Render a provider/model status board: static catalog, live model discovery,
    /// credential/local endpoint configuration, and optional prompt-level health.
    /// </summary>
    private static async Task<int> StatusAsync(string[] args)
    {
        var runProbe = true;
        var json = false;
        var timeout = TimeSpan.FromSeconds(20);
        var providerIds = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--no-probe":
                    runProbe = false;
                    break;
                case "--json":
                    json = true;
                    break;
                case "--timeout":
                    if (i + 1 >= args.Length || !double.TryParse(args[++i], out var seconds) || seconds <= 0)
                    {
                        Console.Error.WriteLine("usage: legion status [--no-probe] [--json] [--timeout seconds] [provider...]");
                        return 1;
                    }
                    timeout = TimeSpan.FromSeconds(seconds);
                    break;
                default:
                    providerIds.Add(args[i]);
                    break;
            }
        }

        if (providerIds.Count == 0)
            providerIds.AddRange(LlmProviderCatalog.AllIds);

        using var http = new HttpClient { Timeout = timeout + TimeSpan.FromSeconds(5) };
        var discovery = new LlmModelDiscovery(http);
        var inventory = await discovery.DiscoverAsync(providerIds, timeout);

        IReadOnlyList<LlmHealthResult> health = Array.Empty<LlmHealthResult>();
        if (runProbe)
        {
            var client = new LegionClient(http, LegionClientOptions.NoResilience);
            health = await new LlmHealthCheck(client).CheckAsync(providerIds, timeout);
        }

        if (json)
        {
            WriteStatusJson(inventory, health, runProbe, timeout);
            return runProbe && !health.Any(r => r.RespondedCorrectly) ? 1 : 0;
        }

        WriteStatusText(inventory, health, runProbe, timeout);
        return runProbe && !health.Any(r => r.RespondedCorrectly) ? 1 : 0;
    }

    /// <summary>
    /// Render the status board as plain text: a short summary header, a
    /// fixed-width table with one row per provider (config / live-model
    /// count / connectivity / effective model), then a per-provider detail
    /// section with vendor, auth state, endpoints, model lists, and the
    /// next-step hint when discovery or connectivity failed.
    /// </summary>
    private static void WriteStatusText(
        IReadOnlyList<LlmModelDiscoveryResult> inventory,
        IReadOnlyList<LlmHealthResult> health,
        bool runProbe,
        TimeSpan timeout)
    {
        var healthByProvider = health.ToDictionary(r => r.ProviderId, StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("MindAttic.Legion model status");
        Console.WriteLine($"Credential store: {MindAtticCredentialStore.CredentialDirectory}");
        Console.WriteLine($"providers.json:   {MindAtticCredentialStore.ProvidersFilePath} ({(MindAtticCredentialStore.ProvidersFileExists() ? "found" : "missing")})");
        Console.WriteLine($"Probe:            {(runProbe ? "enabled" : "disabled")} ({timeout.TotalSeconds:0}s timeout)");
        Console.WriteLine();

        Console.WriteLine($"{"ID",-12} {"CONFIG",-13} {"LIVE",-8} {"CONNECTIVITY",-16} {"EFFECTIVE MODEL"}");
        Console.WriteLine(new string('-', 110));
        foreach (var item in inventory)
        {
            healthByProvider.TryGetValue(item.Provider.Id, out var h);
            var config = item.HasCredential ? "key ok" : "missing key";
            var live = item.LiveModelQuerySucceeded ? item.LiveModels.Count.ToString() : item.Diagnosis.ToString();
            var connectivity = h is null
                ? "not probed"
                : h.RespondedCorrectly ? "OK"
                : h.Diagnosis.ToString();

            Console.WriteLine($"{item.Provider.Id,-12} {config,-13} {Truncate(live, 8),-8} {Truncate(connectivity, 16),-16} {Truncate(item.EffectiveModel, 44)}");
        }

        foreach (var item in inventory)
        {
            healthByProvider.TryGetValue(item.Provider.Id, out var h);

            Console.WriteLine();
            Console.WriteLine($"{item.Provider.DisplayName} ({item.Provider.Id})");
            Console.WriteLine($"  vendor:          {item.Provider.Vendor}");
            Console.WriteLine($"  auth:            {(item.HasCredential ? "API key configured" : "missing API key")}");
            if (!string.IsNullOrWhiteSpace(item.ModelsEndpoint))
                Console.WriteLine($"  models endpoint: {item.ModelsEndpoint}");
            if (!string.IsNullOrWhiteSpace(item.ConfiguredModel))
                Console.WriteLine($"  configured model: {item.ConfiguredModel}");
            Console.WriteLine($"  effective model: {item.EffectiveModel}");

            if (item.LiveModelQuerySucceeded)
                WriteModelList("live models", item.LiveModels);
            else
            {
                var nextStep = LlmHealthDiagnoser.ActionableMessage(
                    item.Diagnosis, item.Provider.DisplayName, item.Provider.KeysUrl, item.Provider.DashboardUrl);
                Console.WriteLine($"  model discovery: {item.Diagnosis} - {Truncate(item.ErrorMessage ?? "not queried", 120)}");
                Console.WriteLine($"  next step:       {nextStep}");
                WriteModelList("catalog models", item.KnownModels);
            }

            if (h is not null)
            {
                var status = h.RespondedCorrectly
                    ? $"OK ({h.ElapsedMilliseconds}ms)"
                    : $"{h.Diagnosis} ({h.ElapsedMilliseconds}ms)";
                Console.WriteLine($"  connectivity:    {status}");
                if (!h.RespondedCorrectly)
                {
                    var detail = !string.IsNullOrWhiteSpace(h.ErrorMessage) ? h.ErrorMessage : h.Response;
                    if (!string.IsNullOrWhiteSpace(detail))
                        Console.WriteLine($"  detail:          {Truncate(detail, 120)}");
                    Console.WriteLine($"  next step:       {h.ActionableMessage}");
                }
            }
        }
    }

    /// <summary>
    /// Same status data as <see cref="WriteStatusText"/>, but as a single
    /// pretty-printed JSON document on stdout. Designed for piping to
    /// <c>jq</c>, dashboards, or CI scripts that need structured input.
    /// </summary>
    private static void WriteStatusJson(
        IReadOnlyList<LlmModelDiscoveryResult> inventory,
        IReadOnlyList<LlmHealthResult> health,
        bool runProbe,
        TimeSpan timeout)
    {
        var healthByProvider = health.ToDictionary(r => r.ProviderId, StringComparer.OrdinalIgnoreCase);
        var output = new
        {
            credentialStore = MindAtticCredentialStore.CredentialDirectory,
            providersJson = MindAtticCredentialStore.ProvidersFilePath,
            providersJsonExists = MindAtticCredentialStore.ProvidersFileExists(),
            probeEnabled = runProbe,
            timeoutSeconds = timeout.TotalSeconds,
            providers = inventory.Select(item =>
            {
                healthByProvider.TryGetValue(item.Provider.Id, out var h);
                return new
                {
                    id = item.Provider.Id,
                    displayName = item.Provider.DisplayName,
                    vendor = item.Provider.Vendor,
                    hasCredential = item.HasCredential,
                    configuredModel = item.ConfiguredModel,
                    effectiveModel = item.EffectiveModel,
                    modelsEndpoint = item.ModelsEndpoint,
                    liveModelQuerySucceeded = item.LiveModelQuerySucceeded,
                    liveModelDiagnosis = item.Diagnosis.ToString(),
                    liveModelError = item.ErrorMessage,
                    liveModels = item.LiveModels,
                    catalogModels = item.KnownModels,
                    connectivity = h is null ? null : new
                    {
                        status = h.Status,
                        diagnosis = h.Diagnosis.ToString(),
                        httpStatusCode = h.HttpStatusCode,
                        elapsedMilliseconds = h.ElapsedMilliseconds,
                        respondedCorrectly = h.RespondedCorrectly,
                        response = h.Response,
                        error = h.ErrorMessage,
                        nextStep = h.ActionableMessage,
                    },
                };
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        }));
    }

    /// <summary>
    /// Print a labelled bulleted list of model ids — used by the per-provider
    /// detail block to show either live or catalog models with a count.
    /// </summary>
    private static void WriteModelList(string label, IReadOnlyList<string> models)
    {
        Console.WriteLine($"  {label}:     {models.Count}");
        foreach (var model in models)
            Console.WriteLine($"    - {model}");
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
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt) && mt > 0)
                        maxTokens = mt;
                    break;
                case "--no-narrative":
                    synthesizeNarrative = false;
                    break;
            }
        }

        var config = new VotingConfiguration();
        // Honor the per-project legion.json (voters/judge/models/apiKeys)
        // discovered by walking up from the current directory. Without this
        // the CLI's `vote` subcommand bypassed every project-local panel
        // declaration and used the default trusted four instead.
        LegionConfig.LoadFromDirectory()?.ApplyTo(config);
        var activeIds    = config.ActiveProviderIds;
        if (activeIds.Count == 0)
        {
            Console.Error.WriteLine("error: no providers have a configured API key — add keys at %APPDATA%/MindAttic/LLM/ or set ApiKeys explicitly");
            return 1;
        }

        using var http = new HttpClient { Timeout = config.ProviderTimeout };
        var provider   = new LlmVotingProvider(http, config);
        var service    = new LlmVotingService(provider, config, NullLogger<LlmVotingService>.Instance);

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
