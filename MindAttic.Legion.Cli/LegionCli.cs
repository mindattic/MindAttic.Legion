namespace MindAttic.Legion.Cli;

using MindAttic.Legion;

public class LegionCli
{
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
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static bool IsHelp(string a) =>
        a is "-h" or "--help" or "help" or "/?";

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"unknown command: {cmd}");
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

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
        Console.WriteLine();
        Console.WriteLine("All commands read keys from the shared store at %APPDATA%/MindAttic/LLM/.");
    }

    // ── health ─────────────────────────────────────────────────────────────────

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

    private static int Providers()
    {
        Console.WriteLine($"{"ID",-12} {"NAME",-12} {"VENDOR",-14} {"DEFAULT MODEL",-46} {"DASHBOARD"}");
        Console.WriteLine(new string('─', 130));
        foreach (var p in LlmProviderCatalog.All)
            Console.WriteLine($"{p.Id,-12} {p.DisplayName,-12} {p.Vendor,-14} {Truncate(p.DefaultModel, 44),-46} {p.DashboardUrl}");
        return 0;
    }

    // ── models ─────────────────────────────────────────────────────────────────

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

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");
}
