namespace MindAttic.Legion.Cli;

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MindAttic.Legion;
using MindAttic.Legion.Providers;

/// <summary>
/// <c>legion ask</c> — autonomous architectural-decision command.
///
/// Designed for the loop: an upstream coding CLI (Claude Code, Codex) blocks on
/// a user prompt; an outer monitor pipes the prompt to <c>legion ask</c>; the
/// LLM panel deliberates and emits a single answer the monitor feeds back to
/// the blocked CLI.
///
/// Differences from <c>legion vote</c>:
///   <list type="bullet">
///     <item>Default stdout is the bare consensus answer, not JSON. Use
///       <c>--json</c> to get the full audit blob.</item>
///     <item>Voters are framed as senior software architects making a call a
///       teammate can act on right now (boring, reversible, conventional).</item>
///     <item>Auto-collects project context (CLAUDE.md, README, git status,
///       git log) so the caller doesn't have to. Disable with
///       <c>--no-auto-context</c>.</item>
///     <item>Default quorum is <see cref="Quorum.Plurality"/> — always emit
///       <em>some</em> answer; raise the bar with <c>--quorum twothirds</c>
///       when dissent should fail closed.</item>
///   </list>
/// </summary>
public static class AskCommand
{
    /// <summary>
    /// Parse args, run the ask, and return a process exit code:
    /// <c>0</c> when quorum was reached, <c>1</c> on quorum miss or usage
    /// error, <c>2</c> on unhandled exception.
    /// </summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var question          = args[0];
        var options           = new List<string>();
        var explicitContext   = "";
        var contextFile       = "";
        var projectDir        = Environment.CurrentDirectory;
        var autoContext       = true;
        var quorum            = Quorum.Plurality;
        var maxTokens         = 1024;
        var emitJson          = false;
        var timeoutSeconds    = 60.0;
        var providerOverride  = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--options":
                    if (i + 1 < args.Length)
                        options = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                case "--context":
                    if (i + 1 < args.Length) explicitContext = args[++i];
                    break;
                case "--context-file":
                    if (i + 1 < args.Length) contextFile = args[++i];
                    break;
                case "--project-dir":
                    if (i + 1 < args.Length) projectDir = args[++i];
                    break;
                case "--no-auto-context":
                    autoContext = false;
                    break;
                case "--quorum":
                    if (i + 1 < args.Length && Enum.TryParse<Quorum>(args[++i], ignoreCase: true, out var q))
                        quorum = q;
                    break;
                case "--max-tokens":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt))
                        maxTokens = mt;
                    break;
                case "--json":
                    emitJson = true;
                    break;
                case "--timeout":
                    if (i + 1 < args.Length && double.TryParse(args[++i], out var ts) && ts > 0)
                        timeoutSeconds = ts;
                    break;
                case "--providers":
                    if (i + 1 < args.Length)
                        providerOverride = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
            }
        }

        // ── Build context: auto + file + inline ─────────────────────────────
        var contextSb = new StringBuilder();
        if (autoContext)
        {
            var auto = await CollectAutoContextAsync(projectDir);
            if (!string.IsNullOrWhiteSpace(auto))
                contextSb.AppendLine(auto);
        }
        if (!string.IsNullOrWhiteSpace(contextFile))
        {
            try
            {
                var contents = await File.ReadAllTextAsync(contextFile);
                contextSb.AppendLine("=== EXTRA CONTEXT (from --context-file) ===");
                contextSb.AppendLine(contents);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to read --context-file '{contextFile}': {ex.Message}");
                return 1;
            }
        }
        if (!string.IsNullOrWhiteSpace(explicitContext))
        {
            contextSb.AppendLine("=== EXTRA CONTEXT (from --context) ===");
            contextSb.AppendLine(explicitContext);
        }

        // ── Resolve provider panel ──────────────────────────────────────────
        var config = new VotingConfiguration
        {
            ProviderTimeout  = TimeSpan.FromSeconds(timeoutSeconds),
            DefaultMaxTokens = maxTokens,
        };
        if (providerOverride.Count > 0)
        {
            config.AllowedProviderIds = new HashSet<string>(providerOverride, StringComparer.OrdinalIgnoreCase);
        }

        var activeIds = config.ActiveProviderIds;
        if (activeIds.Count == 0)
        {
            Console.Error.WriteLine("error: no providers configured. Add API keys at %APPDATA%/MindAttic/LLM/ or pass --providers.");
            return 1;
        }

        // Architect-framed voter profiles — one per active provider.
        var architectFraming = BuildArchitectFraming();
        var voters = activeIds.Select(id => new VoterProfile
        {
            ProviderId          = id,
            Name                = id,
            PersonalityMarkdown = architectFraming,
        }).ToList();

        using var http = new HttpClient { Timeout = config.ProviderTimeout };
        var provider   = new LlmVotingProvider(http, config);
        var service    = new LLMVotingService(provider, config, NullLogger<LLMVotingService>.Instance);

        var request = new VoteRequest
        {
            Question            = question,
            Context             = contextSb.ToString(),
            Options             = options,
            MaxTokens           = maxTokens,
            SynthesizeNarrative = true,
        };

        VotingResult result;
        try
        {
            result = await service.VoteWithProfilesAsync(request, quorum, voters);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: vote failed: {ex.Message}");
            return 2;
        }

        return emitJson
            ? EmitJson(result)
            : EmitPlain(result);
    }

    // ── Output ─────────────────────────────────────────────────────────────

    private static int EmitJson(VotingResult result)
    {
        var json = JsonSerializer.Serialize(new
        {
            question           = result.Question,
            answer             = result.Consensus,
            consensus_strength = result.ConsensusStrength,
            quorum_type        = result.QuorumType.ToString(),
            quorum_reached     = result.QuorumReached,
            successful_voters  = result.SuccessfulVoters,
            total_voters       = result.IndividualVotes.Count,
            duration_ms        = (int)result.Duration.TotalMilliseconds,
            narrative          = result.NarrativeSummary,
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
        }, new JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        Console.WriteLine(json);
        return result.QuorumReached ? 0 : 1;
    }

    private static int EmitPlain(VotingResult result)
    {
        if (!result.QuorumReached)
        {
            Console.Error.WriteLine(
                $"warning: quorum not reached ({result.ConsensusStrength:P0} agreement among {result.SuccessfulVoters}/{result.IndividualVotes.Count} voters)");

            // Fall back to the most popular answer so callers always have something
            // to feed back to the blocked upstream CLI. The non-zero exit code
            // signals "unsure" without losing the best guess.
            var fallback = result.IndividualVotes
                .Where(v => !v.IsError && !string.IsNullOrWhiteSpace(v.Decision))
                .GroupBy(v => v.Decision, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "";
            Console.WriteLine(fallback);
            return 1;
        }

        Console.WriteLine(result.Consensus);
        return 0;
    }

    // ── Auto-context collection ─────────────────────────────────────────────

    private const int CLAUDE_MD_CAP   = 8 * 1024;
    private const int README_CAP     = 8 * 1024;
    private const int GIT_STATUS_CAP = 4 * 1024;
    private const int GIT_LOG_CAP    = 1 * 1024;

    /// <summary>
    /// Best-effort: assemble the project's CLAUDE.md, README, and recent git
    /// activity into a single context blob. Each piece is independently capped
    /// so a 200KB README can't blow the prompt budget. Missing pieces are
    /// silently skipped — this is auto-context, not a contract.
    /// </summary>
    private static async Task<string> CollectAutoContextAsync(string projectDir)
    {
        var sb = new StringBuilder();

        var claudeMdPath = FindFirst(projectDir, new[] { "CLAUDE.md", "claude.md", ".claude/CLAUDE.md" });
        if (claudeMdPath is not null)
        {
            sb.AppendLine("=== CLAUDE.md ===");
            sb.AppendLine(await ReadCappedAsync(claudeMdPath, CLAUDE_MD_CAP));
            sb.AppendLine();
        }

        var readmePath = FindFirst(projectDir, new[] { "README.md", "readme.md", "README", "Readme.md" });
        if (readmePath is not null)
        {
            sb.AppendLine("=== README ===");
            sb.AppendLine(await ReadCappedAsync(readmePath, README_CAP));
            sb.AppendLine();
        }

        var gitStatus = TryRunGit(projectDir, "status -s");
        if (!string.IsNullOrWhiteSpace(gitStatus))
        {
            sb.AppendLine("=== git status -s ===");
            sb.AppendLine(Truncate(gitStatus, GIT_STATUS_CAP));
            sb.AppendLine();
        }

        var gitLog = TryRunGit(projectDir, "log --oneline -10");
        if (!string.IsNullOrWhiteSpace(gitLog))
        {
            sb.AppendLine("=== git log --oneline -10 ===");
            sb.AppendLine(Truncate(gitLog, GIT_LOG_CAP));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string? FindFirst(string dir, string[] names)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(dir, n);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static async Task<string> ReadCappedAsync(string path, int cap)
    {
        var text = await File.ReadAllTextAsync(path);
        return Truncate(text, cap);
    }

    private static string Truncate(string s, int cap) =>
        s.Length <= cap ? s : s[..cap] + $"\n…[truncated, original {s.Length} chars]";

    /// <summary>
    /// Run a git command in <paramref name="dir"/> and return stdout.
    /// Swallows any failure (no git, not a repo, command not found) and
    /// returns "" — callers treat empty output as "skip this section".
    /// </summary>
    private static string TryRunGit(string dir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory       = dir,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return "";
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output;
        }
        catch
        {
            return "";
        }
    }

    // ── Architect framing ───────────────────────────────────────────────────

    /// <summary>
    /// The architect-panel system prompt overlay. Public so tests and other
    /// callers (e.g. an in-process StreetSamurai integration) can reuse the
    /// exact same framing that the CLI applies.
    /// </summary>
    public static string BuildArchitectFraming() => """
        You are a senior software architect on this project.

        You are answering on behalf of a developer who is mid-task and needs a decision they can act on right now. Be decisive — no "it depends" without a recommended default. Briefly note any meaningful tradeoff.

        Guiding heuristics, in order:
          1. Prefer the boring, conventional, well-supported choice.
          2. Prefer the reversible choice over the irreversible one. If the decision is hard to reverse, say so explicitly.
          3. Match the project's existing style and stack — do not introduce a new tool/pattern unless the question requires one.
          4. Optimize for the developer's next 30 minutes, not their next 30 months.
          5. If the choice has security or data-loss implications, flag them in your reasoning.

        Do not refuse to answer. If the question is underspecified, pick the most likely intent and proceed.
        """;

    // ── Help ────────────────────────────────────────────────────────────────

    private static bool IsHelp(string a) => a is "-h" or "--help" or "help" or "/?";

    private static void PrintUsage()
    {
        Console.WriteLine("legion ask <question> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Ask a panel of LLMs (architect-framed) a single decision question.");
        Console.WriteLine("  Stdout = bare answer; --json for full audit. Designed for piping back into a");
        Console.WriteLine("  monitored Claude Code or Codex session that's blocking on a user prompt.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --options A,B,C        Force choice mode; voters must pick exactly one.");
        Console.WriteLine("  --context <text>       Extra context appended after auto-context.");
        Console.WriteLine("  --context-file <path>  Read extra context from a file.");
        Console.WriteLine("  --project-dir <path>   Where to look for CLAUDE.md/README/git (default: cwd).");
        Console.WriteLine("  --no-auto-context      Skip CLAUDE.md/README/git auto-include.");
        Console.WriteLine("  --quorum <q>           plurality | simplemajority | twothirds | unanimous (default plurality).");
        Console.WriteLine("  --max-tokens N         Per-voter cap (default 1024).");
        Console.WriteLine("  --timeout S            Per-provider timeout in seconds (default 60).");
        Console.WriteLine("  --providers a,b,c      Override the active provider whitelist.");
        Console.WriteLine("  --json                 Emit full vote audit JSON instead of bare answer.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0  quorum reached");
        Console.WriteLine("  1  quorum not reached or usage error");
        Console.WriteLine("  2  unhandled exception");
    }
}
