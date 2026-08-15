# MindAttic.Legion

**Multi-LLM consensus engine for .NET 10.** Turn a panel of frontier models — Claude, ChatGPT, Gemini, DeepSeek, and ten more — into a single trustworthy answer with quorum, reasoning, and confidence. Vote, decide, score, poll, generate, or persona-wear. One panel for the calls you can't afford to get wrong.

One LLM is one opinion. When a contradiction, a misclassification, or a bad route is expensive, you don't want a single model that bluffs — you want a panel that votes. Legion is the panel: unified transport across fourteen provider connections, a voting layer with quorum and dissent, tiered model selection that survives version drift, automatic failover when a provider blips, a 1024-persona library, and a CLI (`legion.exe`) that lets shell scripts, CI jobs, and other coding agents call the panel directly.

Portable: Legion has no dependency on any specific MindAttic project. Drop it into a `csproj`, register it via DI, hand it your API keys (or point it at the shared MindAttic keyring), and you have the panel.

See also: [docs/BIBLE.md](docs/BIBLE.md) (architecture canon and Laws), [docs/AMENDMENTS.md](docs/AMENDMENTS.md) (change log), [docs/USER_STORIES.md](docs/USER_STORIES.md) (test-cited stories).

---

## Why Legion

A single LLM is a single opinion. When the cost of a wrong answer is real — a story contradiction, a misclassified record, a bad route — you want a *panel* that votes, not one model that bluffs.

Legion is the panel:

- **Multi-provider transport** — one `LegionClient` talks to fourteen provider connections: Claude via a direct Anthropic API key (`claude-api`), Claude via the Claude Code CLI's own OAuth session (`claude-team`), ChatGPT, Gemini, DeepSeek, Mistral, xAI/Grok, Groq, Together AI, OpenRouter, Fireworks AI, Cohere, Kimi (Moonshot AI), and Perplexity — plus an explicit-URL escape hatch for self-hosted OpenAI-compatible endpoints (Ollama, vLLM, RunPod, …).
- **Voting** — call every active provider in parallel, tally their answers, return the consensus with reasoning + dissent.
- **Decision-making** — `DecideAsync(question, options)` picks one option from a fixed list with confidence.
- **Scoring** — multi-dimensional rubric evaluation (1–10 per dimension), aggregate scores, weakest-dimension feedback, ready-to-inject improvement directives.
- **Personas** — every voter can wear a persona (a markdown system prompt). Use the bundled 1024-persona library, build a panel of N unique voices, or wrap a fictional character's psychology to vote *as* them.
- **Psychometric profiles** — score the whole persona library on five instruments (OCEAN/Big Five, HEXACO, MBTI-style, Enneagram-style, DISC-style), persisted as one faithful JSON file per persona. The model only answers items in-character; scoring is deterministic in code. Use the profiles to build trait-diverse panels and to segment a vote by composition. See [Psychometric persona profiles](#legionexe-psychometrics--score-the-persona-library).
- **Tiered model selection** — every provider exposes a Low / Medium / High / Higher / Highest tier. The five providers Legion explicitly maps (`claude-api`, `claude-team`, `openai`, `gemini`, `deepseek`) resolve to a concrete model per tier; every other provider falls back to its single `DefaultModel` at any tier. Pick the tier that fits the work: Low for bulk polls, Medium for creative generation, High for architectural decisions. The catalog hides specific model versions behind tier names so a model-id rotation doesn't break callers.
- **Autonomous architectural decisions** — `legion.exe ask` is purpose-built for the loop where another coding CLI (Claude Code, Codex) blocks on a user prompt: an outer monitor pipes the question to `ask`, the panel deliberates on the High tier, and the bare answer flows back to the blocked CLI. Architect-framed voters, auto-pulls `CLAUDE.md`/`README`/git as context, default panel is a four-provider trust list (`claude-api`, `openai`, `gemini`, `deepseek`) with automatic refill on outages.
- **Bulk distribution sampling** — `legion.exe poll` round-robins N voters across the trusted four at a chosen tier (Low by default), reports a count-sorted distribution + plurality winner. The cheap fast tool for "how does the panel split on this?"
- **Bulk creative generation** — `legion.exe generate` fans out one batched call per provider asking for that provider's share of N items, deduplicates across the merge, and emits newline-separated results to stdout. Built for `legion generate "100 hero-vibe names" | head -25 > names.txt`.
- **On-demand connectivity probe** — `legion.exe tiers` probes every (trusted-provider, tier) cell with a tiny prompt and prints a matrix. Use it before a critical session to confirm the panel is healthy.
- **Per-project panels via `legion.json`** — drop a `legion.json` at a project's root to declare that project's voter list, judge, model overrides, API keys, and concurrency cap without touching code. Currently consumed by the CLI's `vote` subcommand (walks up from cwd looking for the file); library consumers apply it explicitly via `LegionConfig.LoadFromDirectory()?.ApplyTo(config)`.
- **Direct transport for non-voting calls** — `LegionClient` also does multi-turn chat, Anthropic prompt caching, OpenAI embeddings, OpenAI (DALL·E) image generation, Claude document input (native PDF, extracted DOCX/EPUB/text), and a fallback chain that tries providers in order until one answers.
- **CLI** — `legion.exe status`, `providers`, `models`, `personas`, `panel`, `health`, `ping`, `vote`, `ask`, `poll`, `generate`, `tiers`, `psychometrics` — same engine, no .NET app required.

---

## Install

The library is published as a NuGet package (`MindAttic.Legion`, MIT-licensed) and also consumable as a project reference from a sibling repo:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\MindAttic.Legion\MindAttic.Legion\MindAttic.Legion.csproj" />
</ItemGroup>
```

Target framework: **net10.0**. The package depends on `MindAttic.Vault` and `Microsoft.Extensions.{DependencyInjection.Abstractions, Http, Logging.Abstractions}` only — no dependency on any specific MindAttic application (see [`docs/BIBLE.md#LEG-LAW-1`](docs/BIBLE.md#LEG-LAW-1)).

---

## Quick start

```csharp
using MindAttic.Legion;
using MindAttic.Legion.Providers;
using Microsoft.Extensions.DependencyInjection;

// 1) Configure
var services = new ServiceCollection();
services.AddLogging();
services.AddLLMVoting(new VotingConfiguration
{
    ApiKeys =
    {
        ["claude-api"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "",
        ["openai"]     = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
    },
    JudgeProviderId = "claude-api",
});
var sp = services.BuildServiceProvider();

// 2) Vote
var voting = sp.GetRequiredService<LlmVotingService>();
var result = await voting.VoteAsync(
    question: "Should Kyle take the contract?",
    context : "Contract details: ...",
    quorum  : Quorum.SimpleMajority);

Console.WriteLine($"Consensus: {result.Consensus}  ({result.ConsensusStrength:P0})");
Console.WriteLine(result.NarrativeSummary);
```

`AddLLMVoting` (see [`ServiceCollectionExtensions`](MindAttic.Legion/Services/ServiceCollectionExtensions.cs)) registers `LlmVotingService`, `LlmVotingProvider`, and a `LegionClient` that shares the same key resolution as the voting layer, so a service that injects `LegionClient` directly sees the same keys as the voting service. Call `services.AddLegionClient()` instead if you only want the transport (health checks, direct provider calls) without the voting machinery.

Zero-config also works: `new VotingConfiguration()` with empty `ApiKeys` resolves every key from the shared MindAttic credential store (`UseSharedCredentials` defaults to `true`).

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

Rate something across multiple dimensions (1–10 each). Returns aggregate scores, failing dimensions, per-dimension consensus strengths/failures, and synthesized improvement directives.

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

A dimension that every voter omitted has no aggregate entry at all (never a phantom `0.0`), so it can't wrongly get flagged as failing or drag the average down. `ConsensusStrengths`/`ConsensusFailures` surface flags that at least half the panel raised.

### `VoteWithPersonasAsync` — character / panel voices

Build a panel of unique voices (or a single character's psychology) and have *them* vote.

```csharp
// Generic 5-voice panel spread across active providers
var panel = voting.CreatePanel(count: 5, fallbackProviderId: "claude-api");
var r = await voting.VoteWithProfilesAsync(req, Quorum.TwoThirds, panel);

// Or vote as a character
var kylePsychology = File.ReadAllText("kyle-psychology.md");
var kyleVoter = VoterProfile.ForCharacter("Kyle", kylePsychology, "claude-api", apiKey: claudeKey);
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
| `SimpleMajority` | > 50% must agree | Default for most decisions. A 2-of-4 tie **fails** (strict `agree*2 > total`, not `>=`). |
| `TwoThirds` | ≥ 66.7% must agree | Computed as `agree*3 >= total*2` (exact integer arithmetic), so a 2-of-3 panel clears it. |
| `Unanimous` | 100% must agree | Use for irreversible / canonical actions. |

If quorum isn't reached, `result.QuorumReached == false` and `result.Consensus == ""`. Your code decides whether to escalate, retry with a different quorum, or accept the plurality answer anyway.

---

## Providers and models

Legion knows how to call **14 provider connections**, all through the single `LegionClient`. Configure them via `VotingConfiguration.ApiKeys`. A provider is "active" for voting when it has a non-empty key (explicit or from the shared store) **and** passes the `AllowedProviderIds` whitelist (see [Trust tiers](#trust-tiers-which-list-applies-where) below). `GetActiveProviderIds()` lists which providers are actually voting.

| Provider id | Vendor | Auth | Default model | Dashboard |
|---|---|---|---|---|
| `claude-api` | Anthropic | API key (`x-api-key` / `Authorization: Bearer sk-ant-oat...`) | `claude-sonnet-5` | console.anthropic.com |
| `claude-team` | Anthropic | OAuth via the Claude Code CLI session (no API key) | `claude-sonnet-5` | claude.ai/settings |
| `openai` | OpenAI | API key | `gpt-5.4-mini` | platform.openai.com |
| `gemini` | Google | API key (`x-goog-api-key` header) | `gemini-3.5-flash` | aistudio.google.com |
| `deepseek` | DeepSeek AI | API key | `deepseek-v4-flash` | platform.deepseek.com |
| `mistral` | Mistral AI | API key | `mistral-large-latest` | console.mistral.ai |
| `xai` | xAI | API key | `grok-4.3` | console.x.ai |
| `groq` | Groq | API key | `llama-3.3-70b-versatile` | console.groq.com |
| `together` | Together AI | API key | `meta-llama/Llama-3-70b-chat-hf` | api.together.xyz |
| `openrouter` | OpenRouter | API key | `meta-llama/llama-3.1-8b-instruct:free` | openrouter.ai |
| `fireworks` | Fireworks AI | API key | `accounts/fireworks/models/llama-v3p1-70b-instruct` | app.fireworks.ai |
| `cohere` | Cohere | API key | `command-r-plus` | dashboard.cohere.com |
| `kimi` | Moonshot AI | API key | `kimi-k3` | platform.moonshot.cn |
| `perplexity` | Perplexity AI | API key | `sonar` | perplexity.ai/settings/api |

Source of truth: [`LlmProviderCatalog`](MindAttic.Legion/Services/LlmProviderCatalog.cs) (metadata, dashboard/keys URLs, per-provider known-model lists) and [`LegionClient.DefaultModels`/`Endpoints`](MindAttic.Legion/Services/LegionClient.cs) (wire endpoints and fallback models). Use `legion.exe providers` from the CLI for the live list, or `legion.exe models <provider>` for a provider's full known-model catalog.

`claude-api` and `claude-team` are the same model family through two different doors: `claude-api` requires an Anthropic API key and bills against it; `claude-team` reads and auto-refreshes the OAuth token from the Claude Code CLI's own `~/.claude/.credentials.json`, so it authenticates as whatever Claude Code account is already logged in on that machine — no separate key, but it shares that session's rate limit (set `"maxConcurrency": 3` in `legion.json` when a panel includes `claude-team` alongside an active Claude Code session).

Default model is what each provider falls back to when **no** model override is supplied and no `model` field is recorded in `providers.json`. For tier-aware selection (Low / Medium / High / Higher / Highest), use `LlmProviderCatalog.GetTieredModel(providerId, ModelTier)` — see [Tier system](#tier-system).

To override the model for a specific provider:

```csharp
config.ModelOverrides["claude-api"] = "claude-opus-4-8";
```

To restrict voting to a subset:

```csharp
var r = await voting.VoteAsync(req, quorum, new[] { "claude-api", "openai" });
```

### Self-hosted / local models (Ollama, vLLM, RunPod, …)

Providers outside the catalog aren't voter-panel members, but `LegionClient` can still call them directly at an explicit URL using the OpenAI-compatible chat-completions shape:

```csharp
var client = new LegionClient(httpClient);
var reply = await client.CallAsync(
    providerId:  "local",                         // any stable id — used only for circuit-breaker tracking
    apiKey:      "ollama",                         // any non-empty string for auth-less local servers
    model:       "llama3.1:8b",
    systemPrompt:"You are a helpful assistant.",
    userMessage: "Summarize this paragraph...",
    endpointUrl: "http://localhost:11434/v1/chat/completions");
```

---

## Tier system

`ModelTier` is the Legion abstraction for "the cheap one" / "the strong one" without naming model versions that drift. Only the five providers below have an explicit tier mapping in [`LlmProviderCatalog`](MindAttic.Legion/Services/LlmProviderCatalog.cs) — every other provider (`mistral`, `xai`, `groq`, `together`, `openrouter`, `fireworks`, `cohere`, `kimi`, `perplexity`) has no tier table and `GetTieredModel` simply returns that provider's `DefaultModel` at any tier:

| Tier | `claude-api` / `claude-team` | `openai` | `gemini` | `deepseek` |
|---|---|---|---|---|
| `Low` | `claude-haiku-4-5-20251001` | `gpt-4.1-nano` | `gemini-2.5-flash-lite` | `deepseek-v4-flash` |
| `Medium` | `claude-sonnet-5` | `gpt-5.4-mini` | `gemini-2.5-flash` | `deepseek-v4-flash` |
| `High` | `claude-opus-4-7` | `gpt-5.4` | `gemini-2.5-pro` | `deepseek-v4-pro` |
| `Higher` | `claude-opus-4-8` | `gpt-5.5` | `gemini-3.1-flash-lite` | `deepseek-v4-pro` |
| `Highest` | `claude-fable-5` | `gpt-5.6-sol` | `gemini-3.5-flash` | `deepseek-v4-pro` |

When a tier isn't directly mapped for a provider, `GetTieredModel` walks **down** the ladder (Highest → Higher → ... → Low) and returns the closest available model — so asking for Highest against a 3-tier provider gives you High, not null. Asking for a tier that's lower than every entry walks back **up** for symmetry. When the provider has no tier table at all, it returns the provider's `DefaultModel` unconditionally.

```csharp
// Pick the strong reasoning model for an architectural decision:
var arch = LlmProviderCatalog.GetTieredModel("claude-api", ModelTier.High);
// → "claude-opus-4-7"

// Pick the cheap one for a 100-voter poll:
var bulk = LlmProviderCatalog.GetTieredModel("claude-api", ModelTier.Low);
// → "claude-haiku-4-5-20251001"

// A provider with no tier table — always the default, regardless of tier:
var m = LlmProviderCatalog.GetTieredModel("mistral", ModelTier.Highest);
// → "mistral-large-latest"
```

The CLI commands embed sensible defaults: `legion ask` defaults to High (architecture wants flagship reasoning), `legion poll` defaults to Low (bulk distribution wants cheap), `legion generate` defaults to Medium (creative balance), `legion psychometrics score` defaults to High (Opus-class administering lens). All accept `--tier <t>` to override.

To pin a whole panel to a tier inside the .NET API:

```csharp
config.ModelOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["claude-api"] = LlmProviderCatalog.GetTieredModel("claude-api", ModelTier.High)!,
    ["openai"]     = LlmProviderCatalog.GetTieredModel("openai",     ModelTier.High)!,
    ["gemini"]     = LlmProviderCatalog.GetTieredModel("gemini",     ModelTier.High)!,
    ["deepseek"]   = LlmProviderCatalog.GetTieredModel("deepseek",   ModelTier.High)!,
};
```

`AskCommand.BuildTierModelOverrides(ModelTier)` is the canonical helper for this in CLI code — copy its shape if you're building a similar command.

### Provider quirks handled automatically

`LegionClient` absorbs a handful of per-model wire-shape differences so callers never see them:

- **Temperature-deprecating models.** Claude's Fable/Mythos families, Sonnet 5+/Haiku 5+, and Opus 4.7+ all reject the `temperature` field (HTTP 400). OpenAI's o-series (o1/o2/o3/o4…) and the GPT-5 family reject `temperature` too and require `max_completion_tokens` instead of `max_tokens`. Both are detected by parsing the model id (not a hard-coded list), so a future point release doesn't silently break.
- **Adaptive "thinking" on Claude 5+.** `claude-sonnet-5+`/`claude-haiku-5+` run adaptive extended thinking by default when the `thinking` field is omitted, which can consume the entire token budget on reasoning and leave zero text. Legion explicitly sends `thinking: {type: "disabled"}` for ordinary (non-thinking) calls to these models. Fable/Mythos always think and reject that field, so they're excluded; Opus 4.7+ correctly default to no-thinking without it.
- **Gemini 2.5+ thinking budget.** For `gemini-*-2.5*` models, Legion sends `thinkingConfig: { thinkingBudget: 0 }` so the full `maxOutputTokens` budget goes to the actual response instead of being eaten by internal reasoning tokens.
- **Claude response parsing.** Thinking-tier Claude models put a `thinking` content block *before* the `text` block; Legion concatenates every `type: "text"` block rather than assuming `content[0]`.
- **Gemini response parsing.** Skips any part flagged `"thought": true` and concatenates the remaining text parts; returns `""` (never throws) on a safety-blocked or `MAX_TOKENS` candidate with no content, so a benign refusal doesn't trip the circuit breaker.

### Trust tiers: which list applies where

Legion has **three different "which providers are eligible" lists**, and they don't all agree — this matters when you're narrowing a panel:

| List | Members | Applies to |
|---|---|---|
| `LlmProviderCatalog.All` / `AllIds` | All 14 providers | Everything Legion *can* call — `legion.exe providers`/`models`, direct `LegionClient` calls. |
| `LlmProviderCatalog.Default` / `DefaultIds` | `claude-api`, `claude-team`, `openai`, `deepseek`, `gemini` (5) | The first-party set apps are expected to surface by default in settings UIs. |
| `VotingConfiguration.AllowedProviderIds` (default) | `claude-api`, `claude-team`, `openai`, `gemini`, `deepseek` (5) | Library voting (`LlmVotingService`) and the CLI's `vote` subcommand — the whitelist `ActiveProviderIds` intersects against. |
| CLI `TrustedProviderIds` (hard-coded per command) | `claude-api`, `openai`, `gemini`, `deepseek` (4 — **no `claude-team`**) | `legion ask`, `legion poll`, `legion generate`, `legion tiers`. `--providers` can only narrow *within* this set; an id outside it is silently dropped, even for callers who ask for it explicitly. |

In short: the library's default voting whitelist includes `claude-team`, but the autonomous-decision CLI commands (`ask`/`poll`/`generate`/`tiers`) deliberately exclude it from their hard-coded trust list — those commands are meant to run unattended (CI, monitored agent loops) where an interactive Claude Code OAuth session may not exist.

When a trusted provider errors mid-vote (network blip, rate limit, transient 5xx), `LlmVotingService.RefillFailedVotersAsync` automatically dispatches a fresh call to one of the *surviving* allowed providers (round-robin), so the panel never shrinks below quorum size. A failed Gemini slot becomes a second Claude or DeepSeek call rather than a missing vote. Refilled slots intentionally drop any persona overlay so a surviving voter doesn't get to "vote twice as the same character."

To run the library with a different shortlist:

```csharp
config.AllowedProviderIds = new(StringComparer.OrdinalIgnoreCase) { "claude-api", "openai" };
```

Or via the CLI:

```bash
legion.exe ask "..." --providers claude-api,openai,gemini,deepseek
```

Set `AllowedProviderIds` to an empty set to disable filtering and let every provider with a key vote.

---

## Credential storage

Legion can read keys from the shared credential store at `%APPDATA%/MindAttic/LLM/` (backed by `MindAttic.Vault`) so every MindAttic app shares one keyring. Set `VotingConfiguration.UseSharedCredentials = true` (the default) to opt in.

Resolution order (see [`docs/BIBLE.md#LEG-LAW-2`](docs/BIBLE.md#LEG-LAW-2)):

1. A per-voter `VoterProfile.ApiKeyOverride`.
2. An explicit entry in `VotingConfiguration.ApiKeys`.
3. The shared credential store — User Secrets / App Service settings / Azure Key Vault when a host has called `MindAtticCredentialStore.UseConfiguration(IConfiguration)`, falling back to the `%APPDATA%/MindAttic/LLM/providers.json` file.

`claude-team` is the one exception: it **never** reads from `providers.json` or `ApiKeys`. Its credential is the OAuth access token in `~/.claude/.credentials.json` (the Claude Code CLI's own session), resolved and auto-refreshed by [`ClaudeCodeOAuthSource`](MindAttic.Legion/Services/ClaudeCodeOAuthSource.cs). Refresh is thread-safe — concurrent callers that all see a token within 60 seconds of expiry block on a single refresh attempt.

`MindAtticCredentialStore` is a static backward-compatible facade over `MindAttic.Vault`'s `LlmCredentialStore`/`CompositeCredentialStore`; new code may inject those types directly via DI instead of calling the static facade. The CLI always uses the shared store (plus environment variables for containerized deployments, e.g. `MindAttic__Vault__LLM__claude-api__apiKey`).

### Per-project panels: `legion.json`

Drop a `legion.json` file at a project's root to declare that project's voter panel without touching code:

```jsonc
// ThinkTank (every provider Legion knows):
{ "voters": ["claude-api","openai","gemini","deepseek","mistral","xai",
             "groq","together","openrouter","fireworks","cohere"] }

// Tutor (two-vendor panel, explicit judge):
{ "voters": ["openai","claude-api"], "judge": "claude-api" }

// A panel that includes claude-team, sharing its Claude Code session's rate limit:
{ "voters": ["claude-team","openai"], "maxConcurrency": 3 }
```

Fields: `voters` (replaces the default `AllowedProviderIds` whitelist), `judge` (`JudgeProviderId` override), `models` (per-provider `ModelOverrides`), `apiKeys` (per-project keys — win over the shared store), `maxConcurrency` (caps simultaneous ballot calls; use `3` when the panel includes `claude-team`, which shares its quota with the Claude Code CLI session it authenticates through).

`LegionConfig.LoadFromDirectory()` walks up from a starting directory (default: cwd) looking for `legion.json`, up to 12 levels, and returns `null` (falling back to `VotingConfiguration` defaults) when none is found or the file is malformed. Apply it with `LegionConfig.LoadFromDirectory()?.ApplyTo(config)`.

**Current wiring:** the CLI's `legion vote` subcommand calls this automatically before voting. `legion ask`/`poll`/`generate`/`tiers` build their `VotingConfiguration` directly and do **not** consult `legion.json` — those commands' provider set is always the hard-coded CLI trust list (see [Trust tiers](#trust-tiers-which-list-applies-where)). Library consumers (a MindAttic app's own DI wiring) call `LegionConfig.LoadFromDirectory()?.ApplyTo(config)` explicitly wherever they want project-local configuration honored.

---

## CLI: `legion.exe`

The CLI exposes the same engine for shell scripts, CI, and rapid iteration. Every subcommand accepts `-h` / `--help` / `help` / `/?`.

```
legion — MindAttic.Legion CLI

Commands:
  health                       Probe every provider with a 'Hello World!' test
  ping <provider>              Probe a single provider
  status [opts] [provider...]  Show model inventory, config, and connectivity
  providers                    List supported providers + dashboard URLs
  models <provider>            Show known models for a provider
  personas <count>              Sample N personas from the 1024-persona library
  panel <count> [provider...]  Build a voter panel: spread across providers, backfill claude-api.
                                --diverse picks personas that maximize psychometric spread.
  vote <question> [opts]       Multi-LLM consensus vote on a question; outputs JSON.
  ask <question> [opts]        Architect-framed decision; stdout = bare answer (or --json).
  poll <question> [opts]       Bulk vote: N voters round-robined across trusted providers.
  generate <prompt> [opts]     Bulk creative output: N distinct items, deduped, to stdout.
  tiers [opts]                 Probe trusted providers × tier mapping (Low/Medium/High).
  psychometrics <sub> [opts]   Score the persona library; subcommands init|score|rescore|show|stats|history|diff

All commands read keys from the shared store at %APPDATA%/MindAttic/LLM/.
```

### Discovery commands

```bash
legion.exe status                 # model inventory, config, and connectivity
legion.exe status --no-probe      # list live/static models without sending prompts
legion.exe status --json          # machine-readable status output
legion.exe status --timeout 30 claude-api openai   # narrow to specific providers, custom timeout

legion.exe providers              # table of all 14 providers + vendor + default model + dashboard URL
legion.exe models <provider>      # a provider's full known-model catalog, default marked, live endpoint if any
legion.exe personas 10            # sample 10 personas from the 1024-persona library (id, name, first line)
legion.exe panel 5                # build a 5-voter panel + show provider mix
legion.exe panel 5 --diverse      # panel chosen to maximize psychometric spread (needs scored profiles)
legion.exe panel 5 claude-api openai --store <dir>   # explicit provider list / psychometrics store dir
```

`status` cross-references three signals per provider: the static catalog (`known models`), a live query against the provider's `/models`-style endpoint (`live models`), and — unless `--no-probe` is passed — an actual prompt-level connectivity probe. It prints the effective model (configured override, else `providers.json`, else the catalog default) and, on failure, an actionable next step from `LlmHealthDiagnoser` (e.g. "key looks revoked, mint a new one at …").

`panel` spreads voters across the supplied provider list (or every provider that currently has a key, when none is given), backfilling with `claude-api` once every provider has at least one voter. `--diverse` requires a psychometrics store with scored profiles (`legion psychometrics score` must have run first) and picks personas via greedy farthest-point selection over the OCEAN+HEXACO+DISC trait vector instead of random sampling.

### Health & connectivity

```bash
legion.exe health                 # probe every one of the 14 providers' DefaultModel with a hello-world
legion.exe ping claude-api        # one-provider probe (DefaultModel)
legion.exe tiers                  # probe trusted-four × Low/Medium/High = 12 cells
```

### Vote (returns JSON on stdout) — one-voter-per-provider consensus

```bash
legion.exe vote "Is the sky blue today?" \
    --context "Cloud cover is 100%." \
    --quorum simplemajority \
    --options yes,no,unclear \
    --max-tokens 256 \
    --no-narrative
```

`vote` is the only CLI subcommand that honors a per-project `legion.json` (see [Per-project panels](#per-project-panels-legionjson)) — it applies the file on top of the default `VotingConfiguration` before dispatching, so a project can declare its own voter list without `--providers`.

### Ask (architect-framed; stdout = bare answer, --json for full audit)

```bash
# Default tier = High (Opus-class / GPT-5.4 / Gemini 2.5 Pro / DeepSeek v4 Pro).
legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" \
    --options "Singleton,Scoped,Transient"
# → Singleton

legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?" --json

# Override tier for cheaper one-shot decisions
legion.exe ask "Use tabs or spaces?" --options "tabs,spaces" --tier low
```

### Poll — N voters round-robined across providers at a chosen tier

```bash
legion.exe poll "Pick 1, 2, or 3" --options "1,2,3" --count 100 --tier low
# → distribution table + plurality winner
```

### Generate — N distinct creative items, deduped, newline-separated

```bash
legion.exe generate "100 hero-vibe character names" --count 100 --tier medium
# → newline list to stdout (pipe into head/shuf/grep/file)
```

Exit codes (`vote`/`ask`):
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
- **Fixed 4-provider trust list.** The panel is always the intersection of `--providers` (if given) with `claude-api, openai, gemini, deepseek` — `claude-team` is never included here, even if requested.

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
| `--max-tokens N` | Per-voter cap (default 1024, must be > 0). |
| `--timeout S` | Per-provider timeout in seconds (default 60). |
| `--providers a,b,c` | Narrow the panel **within** the trusted set (`claude-api, openai, gemini, deepseek`). Untrusted ids are silently dropped — the panel can never include a non-trusted provider, even if you ask. |
| `--tier <t>` | `low` \| `medium` \| `high` \| `higher` \| `highest` (default `high`). High = flagship reasoning — the right tool for architectural decisions. Drop tier for cheaper one-shot calls. |
| `--must-answer` | On 0/N voter failure, retry with doubled budget and no auto-context; on second failure, fall back to a single-provider chain (`claude-api → openai → gemini → deepseek`) calling raw text (no JSON, no persona) until one replies. Use when the calling agent can't tolerate "no answer". |
| `--json` | Emit full vote audit JSON instead of bare answer. |

Output contract:

| stdout | exit | meaning |
|---|---|---|
| answer | `0` | panel agrees, act on it |
| best-guess answer | `1` | panel split — re-ask with more context or escalate |
| (empty) | `2` | unhandled error (network, etc.) |

With `--must-answer`, exit `0` also covers the recovery cases (phase-2 retry, phase-3 single-provider chain). stderr will tell you which phase delivered (`ask: recovered in phase 3 via claude-api`) — log that line if you want a record of the degraded path. stderr carries warnings only; never parse it for the answer.

### `legion.exe psychometrics` — score the persona library

Legion can administer five personality instruments to every persona and persist the results, so panels can be composed and votes read by *trait composition*, not just by provider. This is a separate subsystem from the decision commands (`ask`/`vote`/`poll` do **not** touch it) — it powers persona panels.

**How scoring works.** A single trusted model (default tier **High** / Opus-class) answers each instrument's 1–5 Likert items **in character** as the persona; the LLM never computes a score. Scoring is done deterministically in C# (`PsychometricScorer`) from those raw answers, so the same answers always yield the same profile and the maths is unit-tested. The five instruments are public-domain-derived (IPIP Big Five & HEXACO, OEJTS-style Jungian axes, open DISC- and Enneagram-style banks); the trademarked questionnaires are never used, hence "-style".

| Framework | Output |
|---|---|
| **OCEAN / Big Five** | O, C, E, A, N — each 0–100 |
| **HEXACO** | Honesty-Humility, Emotionality, eXtraversion, Agreeableness, Conscientiousness, Openness — 0–100 |
| **MBTI-style** | 4-letter type + per-axis lean (E/I, S/N, T/F, J/P) |
| **Enneagram-style** | dominant type 1–9, wing, triad (Gut/Heart/Head) |
| **DISC-style** | D, I, S, C scores + primary style |

**Storage.** Each persona is **one faithful JSON file** under the store's `personas/` directory (identity + structured traits + the full nested assessment history), with a small `runs.json` index alongside — no database. A persona is fully reconstructable from its single file: portable, git-diffable, human-readable. The administering LLM is recorded **on each assessment, not on the persona** — so re-scoring through a different provider records a new *variant* of the same person rather than overwriting it. Each run is stamped with the model and an **instrument-set version** (`PsychometricInstruments.SetVersion`) so re-runs stay comparable. Store location: an explicit `--store <dir>`, then the `MINDATTIC_LEGION_STORE` environment variable, then `%APPDATA%/MindAttic/Legion`. Writes are atomic (temp file + move); the store assumes a single writer at a time.

```bash
legion.exe psychometrics init                     # create the store + seed the persona files
legion.exe psychometrics score --limit 8          # pilot: score 8 personas (resumable; default tier high)
legion.exe psychometrics score                    # score everything still missing a current-version profile
legion.exe psychometrics show persona-0000        # a persona's latest profile (--json for the raw record)
legion.exe psychometrics stats                    # MBTI/DISC/Enneagram distribution + mean OCEAN/HEXACO
legion.exe psychometrics history persona-0000      # every assessment run recorded for one persona
legion.exe psychometrics rescore                  # fresh full versioned run — point cron/Task Scheduler at this
legion.exe psychometrics diff 1 2                 # per-framework drift between two runs
```

| `score` / `rescore` option | Meaning |
|---|---|
| `--provider <id>` | Administering lens (default `claude`); must be in the CLI's trusted set (`claude-api, openai, gemini, deepseek`) or the command errors out. |
| `--tier <t>` | `low`…`highest` (default `high` = Opus class). |
| `--limit N` | Score at most N personas — use for a cheap pilot first. |
| `--concurrency N` | Personas assessed in parallel (default 4). |
| `--timeout S` | Per-provider timeout in seconds (default 120). |
| `--store-raw` | Also persist every raw item answer for audit. |
| `--notes <text>` | Free-form note recorded on the run (`rescore` sets this to `"rescore"` automatically). |
| `--store <dir>` | Override the store directory. |

> **Caveat — check the `--provider` default.** The command's default `--provider` value is the literal string `claude`, but the CLI's trust check (`AskCommand.TrustedProviderIds`) only recognizes `claude-api` (not bare `claude`) alongside `openai`, `gemini`, `deepseek`. Running `legion psychometrics score`/`rescore` with no `--provider` flag will therefore report `error: 'claude' is not a trusted provider` rather than silently defaulting to Claude — always pass `--provider claude-api` (or another trusted id) explicitly until this default is reconciled with the trust list.

`score` is **resumable**: it skips personas that already have a profile at the current instrument-set version *for this provider/lens*, so re-running continues where it left off (and scoring through a different `--provider` produces a fresh variant instead of skipping). `rescore` forces a brand-new run scoring everyone (for drift tracking) and is the command an external scheduler should invoke. Scoring the full library is ≈ `personas × 5` model calls — pilot with `--limit` before committing to the whole run.

**Using the profiles.** Once scored:

- `legion.exe panel N --diverse` builds a panel chosen to **maximize psychometric spread** (greedy farthest-point over the OCEAN+HEXACO+DISC vector) instead of sampling at random, and tags each voter with its type.
- In code, `VoterFactory.GenerateDiverseVoters(count, providers, profiles)` does the same and attaches each `PsychometricProfile` to its `VoterProfile`; `PsychometricVoteAnalysis.Segment(voters, result, selector)` then splits a completed `VotingResult` by trait (built-in selectors: `ByMbtiType`, `ByDiscPrimary`, `ByEnneagramTriad`, `ByOpennessHalf`) to see, e.g., whether high-Openness voters split from low-Openness ones.

> **Caveat — self-report skew.** Because a model answering "in character" still drifts toward dutiful, agreeable, conscientious self-presentation, LLM-administered profiles cluster toward a corner of the trait space (expect lots of *_STJ types and high Conscientiousness). Personas are still differentiated, just less than real humans would be. Sharper separation would need forced-choice/ipsative items or per-trait anchoring — a future instrument-set version, comparable to the current one via `diff`.

### `legion.exe poll` — bulk distribution sampling

`poll` is a fan-out command, not a consensus command. It round-robins **N independent voters** across the trusted four, all on a single tier, and reports a **count-sorted distribution + plurality winner**. Use it for "how does the panel split on this?" when you want a sample, not a verdict.

Distinct from `vote` (one voter per provider, requires quorum) and `ask` (one architect-framed answer): `poll` reports raw distributions, no quorum concept, plurality winner is whichever option got the most votes — even by 1.

```bash
legion.exe poll <question> [opts]
```

| Option | Meaning |
|---|---|
| `--count N` | Total voters across the panel (default 10). With four providers, count=100 → exactly 25 per provider; count=10 → 3,3,2,2. |
| `--tier <t>` | `low` \| `medium` \| `high` \| `higher` \| `highest` (default `low`). Low scales cheaply for the "100 voters" use case. |
| `--options A,B,C` | Force choice mode; off-ballot replies count as errors (excluded from the distribution). Free-form mode is allowed when omitted. |
| `--providers a,b,c` | Narrow within the trusted set (untrusted ids are silently dropped). |
| `--context <text>` | Extra context appended to every voter's prompt. |
| `--max-tokens N` | Per-voter cap (default 200 — voters reply briefly). |
| `--timeout S` | Per-voter timeout in seconds (default 30). |
| `--concurrency N` | In-flight call cap (default 8) — prevents 100 voters from bursting all at once. |
| `--json` | Emit full poll record (per-voter, distribution, summary) as JSON instead of a table. |

Round-robin distribution: voter `i` goes to `providers[i % providers.Count]`. With four providers and count=100, each gets 25; with count=10 each gets 3,3,2,2 (front buckets get the remainder). Failures don't shift the index — we'd rather have an uneven distribution that's reproducible than a "rebalance on failure" rule that drifts under retry.

```bash
# 100 voters at Low — quick, cheap distribution
legion.exe poll "Should this PR ship today?" --options "yes,no,not-yet" --count 100 --tier low

# 30 free-form voters at Medium — cluster their answers afterward
legion.exe poll "One word that describes this codebase" --count 30 --tier medium --json

# 50 voters at High but only Claude+OpenAI (more careful sampling)
legion.exe poll "Severity?" --options "low,medium,high,critical" --count 50 --tier high --providers claude-api,openai
```

Exit codes:
- `0` — at least one voter replied; a winner was chosen
- `1` — every voter errored (or usage error)

### `legion.exe generate` — bulk creative output

`generate` produces **N distinct creative items** by fanning out one batched call per trusted provider, extracting line-separated items from each reply, deduping case-insensitively across all batches, and emitting newline-separated results to stdout (Unix-pipe convention). Built for `legion generate "100 hero-vibe names" | head -25 > names.txt`.

Distinct from `poll` (which counts votes) and `ask` (which seeks one decision): `generate` produces *many distinct items* on a single prompt — names, taglines, alternatives, function names, scenario hooks.

```bash
legion.exe generate <prompt> [opts]
```

| Option | Meaning |
|---|---|
| `--count N` | Total distinct items (default 10). Each provider gets a round-robin share via `SplitCount` — 100 → \[25, 25, 25, 25]. |
| `--tier <t>` | `low` \| `medium` \| `high` \| `higher` \| `highest` (default `medium`). Medium = creative balance; Low produces flat output for creative bulk. |
| `--providers a,b,c` | Narrow within the trusted set. Pass a single provider for stylistic consistency; default round-robin maximizes variety. |
| `--max-tokens N` | Per-batch cap (default 1500 ≈ 50 short items per provider). |
| `--timeout S` | Per-call timeout in seconds (default 60). |
| `--temperature T` | Sampling temperature (default `0.9` — favors creative variance, not consensus). |
| `--no-dedup` | Emit duplicates from across providers; default is dedup case-insensitively, first-seen wins. |
| `--json` | Emit JSON record (prompt, requested, returned, items, per-provider batches) instead of newline list. |

Item extraction is defensive: a model that ignores the "no markers" instruction still yields clean items because `ExtractItems` strips:
- Numbered markers: `1.`, `12.`, `1)`, `99)`
- Bulleted markers: `- `, `* `, `• `
- Wrapping quotes: `"`, `'`, `“…”`, `‘…’`

Diagnostics go to **stderr** so stdout stays clean for piping. The summary line ("19 unique item(s) from 4/4 provider(s) (1 dup/empty trimmed)") is informational only.

```bash
# 100 fantasy character names, deduped, fed straight into a names file
legion.exe generate "single-word hero-vibe character names for a fantasy CLI" --count 100 > names.txt

# 30 product taglines on High tier (slower but more polished)
legion.exe generate "product taglines for a calm-tech tea brand" --count 30 --tier high

# Stylistic consistency: only Claude, smaller temperature
legion.exe generate "function names for queue.dequeue helpers" --count 20 --providers claude-api --temperature 0.4

# Pipe through standard tools
legion.exe generate "fictional country names" --count 50 | shuf | head -10
```

Exit codes:
- `0` — at least one item was produced
- `1` — every provider's batch errored (or usage error)

### `legion.exe tiers` — connectivity probe across the tier matrix

`tiers` answers "is the panel ready to vote on High right now?" without spinning up a real `ask` or `vote`. It probes every (trusted-provider, tier) cell with a tiny "reply OK" prompt and prints a connectivity table, defaulting to the trusted four × Low/Medium/High = 12 calls. Distinct from `legion health`, which only probes per-provider `DefaultModel`, missing tier-mapping breakage.

```bash
legion.exe tiers [opts]
```

| Option | Meaning |
|---|---|
| `--providers a,b,c` | Narrow within the trusted set (untrusted ids dropped). |
| `--tiers low,medium,high` | Narrow the tier sweep. Default: `low,medium,high`. |
| `--all-tiers` | Shorthand for all five tiers (Low, Medium, High, Higher, Highest). |
| `--max-tokens N` | Token budget per probe (default 400 — large enough for thinking models like `gemini-2.5-pro` to actually emit text after reasoning). |
| `--timeout S` | Per-probe timeout in seconds (default 45). |
| `--json` | Emit JSON record (one entry per probe + summary) instead of a table. |

Output is a one-row-per-probe table:

```
PROVIDER   TIER     MODEL                            STATUS  TIME     DETAIL
────────────────────────────────────────────────────────────────────────────
claude-api Low      claude-haiku-4-5-20251001        OK      2600ms   OK
claude-api Medium   claude-sonnet-5                  OK      999ms    OK
claude-api High     claude-opus-4-7                  OK      1404ms   OK
...
summary: 12/12 ok
```

Exit codes:
- `0` — every probe succeeded
- `1` — at least one probe failed

Use it before a critical session, after a model-id rotation, or as a lightweight CI smoke-test (paid, so wire it behind a manual `workflow_dispatch`).

---

## Direct `LegionClient` usage (no voting)

Apps that don't need voting/quorum can call `AddLegionClient()` and inject `LegionClient` directly for the same connection scaffolding (endpoints, auth headers, request/response shape, model defaults, shared-credential lookup, retry, circuit breaking) without pulling in the voting machinery:

```csharp
services.AddLegionClient();
// ...
public class MyService(LegionClient legion)
{
    public Task<string> AskClaude(string prompt) =>
        legion.CallAsync("claude-api", systemPrompt: "...", userMessage: prompt);
}
```

`LegionClient` covers more than single-turn text completions:

```csharp
// Multi-turn chat (shared-credential overload)
var reply = await legion.CallChatAsync("openai",
    new[] { new ChatTurn("user", "Hi"), new ChatTurn("assistant", "Hello!"), new ChatTurn("user", "What's 2+2?") },
    systemPrompt: "Be terse.");

// Anthropic prompt caching — cache a large stable prefix (e.g. story canon) once,
// then reuse it across many calls that vary only the trailing instructions.
var cached = await legion.CallAsync("claude-api", apiKey, model,
    systemPrompt: dynamicInstructions, userMessage: userPrompt,
    cachedSystemPrefix: stableCanonText, cacheUserMessage: false);

// Fallback chain — try providers in order until one succeeds
var (providerId, text) = await legion.CallWithFallbackAsync(
    new[] { "claude-api", "openai", "gemini", "deepseek" },
    systemPrompt: "...", userMessage: "...");

// OpenAI embeddings
var vectors = await legion.EmbedAsync("openai", apiKey, "text-embedding-3-small",
    new[] { "first passage", "second passage" });

// OpenAI (DALL·E) image generation
var urls  = await legion.GenerateImageAsync("openai", apiKey, "dall-e-3", "a lighthouse at dusk, watercolor");
var bytes = await legion.GenerateImageBytesAsync("openai", apiKey, "dall-e-3", "a lighthouse at dusk, watercolor");

// Claude document input — native PDF, or extracted text for DOCX/EPUB/plain text
var summary = await legion.CallWithDocumentAsync(apiKey, "claude-sonnet-5",
    documentBytes: pdfBytes, mediaType: "application/pdf",
    userPrompt: "Summarize this contract in 3 bullets.");
```

`IsProviderConfigured(providerId)` reports whether a credential currently resolves (OAuth for `claude-team`, credential-store key for everything else); `LegionClient.IsSupported(providerId)` / the static `LegionClient.DefaultModels` dictionary are useful for building settings screens without an HTTP round-trip. `LlmProviderRuntimeConfigurationResolver.Get(providerId)` reads the optional `apiKey`/`type`/`model`/`maxTokens` fields recorded for a provider in `providers.json` without requiring callers to parse that file themselves.

### Resilience

`LegionClientOptions` tunes retry and circuit-breaker behavior per `LegionClient` instance:

| Setting | Default | Meaning |
|---|---|---|
| `MaxRetries` | 2 | Extra attempts after the first failure, on transient errors only (network errors, HTTP 408/429/5xx). |
| `InitialBackoff` | 500ms | Delay before the first retry. |
| `BackoffMultiplier` | 2.0 | Multiplier applied to the backoff after each retry. |
| `CircuitBreakerThreshold` | 5 | Consecutive failures that open the per-provider breaker. |
| `CircuitBreakerCooldown` | 2 minutes | How long the breaker stays open before allowing another attempt. |

`LegionClientOptions.NoResilience` (no retries, breaker effectively disabled) is what the CLI's `health`/`ping`/`poll`/`generate`/`tiers` commands use — those already fan out many parallel calls and want a clean per-call pass/fail rather than retry noise. The `CircuitBreaker` itself is `static` and process-wide: "Claude is down" means the same thing to every `LegionClient` instance in the process, so a sick provider fails fast everywhere rather than per-instance. Non-transient failures (e.g. 401 auth) and client-side validation errors (unsupported provider) are never retried.

### Health checks and diagnosis

`LlmHealthCheck.CheckAsync`/`CheckOneAsync`/`CheckAllAsync` send a tiny "Reply with exactly the two words: Hello World!" probe and classify the outcome via `LlmHealthDiagnoser` into an `LlmHealthDiagnosis`:

`Healthy` · `ResponseMismatch` · `BadResponse` · `MissingCredential` · `AuthInvalid` (401) · `AuthForbidden` (403) · `QuotaExhausted` (402 / 429-with-quota) · `RateLimited` (429) · `BadRequest` (400) · `NotFound` (404) · `PayloadTooLarge` (413) · `ServerError` (5xx) · `ServiceUnavailable` (503) · `GatewayTimeout` (504/408) · `Timeout` · `Offline` (network error, no HTTP status) · `CircuitOpen` · `CancelledByUser`

Each `LlmHealthResult` carries an `ActionableMessage` — a human-readable next step ("rotate your key at …", "top up your account at …") built from the diagnosis plus the provider's dashboard/keys URLs, so a settings page can render a fix instead of a stack trace.

`LlmModelDiscovery.DiscoverAsync`/`DiscoverOneAsync`/`DiscoverAllAsync` queries a provider's live `/models`-style endpoint (when it has one) and normalizes the response shape (`data[]`, `models[]`, bare arrays, legacy `model_id`, …) into a plain model-id list, independent of `LegionClient` — useful for building a status screen without sending an actual prompt.

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
IReadOnlyList<VoterProfile>      CreatePanel(int count, string fallbackProviderId = "claude-api", Random?)
```

`VoterProfile.ForCharacter(name, psychologyMarkdown, providerId, apiKey?, model?)` wraps a character's psychology into a voter profile suitable for in-story decisions.

`LegionClient` (direct transport, no voting):

```csharp
Task<string> CallAsync(providerId, apiKey, model, systemPrompt, userMessage, maxTokens, temperature, CT, cachedSystemPrefix?, cacheUserMessage, userCancelToken?)
Task<string> CallAsync(providerId, systemPrompt, userMessage, maxTokens, temperature, modelOverride?, CT, cachedSystemPrefix?, cacheUserMessage)   // shared-credential lookup
Task<string> CallAsync(providerId, apiKey, model, systemPrompt, userMessage, endpointUrl, maxTokens, temperature, CT)                              // explicit-URL / self-hosted
Task<string> CallChatAsync(providerId, apiKey, model, IEnumerable<ChatTurn>, systemPrompt?, maxTokens, temperature, CT)
Task<string> CallChatAsync(providerId, IEnumerable<ChatTurn>, systemPrompt?, maxTokens, temperature, modelOverride?, CT)                           // shared-credential lookup
Task<(string ProviderId, string Response)> CallWithFallbackAsync(IEnumerable<string> fallbackChain, systemPrompt, userMessage, maxTokens, temperature, CT)
Task<string> CallWithDocumentAsync(apiKey, model, documentBytes, mediaType, userPrompt, systemPrompt?, maxTokens, temperature, CT)                 // Claude only
Task<IReadOnlyList<float[]>> EmbedAsync(providerId, apiKey, model, IReadOnlyList<string> inputs, dimensions?, CT)                                  // OpenAI only
Task<IReadOnlyList<string>>  GenerateImageAsync(providerId, apiKey, model, prompt, size, n, CT)                                                    // OpenAI (DALL·E) only
Task<IReadOnlyList<byte[]>>  GenerateImageBytesAsync(providerId, apiKey, model, prompt, size, quality, n, CT)                                      // OpenAI (DALL·E) only
bool IsProviderConfigured(providerId)
static bool IsSupported(providerId)
static string? GetClaudeTeamOAuthToken()
static IReadOnlyDictionary<string,string> DefaultModels
```

---

## Architecture

```
┌─ Your app / legion.exe CLI
│   └─ LlmVotingService     public API: VoteAsync / DecideAsync / ScoreAsync
│         └─ VoterFactory   builds VoterProfile lists (CreatePanel, personas, diverse panels)
│         └─ LlmVotingProvider
│               └─ LegionClient   universal LLM transport
│                     ├─ Claude wire shape (claude-api key auth / claude-team OAuth)
│                     ├─ OpenAI-compatible wire shape (openai, deepseek, mistral, xai,
│                     │    groq, together, openrouter, fireworks, kimi, perplexity,
│                     │    and any self-hosted endpoint via an explicit URL)
│                     ├─ Gemini wire shape
│                     └─ Cohere wire shape
│                     ├─ CircuitBreaker (process-static, per-provider)
│                     └─ ClaudeCodeOAuthSource (~/.claude/.credentials.json, auto-refresh)
├─ LegionConfig (legion.json — per-project voters/judge/models/apiKeys/maxConcurrency)
└─ MindAtticCredentialStore (facade over MindAttic.Vault; shared keyring at %APPDATA%/MindAttic/LLM/)
```

`LegionClient` owns the socket pool, retry policy, and circuit breaker. `LlmVotingProvider` adds vote-specific shaping and per-voter key resolution. `LlmVotingService` is the public API — you almost never need to touch the lower layers. See [`docs/BIBLE.md §4`](docs/BIBLE.md#LEG-§4) for the canonical architecture diagram and project layout, and [`docs/BIBLE.md §5`](docs/BIBLE.md#LEG-§5) for the Laws this design must never violate.

---

## Personas: the 1024-persona library

`PersonaLibrary` (`MindAttic.Legion/Services/PersonaLibrary.cs`) is not a hand-authored list — it's a deterministic sample from a diversity skeleton:

- **40 vocational archetypes** (retired schoolteacher, ER nurse, trial lawyer, software engineer, parish priest, long-haul truck driver, …)
- **16 worldviews** (cautious traditionalist, dry-witted skeptic, data-driven empiricist, blunt populist, …)
- **16 cultural backgrounds** (rural Midwestern, coastal urban, first-generation immigrant, Appalachian, Gulf Coast bayou, …)

That 40×16×16 = 10,240-point cube is sampled down to a fixed **1024** personas using a hard-coded seed (`SampleSeed = 0x9E3779B1`), so the library is exactly reproducible build-to-build. Each persona is further enriched with a deterministic age, one of two pronoun sets (`she/her`, `he/him`), and one of 50 signature quirks (~20 personas per quirk) so neighboring entries in the catalog still read as distinct people. Every persona has a unique id and name (`PersonaLibraryShapeTests` asserts both). There are no per-provider "default" personas — a bare LLM voter with no persona is simply a `VoterProfile` with empty `PersonalityMarkdown`.

`PersonaLibrary.Sample(count, rng?)` draws **without replacement** via a partial Fisher-Yates shuffle, so a panel built through `VoterFactory` never repeats a persona in the same batch; pass a seeded `Random` for reproducible test panels. `PersonaLibrary.Profiles` embeds the persona library's latest psychometric scores directly in the NuGet package (`Resources/psychometric-profiles.json`), so consumers get profile-carrying personas with zero external data source — pair with `VoterFactory.GenerateDiverseVoters` to build trait-diverse panels out of the box.

`VoterFactory.GenerateUniqueVoters(count, providerIds, fallbackProviderId = "claude-api", rng?)` spreads voters across every supplied provider at least once before doubling up, backfilling extra slots with the fallback provider. `VoterFactory.GenerateDiverseVoters(count, providerIds, profiles, fallbackProviderId, rng?)` instead does greedy farthest-point selection over the 15-dimensional OCEAN+HEXACO+DISC trait vector — seeding on the persona farthest from the panel's centroid, then repeatedly adding whichever remaining candidate is farthest from everyone already chosen — so a small panel spans the trait space instead of clustering.

---

## Testing

`MindAttic.Legion.Tests/` is a two-tier suite: a **unit suite** (461 tests, runs on every `dotnet test`, no network) and a **live integration suite** (21 explicit tests across four categories, runs only when filtered).

### Unit suite (461 tests)

Covers, with no network calls:

- Vote tally correctness (plurality, simple-majority, two-thirds, unanimous) and quorum enforcement at threshold edges
- Persona injection (system-prompt wrapping)
- Provider failover and refill (one voter erroring doesn't break the vote; failed slots are reissued against the surviving providers)
- Choice-option exact-match matching
- Scored-vote dimension aggregation
- Wire-format adapters per provider (Claude / OpenAI-compatible / Gemini / Cohere) — including the Opus 4.7+/Sonnet 5+/Haiku 5+ temperature-omission invariants and the Claude 5+ adaptive-thinking-disable invariant
- Resilience policy: retry / circuit-breaker / fallback-chain
- Health-check diagnosis classification (auth / quota / rate-limit / offline / wrong-reply)
- Live model discovery + JSON shape normalization (every wire shape Legion has met: OpenAI `data[]`, Gemini `models[]` with `models/` prefix trim, Cohere/Anthropic variants, bare arrays, `model_id` legacy, mixed-type arrays, malformed JSON soft-failure)
- `VotingConfiguration.ActiveProviderIds` gating: explicit keys, blank-key filtering, default trusted-set whitelist, untrusted-provider rejection, shared-credential merging, dedup
- `AskCommand` helpers: trust-list intersection, choice-mode option snapping, auto-context assembly + caps, architect-prompt heuristics, help-flag recognition, tier override map (`BuildTierModelOverrides` Low/Medium/High/Higher), default tier pin (High)
- `TiersCommand` helpers: trust-list parity with `ask`, default tier sweep (Low/Medium/High), provider resolution, table truncation
- `PollCommand` helpers: round-robin assignment math (`AssignRoundRobin` at counts 8, 10, 100), tier model resolution per assignment, case-insensitive distribution aggregation, off-ballot handling
- `GenerateCommand` helpers: count-splitting math (with a property test that totals always equal N), every list-marker shape (`1.`, `1)`, `- `, `* `, `• `), every quote variant including curly, order preservation, case-insensitive dedup with first-seen-wins
- Persona-library shape (exactly 1024 personas, unique ids/names), persona-store round-tripping, psychometric instrument item parsing/scoring/model-shape tests

Run from the repo root:

```bash
dotnet test MindAttic.Legion.Tests/MindAttic.Legion.Tests.csproj
```

Typical wall time: ~2s. Verified locally: **461 passed / 0 failed / 0 skipped**.

### Live integration suite (21 explicit tests, four categories)

The live suite spans four categories, all marked `[Explicit]` so they do **not** run on normal `dotnet test` invocations (no surprise spend on CI). Run on demand to verify wire-shape, tier mapping, key validity, and psychometric scoring against live providers.

**`LiveApi` (17 tests) — `LiveApiIntegrationTests.cs`:** End-to-end tests that hit the real trusted-provider APIs.
- 12 per-(provider, tier) connectivity tests, one per cell of the trusted × Low/Medium/High matrix. A failure points at the exact cell.
- 1 whole-matrix sanity check using `TiersCommand.ProbeMatrixAsync` — the "panel is healthy" assertion in one line.
- 1 override-vs-catalog parity guard — pins that `BuildHighTierModelOverrides` matches `LlmProviderCatalog.GetTieredModel(..., High)` for every trusted provider.
- 3 end-to-end smoke tests of `legion ask`, `legion poll`, `legion generate` at Low tier with tiny budgets.

**`LiveKeys` (3 tests) — `LiveKeyValidationTests.cs`:** Verify the shared Vault keys actually authenticate against each provider they're present for — data-driven over whatever keys are currently in the store, so adding/removing a key adds/removes a test case with no edit required. One of the three is additionally tagged `LiveKeysTrusted` (a narrower run scoped to the trusted set).

**`LivePsychometrics` (1 test) — `PsychometricLiveTests.cs`:** End-to-end persona scoring through a real provider (≈5 Opus-class calls).

Run on demand:

```bash
# All live tests in a specific category
dotnet test --filter "Category=LiveApi"
dotnet test --filter "Category=LiveKeys"
dotnet test --filter "Category=LiveKeysTrusted"
dotnet test --filter "Category=LivePsychometrics"

# One specific cell — useful when a single provider is flaky
dotnet test --filter "FullyQualifiedName~LiveApi.Claude_High"

# All Claude tiers
dotnet test --filter "FullyQualifiedName~LiveApi.Claude_"
```

Cost is small (~12 tiny probes + 3 small smoke tests on Low tier for LiveApi) but real — wire it behind a manual GitHub Actions `workflow_dispatch` if you want it in CI without paying every run.

The full offline filter used in CI/pre-release verification:

```bash
dotnet test MindAttic.Legion.Tests -c Release --filter "Category!=LiveKeys&Category!=LiveKeysTrusted&Category!=LiveApi&Category!=LivePsychometrics"
```

---

## Build, pack, publish

```bash
# Build the whole solution (library + CLI + tests)
dotnet build MindAttic.Legion.slnx -c Release

# Run the offline test suite
dotnet test MindAttic.Legion.Tests/MindAttic.Legion.Tests.csproj

# Pack the library as a NuGet package (+ .snupkg symbols)
dotnet pack MindAttic.Legion/MindAttic.Legion.csproj -c Release -o pack-out

# Build the CLI executable
dotnet build MindAttic.Legion.Cli/MindAttic.Legion.Cli.csproj -c Release
# → MindAttic.Legion.Cli/bin/Release/net10.0/legion.exe (also legion.dll, runnable via `dotnet legion.dll`)
```

Versioning is whole-number/major-only across every MindAttic project (see [`MindAttic.HouseRules.md`](../MindAttic.HouseRules.md), inherited via [`docs/BIBLE.md §5`](docs/BIBLE.md#LEG-§5)): the package's `<Version>` bumps by a whole major number per release (`N.0.0`) and nothing else.

`docs/` canon has its own tooling, unrelated to the library build:

```bash
powershell -File tools/codex.ps1 digest   # regenerate docs/BIBLE.digest.md after editing BIBLE §1/§3/§5/§9 or the latest amendment
powershell -File tools/codex.ps1 doctor   # validate IDs, links, front-matter, cited tests/paths, digest freshness — must exit 0
```

There is also a separate, unrelated Node-based landing-page renderer (`package.json`, `scripts/cli/`, `index.htm`) that turns this README into the `mindattic.com` landing page for the project; it is not part of the library/CLI build and is deployed by the sibling `MindAttic.Deploy` repo. `tools/build-readme.ps1` (this repo's copy of the shared Codex `README.md → README.htm` engine) is a separate, simpler doc-preview artifact and does not touch `index.htm`.

---

## Directory layout

```
MindAttic.Legion/                    the library (net10.0, PackageId MindAttic.Legion)
  Models/                            Quorum, ModelTier, VoteRequest/Result, VoterProfile,
                                      VotingConfiguration, LegionConfig, ChatTurn, Persona(Detail),
                                      Psychometrics/ (OceanScores, HexacoScores, MbtiResult,
                                      EnneagramResult, DiscResult, PersonaDocument, PsychometricProfile)
  Providers/                         LlmVotingProvider — fans a vote across voters, resolves keys
  Services/                          LlmVotingService, LegionClient, LlmProviderCatalog,
                                      CircuitBreaker, ClaudeCodeOAuthSource, LegionClientOptions,
                                      LlmHealthCheck, LlmHealthDiagnosis, LlmModelDiscovery,
                                      MindAtticCredentialStore, LlmProviderRuntimeConfiguration,
                                      PersonaLibrary, PersonaNames, VoterFactory, LegionJson,
                                      Psychometrics/ (PersonaStore, PsychometricInstruments,
                                      PsychometricScorer, LlmPsychometricAssessor, PsychometricVoteAnalysis)
  Resources/psychometric-profiles.json   embedded id → latest-profile map shipped with the package
MindAttic.Legion.Cli/                 legion.exe host
  Program.cs, LegionCli.cs            entry point + health/ping/status/providers/models/personas/panel/vote
  AskCommand.cs, PollCommand.cs, GenerateCommand.cs, TiersCommand.cs, PsychometricsCommand.cs
MindAttic.Legion.Tests/                NUnit 4 test project (461 offline + 21 live-explicit)
docs/                                 Codex canon — BIBLE.md (L0), AMENDMENTS.md (L1),
                                       USER_STORIES.md (L2), rfc/, generated BIBLE.digest.md
tools/codex.ps1                       digest + doctor tooling for docs/
tools/build-readme.ps1                thin wrapper around the shared codex-standard README→HTML engine
scripts/, package.json, index.htm     separate Node-based mindattic.com landing-page renderer (not part
                                       of the library/CLI build)
```

---

## Glossary

- **Panel** — the set of active voters (provider × persona) participating in a vote.
- **Quorum** — agreement threshold: `Plurality` (any winner) · `SimpleMajority` (>50%) · `TwoThirds` (≥66.7%) · `Unanimous` (100%).
- **Voter** — one `VoterProfile`: a provider id + optional persona + optional key/model/max-tokens override.
- **Persona** — a markdown system-prompt worldview from the 1024-member `PersonaLibrary`.
- **Tier** — a `ModelTier` (Low…Highest) the catalog maps to a concrete model per provider (five providers have an explicit map; the rest fall back to their single default model at every tier).
- **Trusted set(s)** — see [Trust tiers](#trust-tiers-which-list-applies-where): the library's default voting whitelist is 5 providers (including `claude-team`); the CLI's autonomous-decision commands (`ask`/`poll`/`generate`/`tiers`) hard-code a stricter 4-provider list that excludes `claude-team`.
- **Judge** — the LLM that synthesizes free-form votes into a consensus (`JudgeProviderId`).
- **Consensus / dissent** — the agreed answer and the recorded minority positions.
- **Circuit breaker** — per-provider fast-fail after repeated failures, shared process-wide.
- **`legion.json`** — optional per-project config file (voters/judge/models/apiKeys/maxConcurrency), currently auto-applied only by the CLI's `vote` subcommand.
- **Psychometric instrument** — one of five trait batteries (OCEAN, HEXACO, MBTI-style, Enneagram-style, DISC-style) scored deterministically from in-character answers.

---

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 MindAttic.

---

## Briefing another coding agent

When you want another LLM-driven CLI (Claude Code, Codex, Cursor agent, etc.) to reach for Legion instead of blocking on you, paste the block below into its memory or system prompt. It's self-contained and assumes nothing about the host except that it can run a `.exe`.

```
You now have a Legion panel for hard calls. Use it instead of blocking on me.

Legion is a multi-LLM CLI at:
  D:\Projects\MindAttic\MindAttic.Legion\MindAttic.Legion.Cli\bin\Release\net10.0\legion.exe

The panel is HARDCODED to Claude (API key), ChatGPT, Gemini, DeepSeek — the four providers
I trust for autonomous decisions. No untrusted provider can ever be added, even via --providers
(untrusted ids get silently dropped). If a trusted provider is unreachable, Legion automatically
refills the empty slot with another working provider so the panel never falls below quorum.

CHOOSING THE COMMAND
You have four primary commands. Pick by what shape of answer you need:

  legion ask <q>        ONE decision, panel-voted, architect-framed.
                        Default tier: HIGH (flagship reasoning class for every trusted provider).
                        Use for: architectural choices, breaking-change tradeoffs, ambiguous specs.

  legion poll <q>       N independent voters, count-sorted distribution, plurality winner.
                        Default tier: LOW. Default count: 10.
                        Use for: "how does the panel split?" sampling, sentiment polls, sanity
                        checks where you want a count, not a verdict.

  legion generate <p>   N distinct creative items, deduped, newline to stdout.
                        Default tier: MEDIUM. Default count: 10.
                        Use for: name lists, taglines, alternatives, function-name brainstorms.

  legion tiers          Probe trusted-four × Low/Medium/High = 12 cells. No question, just connectivity.
                        Use for: "is the panel healthy right now?" before a critical session,
                        or after a model deprecation in the wild.

WHEN TO CALL `ask`
Whenever you would otherwise pause to ask me:
  - An architectural choice (DI lifetime, library pick, schema shape, layering decision).
  - A breaking-change tradeoff (rename now vs. soft-deprecate, migrate now vs. shim).
  - An ambiguous spec where two reasonable readings exist and the next file you write depends on which one is right.
  - Anything hard to reverse.

Don't call `ask` for mechanical edits, formatting, or things where you already know the answer — each call costs ~3-8s and four flagship-tier LLM-API requests. For cheap one-shot decisions, use --tier low.

HOW TO CALL `ask`
  legion.exe ask "<question>" [opts]

Modes:
  - Choice (recommended when possible):
      legion.exe ask "Pick the JSON serializer" --options "System.Text.Json,Newtonsoft.Json"
      → stdout = exactly one option, exit 0 on quorum.
  - Free-form:
      legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?"
      → stdout = the synthesized answer.
  - Audit: add --json to get votes, reasoning, confidence, dissent. Use this when you want to surface tradeoffs back to me.
  - Strict consensus: add --quorum twothirds to fail closed (exit 1) if the panel splits. Use for irreversible decisions.
  - Tier override: add --tier low|medium|high|higher|highest. Default is high (architecture). Drop tier for cheap one-shot decisions where flagship reasoning is overkill.
  - MUST-ANSWER: add --must-answer when you absolutely cannot tolerate "no answer". Phase-2 retry doubles budget and drops auto-context; phase-3 falls back to a single-provider chain (claude-api → openai → gemini → deepseek) calling raw text. Always emits an answer if any one provider is reachable.

Auto-context: by default `ask` reads CLAUDE.md, README.md, and `git status -s` / `git log --oneline -10` from cwd and prepends them so voters know the project. Pass --no-auto-context for a clean prompt, or --context-file <path> to inject a specific file (e.g. the file you're about to edit).

`ask` OUTPUT CONTRACT
  stdout              | exit | meaning
  ------------------- | ---- | -------------------------------------------------------
  answer              | 0    | panel agrees, act on it
  best-guess answer   | 1    | panel split — re-ask with more context or escalate to me
  (empty)             | 2    | unhandled error (network, etc.)

With --must-answer, exit 0 also covers the recovery cases (phase-2 retry, phase-3 single-provider chain). stderr will tell you which phase delivered ("ask: recovered in phase 3 via claude-api") — log that line if you want a record of the degraded path. stderr carries warnings only; never parse it for the answer.

WHEN TO CALL `poll`
When you want to *sample* the panel rather than reach a verdict — e.g. "how confidently do they all agree?" — or when you want to feed a question to many voters at the cheapest tier and tally the result.

  # 100 voters at Low — quick distribution
  legion.exe poll "Should we ship today?" --options "yes,no,not-yet" --count 100 --tier low

`poll` writes a distribution table to stdout (or full JSON via --json). Plurality winner is whichever option got the most votes; no quorum.

WHEN TO CALL `generate`
When you need many *distinct creative items* on a single prompt — names, taglines, alternatives, function-name brainstorms.

  legion.exe generate "single-word hero-vibe character names" --count 100 > names.txt

`generate` writes newline-separated items to stdout (Unix-pipe convention) and diagnostics to stderr. Pipe through `head`, `shuf`, `grep`, or redirect to a file.

WHEN TO CALL `tiers`
Use it ONCE at the start of a session that depends on the panel being live, or after a model rotation in the wild. It probes 12 cells (4 providers × Low/Medium/High) with a tiny "reply OK" prompt.

  legion.exe tiers
  # → 12-row table; exit 0 if every cell is OK, 1 otherwise.

Don't call it before every `ask` — that's wasted spend. Once per session is plenty.

EXAMPLES
  # Decide a DI lifetime
  legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" --options "Singleton,Scoped,Transient"

  # Pick between two refactors with full reasoning
  legion.exe ask "Should we extract Persona-rendering into a separate service?" --json

  # Conservative: only act if 2/3+ agree
  legion.exe ask "Migrate the credential store to DPAPI now?" --quorum twothirds

  # Cheap one-shot, no need for flagship reasoning
  legion.exe ask "Should the new flag default true or false?" --options "true,false" --tier low

  # I can't proceed without an answer — pull every lever
  legion.exe ask "Pick the cache key format" --options "user:{id},u-{id},user_{id}" --must-answer

  # Bulk distribution sample
  legion.exe poll "Severity?" --options "low,medium,high,critical" --count 50 --tier low

  # Bulk creative generation
  legion.exe generate "fictional country names with calm vibes" --count 30 --tier medium

  # Pre-flight panel check
  legion.exe tiers

--providers exists but you almost never need it: it can only NARROW within the trusted four (e.g. --providers claude-api,openai). Passing untrusted ids is harmless (they're dropped) but pointless. Don't reach for it unless I specifically ask you to scope a panel.

If `legion ask` exits 1 (no quorum) WITHOUT --must-answer, don't silently pick its best-guess answer for a structural decision — surface the dissent (re-run with --json, summarize the disagreement, ask me). If you used --must-answer and still got exit 1 or 2, the trusted panel is genuinely down — escalate to me, don't guess.
```

---

## Contributing notes (for sibling repos using Legion)

- **Always wrap judgment calls in `DecideAsync`.** If your code has a hard-coded branch that picks among options based on heuristics, replace the heuristic with a Legion decision and pass the relevant context. The panel is cheap; bad decisions are expensive.
- **Prefer `Plurality` for surfacing all viewpoints, `SimpleMajority` for routine decisions, `TwoThirds`+ for canon-affecting actions.** Don't reach for `Unanimous` unless the cost of a single dissent is real.
- **Pass real context.** A vote without context is just a popularity contest. Bundle the canon, the prior chapters, the schema, the rubric — whatever the panel needs to be informed.
- **Watch the cost dial.** A panel of 5 means 5× tokens. Use the `providerIds` overload to scope votes to 2–3 providers when you don't need the full panel.
- **`QuorumReached == false` is a signal, not a failure.** It means the panel saw a real ambiguity. Surface it to a human or escalate the question.
- **Know which trusted list you're in.** The library's default `AllowedProviderIds` includes `claude-team`; the CLI's `ask`/`poll`/`generate`/`tiers` commands don't. If you're porting logic from one surface to the other, re-check which provider set actually applies (see [Trust tiers](#trust-tiers-which-list-applies-where)).
