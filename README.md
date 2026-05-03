# MindAttic.Legion

Multi-LLM consensus engine for any .NET app. One client to talk to every major LLM provider, plus a voting layer that turns a panel of LLMs into a single answer with quorum, reasoning, and confidence.

Portable: Legion has no dependency on any specific MindAttic project. Drop it into a `csproj`, register it via DI, give it API keys, and you have the panel.

---

## Why Legion

A single LLM is a single opinion. When the cost of a wrong answer is real — a story contradiction, a misclassified record, a bad route — you want a *panel* that votes, not one model that bluffs.

Legion is the panel:

- **Multi-provider transport** — Claude, OpenAI, Gemini, DeepSeek, Mistral, xAI, Groq, Together, OpenRouter, Fireworks, Cohere, Ollama, and LM Studio, all behind one client.
- **Voting** — call all configured providers in parallel, tally their answers, return the consensus with reasoning + dissent.
- **Decision-making** — `DecideAsync(question, options)` picks one option from a fixed list with confidence.
- **Scoring** — multi-dimensional rubric evaluation (1–10 per dimension), aggregate scores, weakest-dimension feedback, ready-to-inject improvement directives.
- **Personas** — every voter can wear a persona (a markdown system prompt). Use the bundled 1000-persona library, build a panel of N unique voices, or wrap a fictional character's psychology to vote *as* them.
- **CLI** — `legion.exe status`, `legion.exe vote`, `legion.exe health`, `legion.exe panel` — same engine, no .NET app required.

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

Configure cloud providers via `VotingConfiguration.ApiKeys`. A cloud provider is "active" when it has a non-empty API key. A local provider is active when you opt it in with `ModelOverrides`, `ApiKeys`, or a `providers.json` entry. `GetActiveProviderIds()` lists which providers are voting.

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
| `ollama` | Ollama | first discovered local model, fallback `llama3.2` | local runtime |
| `lmstudio` | LM Studio | first discovered local model, fallback `local-model` | local runtime |

Use `legion.exe providers` from the CLI for the live list and dashboard URLs.

To override the model for a specific provider:

```csharp
config.ModelOverrides["claude"] = "claude-sonnet-4-6";
```

Local runtimes do not need API keys. Legion looks for them at these defaults:

| Provider id | Default base URL | Environment override |
|---|---|---|
| `ollama` | `http://localhost:11434` | `MINDATTIC_LEGION_OLLAMA_BASE_URL` |
| `lmstudio` | `http://localhost:1234/v1` | `MINDATTIC_LEGION_LMSTUDIO_BASE_URL` |

You can also put local settings in `%APPDATA%/MindAttic/LLM/providers.json`:

```json
{
  "ollama": {
    "type": "local",
    "model": "llama3.2",
    "baseUrl": "http://localhost:11434"
  },
  "lmstudio": {
    "type": "local",
    "model": "local-qwen",
    "baseUrl": "http://localhost:1234/v1"
  }
}
```

To restrict voting to a subset:

```csharp
var r = await voting.VoteAsync(req, quorum, new[] { "claude", "openai" });
```

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
```

Exit codes:
- `0` — quorum reached
- `1` — quorum not reached
- `2` — pipeline error

The JSON shape on stdout matches `VotingResult`/`ScoredVotingResult`, so other languages can parse it directly.

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

## Contributing notes (for sibling repos using Legion)

- **Always wrap judgment calls in `DecideAsync`.** If your code has a hard-coded branch that picks among options based on heuristics, replace the heuristic with a Legion decision and pass the relevant context. The panel is cheap; bad decisions are expensive.
- **Prefer `Plurality` for surfacing all viewpoints, `SimpleMajority` for routine decisions, `TwoThirds`+ for canon-affecting actions.** Don't reach for `Unanimous` unless the cost of a single dissent is real.
- **Pass real context.** A vote without context is just a popularity contest. Bundle the canon, the prior chapters, the schema, the rubric — whatever the panel needs to be informed.
- **Watch the cost dial.** A panel of 5 means 5× tokens. Use `providerIds` overload to scope votes to 2–3 providers when you don't need the full panel.
- **`QuorumReached == false` is a signal, not a failure.** It means the panel saw a real ambiguity. Surface it to a human or escalate the question.
