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
- **Tiered model selection** — every provider exposes a Low / Medium / High / Higher / Highest tier (e.g. claude → haiku / sonnet / opus). Pick the tier that fits the work: Low for bulk polls, Medium for creative generation, High for architectural decisions. The catalog hides specific model versions behind tier names so a model-id rotation doesn't break callers.
- **Autonomous architectural decisions** — `legion.exe ask` is purpose-built for the loop where another coding CLI (Claude Code, Codex) blocks on a user prompt: an outer monitor pipes the question to `ask`, the panel deliberates on the High tier (claude-opus-4-7, gpt-4.1, gemini-2.5-pro, deepseek-reasoner), and the bare answer flows back to the blocked CLI. Architect-framed voters, auto-pulls `CLAUDE.md`/`README`/git as context, default panel is the four-provider trust list with automatic refill on outages.
- **Bulk distribution sampling** — `legion.exe poll` round-robins N voters across the trusted four at a chosen tier (Low by default), reports a count-sorted distribution + plurality winner. The cheap fast tool for "how does the panel split on this?"
- **Bulk creative generation** — `legion.exe generate` fans out one batched call per provider asking for that provider's share of N items, deduplicates across the merge, and emits newline-separated results to stdout. Built for `legion generate "100 hero-vibe names" | head -25 > names.txt`.
- **On-demand connectivity probe** — `legion.exe tiers` probes every (trusted-provider, tier) cell with a tiny prompt and prints a matrix. Use it before a critical session to confirm the panel is healthy.
- **CLI** — `legion.exe status`, `legion.exe vote`, `legion.exe ask`, `legion.exe poll`, `legion.exe generate`, `legion.exe tiers`, `legion.exe health`, `legion.exe panel` — same engine, no .NET app required.

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
services.AddSingleton<LlmVotingService>();
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
| `claude` | Anthropic | claude-sonnet-4-6 | console.anthropic.com |
| `openai` | OpenAI | gpt-4.1-mini | platform.openai.com |
| `gemini` | Google | gemini-2.5-flash | aistudio.google.com |
| `deepseek` | DeepSeek | deepseek-chat | platform.deepseek.com |
| `mistral` | Mistral AI | mistral-large-latest | console.mistral.ai |
| `xai` | xAI | grok-3-mini-fast | console.x.ai |
| `groq` | Groq | llama-3.3-70b-versatile | console.groq.com |
| `together` | Together AI | (varies) | api.together.xyz |
| `openrouter` | OpenRouter | (varies) | openrouter.ai |
| `fireworks` | Fireworks AI | (varies) | fireworks.ai |
| `cohere` | Cohere | command-r-plus | dashboard.cohere.com |

Default model is what each provider falls back to when **no** model override is supplied. For tier-aware selection (Low / Medium / High / Higher / Highest), use `LlmProviderCatalog.GetTieredModel(providerId, ModelTier)` — see the next section.

Use `legion.exe providers` from the CLI for the live list and dashboard URLs.

To override the model for a specific provider:

```csharp
config.ModelOverrides["claude"] = "claude-opus-4-7";
```

To restrict voting to a subset:

```csharp
var r = await voting.VoteAsync(req, quorum, new[] { "claude", "openai" });
```

---

## Tier system

`ModelTier` is the Legion abstraction for "the cheap one" / "the strong one" without naming model versions that drift. Every trusted provider has its tiers mapped in `LlmProviderCatalog`:

| Tier | claude | openai | gemini | deepseek |
|---|---|---|---|---|
| `Low` | claude-haiku-4-5-20251001 | gpt-4.1-nano | gemini-2.5-flash-lite | deepseek-chat |
| `Medium` | claude-sonnet-4-6 | gpt-4.1-mini | gemini-2.5-flash | deepseek-chat |
| `High` | claude-opus-4-7 | gpt-4.1 | gemini-2.5-pro | deepseek-reasoner |
| `Higher` | claude-opus-4-7\[1m] | o1 | gemini-2.5-pro | deepseek-reasoner |
| `Highest` | claude-opus-4-7\[1m] | o1 | gemini-2.5-pro | deepseek-reasoner |

When a tier isn't directly mapped for a provider, `GetTieredModel` walks **down** the ladder (Highest → Higher → ... → Low) and returns the closest available model — so asking for Highest against a 3-tier provider gives you High, not null. Asking for a tier that's lower than every entry walks back **up** for symmetry.

```csharp
// Pick the strong reasoning model for an architectural decision:
var arch = LlmProviderCatalog.GetTieredModel("claude", ModelTier.High);
// → "claude-opus-4-7"

// Pick the cheap one for a 100-voter poll:
var bulk = LlmProviderCatalog.GetTieredModel("claude", ModelTier.Low);
// → "claude-haiku-4-5-20251001"
```

The CLI commands embed sensible defaults: `legion ask` defaults to High (architecture wants flagship reasoning), `legion poll` defaults to Low (bulk distribution wants cheap), `legion generate` defaults to Medium (creative balance). All accept `--tier <t>` to override.

To pin a whole panel to a tier inside the .NET API:

```csharp
config.ModelOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["claude"]   = LlmProviderCatalog.GetTieredModel("claude",   ModelTier.High)!,
    ["openai"]   = LlmProviderCatalog.GetTieredModel("openai",   ModelTier.High)!,
    ["gemini"]   = LlmProviderCatalog.GetTieredModel("gemini",   ModelTier.High)!,
    ["deepseek"] = LlmProviderCatalog.GetTieredModel("deepseek", ModelTier.High)!,
};
```

`AskCommand.BuildTierModelOverrides(ModelTier)` is the canonical helper for this in CLI code — copy its shape if you're building a similar command.

### Default trust list

`VotingConfiguration.AllowedProviderIds` defaults to the four first-party frontier providers: **`claude`, `openai`, `gemini`, `deepseek`**. Every other provider is keyable and probeable but excluded from the default voting panel — they don't get a seat unless you explicitly add them.

When a trusted provider errors mid-vote (network blip, rate limit, transient 5xx), `LlmVotingService.RefillFailedVotersAsync` automatically dispatches a fresh call to one of the *surviving* trusted providers (round-robin), so the panel never shrinks below quorum size. A failed Gemini slot becomes a second Claude or DeepSeek call rather than a missing vote. Refilled slots intentionally drop any persona overlay so a surviving voter doesn't get to "vote twice as the same character."

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

# Health & connectivity
legion.exe health                 # probe every provider's DefaultModel with a hello-world
legion.exe ping claude            # one-provider probe (DefaultModel)
legion.exe tiers                  # probe trusted-four × Low/Medium/High = 12 cells

# Vote (returns JSON on stdout) — one-voter-per-provider consensus
legion.exe vote "Is the sky blue today?" \
    --context "Cloud cover is 100%." \
    --quorum simplemajority \
    --options yes,no,unclear \
    --max-tokens 256 \
    --no-narrative

# Ask (architect-framed; stdout = bare answer, --json for full audit)
# Default tier = High (Opus / GPT-4.1 / Gemini 2.5 Pro / DeepSeek Reasoner).
legion.exe ask "Which DI lifetime for the new HttpClient wrapper?" \
    --options "Singleton,Scoped,Transient"
# → Singleton

legion.exe ask "Best way to stream LLM tokens through SignalR without buffering?" --json

# Override tier for cheaper one-shot decisions
legion.exe ask "Use tabs or spaces?" --options "tabs,spaces" --tier low

# Poll — N voters round-robined across providers at a chosen tier
legion.exe poll "Pick 1, 2, or 3" --options "1,2,3" --count 100 --tier low
# → distribution table + plurality winner

# Generate — N distinct creative items, deduped, newline-separated
legion.exe generate "100 hero-vibe character names" --count 100 --tier medium
# → newline list to stdout (pipe into head/shuf/grep/file)
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
| `--tier <t>` | `low` \| `medium` \| `high` \| `higher` \| `highest` (default `high`). High = flagship reasoning (Opus 4.7 / GPT-4.1 / Gemini 2.5 Pro / DeepSeek Reasoner) — the right tool for architectural decisions. Drop tier for cheaper one-shot calls. |
| `--must-answer` | On 0/N voter failure, retry with doubled budget and no auto-context; on second failure, fall back to a single-provider chain (claude → openai → gemini → deepseek) until one replies. Use when the calling agent can't tolerate "no answer". |
| `--json` | Emit full vote audit JSON instead of bare answer. |

Output contract:

| stdout | exit | meaning |
|---|---|---|
| answer | `0` | panel agrees, act on it |
| best-guess answer | `1` | panel split — re-ask with more context or escalate |
| (empty) | `2` | unhandled error (network, etc.) |

stderr carries warnings; never parse it.

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
legion.exe poll "Severity?" --options "low,medium,high,critical" --count 50 --tier high --providers claude,openai
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
legion.exe generate "function names for queue.dequeue helpers" --count 20 --providers claude --temperature 0.4

# Pipe through standard tools
legion.exe generate "fictional country names" --count 50 | shuf | head -10
```

Exit codes:
- `0` — at least one item was produced
- `1` — every provider's batch errored (or usage error)

### `legion.exe tiers` — connectivity probe across the tier matrix

`tiers` answers "is the panel ready to vote on High right now?" without spinning up a real `ask` or `vote`. It probes every (trusted-provider, tier) cell with a tiny "reply OK" prompt and prints a connectivity table, defaulting to the trusted four × Low/Medium/High = 12 calls. Distinct from `legion health`, which only probes per-provider DefaultModel, missing tier-mapping breakage.

```bash
legion.exe tiers [opts]
```

| Option | Meaning |
|---|---|
| `--providers a,b,c` | Narrow within the trusted set (untrusted ids dropped). |
| `--tiers low,medium,high` | Narrow the tier sweep. Default: `low,medium,high`. |
| `--all-tiers` | Shorthand for all five tiers (Low, Medium, High, Higher, Highest). |
| `--max-tokens N` | Token budget per probe (default 400 — large enough for thinking models like gemini-2.5-pro to actually emit text after reasoning). |
| `--timeout S` | Per-probe timeout in seconds (default 45). |
| `--json` | Emit JSON record (one entry per probe + summary) instead of a table. |

Output is a one-row-per-probe table:

```
PROVIDER   TIER     MODEL                            STATUS  TIME     DETAIL
────────────────────────────────────────────────────────────────────────────
claude     Low      claude-haiku-4-5-20251001        OK      2600ms   OK
claude     Medium   claude-sonnet-4-6                OK      999ms    OK
claude     High     claude-opus-4-7                  OK      1404ms   OK
...
summary: 12/12 ok
```

Exit codes:
- `0` — every probe succeeded
- `1` — at least one probe failed

Use it before a critical session, after a model-id rotation, or as a lightweight CI smoke-test (paid, so wire it behind a manual workflow_dispatch).

---

## Architecture

```
┌─ Your app
│   └─ LlmVotingService    public API: VoteAsync / DecideAsync / ScoreAsync
│         └─ VoterFactory  builds VoterProfile lists (CreatePanel, personas)
│         └─ LlmVotingProvider
│               └─ LegionClient   universal LLM transport
│                     ├─ Claude wire shape
│                     ├─ OpenAI wire shape
│                     ├─ Gemini wire shape
│                     └─ ... (one adapter per provider)
└─ MindAtticCredentialStore (optional shared keyring at %APPDATA%/MindAttic/LLM/)
```

`LegionClient` owns the socket pool, retry policy, and circuit breaker. `LlmVotingProvider` adds vote-specific shaping. `LlmVotingService` is the public API — you almost never need to touch the lower layers.

---

## Testing

`MindAttic.Legion.Tests/` is a two-tier suite: a **unit suite** (337 tests, runs on every `dotnet test`, no network) and a **live integration suite** (17 explicit tests, runs only when filtered).

### Unit suite (337 tests)

Covers, with no network calls:

- Vote tally correctness (plurality, simple-majority, two-thirds, unanimous) and quorum enforcement at threshold edges
- Persona injection (system-prompt wrapping)
- Provider failover and refill (one voter erroring doesn't break the vote; failed slots are reissued against the surviving providers)
- Choice-option exact-match matching
- Scored-vote dimension aggregation
- Wire-format adapters per provider (Claude / OpenAI / Gemini / Cohere / OpenAI-compatible) — including the **Opus 4.7 temperature-omission** invariant
- Resilience policy: retry / circuit-breaker / fallback-chain
- Health-check diagnosis classification (auth / quota / rate-limit / offline / wrong-reply)
- Live model discovery + JSON shape normalization (every wire shape Legion has met: OpenAI `data[]`, Gemini `models[]` with `models/` prefix trim, Cohere/Anthropic variants, bare arrays, `model_id` legacy, mixed-type arrays, malformed JSON soft-failure)
- `VotingConfiguration.ActiveProviderIds` gating: explicit keys, blank-key filtering, default trusted-set whitelist, untrusted-provider rejection, shared-credential merging, dedup
- `AskCommand` helpers: trust-list intersection, choice-mode option snapping, auto-context assembly + caps, architect-prompt heuristics, help-flag recognition, **tier override map** (`BuildTierModelOverrides` Low/Medium/High/Higher), default tier pin (High)
- `TiersCommand` helpers: trust-list parity with ask, default tier sweep (Low/Medium/High), provider resolution, table truncation
- `PollCommand` helpers: round-robin assignment math (`AssignRoundRobin` at counts 8, 10, 100), tier model resolution per assignment, case-insensitive distribution aggregation, off-ballot handling
- `GenerateCommand` helpers: count-splitting math (with a property test that totals always equal N), every list-marker shape (`1.`, `1)`, `- `, `* `, `• `), every quote variant including curly, order preservation, case-insensitive dedup with first-seen-wins

Run from the repo root:

```bash
dotnet test MindAttic.Legion.Tests/MindAttic.Legion.Tests.csproj
```

Typical wall time: ~2s.

### Live integration suite (17 explicit tests)

`LiveApiIntegrationTests.cs` is the .NET equivalent of a Cypress / Playwright suite for this CLI: end-to-end tests that hit the **real** trusted-provider APIs. Marked `[Explicit]` at the fixture level so they do **not** run on normal `dotnet test` invocations (no surprise spend on CI). Run on demand to verify wire-shape, tier mapping, and end-to-end command behavior across providers.

Coverage:

- 12 per-(provider, tier) connectivity tests, one per cell of the trusted × Low/Medium/High matrix. A failure points at the exact cell.
- 1 whole-matrix sanity check using `TiersCommand.ProbeMatrixAsync` — the "panel is healthy" assertion in one line.
- 1 override-vs-catalog parity guard — pins that `BuildHighTierModelOverrides` matches `LlmProviderCatalog.GetTieredModel(..., High)` for every trusted provider.
- 3 end-to-end smoke tests of `legion ask`, `legion poll`, `legion generate` at Low tier with tiny budgets.

Run on demand:

```bash
# Whole live suite
dotnet test --filter "Category=LiveApi"

# One specific cell — useful when a single provider is flaky
dotnet test --filter "FullyQualifiedName~LiveApi.Claude_High"

# All Claude tiers
dotnet test --filter "FullyQualifiedName~LiveApi.Claude_"
```

Cost is small (~12 tiny probes + 3 small smoke tests on Low tier) but real — wire it behind a manual GitHub Actions `workflow_dispatch` if you want it in CI without paying every run.

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

Legion is a multi-LLM CLI at:
  D:\Projects\MindAttic\MindAttic.Legion\MindAttic.Legion.Cli\bin\Release\net10.0\legion.exe

The panel is HARDCODED to Claude, ChatGPT, Gemini, DeepSeek — the four providers I trust for autonomous decisions. No untrusted provider can ever be added, even via --providers (untrusted ids get silently dropped). If a trusted provider is unreachable, Legion automatically refills the empty slot with another working provider so the panel never falls below quorum.

CHOOSING THE COMMAND
You have four primary commands. Pick by what shape of answer you need:

  legion ask <q>        ONE decision, panel-voted, architect-framed.
                        Default tier: HIGH (Opus 4.7 / GPT-4.1 / Gemini 2.5 Pro / DeepSeek Reasoner).
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
  - Tier override: add --tier low|medium|high|higher|highest. Default is high (architecture). Drop tier for cheap one-shot decisions where Opus is overkill.
  - MUST-ANSWER: add --must-answer when you absolutely cannot tolerate "no answer". Phase-2 retry doubles budget and drops auto-context; phase-3 falls back to a single-provider chain (claude → openai → gemini → deepseek) calling raw text. Always emits an answer if any one provider is reachable.

Auto-context: by default `ask` reads CLAUDE.md, README.md, and `git status -s` / `git log --oneline -10` from cwd and prepends them so voters know the project. Pass --no-auto-context for a clean prompt, or --context-file <path> to inject a specific file (e.g. the file you're about to edit).

`ask` OUTPUT CONTRACT
  stdout              | exit | meaning
  ------------------- | ---- | -------------------------------------------------------
  answer              | 0    | panel agrees, act on it
  best-guess answer   | 1    | panel split — re-ask with more context or escalate to me
  (empty)             | 2    | unhandled error (network, etc.)

With --must-answer, exit 0 also covers the recovery cases (phase-2 retry, phase-3 single-provider chain). stderr will tell you which phase delivered ("ask: recovered in phase 3 via claude") — log that line if you want a record of the degraded path. stderr carries warnings only; never parse it for the answer.

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

--providers exists but you almost never need it: it can only NARROW within the trusted four (e.g. --providers claude,openai). Passing untrusted ids is harmless (they're dropped) but pointless. Don't reach for it unless I specifically ask you to scope a panel.

If `legion ask` exits 1 (no quorum) WITHOUT --must-answer, don't silently pick its best-guess answer for a structural decision — surface the dissent (re-run with --json, summarize the disagreement, ask me). If you used --must-answer and still got exit 1 or 2, the trusted panel is genuinely down — escalate to me, don't guess.
```

---

## Contributing notes (for sibling repos using Legion)

- **Always wrap judgment calls in `DecideAsync`.** If your code has a hard-coded branch that picks among options based on heuristics, replace the heuristic with a Legion decision and pass the relevant context. The panel is cheap; bad decisions are expensive.
- **Prefer `Plurality` for surfacing all viewpoints, `SimpleMajority` for routine decisions, `TwoThirds`+ for canon-affecting actions.** Don't reach for `Unanimous` unless the cost of a single dissent is real.
- **Pass real context.** A vote without context is just a popularity contest. Bundle the canon, the prior chapters, the schema, the rubric — whatever the panel needs to be informed.
- **Watch the cost dial.** A panel of 5 means 5× tokens. Use `providerIds` overload to scope votes to 2–3 providers when you don't need the full panel.
- **`QuorumReached == false` is a signal, not a failure.** It means the panel saw a real ambiguity. Surface it to a human or escalate the question.
