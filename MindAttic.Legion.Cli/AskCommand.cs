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
///     <item>Every voter is pinned to <see cref="DefaultTier"/> = High
///       (claude-opus-4-7, gpt-4.1, gemini-2.5-pro, deepseek-reasoner) so
///       architecture calls run on flagship reasoning rather than each
///       provider's cheap default. Override with <c>--tier low|medium|
///       high|higher|highest</c> when a cheaper or longer-context tier
///       fits the question better.</item>
///   </list>
/// </summary>
public static class AskCommand
{
    /// <summary>
    /// The only providers permitted to generate voters. Even when callers
    /// pass <c>--providers</c>, the result is intersected with this set —
    /// no untrusted provider can ever be added to the panel from the CLI.
    /// Exposed <c>internal</c> so the test suite can verify the trust list
    /// without duplicating its membership.
    /// </summary>
    internal static readonly string[] TrustedProviderIds =
        { "claude", "openai", "gemini", "deepseek" };

    /// <summary>
    /// Intersects <paramref name="requested"/> with <see cref="TrustedProviderIds"/>
    /// (case-insensitive). Untrusted ids are silently dropped, so the panel
    /// can never include a provider outside the trusted set. When
    /// <paramref name="requested"/> is null or empty, returns the full trusted
    /// set unchanged — i.e. "no narrowing requested" means "every trusted
    /// provider is eligible."
    /// </summary>
    internal static HashSet<string> IntersectWithTrustedSet(IEnumerable<string>? requested)
    {
        var trusted = new HashSet<string>(TrustedProviderIds, StringComparer.OrdinalIgnoreCase);
        var list    = requested?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (list is null || list.Count == 0)
            return trusted;
        trusted.IntersectWith(list);
        return trusted;
    }

    /// <summary>
    /// Default tier used by <c>legion ask</c> when <c>--tier</c> is not
    /// supplied. High = flagship reasoning models (claude-opus-4-7,
    /// gpt-4.1, gemini-2.5-pro, deepseek-reasoner) — the right tool for
    /// architectural / CLI-shape decisions. Cheaper tiers can be chosen
    /// explicitly with <c>--tier low|medium|high|higher|highest</c>.
    /// </summary>
    internal const ModelTier DefaultTier = ModelTier.High;

    /// <summary>
    /// Per-provider model overrides pinning every trusted voter to the
    /// supplied <paramref name="tier"/> via
    /// <see cref="LlmProviderCatalog.GetTieredModel"/>. Without this,
    /// <see cref="Providers.LlmVotingProvider.CallAsync"/> would fall
    /// through to <see cref="LegionClient.DefaultModels"/>, which hands
    /// Claude a Sonnet-tier model — fine for tonal calls, the wrong tool
    /// for an architecture call. Returned dictionary is keyed
    /// case-insensitively to match <see cref="VotingConfiguration.ModelOverrides"/>.
    /// </summary>
    internal static Dictionary<string, string> BuildTierModelOverrides(ModelTier tier)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in TrustedProviderIds)
        {
            var model = LlmProviderCatalog.GetTieredModel(id, tier);
            if (!string.IsNullOrWhiteSpace(model))
                overrides[id] = model!;
        }
        return overrides;
    }

    /// <summary>
    /// Back-compat shim — a hard pin to High tier. Delegates to
    /// <see cref="BuildTierModelOverrides"/>; kept so any caller that took
    /// a dependency on the old name doesn't break. Prefer
    /// <c>BuildTierModelOverrides(ModelTier.High)</c> in new code.
    /// </summary>
    internal static Dictionary<string, string> BuildHighTierModelOverrides()
        => BuildTierModelOverrides(ModelTier.High);

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
        var mustAnswer        = false;
        var tier              = DefaultTier;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--tier":
                    if (i + 1 < args.Length && Enum.TryParse<ModelTier>(args[++i], ignoreCase: true, out var parsedTier))
                        tier = parsedTier;
                    break;
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
                    // Require a positive budget (matching poll/generate). A 0 or
                    // negative value silently flowed into the request and, worse,
                    // the --must-answer phase-2 retry doubles it (0*2 = 0), so the
                    // "always emit an answer" rescue would request 0 tokens.
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var mt) && mt > 0)
                        maxTokens = mt;
                    break;
                case "--json":
                    emitJson = true;
                    break;
                case "--timeout":
                    if (i + 1 < args.Length && double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ts) && ts > 0)
                        timeoutSeconds = ts;
                    break;
                case "--providers":
                    if (i + 1 < args.Length)
                        providerOverride = args[++i].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    break;
                case "--must-answer":
                    mustAnswer = true;
                    break;
            }
        }

        // ── Build context (auto + explicit), tracked separately so the
        //    must-answer phase-2 retry can drop auto-context to fit budget.
        var autoContextBlock     = "";
        var explicitContextBlock = "";

        if (autoContext)
        {
            var auto = await CollectAutoContextAsync(projectDir);
            if (!string.IsNullOrWhiteSpace(auto))
                autoContextBlock = auto;
        }

        var explicitSb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(contextFile))
        {
            try
            {
                var contents = await File.ReadAllTextAsync(contextFile);
                explicitSb.AppendLine("=== EXTRA CONTEXT (from --context-file) ===");
                explicitSb.AppendLine(contents);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to read --context-file '{contextFile}': {ex.Message}");
                return 1;
            }
        }
        if (!string.IsNullOrWhiteSpace(explicitContext))
        {
            explicitSb.AppendLine("=== EXTRA CONTEXT (from --context) ===");
            explicitSb.AppendLine(explicitContext);
        }
        explicitContextBlock = explicitSb.ToString();

        var fullContext = autoContextBlock + explicitContextBlock;

        // ── Resolve provider panel ──────────────────────────────────────────
        // The trust list is the source of truth: even with --providers, the
        // panel is restricted to the intersection of the trusted set and
        // whatever the caller asked for. An untrusted provider id is silently
        // dropped here.
        var config = new VotingConfiguration
        {
            ProviderTimeout    = TimeSpan.FromSeconds(timeoutSeconds),
            DefaultMaxTokens   = maxTokens,
            AllowedProviderIds = IntersectWithTrustedSet(providerOverride),
            ModelOverrides     = BuildTierModelOverrides(tier),
        };

        var activeIds = config.ActiveProviderIds;
        if (activeIds.Count == 0)
        {
            Console.Error.WriteLine(
                "error: no trusted providers available. Trusted set: "
                + string.Join(", ", TrustedProviderIds)
                + ". Check %APPDATA%/MindAttic/LLM/ for API keys, and ensure --providers (if used) names at least one trusted provider.");
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
        var service    = new LlmVotingService(provider, config, NullLogger<LlmVotingService>.Instance);

        var request = new VoteRequest
        {
            Question            = question,
            Context             = fullContext,
            Options             = options,
            MaxTokens           = maxTokens,
            SynthesizeNarrative = true,
        };

        VotingResult result;
        try
        {
            result = await service.VoteWithProfilesAsync(request, quorum, voters);
        }
        catch (Exception ex) when (!mustAnswer)
        {
            Console.Error.WriteLine($"error: vote failed: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            // --must-answer: don't bail. Synthesize a zero-voter result so the
            // fallback chain below has something to react to.
            Console.Error.WriteLine($"ask: panel call threw ({ex.Message}); entering must-answer fallback.");
            result = new VotingResult { Question = question, QuorumType = quorum };
        }

        if (mustAnswer && result.SuccessfulVoters == 0)
        {
            result = await RunMustAnswerFallbackAsync(
                question:           question,
                options:            options,
                explicitContext:    explicitContextBlock,
                voters:             voters,
                quorum:             quorum,
                originalMaxTokens:  maxTokens,
                originalTimeoutSec: timeoutSeconds,
                tier:               tier);
        }

        return emitJson
            ? EmitJson(result)
            : EmitPlain(result);
    }

    // ── --must-answer fallback chain ───────────────────────────────────────

    /// <summary>
    /// Three-phase rescue when the initial panel returned 0 successful
    /// voters: (2) re-vote with budget doubled and auto-context dropped,
    /// (3) iterate the trusted providers in order calling the bare
    /// <see cref="LegionClient"/> directly so a stuck JSON parser or a
    /// strict choice-vote schema can't take the whole answer down.
    /// Returns a synthesized <see cref="VotingResult"/> on success, or the
    /// original empty result if nothing replied.
    /// </summary>
    private static async Task<VotingResult> RunMustAnswerFallbackAsync(
        string question,
        List<string> options,
        string explicitContext,
        List<VoterProfile> voters,
        Quorum quorum,
        int originalMaxTokens,
        double originalTimeoutSec,
        ModelTier tier)
    {
        // ── Phase 2: relaxed-budget retry ───────────────────────────────────
        Console.Error.WriteLine(
            "ask: panel returned 0 voters; retrying with doubled budget and no auto-context.");

        var phase2MaxTokens   = originalMaxTokens * 2;
        var phase2TimeoutSec  = originalTimeoutSec * 2;
        var phase2Config = new VotingConfiguration
        {
            ProviderTimeout    = TimeSpan.FromSeconds(phase2TimeoutSec),
            DefaultMaxTokens   = phase2MaxTokens,
            AllowedProviderIds = new HashSet<string>(TrustedProviderIds, StringComparer.OrdinalIgnoreCase),
            ModelOverrides     = BuildTierModelOverrides(tier),
        };

        using var http2 = new HttpClient { Timeout = phase2Config.ProviderTimeout };
        var provider2   = new LlmVotingProvider(http2, phase2Config);
        var service2    = new LlmVotingService(provider2, phase2Config, NullLogger<LlmVotingService>.Instance);

        var phase2Request = new VoteRequest
        {
            Question            = question,
            Context             = explicitContext, // auto-context deliberately dropped
            Options             = options,
            MaxTokens           = phase2MaxTokens,
            SynthesizeNarrative = true,
        };

        VotingResult phase2Result;
        try
        {
            phase2Result = await service2.VoteWithProfilesAsync(phase2Request, quorum, voters);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ask: phase-2 retry threw ({ex.Message}).");
            phase2Result = new VotingResult { Question = question, QuorumType = quorum };
        }

        if (phase2Result.SuccessfulVoters > 0)
        {
            Console.Error.WriteLine($"ask: recovered in phase 2 ({phase2Result.SuccessfulVoters}/{voters.Count} voters).");
            return phase2Result;
        }

        // ── Phase 3: single-provider chain, raw text, trusted order ────────
        Console.Error.WriteLine(
            "ask: phase 2 still empty; falling back to single-provider chain ("
            + string.Join(" → ", TrustedProviderIds) + ").");

        var systemPrompt = options.Count > 0
            ? "You are a senior software architect. Pick exactly one of these options and reply with ONLY that option's exact text, nothing else: "
              + string.Join(", ", options.Select(o => $"\"{o}\""))
            : "You are a senior software architect on this project. Answer the user's question directly and concisely. No JSON, no preamble — just the answer.";

        var userMessage = string.IsNullOrWhiteSpace(explicitContext)
            ? $"QUESTION: {question}"
            : $"CONTEXT:\n{explicitContext}\n\nQUESTION: {question}";

        using var http3 = new HttpClient { Timeout = TimeSpan.FromSeconds(phase2TimeoutSec) };
        var directClient = new LegionClient(http3);

        foreach (var providerId in TrustedProviderIds)
        {
            try
            {
                var raw = await directClient.CallAsync(
                    providerId:    providerId,
                    systemPrompt:  systemPrompt,
                    userMessage:   userMessage,
                    maxTokens:     phase2MaxTokens,
                    temperature:   0.3,
                    modelOverride: LlmProviderCatalog.GetTieredModel(providerId, tier));

                var answer = raw?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(answer))
                {
                    Console.Error.WriteLine($"ask: fallback {providerId} returned empty.");
                    continue;
                }

                // For choice mode, snap the raw text to the closest exact-or-
                // contained option. If the model went off-ballot, try the
                // next provider rather than emitting noise.
                if (options.Count > 0)
                {
                    var matched = SnapToOption(answer, options);
                    if (matched is null)
                    {
                        Console.Error.WriteLine($"ask: fallback {providerId} replied off-ballot ('{Truncate(answer, 80)}').");
                        continue;
                    }
                    answer = matched;
                }

                Console.Error.WriteLine($"ask: recovered in phase 3 via {providerId}.");
                return new VotingResult
                {
                    Question          = question,
                    QuorumType        = quorum,
                    QuorumReached     = true,
                    Consensus         = answer,
                    ConsensusStrength = 1.0,
                    NarrativeSummary  = $"Fallback answer from {providerId} (panel collapsed; --must-answer single-provider chain).",
                    IndividualVotes   = new List<VoteResult>
                    {
                        new()
                        {
                            VoterName  = $"{providerId}#fallback",
                            ProviderId = providerId,
                            Decision   = answer,
                            Reasoning  = "Single-provider must-answer fallback (raw text, no JSON wrapper).",
                            Confidence = 5,
                        },
                    },
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ask: fallback {providerId} errored: {ex.Message}");
            }
        }

        Console.Error.WriteLine("ask: every fallback exhausted — no answer available.");
        return new VotingResult { Question = question, QuorumType = quorum };
    }

    /// <summary>
    /// Snaps a free-text answer to one of <paramref name="options"/> in choice
    /// mode. First tries an exact case-insensitive match against the whole
    /// answer; if none, tries to find an option name embedded in the answer
    /// (so "I'd pick Singleton" maps to "Singleton"). Returns <c>null</c>
    /// when the answer can't be reconciled with the ballot — callers should
    /// treat that as off-ballot and try the next provider rather than
    /// printing the raw text.
    /// </summary>
    internal static string? SnapToOption(string answer, IReadOnlyList<string> options)
    {
        if (string.IsNullOrWhiteSpace(answer) || options is null || options.Count == 0)
            return null;

        var trimmed = answer.Trim();

        var exact = options.FirstOrDefault(o => trimmed.Equals(o, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        // Prefer the longest option that the answer contains as a WHOLE TOKEN, so
        // "FirstChoiceOption" wins over "First" when both are on the ballot. A
        // whole-token match (not raw substring) also prevents a short option like
        // "No" from matching inside "Notify" or "cat" inside "communicate", which
        // would otherwise register an off-ballot reply as a (wrong) vote.
        return options
            .Where(o => !string.IsNullOrWhiteSpace(o) && ContainsWholeToken(trimmed, o))
            .OrderByDescending(o => o.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// True when <paramref name="option"/> occurs in <paramref name="haystack"/>
    /// as a whole token — i.e. not embedded inside a larger alphanumeric word.
    /// Boundaries are any non-alphanumeric char (or the string edge), so "No"
    /// matches in "No, thanks" but not in "Notify". Case-insensitive.
    /// </summary>
    private static bool ContainsWholeToken(string haystack, string option)
    {
        var idx = 0;
        while ((idx = haystack.IndexOf(option, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var beforeOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var after    = idx + option.Length;
            var afterOk  = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);
            if (beforeOk && afterOk) return true;
            idx++;
        }
        return false;
    }

    // ── Output ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Emit the full vote audit blob as pretty-printed JSON on stdout.
    /// Returns exit code 0 when quorum was reached, 1 otherwise — matching
    /// <see cref="EmitPlain"/> so callers can swap modes without re-reading
    /// the contract.
    /// </summary>
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

    /// <summary>
    /// Emit the bare consensus answer on stdout. On a quorum miss, prints the
    /// most-popular fallback answer (so the upstream CLI always has something
    /// to feed back) plus a stderr warning, and returns exit code 1. Returns
    /// 0 when quorum was reached.
    /// </summary>
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
    internal static async Task<string> CollectAutoContextAsync(string projectDir)
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

    /// <summary>
    /// Returns the absolute path of the first file in <paramref name="names"/>
    /// that exists under <paramref name="dir"/>, or <c>null</c> if none of
    /// them are present. Used by auto-context to handle case/spelling variants
    /// of well-known files (CLAUDE.md / README.md / Readme.md / etc.).
    /// </summary>
    internal static string? FindFirst(string dir, string[] names)
    {
        foreach (var n in names)
        {
            var p = Path.Combine(dir, n);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// Reads <paramref name="path"/> into memory and truncates to <paramref name="cap"/>
    /// characters. The cap protects the prompt budget from oversized
    /// CLAUDE.md/README files; truncated content is marked with a footer so
    /// voters know they're seeing only the head of the file.
    /// </summary>
    private static async Task<string> ReadCappedAsync(string path, int cap)
    {
        var text = await File.ReadAllTextAsync(path);
        return Truncate(text, cap);
    }

    /// <summary>
    /// Returns <paramref name="s"/> unchanged when ≤ <paramref name="cap"/>,
    /// otherwise the first <c>cap</c> characters plus a marker noting the
    /// original size. Used both for capped auto-context pieces and for
    /// trimming error messages so a runaway response can't blow the log.
    /// </summary>
    internal static string Truncate(string s, int cap) =>
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

    /// <summary>
    /// Recognises the help flags accepted by every Legion subcommand:
    /// <c>-h</c>, <c>--help</c>, <c>help</c>, <c>/?</c>.
    /// </summary>
    internal static bool IsHelp(string a) => a is "-h" or "--help" or "help" or "/?";

    /// <summary>Print the <c>legion ask</c> usage banner to stdout.</summary>
    private static void PrintUsage()
    {
        Console.WriteLine("legion ask <question> [opts]");
        Console.WriteLine();
        Console.WriteLine("  Ask the trusted panel (Claude, ChatGPT, Gemini, DeepSeek) a single decision");
        Console.WriteLine("  question. Architect-framed voters. Stdout = bare answer; --json for full audit.");
        Console.WriteLine("  Designed for piping back into a monitored Claude Code or Codex session that's");
        Console.WriteLine("  blocking on a user prompt.");
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
        Console.WriteLine("  --providers a,b,c      Narrow the panel WITHIN the trusted set. Untrusted ids are dropped.");
        Console.WriteLine("                         Trusted: claude, openai, gemini, deepseek.");
        Console.WriteLine("  --tier <t>             low | medium | high | higher | highest (default high).");
        Console.WriteLine("                         High = flagship reasoning (Opus 4.7, GPT-4.1, Gemini 2.5 Pro,");
        Console.WriteLine("                         DeepSeek Reasoner). Drop tier for cheaper/faster decisions.");
        Console.WriteLine("  --must-answer          On 0/N voter failure, retry with doubled budget and no auto-context;");
        Console.WriteLine("                         on second failure, single-provider chain (claude → openai → gemini →");
        Console.WriteLine("                         deepseek) until one replies. Always emit an answer if any provider works.");
        Console.WriteLine("  --json                 Emit full vote audit JSON instead of bare answer.");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0  quorum reached");
        Console.WriteLine("  1  quorum not reached or usage error");
        Console.WriteLine("  2  unhandled exception");
    }
}
