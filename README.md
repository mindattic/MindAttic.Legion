# MindAttic.Legion

.NET 10 library and CLI that turns a panel of LLMs (Claude, ChatGPT, Gemini, DeepSeek and nine more) into one answer with quorum, reasoning and dissent, for the calls you cannot afford to get wrong.

[![C#](https://img.shields.io/badge/language-C%23-512BD4)](MindAttic.Legion/MindAttic.Legion.csproj) [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](MindAttic.Legion/MindAttic.Legion.csproj) [![Providers](https://img.shields.io/badge/providers-13-0a7ea4)](MindAttic.Legion/Services/LlmProviderCatalog.cs) [![NuGet](https://img.shields.io/nuget/v/MindAttic.Legion)](https://www.nuget.org/packages/MindAttic.Legion) [![License MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

```text
$ legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" --options "Singleton,Scoped,Transient"
Singleton

$ legion.exe tiers
PROVIDER   TIER     MODEL                            STATUS  TIME     DETAIL
────────────────────────────────────────────────────────────────────────────
claude     Low      claude-haiku-4-5-20251001        OK      2600ms   OK
claude     Medium   claude-sonnet-5                  OK      999ms    OK
claude     High     claude-opus-4-7                  OK      1404ms   OK
...
summary: 12/12 ok
```

Try it: add a project reference (see [Quick start](#quick-start)) or the [MindAttic.Legion package on nuget.org](https://www.nuget.org/packages/MindAttic.Legion).

## Why

One LLM is one opinion. When a contradiction, a misclassification or a bad route is expensive, you want a panel that votes, not one model that bluffs.

- Get a consensus answer with quorum, confidence and the dissenting views, instead of trusting whichever model you happened to call.
- Hand a branch your code would otherwise guess at to `DecideAsync` and get one option back, with reasoning.
- Let a blocked coding agent (Claude Code, Codex) get an architectural decision from the panel without waiting for you.
- Survive provider outages: failed voters are refilled from the surviving providers, so the panel never drops below quorum.
- Stop chasing model-version churn: ask for a Low, Medium or High tier and the catalog picks the concrete model.
- Drop it into any `.csproj`: Legion depends on no specific MindAttic application, only `MindAttic.Vault` and `Microsoft.Extensions.*`.

## Features

### Voting and decisions

- Voting: call every active provider in parallel, tally the answers, return the consensus with reasoning and dissent.
- Decision-making: `DecideAsync(question, options)` picks one option from a fixed list with a confidence.
- Scoring: multi-dimensional rubric evaluation (1 to 10 per dimension), aggregate scores, weakest-dimension feedback and ready-to-inject improvement directives.
- Four quorum levels: Plurality, SimpleMajority, TwoThirds, Unanimous.

### Providers and tiers

- Multi-provider transport: one `LegionClient` talks to thirteen provider connections: Claude (`claude`, a direct Anthropic API key), ChatGPT, Gemini, DeepSeek, Mistral, xAI/Grok, Groq, Together AI, OpenRouter, Fireworks AI, Cohere, Kimi (Moonshot AI) and Perplexity, plus an explicit-URL escape hatch for self-hosted OpenAI-compatible endpoints (Ollama, vLLM, RunPod).
- Tiered model selection: every provider exposes a Low, Medium, High, Higher and Highest tier. The four providers Legion explicitly maps (`claude`, `openai`, `gemini`, `deepseek`) resolve to a concrete model per tier; every other provider falls back to its single default model at any tier.
- Per-project panels via `legion.json`: declare a project's voters, judge, model overrides, API keys and concurrency cap without touching code.
- Resilience: retry with backoff on transient errors, a process-wide per-provider circuit breaker, and a fallback chain that tries providers in order until one answers.

### Personas and psychometrics

- Personas: every voter can wear a persona (a markdown system prompt). Use the bundled 1024-persona library, build a panel of N unique voices, or wrap a fictional character's psychology to vote as them.
- Psychometric profiles: score the persona library on five instruments (OCEAN/Big Five, HEXACO, MBTI-style, Enneagram-style, DISC-style), persisted as one JSON file per persona. The model only answers items in character; scoring is deterministic in code.
- Trait-diverse panels: pick personas that maximise psychometric spread, then segment a vote's result by trait.

### CLI

- `legion.exe ask`: an architect-framed decision for the loop where another coding CLI blocks on a user prompt. Auto-pulls `CLAUDE.md`, `README.md` and git state as context, defaults to the High tier and the trusted four providers, with automatic refill on outages.
- `legion.exe poll`: round-robins N voters across the trusted four and reports a count-sorted distribution and plurality winner.
- `legion.exe generate`: fans out one batched call per provider for that provider's share of N items, deduplicates, and prints newline-separated results.
- `legion.exe tiers`: probes every trusted provider and tier with a tiny prompt and prints a matrix.
- Also `status`, `providers`, `models`, `personas`, `panel`, `health`, `ping`, `vote` and `psychometrics`: the same engine, no .NET app required.

### Direct transport

- `LegionClient` also does multi-turn chat, Anthropic prompt caching, OpenAI embeddings, OpenAI (DALL-E) image generation, Claude document input (native PDF, extracted DOCX/EPUB/text) and health checks with actionable diagnoses.

## Quick start

Prerequisites: the .NET 10 SDK and at least one provider API key.

Reference the library from a sibling checkout:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\MindAttic.Legion\MindAttic.Legion\MindAttic.Legion.csproj" />
</ItemGroup>
```

The `MindAttic.Legion` package is also on nuget.org, but the newest published version there (2.6.0) is older than this repo's version (26.0.0), so use a project reference for the API described here. Target framework: net10.0. The library depends on `MindAttic.Vault` and `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Http` and `Microsoft.Extensions.Logging.Abstractions` only (see [Law LEG-LAW-1](docs/BIBLE.md#LEG-LAW-1)).

Configure and vote:

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
        ["claude"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "",
        ["openai"] = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
    },
    JudgeProviderId = "claude",
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

You should see the consensus answer, its strength as a percentage, and a narrative summary of how the panel voted.

`AddLLMVoting` (see [ServiceCollectionExtensions](MindAttic.Legion/Services/ServiceCollectionExtensions.cs)) registers `LlmVotingService`, `LlmVotingProvider` and a `LegionClient` that shares the same key resolution as the voting layer, so a service that injects `LegionClient` directly sees the same keys. Call `services.AddLegionClient()` instead if you only want the transport (health checks, direct provider calls) without the voting machinery.

Zero-config also works: `new VotingConfiguration()` with empty `ApiKeys` resolves every key from the shared MindAttic credential store (`UseSharedCredentials` defaults to `true`).

To use the CLI instead, build it and run a health check:

```bash
dotnet build MindAttic.Legion.Cli/MindAttic.Legion.Cli.csproj -c Release
MindAttic.Legion.Cli/bin/Release/net10.0/legion.exe health
```

## How it works

```text
┌─ Your app / legion.exe CLI
│   └─ LlmVotingService     public API: VoteAsync / DecideAsync / ScoreAsync
│         └─ VoterFactory   builds VoterProfile lists (CreatePanel, personas, diverse panels)
│         └─ LlmVotingProvider
│               └─ LegionClient   universal LLM transport
│                     ├─ Claude wire shape (API key auth)
│                     ├─ OpenAI-compatible wire shape (openai, deepseek, mistral, xai,
│                     │    groq, together, openrouter, fireworks, kimi, perplexity,
│                     │    and any self-hosted endpoint via an explicit URL)
│                     ├─ Gemini wire shape
│                     ├─ Cohere wire shape
│                     └─ CircuitBreaker (process-static, per-provider)
├─ LegionConfig (legion.json: per-project voters/judge/models/apiKeys/maxConcurrency)
└─ MindAtticCredentialStore (facade over MindAttic.Vault; shared keyring at %APPDATA%/MindAttic/LLM/)
```

`LegionClient` owns the socket pool, retry policy and circuit breaker. `LlmVotingProvider` adds vote-specific shaping and per-voter key resolution. `LlmVotingService` is the public API; you almost never need to touch the lower layers. See [BIBLE section 4](docs/BIBLE.md#LEG-§4) for the canonical architecture and [section 5](docs/BIBLE.md#LEG-§5) for the Laws this design must never violate.

## Voting modes

### Open-ended consensus

The simplest call: ask a question, every voter writes a free-form answer, a judge LLM synthesizes the consensus.

```csharp
var r = await voting.VoteAsync("What weapon does Kyle carry?", canonContext, Quorum.Plurality);
// r.Consensus == "Silence (a corundum-edged tantō)"
```

### Choice vote

`VoteAsync` with `Options` runs a vote among fixed options. It is much cheaper to tally: exact match wins.

```csharp
var req = new VoteRequest
{
    Question = "Severity of this canon contradiction?",
    Context  = chapterPlusCanon,
    Options  = new() { "low", "medium", "high" },
};
var r = await voting.VoteAsync(req, Quorum.SimpleMajority);
// r.Consensus is one of "low", "medium", "high"
```

### Decide

`DecideAsync` is sugar over choice voting. Use it when an automated workflow has to pick one option and move on (route a request, fill in a field, resolve a tie). It returns a `DecisionResult` with `Choice`, `Reasoning`, `Confidence` and `QuorumReached`.

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
    // Panel was too divided: escalate to a human, or rerun with stricter quorum.
}
```

`DecideAsync` is the right entry point any time your code would otherwise have to hard-code a branch or guess.

### Score

`ScoreAsync` rates something across multiple dimensions (1 to 10 each). It returns aggregate scores, failing dimensions, per-dimension consensus strengths and failures, and synthesized improvement directives.

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

A dimension that every voter omitted has no aggregate entry at all (never a phantom `0.0`), so it cannot wrongly be flagged as failing or drag the average down. `ConsensusStrengths` and `ConsensusFailures` surface flags that at least half the panel raised.

### Persona voices

`VoteWithPersonasAsync` and `VoteWithProfilesAsync` let a panel of unique voices, or a single character's psychology, do the voting.

```csharp
// Generic 5-voice panel spread across active providers
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

## Quorum

`Quorum` controls how strict the agreement threshold is.

| Value | Threshold | Use when |
|---|---|---|
| `Plurality` | Any winning answer counts | Cheapest. The vote always returns something, even a 1-of-4 answer. Good for surfacing all viewpoints. |
| `SimpleMajority` | More than 50% must agree | Default for most decisions. A 2-of-4 tie fails (strict `agree*2 > total`). |
| `TwoThirds` | At least 66.7% must agree | Computed as `agree*3 >= total*2` (exact integer arithmetic), so a 2-of-3 panel clears it. |
| `Unanimous` | 100% must agree | Irreversible or canonical actions. |

If quorum is not reached, `result.QuorumReached == false` and `result.Consensus == ""`. Your code decides whether to escalate, retry with a different quorum, or accept the plurality answer anyway.

## Providers and models

Legion calls 13 provider connections, all through the single `LegionClient`. Configure them via `VotingConfiguration.ApiKeys`. A provider is active for voting when it has a non-empty key (explicit or from the shared store) and passes the `AllowedProviderIds` whitelist (see [Trust tiers](#trust-tiers)). `GetActiveProviderIds()` lists which providers are actually voting.

| Provider id | Vendor | Auth | Default model | Dashboard |
|---|---|---|---|---|
| `claude` | Anthropic | API key (`x-api-key`) | `claude-sonnet-5` | console.anthropic.com |
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

Source of truth: [LlmProviderCatalog](MindAttic.Legion/Services/LlmProviderCatalog.cs) (metadata, dashboard and keys URLs, per-provider known-model lists) and [LegionClient](MindAttic.Legion/Services/LegionClient.cs) `DefaultModels` and `Endpoints` (wire endpoints and fallback models). Run `legion.exe providers` for the live list, or `legion.exe models <provider>` for a provider's full known-model catalog.

The default model is what each provider falls back to when no model override is supplied and no `model` field is recorded in `providers.json`. For tier-aware selection use `LlmProviderCatalog.GetTieredModel(providerId, ModelTier)`; see [Tier system](#tier-system).

Override the model for one provider:

```csharp
config.ModelOverrides["claude"] = "claude-opus-4-8";
```

Restrict a vote to a subset:

```csharp
var r = await voting.VoteAsync(req, quorum, new[] { "claude", "openai" });
```

### Self-hosted and local models

Providers outside the catalog are not voter-panel members, but `LegionClient` can still call them at an explicit URL using the OpenAI-compatible chat-completions shape (Ollama, vLLM, RunPod and similar):

```csharp
var client = new LegionClient(httpClient);
var reply = await client.CallAsync(
    providerId:  "local",                         // any stable id, used only for circuit-breaker tracking
    apiKey:      "ollama",                         // any non-empty string for auth-less local servers
    model:       "llama3.1:8b",
    systemPrompt:"You are a helpful assistant.",
    userMessage: "Summarize this paragraph...",
    endpointUrl: "http://localhost:11434/v1/chat/completions");
```

### Tier system

`ModelTier` is the Legion abstraction for "the cheap one" and "the strong one" without naming model versions that drift. Only the four providers below have an explicit tier mapping in [LlmProviderCatalog](MindAttic.Legion/Services/LlmProviderCatalog.cs). Every other provider (`mistral`, `xai`, `groq`, `together`, `openrouter`, `fireworks`, `cohere`, `kimi`, `perplexity`) has no tier table, and `GetTieredModel` returns its `DefaultModel` at any tier.

| Tier | `claude` | `openai` | `gemini` | `deepseek` |
|---|---|---|---|---|
| `Low` | `claude-haiku-4-5-20251001` | `gpt-4.1-nano` | `gemini-2.5-flash-lite` | `deepseek-v4-flash` |
| `Medium` | `claude-sonnet-5` | `gpt-5.4-mini` | `gemini-2.5-flash` | `deepseek-v4-flash` |
| `High` | `claude-opus-4-7` | `gpt-5.4` | `gemini-2.5-pro` | `deepseek-v4-pro` |
| `Higher` | `claude-opus-4-8` | `gpt-5.5` | `gemini-3.1-flash-lite` | `deepseek-v4-pro` |
| `Highest` | `claude-fable-5` | `gpt-5.6-sol` | `gemini-3.5-flash` | `deepseek-v4-pro` |

When a tier is not directly mapped for a provider, `GetTieredModel` walks down the ladder (Highest, Higher, ..., Low) and returns the closest available model, so asking for Highest against a 3-tier provider gives you High, not null. Asking for a tier lower than every entry walks back up. When the provider has no tier table at all, it returns the provider's `DefaultModel`.

```csharp
// Strong reasoning model for an architectural decision:
var arch = LlmProviderCatalog.GetTieredModel("claude", ModelTier.High);
// → "claude-opus-4-7"

// Cheap model for a 100-voter poll:
var bulk = LlmProviderCatalog.GetTieredModel("claude", ModelTier.Low);
// → "claude-haiku-4-5-20251001"

// A provider with no tier table: always the default, regardless of tier
var m = LlmProviderCatalog.GetTieredModel("mistral", ModelTier.Highest);
// → "mistral-large-latest"
```

CLI defaults: `legion ask` uses High (architecture wants flagship reasoning), `legion poll` uses Low (bulk distribution wants cheap), `legion generate` uses Medium (creative balance), and `legion psychometrics score` uses High. All accept `--tier <t>`.

Pin a whole panel to a tier in the .NET API:

```csharp
config.ModelOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["claude"]   = LlmProviderCatalog.GetTieredModel("claude",   ModelTier.High)!,
    ["openai"]   = LlmProviderCatalog.GetTieredModel("openai",   ModelTier.High)!,
    ["gemini"]   = LlmProviderCatalog.GetTieredModel("gemini",   ModelTier.High)!,
    ["deepseek"] = LlmProviderCatalog.GetTieredModel("deepseek", ModelTier.High)!,
};
```

`AskCommand.BuildTierModelOverrides(ModelTier)` is the canonical helper for this in CLI code; copy its shape for a similar command.

### Provider quirks handled automatically

`LegionClient` absorbs per-model wire-shape differences so callers never see them:

- Temperature-deprecating models. Claude's Fable and Mythos families, Sonnet 5+ and Haiku 5+, and Opus 4.7+ reject the `temperature` field (HTTP 400). OpenAI's o-series and the GPT-5 family reject `temperature` too and require `max_completion_tokens` instead of `max_tokens`. Both are detected by parsing the model id, not a hard-coded list, so a future point release does not silently break.
- Adaptive thinking on Claude 5+. `claude-sonnet-5+` and `claude-haiku-5+` run adaptive extended thinking by default when the `thinking` field is omitted, which can consume the whole token budget. Legion sends `thinking: {type: "disabled"}` for ordinary calls to these models. Fable and Mythos always think and reject that field, so they are excluded; Opus 4.7+ already defaults to no thinking.
- Gemini 2.5+ thinking budget. For Gemini 2.5 models Legion sends `thinkingConfig: { thinkingBudget: 0 }` so the full `maxOutputTokens` budget goes to the response.
- Claude response parsing. Thinking-tier Claude models put a `thinking` block before the `text` block; Legion concatenates every text block rather than assuming the first.
- Gemini response parsing. Parts flagged `"thought": true` are skipped and the remaining text concatenated; a safety-blocked or `MAX_TOKENS` candidate with no content returns `""` (never throws), so a benign refusal does not trip the circuit breaker.

### Trust tiers

Legion has two different "which providers are eligible" lists, and this matters when you narrow a panel:

| List | Members | Applies to |
|---|---|---|
| `LlmProviderCatalog.All` and `AllIds` | All 13 providers | Everything Legion can call: `legion.exe providers` and `models`, direct `LegionClient` calls. |
| `LlmProviderCatalog.Default` and `DefaultIds`, `VotingConfiguration.AllowedProviderIds` (default), CLI `TrustedProviderIds` | `claude`, `openai`, `gemini`, `deepseek` | The trusted four that every first-party surface (settings UIs, library voting, the CLI's `ask`, `poll`, `generate`, `tiers` and `vote`) restricts to by default. `--providers` can only narrow within this set; an id outside it is silently dropped. |

When a trusted provider errors mid-vote (network blip, rate limit, transient 5xx), `LlmVotingService.RefillFailedVotersAsync` dispatches a fresh call to one of the surviving allowed providers (round-robin), so the panel never shrinks below quorum size. A failed Gemini slot becomes a second Claude or DeepSeek call rather than a missing vote. Refilled slots drop any persona overlay so a surviving voter does not vote twice as the same character.

Run the library with a different shortlist:

```csharp
config.AllowedProviderIds = new(StringComparer.OrdinalIgnoreCase) { "claude", "openai" };
```

Or via the CLI:

```bash
legion.exe ask "..." --providers claude,openai,gemini,deepseek
```

Set `AllowedProviderIds` to an empty set to disable filtering and let every provider with a key vote.

## Configuration

### Credential storage

Legion reads keys from the shared credential store at `%APPDATA%/MindAttic/LLM/` (backed by `MindAttic.Vault`) so every MindAttic app shares one keyring. `VotingConfiguration.UseSharedCredentials = true` (the default) opts in.

Resolution order (see [Law LEG-LAW-2](docs/BIBLE.md#LEG-LAW-2)):

1. A per-voter `VoterProfile.ApiKeyOverride`.
2. An explicit entry in `VotingConfiguration.ApiKeys`.
3. The shared credential store: User Secrets, App Service settings or Azure Key Vault when a host has called `MindAtticCredentialStore.UseConfiguration(IConfiguration)`, falling back to the `%APPDATA%/MindAttic/LLM/providers.json` file.

`MindAtticCredentialStore` is a static backward-compatible facade over `MindAttic.Vault`'s `LlmCredentialStore` and `CompositeCredentialStore`; new code may inject those types via DI instead. The CLI always uses the shared store, plus environment variables for containerized deployments (for example `MindAttic__Vault__LLM__claude__apiKey`).

### Per-project panels

Drop a `legion.json` file at a project's root to declare that project's voter panel without touching code. A panel of every provider Legion knows:

```json
{ "voters": ["claude","openai","gemini","deepseek","mistral","xai",
             "groq","together","openrouter","fireworks","cohere"] }
```

A two-vendor panel with an explicit judge:

```json
{ "voters": ["openai","claude"], "judge": "claude" }
```

Fields:

- `voters` replaces the default `AllowedProviderIds` whitelist.
- `judge` overrides `JudgeProviderId`.
- `models` sets per-provider `ModelOverrides`.
- `apiKeys` sets per-project keys, which win over the shared store.
- `maxConcurrency` caps simultaneous ballot calls; use a lower value when a provider has a tighter rate limit than the rest of the panel.

`LegionConfig.LoadFromDirectory()` walks up from a starting directory (default: cwd) looking for `legion.json`, up to 12 levels, and returns `null` (falling back to `VotingConfiguration` defaults) when none is found or the file is malformed. Apply it with `LegionConfig.LoadFromDirectory()?.ApplyTo(config)`.

Current wiring: the CLI's `legion vote` subcommand applies it automatically before voting. `legion ask`, `poll`, `generate` and `tiers` build their `VotingConfiguration` directly and do not consult `legion.json`; their provider set is always the CLI trust list (see [Trust tiers](#trust-tiers)). Library consumers call `LegionConfig.LoadFromDirectory()?.ApplyTo(config)` wherever they want project-local configuration honoured.

## CLI reference

The CLI exposes the same engine for shell scripts, CI and rapid iteration. Every subcommand accepts `-h`, `--help`, `help` or `/?`.

```text
legion — MindAttic.Legion CLI

Commands:
  health                       Probe every provider with a 'Hello World!' test
  ping <provider>              Probe a single provider
  status [opts] [provider...]  Show model inventory, config, and connectivity
  providers                    List supported providers + dashboard URLs
  models <provider>            Show known models for a provider
  personas <count>             Sample N personas from the 1024-persona library
  panel <count> [provider...]  Build a voter panel: spread across providers, backfill claude.
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
legion.exe status --timeout 30 claude openai   # narrow to specific providers, custom timeout

legion.exe providers              # all 13 providers + vendor + default model + dashboard URL
legion.exe models <provider>      # a provider's full known-model catalog, default marked, live endpoint if any
legion.exe personas 10            # sample 10 personas from the 1024-persona library
legion.exe panel 5                # build a 5-voter panel + show provider mix
legion.exe panel 5 --diverse      # panel chosen to maximize psychometric spread (needs scored profiles)
legion.exe panel 5 claude openai --store <dir>   # explicit provider list / psychometrics store dir
```

`status` cross-references three signals per provider: the static catalog (known models), a live query against the provider's models endpoint (live models), and, unless `--no-probe` is passed, a prompt-level connectivity probe. It prints the effective model (configured override, else `providers.json`, else the catalog default) and, on failure, an actionable next step from `LlmHealthDiagnoser` (for example "key looks revoked, mint a new one at ...").

`panel` spreads voters across the supplied provider list (or every provider that currently has a key), backfilling with `claude` once every provider has at least one voter. `--diverse` requires a psychometrics store with scored profiles and picks personas by greedy farthest-point selection over the OCEAN, HEXACO and DISC trait vector instead of random sampling.

### Health and connectivity

```bash
legion.exe health                 # probe every one of the 13 providers' DefaultModel with a hello-world
legion.exe ping claude            # one-provider probe (DefaultModel)
legion.exe tiers                  # probe trusted four × Low/Medium/High = 12 cells
```

### Vote

One voter per provider, consensus output as JSON on stdout.

```bash
legion.exe vote "Is the sky blue today?" \
    --context "Cloud cover is 100%." \
    --quorum simplemajority \
    --options yes,no,unclear \
    --max-tokens 256 \
    --no-narrative
```

`vote` is the only CLI subcommand that honours a per-project `legion.json` (see [Per-project panels](#per-project-panels)). The JSON shape on stdout matches `VotingResult` and `ScoredVotingResult`, so other languages can parse it directly.

Exit codes for `vote` and `ask`:

- `0`: quorum reached
- `1`: quorum not reached
- `2`: pipeline error

### Ask

`ask` is the variant tuned for the loop where a panel-voted answer flows back into another coding CLI without a human in between.

```bash
# Default tier = High (Opus-class / GPT-5.4 / Gemini 2.5 Pro / DeepSeek v4 Pro).
legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" \
    --options "Singleton,Scoped,Transient"
# → Singleton

legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?" --json

# Override tier for cheaper one-shot decisions
legion.exe ask "Use tabs or spaces?" --options "tabs,spaces" --tier low
```

Differences from `vote`:

- Stdout is the bare answer by default. Choice mode prints exactly the picked option; free-form mode prints the synthesized consensus. Add `--json` for the full audit (votes, reasoning, confidence, dissent).
- Architect-framed voters. Each voter acts as a senior software architect on this project: decisive, preferring the boring, reversible, conventional choice, flagging irreversible decisions.
- Auto-context. Inside a repo, `ask` prepends `CLAUDE.md`, `README.md`, `git status -s` and `git log --oneline -10` to every voter's context, each capped (8 KB, 8 KB, 4 KB, 1 KB). Disable with `--no-auto-context`.
- Default quorum is Plurality, so `ask` always emits some answer. Use `--quorum twothirds` when dissent should fail closed.
- Fixed trust list. The panel is always the intersection of `--providers` (if given) with `claude`, `openai`, `gemini`, `deepseek`.

| Option | Meaning |
|---|---|
| `--options A,B,C` | Force choice mode; voters must pick exactly one. |
| `--context <text>` | Extra context appended after auto-context. |
| `--context-file <path>` | Read extra context from a file (for example the file you are about to edit). |
| `--project-dir <path>` | Where to look for `CLAUDE.md`, README and git (default: cwd). |
| `--no-auto-context` | Skip the auto-include. |
| `--quorum <q>` | `plurality`, `simplemajority`, `twothirds` or `unanimous` (default `plurality`). |
| `--max-tokens N` | Per-voter cap (default 1024, must be greater than 0). |
| `--timeout S` | Per-provider timeout in seconds (default 60). |
| `--providers a,b,c` | Narrow the panel within the trusted set. Untrusted ids are silently dropped. |
| `--tier <t>` | `low`, `medium`, `high`, `higher` or `highest` (default `high`). |
| `--must-answer` | On 0/N voter failure, retry with doubled budget and no auto-context; on a second failure, fall back to a single-provider chain (claude, openai, gemini, deepseek) calling raw text until one replies. |
| `--json` | Emit the full vote audit JSON instead of the bare answer. |

Output contract:

| stdout | exit | meaning |
|---|---|---|
| answer | `0` | panel agrees, act on it |
| best-guess answer | `1` | panel split: re-ask with more context or escalate |
| (empty) | `2` | unhandled error (network and similar) |

With `--must-answer`, exit `0` also covers the recovery cases. stderr says which phase delivered (`ask: recovered in phase 3 via claude`); stderr carries warnings only, never parse it for the answer.

### Poll

`poll` is a fan-out command, not a consensus command. It round-robins N independent voters across the trusted four on a single tier and reports a count-sorted distribution and plurality winner. Unlike `vote` (one voter per provider, quorum) and `ask` (one architect-framed answer), it has no quorum: the winner is whichever option got the most votes, even by one.

| Option | Meaning |
|---|---|
| `--count N` | Total voters (default 10). With four providers, 100 gives exactly 25 each; 10 gives 3, 3, 2, 2. |
| `--tier <t>` | `low` to `highest` (default `low`). |
| `--options A,B,C` | Force choice mode; off-ballot replies count as errors and are excluded. Free-form is allowed when omitted. |
| `--providers a,b,c` | Narrow within the trusted set. |
| `--context <text>` | Extra context appended to every voter's prompt. |
| `--max-tokens N` | Per-voter cap (default 200). |
| `--timeout S` | Per-voter timeout in seconds (default 30). |
| `--concurrency N` | In-flight call cap (default 8). |
| `--json` | Emit the full poll record (per voter, distribution, summary) as JSON. |

Voter `i` goes to `providers[i % providers.Count]`; front buckets get the remainder. Failures do not shift the index, so the distribution is reproducible rather than rebalanced under retry.

```bash
# 100 voters at Low: quick, cheap distribution
legion.exe poll "Should this PR ship today?" --options "yes,no,not-yet" --count 100 --tier low

# 30 free-form voters at Medium: cluster their answers afterward
legion.exe poll "One word that describes this codebase" --count 30 --tier medium --json

# 50 voters at High but only Claude and OpenAI
legion.exe poll "Severity?" --options "low,medium,high,critical" --count 50 --tier high --providers claude,openai
```

Exit codes: `0` when at least one voter replied and a winner was chosen; `1` when every voter errored (or on a usage error).

### Generate

`generate` produces N distinct creative items (names, taglines, alternatives, function names, scenario hooks) by fanning out one batched call per trusted provider, extracting line-separated items, deduplicating case-insensitively across batches, and printing newline-separated results to stdout.

| Option | Meaning |
|---|---|
| `--count N` | Total distinct items (default 10). Each provider gets a round-robin share via `SplitCount`: 100 becomes 25, 25, 25, 25. |
| `--tier <t>` | `low` to `highest` (default `medium`). Low produces flat output for creative bulk. |
| `--providers a,b,c` | Narrow within the trusted set. One provider gives stylistic consistency; the default maximises variety. |
| `--max-tokens N` | Per-batch cap (default 1500, about 50 short items per provider). |
| `--timeout S` | Per-call timeout in seconds (default 60). |
| `--temperature T` | Sampling temperature (default `0.9`). |
| `--no-dedup` | Keep duplicates across providers; the default dedups case-insensitively, first seen wins. |
| `--json` | Emit a JSON record (prompt, requested, returned, items, per-provider batches). |

Item extraction is defensive: `ExtractItems` strips numbered markers (`1.`, `12.`, `1)`), dash, asterisk and bullet list markers, and wrapping straight or curly quotes, so a model that ignores the "no markers" instruction still yields clean items. Diagnostics go to stderr so stdout stays clean for piping.

```bash
# 100 fantasy character names, deduped, into a file
legion.exe generate "single-word hero-vibe character names for a fantasy CLI" --count 100 > names.txt

# 30 product taglines on High tier
legion.exe generate "product taglines for a calm-tech tea brand" --count 30 --tier high

# Stylistic consistency: only Claude, lower temperature
legion.exe generate "function names for queue.dequeue helpers" --count 20 --providers claude --temperature 0.4

# Pipe through standard tools
legion.exe generate "fictional country names" --count 50 | shuf | head -10
```

Exit codes: `0` when at least one item was produced; `1` when every provider's batch errored (or on a usage error).

### Tiers

`tiers` answers "is the panel ready to vote on High right now?" without a real `ask` or `vote`. It probes every trusted provider and tier with a tiny "reply OK" prompt, by default the trusted four × Low, Medium and High (12 calls). Unlike `legion health`, which only probes each provider's default model, it catches tier-mapping breakage.

| Option | Meaning |
|---|---|
| `--providers a,b,c` | Narrow within the trusted set. |
| `--tiers low,medium,high` | Narrow the tier sweep. Default: `low,medium,high`. |
| `--all-tiers` | All five tiers. |
| `--max-tokens N` | Token budget per probe (default 400, enough for thinking models to emit text). |
| `--timeout S` | Per-probe timeout in seconds (default 45). |
| `--json` | Emit a JSON record (one entry per probe plus summary). |

Output is one row per probe (see the sample at the top of this page). Exit `0` when every probe succeeded, `1` when at least one failed. Use it before a critical session, after a model-id rotation, or as a manual CI smoke test (it costs real API calls).

### Psychometrics

Legion can administer five personality instruments to every persona and persist the results, so panels can be composed and votes read by trait composition, not just by provider. This subsystem is separate from the decision commands (`ask`, `vote` and `poll` do not touch it).

How scoring works: a single trusted model (default tier High) answers each instrument's 1 to 5 Likert items in character as the persona; the LLM never computes a score. Scoring is deterministic C# (`PsychometricScorer`), so the same answers always yield the same profile. The instruments are public-domain-derived (IPIP Big Five and HEXACO, OEJTS-style Jungian axes, open DISC- and Enneagram-style banks); the trademarked questionnaires are never used, hence "-style".

| Framework | Output |
|---|---|
| OCEAN / Big Five | O, C, E, A, N, each 0 to 100 |
| HEXACO | Honesty-Humility, Emotionality, eXtraversion, Agreeableness, Conscientiousness, Openness, 0 to 100 |
| MBTI-style | 4-letter type plus per-axis lean (E/I, S/N, T/F, J/P) |
| Enneagram-style | dominant type 1 to 9, wing, triad (Gut, Heart, Head) |
| DISC-style | D, I, S, C scores plus primary style |

Storage: each persona is one JSON file under the store's `personas/` directory (identity, structured traits and the full assessment history), with a small `runs.json` index alongside; no database. The administering LLM is recorded on each assessment, not on the persona, so re-scoring through a different provider records a new variant rather than overwriting. Each run is stamped with the model and an instrument-set version (`PsychometricInstruments.SetVersion`) so re-runs stay comparable. Store location: `--store <dir>`, then the `MINDATTIC_LEGION_STORE` environment variable, then `%APPDATA%/MindAttic/Legion`. Writes are atomic (temp file plus move); the store assumes a single writer.

```bash
legion.exe psychometrics init                     # create the store + seed the persona files
legion.exe psychometrics score --limit 8          # pilot: score 8 personas (resumable; default tier high)
legion.exe psychometrics score                    # score everything still missing a current-version profile
legion.exe psychometrics show persona-0000        # a persona's latest profile (--json for the raw record)
legion.exe psychometrics stats                    # MBTI/DISC/Enneagram distribution + mean OCEAN/HEXACO
legion.exe psychometrics history persona-0000     # every assessment run recorded for one persona
legion.exe psychometrics rescore                  # fresh full versioned run; point a scheduler at this
legion.exe psychometrics diff 1 2                 # per-framework drift between two runs
```

| Score and rescore option | Meaning |
|---|---|
| `--provider <id>` | Administering lens (default `claude`); must be in the trusted set or the command errors out. |
| `--tier <t>` | `low` to `highest` (default `high`). |
| `--limit N` | Score at most N personas; use for a cheap pilot first. |
| `--concurrency N` | Personas assessed in parallel (default 4). |
| `--timeout S` | Per-provider timeout in seconds (default 120). |
| `--store-raw` | Also persist every raw item answer for audit. |
| `--notes <text>` | Free-form note recorded on the run (`rescore` sets `"rescore"`). |
| `--store <dir>` | Override the store directory. |

`score` is resumable: it skips personas that already have a profile at the current instrument-set version for this provider, so re-running continues where it left off. `rescore` forces a brand-new run for drift tracking and is the command an external scheduler should invoke. Scoring the full library is about personas × 5 model calls, so pilot with `--limit` first.

Using the profiles:

- `legion.exe panel N --diverse` builds a panel chosen to maximise psychometric spread and tags each voter with its type.
- In code, `VoterFactory.GenerateDiverseVoters(count, providers, profiles)` does the same and attaches each `PsychometricProfile` to its `VoterProfile`. `PsychometricVoteAnalysis.Segment(voters, result, selector)` then splits a completed `VotingResult` by trait (built-in selectors `ByMbtiType`, `ByDiscPrimary`, `ByEnneagramTriad`, `ByOpennessHalf`).

> Caveat, self-report skew: a model answering in character still drifts toward dutiful, agreeable, conscientious self-presentation, so LLM-administered profiles cluster toward one corner of the trait space (expect many xSTJ types and high Conscientiousness). Personas are still differentiated, just less than real humans would be; sharper separation would need forced-choice items or per-trait anchoring in a future instrument-set version, comparable to the current one via `diff`.

## Direct LegionClient usage

Apps that do not need voting can call `AddLegionClient()` and inject `LegionClient` directly for the same connection scaffolding (endpoints, auth headers, request and response shape, model defaults, shared-credential lookup, retry, circuit breaking):

```csharp
services.AddLegionClient();
// ...
public class MyService(LegionClient legion)
{
    public Task<string> AskClaude(string prompt) =>
        legion.CallAsync("claude", systemPrompt: "...", userMessage: prompt);
}
```

`LegionClient` covers more than single-turn text completions:

```csharp
// Multi-turn chat (shared-credential overload)
var reply = await legion.CallChatAsync("openai",
    new[] { new ChatTurn("user", "Hi"), new ChatTurn("assistant", "Hello!"), new ChatTurn("user", "What's 2+2?") },
    systemPrompt: "Be terse.");

// Anthropic prompt caching: cache a large stable prefix once,
// then reuse it across calls that vary only the trailing instructions.
var cached = await legion.CallAsync("claude", apiKey, model,
    systemPrompt: dynamicInstructions, userMessage: userPrompt,
    cachedSystemPrefix: stableCanonText, cacheUserMessage: false);

// Fallback chain: try providers in order until one succeeds
var (providerId, text) = await legion.CallWithFallbackAsync(
    new[] { "claude", "openai", "gemini", "deepseek" },
    systemPrompt: "...", userMessage: "...");

// OpenAI embeddings
var vectors = await legion.EmbedAsync("openai", apiKey, "text-embedding-3-small",
    new[] { "first passage", "second passage" });

// OpenAI (DALL-E) image generation
var urls  = await legion.GenerateImageAsync("openai", apiKey, "dall-e-3", "a lighthouse at dusk, watercolor");
var bytes = await legion.GenerateImageBytesAsync("openai", apiKey, "dall-e-3", "a lighthouse at dusk, watercolor");

// Claude document input: native PDF, or extracted text for DOCX/EPUB/plain text
var summary = await legion.CallWithDocumentAsync(apiKey, "claude-sonnet-5",
    documentBytes: pdfBytes, mediaType: "application/pdf",
    userPrompt: "Summarize this contract in 3 bullets.");
```

`IsProviderConfigured(providerId)` reports whether a credential-store key currently resolves; `LegionClient.IsSupported(providerId)` and the static `LegionClient.DefaultModels` dictionary help build settings screens without an HTTP round-trip. `LlmProviderRuntimeConfigurationResolver.Get(providerId)` reads the optional `apiKey`, `type`, `model` and `maxTokens` fields recorded for a provider in `providers.json`.

### Resilience

`LegionClientOptions` tunes retry and circuit-breaker behaviour per `LegionClient` instance:

| Setting | Default | Meaning |
|---|---|---|
| `MaxRetries` | 2 | Extra attempts after the first failure, on transient errors only (network errors, HTTP 408, 429, 5xx). |
| `InitialBackoff` | 500 ms | Delay before the first retry. |
| `BackoffMultiplier` | 2.0 | Multiplier applied to the backoff after each retry. |
| `CircuitBreakerThreshold` | 5 | Consecutive failures that open the per-provider breaker. |
| `CircuitBreakerCooldown` | 2 minutes | How long the breaker stays open before allowing another attempt. |

`LegionClientOptions.NoResilience` (no retries, breaker effectively disabled) is what the CLI's `health`, `ping`, `poll`, `generate` and `tiers` commands use, since they already fan out many parallel calls and want a clean per-call pass or fail. The `CircuitBreaker` is static and process-wide: "Claude is down" means the same thing to every `LegionClient` in the process. Non-transient failures (for example 401) and client-side validation errors are never retried.

### Health checks and diagnosis

`LlmHealthCheck.CheckAsync`, `CheckOneAsync` and `CheckAllAsync` send a tiny "Reply with exactly the two words: Hello World!" probe and classify the outcome via `LlmHealthDiagnoser` into an `LlmHealthDiagnosis`: `Healthy`, `ResponseMismatch`, `BadResponse`, `MissingCredential`, `AuthInvalid` (401), `AuthForbidden` (403), `QuotaExhausted` (402 or 429 with quota), `RateLimited` (429), `BadRequest` (400), `NotFound` (404), `PayloadTooLarge` (413), `ServerError` (5xx), `ServiceUnavailable` (503), `GatewayTimeout` (504 or 408), `Timeout`, `Offline` (network error, no HTTP status), `CircuitOpen`, `CancelledByUser`.

Each `LlmHealthResult` carries an `ActionableMessage`, a human-readable next step ("rotate your key at ...", "top up your account at ...") built from the diagnosis plus the provider's dashboard and keys URLs, so a settings page can render a fix instead of a stack trace.

`LlmModelDiscovery.DiscoverAsync`, `DiscoverOneAsync` and `DiscoverAllAsync` query a provider's live models endpoint (when it has one) and normalise the response shape (`data[]`, `models[]`, bare arrays, legacy `model_id`) into a plain model-id list, independent of `LegionClient`.

## API

Voting service:

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
IReadOnlyList<VoterProfile>      CreatePanel(int count, string fallbackProviderId = "claude", Random?)
```

`VoterProfile.ForCharacter(name, psychologyMarkdown, providerId, apiKey?, model?)` wraps a character's psychology into a voter profile for in-story decisions.

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
Task<IReadOnlyList<string>>  GenerateImageAsync(providerId, apiKey, model, prompt, size, n, CT)                                                    // OpenAI (DALL-E) only
Task<IReadOnlyList<byte[]>>  GenerateImageBytesAsync(providerId, apiKey, model, prompt, size, quality, n, CT)                                      // OpenAI (DALL-E) only
bool IsProviderConfigured(providerId)
static bool IsSupported(providerId)
static IReadOnlyDictionary<string,string> DefaultModels
```

## Persona library

`PersonaLibrary` ([PersonaLibrary.cs](MindAttic.Legion/Services/PersonaLibrary.cs)) is not a hand-authored list; it is a deterministic sample from a diversity skeleton:

- 40 vocational archetypes (retired schoolteacher, ER nurse, trial lawyer, software engineer, parish priest, long-haul truck driver, ...)
- 16 worldviews (cautious traditionalist, dry-witted skeptic, data-driven empiricist, blunt populist, ...)
- 16 cultural backgrounds (rural Midwestern, coastal urban, first-generation immigrant, Appalachian, Gulf Coast bayou, ...)

That 40 × 16 × 16 = 10,240-point cube is sampled down to a fixed 1024 personas with a hard-coded seed (`SampleSeed = 0x9E3779B1`), so the library is exactly reproducible build to build. Each persona gets a deterministic age, one of two pronoun sets (she/her, he/him) and one of 50 signature quirks (about 20 personas per quirk), so neighbouring entries still read as distinct people. Every persona has a unique id and name (`PersonaLibraryShapeTests` asserts both). There are no per-provider default personas: a bare LLM voter is a `VoterProfile` with empty `PersonalityMarkdown`.

`PersonaLibrary.Sample(count, rng?)` draws without replacement via a partial Fisher-Yates shuffle, so a panel built through `VoterFactory` never repeats a persona; pass a seeded `Random` for reproducible test panels. `PersonaLibrary.Profiles` embeds the library's latest psychometric scores in the package (`Resources/psychometric-profiles.json`), so consumers get profile-carrying personas with no external data source.

`VoterFactory.GenerateUniqueVoters(count, providerIds, fallbackProviderId = "claude", rng?)` spreads voters across every supplied provider at least once before doubling up, backfilling extra slots with the fallback provider. `VoterFactory.GenerateDiverseVoters(count, providerIds, profiles, fallbackProviderId, rng?)` instead does greedy farthest-point selection over the 15-dimensional OCEAN, HEXACO and DISC trait vector: it seeds on the persona farthest from the centroid, then repeatedly adds the remaining candidate farthest from everyone already chosen, so a small panel spans the trait space.

## Testing

`MindAttic.Legion.Tests/` is an NUnit 4 project with two tiers: an offline unit suite that runs on every `dotnet test`, and a live integration suite marked `[Explicit]` that runs only when filtered.

The unit suite covers, with no network calls:

- Vote tally correctness for every quorum, and quorum enforcement at threshold edges
- Persona injection (system-prompt wrapping)
- Provider failover and refill (one voter erroring does not break the vote; failed slots are reissued against surviving providers)
- Choice-option exact matching and scored-vote dimension aggregation
- Wire-format adapters per provider (Claude, OpenAI-compatible, Gemini, Cohere), including the temperature-omission and Claude 5+ thinking-disable invariants
- Resilience: retry, circuit breaker, fallback chain
- Health-check diagnosis classification (auth, quota, rate limit, offline, wrong reply)
- Live model discovery and JSON shape normalisation for every wire shape Legion has met
- `VotingConfiguration.ActiveProviderIds` gating: explicit keys, blank-key filtering, trusted-set whitelist, untrusted-provider rejection, shared-credential merging, dedup
- CLI helpers for `ask`, `tiers`, `poll` and `generate`: trust-list intersection, option snapping, auto-context caps, tier override maps, round-robin math, count splitting (with a property test that totals always equal N), list-marker and quote stripping, case-insensitive dedup
- Persona-library shape (exactly 1024 personas, unique ids and names), persona-store round-tripping, psychometric item parsing, scoring and model shape

```bash
dotnet test MindAttic.Legion.Tests/MindAttic.Legion.Tests.csproj
```

The live suite has four categories:

- `LiveApi` ([LiveApiIntegrationTests.cs](MindAttic.Legion.Tests/LiveApiIntegrationTests.cs)): per-provider-and-tier connectivity cells for the trusted × Low/Medium/High matrix, a whole-matrix sanity check via `TiersCommand.ProbeMatrixAsync`, an override-versus-catalog parity guard, and low-tier smoke tests of `ask`, `poll` and `generate`.
- `LiveKeys` ([LiveKeyValidationTests.cs](MindAttic.Legion.Tests/LiveKeyValidationTests.cs)): checks that the shared Vault keys authenticate against each provider they exist for, data-driven over the keys currently in the store.
- `LiveKeysTrusted`: the same, scoped to the trusted set.
- `LivePsychometrics` ([PsychometricLiveTests.cs](MindAttic.Legion.Tests/PsychometricLiveTests.cs)): end-to-end persona scoring through a real provider (about 5 Opus-class calls).

```bash
# All live tests in a category
dotnet test --filter "Category=LiveApi"
dotnet test --filter "Category=LiveKeys"
dotnet test --filter "Category=LiveKeysTrusted"
dotnet test --filter "Category=LivePsychometrics"

# One specific cell, useful when a single provider is flaky
dotnet test --filter "FullyQualifiedName~LiveApi.Claude_High"

# All Claude tiers
dotnet test --filter "FullyQualifiedName~LiveApi.Claude_"
```

The live tests cost real (small) API spend; wire them behind a manual `workflow_dispatch` if you want them in CI. The full offline filter used for pre-release verification:

```bash
dotnet test MindAttic.Legion.Tests -c Release --filter "Category!=LiveKeys&Category!=LiveKeysTrusted&Category!=LiveApi&Category!=LivePsychometrics"
```

## Building

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

Versioning is whole-number, major-only across every MindAttic project (inherited via [BIBLE section 5](docs/BIBLE.md#LEG-§5)): the package `<Version>` bumps by a whole major number per release (`N.0.0`) and nothing else.

The `docs/` canon has its own tooling, unrelated to the library build:

```powershell
powershell -File tools/codex.ps1 digest   # regenerate docs/BIBLE.digest.md after editing BIBLE §1/§3/§5/§9 or docs/AMENDMENTS.md
powershell -File tools/codex.ps1 doctor   # validate IDs, links, front-matter, cited tests/paths, digest freshness; must exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-readme.ps1   # regenerate README.htm from this file
```

The project page is this README on GitHub; there is no web deploy.

## Project layout

```text
MindAttic.Legion/                    the library (net10.0, PackageId MindAttic.Legion)
  Models/                            Quorum, ModelTier, VoteRequest/Result, VoterProfile,
                                      VotingConfiguration, LegionConfig, ChatTurn, Persona(Detail),
                                      Psychometrics/ (OceanScores, HexacoScores, MbtiResult,
                                      EnneagramResult, DiscResult, PersonaDocument, PsychometricProfile)
  Providers/                         LlmVotingProvider: fans a vote across voters, resolves keys
  Services/                          LlmVotingService, LegionClient, LlmProviderCatalog,
                                      CircuitBreaker, LegionClientOptions,
                                      LlmHealthCheck, LlmHealthDiagnosis, LlmModelDiscovery,
                                      MindAtticCredentialStore, LlmProviderRuntimeConfiguration,
                                      PersonaLibrary, PersonaNames, VoterFactory, LegionJson,
                                      Psychometrics/ (PersonaStore, PsychometricInstruments,
                                      PsychometricScorer, LlmPsychometricAssessor, PsychometricVoteAnalysis)
  Resources/psychometric-profiles.json   embedded id → latest-profile map shipped with the package
MindAttic.Legion.Cli/                legion.exe host
  Program.cs, LegionCli.cs           entry point + health/ping/status/providers/models/personas/panel/vote
  AskCommand.cs, PollCommand.cs, GenerateCommand.cs, TiersCommand.cs, PsychometricsCommand.cs
MindAttic.Legion.Tests/              NUnit 4 tests (offline unit suite + explicit live suite)
docs/                                Codex canon: BIBLE.md, AMENDMENTS.md, USER_STORIES.md, rfc/,
                                      generated BIBLE.digest.md
tools/codex.ps1                      digest + doctor tooling for docs/
tools/build-readme.ps1               wrapper around the shared codex-standard README → HTML engine
```

## Glossary

- Panel: the set of active voters (provider × persona) in a vote.
- Quorum: the agreement threshold: Plurality (any winner), SimpleMajority (more than 50%), TwoThirds (at least 66.7%), Unanimous (100%).
- Voter: one `VoterProfile`: a provider id plus optional persona and optional key, model or max-tokens override.
- Persona: a markdown system-prompt worldview from the 1024-member `PersonaLibrary`.
- Tier: a `ModelTier` (Low to Highest) that the catalog maps to a concrete model per provider.
- Trusted set: the four providers (`claude`, `openai`, `gemini`, `deepseek`) that library voting and the CLI's decision commands restrict to by default; see [Trust tiers](#trust-tiers).
- Judge: the LLM that synthesizes free-form votes into a consensus (`JudgeProviderId`).
- Consensus and dissent: the agreed answer and the recorded minority positions.
- Circuit breaker: per-provider fast-fail after repeated failures, shared process-wide.
- `legion.json`: optional per-project config (voters, judge, models, apiKeys, maxConcurrency), auto-applied only by `legion vote`.
- Psychometric instrument: one of five trait batteries scored deterministically from in-character answers.

## Using Legion from other projects

- Wrap judgment calls in `DecideAsync`. If your code has a hard-coded branch that picks among options by heuristic, replace it with a Legion decision and pass the relevant context.
- Prefer Plurality for surfacing all viewpoints, SimpleMajority for routine decisions, TwoThirds or stricter for canon-affecting actions. Reach for Unanimous only when a single dissent really matters.
- Pass real context. A vote without context is a popularity contest; bundle the canon, prior chapters, schema or rubric the panel needs.
- Watch the cost dial. A panel of 5 means 5× tokens; use the `providerIds` overload to scope votes to 2 or 3 providers when you do not need the full panel.
- `QuorumReached == false` is a signal, not a failure: the panel saw a real ambiguity. Surface it to a human or escalate.
- Know which trusted list you are in; see [Trust tiers](#trust-tiers).

## Briefing another coding agent

To let another LLM-driven CLI (Claude Code, Codex, Cursor agent) reach for Legion instead of blocking on you, paste this block into its memory or system prompt, replacing the path with your build output:

```text
You now have a Legion panel for hard calls. Use it instead of blocking on me.

Legion is a multi-LLM CLI at:
  <your clone>\MindAttic.Legion.Cli\bin\Release\net10.0\legion.exe

The panel is HARDCODED to Claude (API key), ChatGPT, Gemini, DeepSeek: the four providers
I trust for autonomous decisions. No untrusted provider can ever be added, even via --providers
(untrusted ids get silently dropped). If a trusted provider is unreachable, Legion automatically
refills the empty slot with another working provider so the panel never falls below quorum.

CHOOSING THE COMMAND
  legion ask <q>        ONE decision, panel-voted, architect-framed.
                        Default tier: HIGH. Use for: architectural choices, breaking-change
                        tradeoffs, ambiguous specs.
  legion poll <q>       N independent voters, count-sorted distribution, plurality winner.
                        Default tier: LOW. Default count: 10. Use for "how does the panel split?"
  legion generate <p>   N distinct creative items, deduped, newline to stdout.
                        Default tier: MEDIUM. Default count: 10. Use for names, taglines, alternatives.
  legion tiers          Probe trusted four × Low/Medium/High = 12 cells. Connectivity only.
                        Use once before a critical session, or after a model deprecation.

WHEN TO CALL ask
Whenever you would otherwise pause to ask me:
  - An architectural choice (DI lifetime, library pick, schema shape, layering decision).
  - A breaking-change tradeoff (rename now vs. soft-deprecate, migrate now vs. shim).
  - An ambiguous spec where two reasonable readings exist and the next file depends on which is right.
  - Anything hard to reverse.
Don't call ask for mechanical edits, formatting, or things where you already know the answer:
each call costs a few seconds and four flagship-tier API requests. For cheap one-shot decisions, use --tier low.

HOW TO CALL ask
  legion.exe ask "<question>" [opts]
  - Choice (recommended): legion.exe ask "Pick the JSON serializer" --options "System.Text.Json,Newtonsoft.Json"
      → stdout = exactly one option, exit 0 on quorum.
  - Free-form: legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?"
      → stdout = the synthesized answer.
  - Audit: add --json to get votes, reasoning, confidence, dissent.
  - Strict consensus: add --quorum twothirds to fail closed (exit 1) if the panel splits.
  - Tier override: add --tier low|medium|high|higher|highest. Default is high.
  - MUST-ANSWER: add --must-answer when you cannot tolerate "no answer". Phase 2 retries with
    doubled budget and no auto-context; phase 3 falls back to a single-provider chain
    (claude → openai → gemini → deepseek) calling raw text.
Auto-context: by default ask reads CLAUDE.md, README.md, git status -s and git log --oneline -10
from cwd. Pass --no-auto-context for a clean prompt, or --context-file <path> to inject a file.

ask OUTPUT CONTRACT
  answer              exit 0   panel agrees, act on it
  best-guess answer   exit 1   panel split: re-ask with more context or escalate to me
  (empty)             exit 2   unhandled error (network, etc.)
With --must-answer, exit 0 also covers the recovery phases; stderr says which phase delivered.
stderr carries warnings only; never parse it for the answer.

poll, generate, tiers
  legion.exe poll "Should we ship today?" --options "yes,no,not-yet" --count 100 --tier low
  legion.exe generate "single-word hero-vibe character names" --count 100 > names.txt
  legion.exe tiers      # exit 0 if every cell is OK, 1 otherwise. Once per session is plenty.

--providers can only NARROW within the trusted four. Don't reach for it unless I ask.

If ask exits 1 (no quorum) WITHOUT --must-answer, don't silently pick its best-guess answer for a
structural decision: re-run with --json, summarize the disagreement, ask me. If you used
--must-answer and still got exit 1 or 2, the trusted panel is down: escalate to me, don't guess.
```

## Documentation

- [docs/BIBLE.md](docs/BIBLE.md): architecture canon and the Laws.
- [docs/BIBLE.digest.md](docs/BIBLE.digest.md): generated digest of the BIBLE.
- [docs/AMENDMENTS.md](docs/AMENDMENTS.md): pending decisions not yet folded into the bible (normally empty).
- [docs/USER_STORIES.md](docs/USER_STORIES.md): test-cited user stories.
- [AGENTS.md](AGENTS.md): instructions for coding agents working in this repo.

## License

MIT; see [LICENSE](LICENSE). Copyright (c) 2026 MindAttic.

Part of [MindAttic](https://mindattic.com) — see more projects at [github.com/mindattic](https://github.com/mindattic). Related: [MindAttic.Vault](https://github.com/mindattic/MindAttic.Vault) (the shared credential store Legion reads keys from).
