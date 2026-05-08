# MindAttic.Legion

Multi-LLM consensus engine for any .NET app. One client to talk to every major LLM provider, plus a voting layer that turns a panel of LLMs into a single answer with quorum, reasoning, and confidence.

Portable: Legion has no dependency on any specific MindAttic project. Drop it into a `csproj`, register it via DI, give it API keys, and you have the panel.

---

## Why Legion

A single LLM is a single opinion. When the cost of a wrong answer is real — a story contradiction, a misclassified record, a bad route — you want a *panel* that votes, not one model that bluffs.

Legion is the panel:

- **Multi-provider transport** — Claude, OpenAI, Gemini, DeepSeek, Mistral, xAI, Groq, Together, OpenRouter, Fireworks, and Cohere, all behind one client.
- **Voting** — call all configured providers in parallel, tally their answers, return the consensus with reasoning + dissent.
- **Decision-making** — `DecideAsync(question, options)` picks one option from a fixed list with confidence.
- **Scoring** — multi-dimensional rubric evaluation (1–10 per dimension), aggregate scores, weakest-dimension feedback, ready-to-inject improvement directives.
- **Personas** — every voter can wear a persona (a markdown system prompt). Use the bundled 1000-persona library, build a panel of N unique voices, or wrap a fictional character's psychology to vote *as* them.
- **Autonomous architectural decisions** — `legion.exe ask` is purpose-built for the loop where another coding CLI (Claude Code, Codex) blocks on a user prompt: an outer monitor pipes the question to `ask`, the panel deliberates, and the bare answer flows back to the blocked CLI. Architect-framed voters, auto-pulls `CLAUDE.md`/`README`/git as context, default panel is the four-provider trust list with automatic refill on outages.
- **CLI** — `legion.exe status`, `legion.exe vote`, `legion.exe ask`, `legion.exe health`, `legion.exe panel` — same engine, no .NET app required.

---

## Install

The library is published as a project reference. From a sibling repo:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\MindAttic.Legion\MindAttic.Legion\MindAttic.Legion.csproj" />
</ItemGroup>
```

Target framework: **net10.0**.

---

## Quick start

```csharp
using MindAttic.Legion;
using MindAttic.Legion.Providers;
using Microsoft.Extensions.DependencyInjection;

// 1) Configure
var services = new ServiceCollection();
services.AddLogging();
services.AddLegionClient();
services.AddHttpClient<LegionClient>();
services.AddHttpClient<LlmVotingProvider>();
services.AddSingleton(new VotingConfiguration
{
    ApiKeys =
    {
        ["claude"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "",
        ["openai"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
    },
    JudgeProviderId = "claude",
});
services.AddSingleton<LlmVotingProvider>();
services.AddSingleton<LLMVotingService>();
var sp = services.BuildServiceProvider();

// 2) Vote
var voting = sp.GetRequiredService<LLMVotingService>();
var result = await voting.VoteAsync(
    question: "Should Kyle take the contract?",
    context : "Contract details: ...",
    quorum  : Quorum.SimpleMajority);

Console.WriteLine($"Consensus: {result.Consensus}  ({result.ConsensusStrength:P0})");
Console.WriteLine(result.NarrativeSummary);
```

---

## Voting modes

### `VoteAsync` — open-ended consensus

The simplest call: ask a question, every voter writes a free-form answer, a judge LLM synthesizes the consensus.

```csharp
var r = await voting.VoteAsync("What weapon does Kyle carry?", canonContext, Quorum.Plurality);
// r.Consensus == "Silence (a corundum-edged tantō)"
```

### `VoteAsync` with `Options` — choice vote

When you want a vote among fixed options (much cheaper to tally — exact-match wins).

```csharp
var req = new VoteRequest
{
    Question = "Severity of this canon contradiction?",
    Context  = chapterPlusCanon,
    Options  = new() { "low", "medium", "high" },
};
var r = await voting.VoteAsync(req, Quorum.SimpleMajority);
// r.Consensus is one of "low" | "medium" | "high"
```

### `DecideAsync` — judgment call with reasoning

Sugar over choice voting. **Use this when an automated workflow has to pick one option and move on** (route a request, fill in a field, resolve a tie). Returns a `DecisionResult` with `Choice`, `Reasoning`, `Confidence`, and `QuorumReached`.

```csharp
var d = await voting.DecideAsync(
    question: "Which field in this entity record stores Kyle's primary weapon carry location?",
    options : new[] { "personality", "equipment", "tags", "story_hooks" },
    context : kyleEntityFileJson,
    quorum  : Quorum.Plurality);

if (d.QuorumReached)
{
    Console.WriteLine($"Use field: {d.Choice}  ({d.Confidence:P0})");
    Console.WriteLine($"Why: {d.Reasoning}");
}
else
{
    // Panel was too divided — escalate to a human, or rerun with stricter quorum.
}
```

`DecideAsync` is the right entry point any time your code would otherwise have to hard-code a branch or guess. Hand the decision to the panel.

### `ScoreAsync` — multi-dimensional rubric

Rate something across multiple dimensions (1–10 each). Returns aggregate scores, failing dimensions, and synthesized improvement directives.

```csharp
var req = new ScoredVoteRequest
{
    Question         = "Score this scene against the rubric.",
    Context          = sceneText,
    Dimensions       = new() { "voice", "tension", "specificity", "clichéness" },
    FailureThreshold = 6,
};
var r = await voting.ScoreAsync(req);

foreach (var (dim, score) in r.AggregateScores)
    Console.WriteLine($"  {dim,-15}  {score:0.0}");
foreach (var directive in r.ImprovementDirectives)
    Console.WriteLine($"  → {directive}");
```

### `VoteWithPersonasAsync` — character / panel voices

Build a panel of unique voices (or a single character's psychology) and have *them* vote.

```csharp
// Generic 5-voice panel spread across providers
var panel = voting.CreatePanel(count: 5, fallbackProviderId: "claude");
var r = await voting.VoteWithProfilesAsync(req, Quorum.TwoThirds, panel);

// Or vote as a character
var kylePsychology = File.ReadAllText("kyle-psychology.md");
var kyleVoter = VoterProfile.ForCharacter("Kyle", kylePsychology, "claude", apiKey: claudeKey);
var rk = await voting.VoteWithPersonasAsync(
    "Would Kyle accept this contract?",
    contractContext,
    Quorum.Unanimous,
    new[] { kyleVoter });
```

---

## Quorum

`Quorum` controls how strict the agreement threshold is.

| Value | Threshold | Use when |
|---|---|---|
| `Plurality` | Any winning answer counts | Cheapest. The vote *will* return something — even a 1-of-4 answer wins. Good for surfacing all viewpoints. |
| `SimpleMajority` | > 50% must agree | Default for most decisions. |
| `TwoThirds` | ≥ 66.7% must agree | Stronger confidence required. |
| `Unanimous` | 100% must agree | Use for irreversible / canonical actions. |

If quorum isn't reached, `result.QuorumReached == false` and `result.Consensus == ""`. Your code decides whether to escalate, retry with a different quorum, or accept the plurality answer anyway.

---

## Providers and models

Configure providers via `VotingConfiguration.ApiKeys`. A provider is "active" when it has a non-empty API key. `GetActiveProviderIds()` lists which providers are voting.

| Provider id | Vendor | Default model | Dashboard |
|---|---|---|---|
| `claude` | Anthropic | Claude 4.7 Opus | console.anthropic.com |
| `openai` | OpenAI | gpt-5 | platform.openai.com |
| `gemini` | Google | Gemini 2.5 Pro | aistudio.google.com |
| `deepseek` | DeepSeek | deepseek-chat | platform.deepseek.com |
| `mistral` | Mistral AI | mistral-large | console.mistral.ai |
| `xai` | xAI | grok-4 | console.x.ai |
| `groq` | Groq | llama-3.3-70b | console.groq.com |
| `together` | Together AI | (varies) | api.together.xyz |
| `openrouter` | OpenRouter | (varies) | openrouter.ai |
| `fireworks` | Fireworks AI | (varies) | fireworks.ai |
| `cohere` | Cohere | command-r-plus | dashboard.cohere.com |

Use `legion.exe providers` from the CLI for the live list and dashboard URLs.

To override the model for a specific provider:

```csharp
config.ModelOverrides["claude"] = "claude-sonnet-4-6";
```

To restrict voting to a subset:

```csharp
var r = await voting.VoteAsync(req, quorum, new[] { "claude", "openai" });
```

### Default trust list

`VotingConfiguration.AllowedProviderIds` defaults to the four first-party frontier providers: **`claude`, `openai`, `gemini`, `deepseek`**. Every other provider is keyable and probeable but excluded from the default voting panel — they don't get a seat unless you explicitly add them.

When a trusted provider errors mid-vote (network blip, rate limit, transient 5xx), `LLMVotingService.RefillFailedVotersAsync` automatically dispatches a fresh call to one of the *surviving* trusted providers (round-robin), so the panel never shrinks below quorum size. A failed Gemini slot becomes a second Claude or DeepSeek call rather than a missing vote. Refilled slots intentionally drop any persona overlay so a surviving voter doesn't get to "vote twice as the same character."

To run with a different shortlist:

```csharp
config.AllowedProviderIds = new(StringComparer.OrdinalIgnoreCase) { "claude", "openai" };
```

Or via the CLI:

```bash
legion.exe ask "..." --providers claude,openai,gemini,deepseek
```

Set `AllowedProviderIds` to an empty set to disable filtering and let every provider with a key vote.

---

## Credential storage

Legion can read keys from the shared `MindAtticCredentialStore` at `%APPDATA%/MindAttic/LLM/` so every MindAttic-app shares one keyring. Set `VotingConfiguration.UseSharedCredentials = true` to opt in.

Otherwise, populate `ApiKeys` directly (env-vars, secret manager, etc).

The CLI always uses the shared store.

---

## CLI: `legion.exe`

The CLI exposes the same engine for shell scripts, CI, and rapid iteration.

```bash
# Discovery
legion.exe status                 # model inventory, config, and connectivity
legion.exe status --no-probe      # list live/static models without sending prompts
legion.exe status --json          # machine-readable status output
legion.exe providers              # list all providers + dashboard URLs
legion.exe models <provider>      # catalog models for a provider
legion.exe personas 10            # sample 10 personas from the 1000-persona library
legion.exe panel 5                # build a 5-voter panel + show provider mix

# Health
legion.exe health                 # probe every provider with a hello-world
legion.exe ping claude            # one-provider probe

# Vote (returns JSON on stdout)
legion.exe vote "Is the sky blue today?" \
    --context "Cloud cover is 100%." \
    --quorum simplemajority \
    --options yes,no,unclear \
    --max-tokens 256 \
    --no-narrative

# Ask (architect-framed; stdout = bare answer, --json for full audit)
legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" \
    --options "Singleton,Scoped,Transient"
# → Singleton

legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?" --json
```

Exit codes:
- `0` — quorum reached
- `1` — quorum not reached
- `2` — pipeline error

The JSON shape on stdout matches `VotingResult`/`ScoredVotingResult`, so other languages can parse it directly.

### `legion.exe ask` — autonomous decisions for monitored agents

`ask` is the variant tuned for the loop where you want a panel-voted answer to flow back into another coding CLI without a human in between.

Differences from `vote`:

- **Stdout = bare answer by default.** Choice mode prints exactly the picked option. Free-form mode prints the synthesized consensus prose. Add `--json` to get the full audit blob (votes, reasoning, confidence, dissent).
- **Architect-framed voters.** Each voter is told to act as a senior software architect on this project: be decisive, prefer the boring/reversible/conventional choice, flag irreversible decisions, optimize for the developer's next 30 minutes.
- **Auto-context.** When invoked inside a repo, `ask` prepends `CLAUDE.md`, `README.md`, and `git status -s` / `git log --oneline -10` to every voter's context so the panel sees the project shape. Disable with `--no-auto-context`. Each piece is independently capped (8 KB / 8 KB / 4 KB / 1 KB) so a 200 KB README can't blow the prompt budget.
- **Default quorum is `Plurality`.** `ask` always emits *some* answer rather than blocking. Raise the bar with `--quorum twothirds` when dissent should fail closed.

```bash
legion.exe ask <question> [opts]
```

| Option | Meaning |
|---|---|
| `--options A,B,C` | Force choice mode; voters must pick exactly one. |
| `--context <text>` | Extra context appended after auto-context. |
| `--context-file <path>` | Read extra context from a file (e.g. the file you're about to edit). |
| `--project-dir <path>` | Where to look for `CLAUDE.md`/`README`/git (default: cwd). |
| `--no-auto-context` | Skip the auto-include. |
| `--quorum <q>` | `plurality` \| `simplemajority` \| `twothirds` \| `unanimous` (default `plurality`). |
| `--max-tokens N` | Per-voter cap (default 1024). |
| `--timeout S` | Per-provider timeout in seconds (default 60). |
| `--providers a,b,c` | Narrow the panel **within** the trusted set. Untrusted ids are silently dropped — the panel can never include a non-trusted provider, even if you ask. |
| `--must-answer` | On 0/N voter failure, retry with doubled budget and no auto-context; on second failure, fall back to a single-provider chain (claude → openai → gemini → deepseek) until one replies. Use when the calling agent can't tolerate "no answer". |
| `--json` | Emit full vote audit JSON instead of bare answer. |

Output contract:

| stdout | exit | meaning |
|---|---|---|
| answer | `0` | panel agrees, act on it |
| best-guess answer | `1` | panel split — re-ask with more context or escalate |
| (empty) | `2` | unhandled error (network, etc.) |

stderr carries warnings; never parse it.

---

## Architecture

```
┌─ Your app
│   └─ LLMVotingService    public API: VoteAsync / DecideAsync / ScoreAsync
│         └─ VoterFactory  builds VoterProfile lists (CreatePanel, personas)
│         └─ LlmVotingProvider
│               └─ LegionClient   universal LLM transport
│                     ├─ Claude wire shape
│                     ├─ OpenAI wire shape
│                     ├─ Gemini wire shape
│                     └─ ... (one adapter per provider)
└─ MindAtticCredentialStore (optional shared keyring at %APPDATA%/MindAttic/LLM/)
```

`LegionClient` owns the socket pool, retry policy, and circuit breaker. `LlmVotingProvider` adds vote-specific shaping. `LLMVotingService` is the public API — you almost never need to touch the lower layers.

---

## Testing

`MindAttic.Legion.Tests/` covers:

- Vote tally correctness (plurality, majority, unanimous)
- Quorum enforcement
- Persona injection (system-prompt wrapping)
- Provider failover (one voter erroring doesn't break the vote)
- Choice-option exact-match matching
- Scored-vote dimension aggregation

Run from the repo root:

```bash
dotnet test MindAttic.Legion.Tests/MindAttic.Legion.Tests.csproj
```

---

## Public API at a glance

```csharp
// Voting
Task<VotingResult>   VoteAsync(string question, string context, Quorum, CT)
Task<VotingResult>   VoteAsync(VoteRequest, Quorum, CT)
Task<VotingResult>   VoteAsync(VoteRequest, Quorum, IEnumerable<string> providerIds, CT)
Task<VotingResult>   VoteWithProfilesAsync(VoteRequest, Quorum, IEnumerable<VoterProfile>, CT)
Task<VotingResult>   VoteWithPersonasAsync(string question, string context, Quorum, IEnumerable<VoterProfile>, CT)

// Decisions
Task<DecisionResult> DecideAsync(string question, IEnumerable<string> options, string context, Quorum, int maxTokens, CT)

// Scoring
Task<ScoredVotingResult> ScoreAsync(ScoredVoteRequest, CT)
Task<ScoredVotingResult> ScoreWithProfilesAsync(ScoredVoteRequest, IEnumerable<VoterProfile>, CT)

// Panel construction
List<string>                     GetActiveProviderIds()
IReadOnlyList<VoterProfile>      CreatePanel(int count, string fallbackProviderId, Random?)
```

`VoterProfile.ForCharacter(name, psychologyMarkdown, providerId, apiKey?, model?)` wraps a character's psychology into a voter profile suitable for in-story decisions.

---

## License

Internal MindAttic library.

---

## Briefing another coding agent

When you want another LLM-driven CLI (Claude Code, Codex, Cursor agent, etc.) to reach for Legion instead of blocking on you, paste the block below into its memory or system prompt. It's self-contained and assumes nothing about the host except that it can run a `.exe`.

```
You now have a Legion panel for hard calls. Use it instead of blocking on me.

Legion is a multi-LLM consensus CLI at:
  D:\Projects\MindAttic\MindAttic.Legion\MindAttic.Legion.Cli\bin\Debug\net10.0\legion.exe

The panel is HARDCODED to Claude, ChatGPT, Gemini, DeepSeek — the four providers I trust for autonomous decisions. No untrusted provider can ever be added, even via --providers (untrusted ids get silently dropped). If a trusted provider is unreachable, Legion automatically refills the empty slot with another working provider so the panel never falls below quorum.

WHEN TO CALL IT
Use `legion ask` whenever you would otherwise pause to ask me:
  - An architectural choice (DI lifetime, library pick, schema shape, layering decision).
  - A breaking-change tradeoff (rename now vs. soft-deprecate, migrate now vs. shim).
  - An ambiguous spec where two reasonable readings exist and the next file you write depends on which one is right.
  - Anything hard to reverse.

Don't call it for mechanical edits, formatting, or things where you already know the answer — each call costs ~3-5s and four LLM-API requests.

HOW TO CALL IT
  legion.exe ask "<question>" [opts]

Modes:
  - Choice (recommended when possible):
      legion.exe ask "Pick the JSON serializer" --options "System.Text.Json,Newtonsoft.Json"
      → stdout = exactly one option, exit 0 on quorum.
  - Free-form:
      legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?"
      → stdout = the synthesized answer.
  - Audit mode: add --json to get votes, reasoning, confidence, dissent. Use this when you want to surface tradeoffs back to me.
  - Strict consensus: add --quorum twothirds to fail closed (exit 1) if the panel splits. Use for irreversible decisions.
  - MUST-ANSWER mode: add --must-answer when you absolutely cannot tolerate "no answer" — for example, the next file you write depends on the result and you have nothing to fall back to. With --must-answer, if the panel returns 0 voters Legion auto-retries with doubled budget and no auto-context; if that's still empty it falls back to a single-provider chain (claude → openai → gemini → deepseek) calling raw text instead of the JSON-shaped voting wrapper. Always emits an answer if any one provider is reachable.

Auto-context: by default `ask` reads CLAUDE.md, README.md, and `git status -s` / `git log --oneline -10` from the current working directory and prepends them so voters know the project. Pass --no-auto-context for a clean prompt, or --context-file <path> to inject a specific file (e.g. the file you're about to edit).

OUTPUT CONTRACT
  stdout              | exit | meaning
  ------------------- | ---- | -------------------------------------------------------
  answer              | 0    | panel agrees, act on it
  best-guess answer   | 1    | panel split — re-ask with more context or escalate to me
  (empty)             | 2    | unhandled error (network, etc.)

With --must-answer, exit 0 also covers the recovery cases (phase-2 retry, phase-3 single-provider chain). stderr will tell you which phase delivered ("ask: recovered in phase 3 via claude") — log that line if you want a record of the degraded path. stderr carries warnings only; never parse it for the answer.

EXAMPLES
  # Decide a DI lifetime
  legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" --options "Singleton,Scoped,Transient"

  # Pick between two refactors with full reasoning
  legion.exe ask "Should we extract Persona-rendering into a separate service?" --json

  # Conservative: only act if 2/3+ agree
  legion.exe ask "Migrate the credential store to DPAPI now?" --quorum twothirds

  # I can't proceed without an answer — pull every lever
  legion.exe ask "Pick the cache key format" --options "user:{id},u-{id},user_{id}" --must-answer

--providers exists but you almost never need it: it can only NARROW within the trusted four (e.g. --providers claude,openai). Passing untrusted ids is harmless (they're dropped) but pointless. Don't reach for it unless I specifically ask you to scope a vote.

If `legion ask` exits 1 (no quorum) WITHOUT --must-answer, don't silently pick its best-guess answer for a structural decision — surface the dissent (re-run with --json, summarize the disagreement, ask me). If you used --must-answer and still got exit 1 or 2, the trusted panel is genuinely down — escalate to me, don't guess.
```

---

## Contributing notes (for sibling repos using Legion)

- **Always wrap judgment calls in `DecideAsync`.** If your code has a hard-coded branch that picks among options based on heuristics, replace the heuristic with a Legion decision and pass the relevant context. The panel is cheap; bad decisions are expensive.
- **Prefer `Plurality` for surfacing all viewpoints, `SimpleMajority` for routine decisions, `TwoThirds`+ for canon-affecting actions.** Don't reach for `Unanimous` unless the cost of a single dissent is real.
- **Pass real context.** A vote without context is just a popularity contest. Bundle the canon, the prior chapters, the schema, the rubric — whatever the panel needs to be informed.
- **Watch the cost dial.** A panel of 5 means 5× tokens. Use `providerIds` overload to scope votes to 2–3 providers when you don't need the full panel.
- **`QuorumReached == false` is a signal, not a failure.** It means the panel saw a real ambiguity. Surface it to a human or escalate the question.
