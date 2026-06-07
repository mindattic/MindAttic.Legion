---
codex: 1
project: MindAttic.Legion
code: LEG
layer: bible
status: living
updated: 2026-06-07
---

# MindAttic.Legion — Project Bible

> Single source of truth for what MindAttic.Legion IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/run/call it; this says how to think about the system.

## 1. The one sentence {#LEG-§1}

MindAttic.Legion is a portable .NET 10 library (plus a `legion.exe` CLI) that turns a panel of frontier LLMs across eleven providers into one trustworthy answer — via voting, deciding, scoring, polling, generating, and persona-wearing, with quorum, failover, and confidence.

## 2. The product promise {#LEG-§2}

One LLM is one opinion; when a wrong answer is expensive you want a panel that votes, not a single model that bluffs. Legion delivers:

- **Multi-provider transport** behind one [`LegionClient`](#LEG-§4) — Claude, OpenAI, Gemini, DeepSeek, Mistral, xAI/Grok, Groq, Together, OpenRouter, Fireworks, Cohere.
- **Consensus voting** — call all active providers in parallel, tally answers, return the consensus with reasoning + dissent under a chosen [Quorum](#LEG-§9).
- **Decisions** — `DecideAsync(question, options)` picks one option with confidence and reasoning.
- **Scoring** — multi-dimensional rubric evaluation (1–10/dimension) with aggregate scores, failing dimensions, and improvement directives.
- **Personas** — a baked-in 1024-persona library (16 archetypes × 8 worldviews × 8 cultural backgrounds, enriched with age/pronouns/quirk); build trait-diverse panels or vote *as* a character.
- **Psychometric profiles** — score the persona library on five instruments (OCEAN/Big Five, HEXACO, MBTI-style, Enneagram-style, DISC-style); the model answers items in-character, scoring is deterministic in code, persisted as one JSON profile per persona.
- **Tiered model selection** — every provider exposes Low/Medium/High/Higher/Highest; tier names hide drifting model ids ([Law LEG-LAW-4](#LEG-LAW-4)).
- **Resilience** — per-provider retry with backoff and a process-wide [circuit breaker](#LEG-§4); `CallWithFallbackAsync` walks a provider chain until one succeeds.
- **CLI** — `legion.exe status | vote | ask | poll | generate | tiers | health | panel | psychometrics`, the same engine with no .NET host required.
- **Portable** — no dependency on any specific MindAttic app; register via DI, hand it keys (or rely on the shared Vault store), and you have the panel.

## 3. What it is NOT {#LEG-§3}

- **Not a single-model SDK.** Legion's value is the panel and the voting/quorum machinery; a one-provider call is a degenerate case, not the point.
- **Not a prompt/business-logic framework.** Apps keep their own prompts, structured-reply parsing, and domain logic; Legion owns only "send prompt → get text/consensus" plus transport, credentials, retry, and circuit breaking.
- **Not a model host or inference engine.** It calls vendor HTTP APIs; it does not run models locally.
- **Not coupled to any MindAttic app.** It must not take a dependency on StreetSamurai, the Ideas library, or any sibling project ([Law LEG-LAW-1](#LEG-LAW-1)).
- **Not a credential vault.** Credential storage/resolution is delegated to `MindAttic.Vault`; Legion only consumes it ([Law LEG-LAW-2](#LEG-LAW-2)).
- **Not a long-running service.** It is a library + CLI; quorum failure returns an empty consensus for the caller to escalate, it does not retry forever.

## 4. Architecture canon {#LEG-§4}

```
                          consumers
        ┌───────────────────────┬────────────────────────┐
   MindAttic apps (DI)     legion.exe CLI            sibling repos
        └───────────┬───────────┴────────────┬───────────┘
                    │                         │
              LlmVotingService          LegionCli (commands)
            (vote/decide/score/         ask/poll/generate/
             persona panels)            tiers/health/panel/
                    │                   psychometrics/status/vote
                    ▼                         │
              LlmVotingProvider ◄─────────────┘
                    │
                    ▼
                LegionClient ──────────── universal transport
          (endpoints, auth headers, request/response shape,
           model defaults, retry + backoff, CircuitBreaker)
                    │
        ┌───────────┴───────────────────────────────────┐
        ▼                ▼              ▼                 ▼
   LlmProviderCatalog  ModelTier   MindAtticCredentialStore  PersonaLibrary
   (11 providers,      (Low..       (facade over             (1024 personas
    tiered models)     Highest)      MindAttic.Vault)         + Profiles)
        │
        ▼
   11 vendor HTTP APIs (Anthropic / OpenAI / Google / DeepSeek / …)
```

### 4.1 Projects
- **`MindAttic.Legion`** — the library (`net10.0`, `PackageId` MindAttic.Legion). Depends on `MindAttic.Vault`, `Microsoft.Extensions.{DependencyInjection.Abstractions, Http, Logging.Abstractions}`. `InternalsVisibleTo` the test project. Path: `MindAttic.Legion/MindAttic.Legion.csproj`.
- **`MindAttic.Legion.Cli`** — `legion.exe` host; command classes in `MindAttic.Legion.Cli/` (`AskCommand`, `PollCommand`, `GenerateCommand`, `TiersCommand`, `PsychometricsCommand`, `LegionCli`, `Program`).
- **`MindAttic.Legion.Tests`** — NUnit 4 test project (`MindAttic.Legion.Tests/`).
- Solution: `MindAttic.Legion.slnx`. A separate Node landing-page renderer (`package.json`, `scripts/`, `index.htm`) is *not* part of the library/runtime; deployment of the landing page is handled by the sibling `MindAttic.Deploy` repo (see `.claude/skills/deploy/SKILL.md`).

### 4.2 Domain model (NOUNS)
- **Quorum** (`MindAttic.Legion/Models/Quorum.cs`) — `Plurality | SimpleMajority | TwoThirds | Unanimous`; `IsSatisfiedBy` uses exact integer arithmetic ([Law LEG-LAW-3](#LEG-LAW-3)).
- **ModelTier** (`Models/ModelTier.cs`) — `Low | Medium | High | Higher | Highest`.
- **VoteRequest / VoteResult / VotingResult** (`Models/VoteRequest.cs`, `Models/VoteResult.cs`) — the question/options/dimensions in, consensus/strength/rationales/dissent out.
- **DecisionResult** (`Models/DecisionResult.cs`) — `Choice`, `Reasoning`, `Confidence`, `QuorumReached`.
- **VoterProfile** (`Models/VoterProfile.cs`) — a voter: provider id, name, personality markdown, optional per-voter API-key override.
- **Persona / PersonaDetail** (`Models/Persona.cs`, `Models/PersonaDetail.cs`) — a library member with unique id, name, personality prompt.
- **VotingConfiguration** (`Models/VotingConfiguration.cs`) — API keys, allowed-provider set, shared-credentials toggle, judge provider, default personality.
- **LegionConfig / ChatTurn** (`Models/LegionConfig.cs`, `Models/ChatTurn.cs`) — config and multi-turn chat shape.
- **Psychometrics** (`Models/Psychometrics/`) — instrument items, answers, scored profiles.
- **LlmProviderInfo** (`Services/LlmProviderCatalog.cs`) — id, display name, vendor, default model, dashboard/keys URLs, available models, optional live-models endpoint.

### 4.3 Key services (VERBS)
- **`LlmVotingService`** (`Services/LlmVotingService.cs`) — `VoteAsync`, `DecideAsync`, `ScoreAsync`, `VoteWithPersonasAsync`, `VoteWithProfilesAsync`, `CreatePanel`.
- **`LegionClient`** (`Services/LegionClient.cs`) — `CallAsync`, `CallWithFallbackAsync`, multi-turn chat; the universal transport.
- **`LlmVotingProvider`** (`Providers/LlmVotingProvider.cs`) — fans a request across voters; resolves per-voter keys.
- **`LlmProviderCatalog`** (`Services/LlmProviderCatalog.cs`) — `All`, `GetTieredModel`, tier-override builders; the eleven-provider catalog.
- **`CircuitBreaker`** (`Services/CircuitBreaker.cs`) — process-static per-provider failure tracking; opens after threshold, fails fast.
- **`LlmHealthCheck` / `LlmHealthDiagnosis`** (`Services/LlmHealthCheck.cs`, `Services/LlmHealthDiagnosis.cs`) — probe keys/connectivity and classify failure modes with actionable URLs.
- **`LlmModelDiscovery`** (`Services/LlmModelDiscovery.cs`) — fetch a provider's live model list.
- **`PersonaLibrary` / `PersonaNames` / `VoterFactory`** (`Services/PersonaLibrary.cs`, `Services/PersonaNames.cs`, `Services/VoterFactory.cs`) — the 1024 personas, naming, and panel assembly (round-robin provider spread, sample without replacement).
- **`MindAtticCredentialStore`** (`Services/MindAtticCredentialStore.cs`) — backward-compatible facade over `MindAttic.Vault`.
- **`ServiceCollectionExtensions`** (`Services/ServiceCollectionExtensions.cs`) — `AddLLMVoting`, `AddLegionClient` DI wiring.
- **Psychometrics services** (`Services/Psychometrics/`) — administer instruments in-character and score deterministically.

## 5. The Laws {#LEG-§5}

These project laws are *in addition to* the house rules, which are INHERITED, not restated:
**[MindAttic.HouseRules.md](../../MindAttic.HouseRules.md)** (shared across every MindAttic repo — versioning, tooling, conventions). The house rules apply in full; only Legion-specific laws live below.

1. {#LEG-LAW-1} **Portability is non-negotiable.** The library takes no dependency on any specific MindAttic application. Allowed dependencies are the shared infrastructure packages (`MindAttic.Vault`) and `Microsoft.Extensions.*` only. *(Asserted by the namespace/portability note in `LlmVotingService.cs`.)*
2. {#LEG-LAW-2} **Credentials flow through `MindAttic.Vault`.** Legion never invents its own secret storage. Resolution order is per-voter override → `VotingConfiguration.ApiKeys` → shared Vault store (User Secrets / App Service settings / Key Vault, with the legacy `%APPDATA%\MindAttic\LLM` file as fallback). `MindAtticCredentialStore` is a compatibility facade only.
3. {#LEG-LAW-3} **Quorum thresholds are exact, computed with integer arithmetic.** `SimpleMajority` is strictly `agree*2 > total` (a 2-of-4 tie FAILS); `TwoThirds` is `agree*3 >= total*2` so a 2-of-3 panel clears it. Never approximate with a rounded float. *(verified by `SimpleMajority_RejectsExactTie`, `TwoThirds_IsSatisfiedBy_AdmitsTwoOfThree_RejectsOneOfTwo`, `TwoThirds_IsExactlyTwoThirds`.)*
4. {#LEG-LAW-4} **Callers request tiers, never raw model ids.** The catalog maps `ModelTier` → a concrete model per provider so a vendor model-id rotation never breaks a caller. `GetTieredModel` climbs down to the nearest available tier when a provider lacks the requested one.
5. {#LEG-LAW-5} **A provider is "active" only with a non-empty key AND membership in the allowed set.** The default allowed set is the trusted four (claude, openai, gemini, deepseek). Untrusted keys are filtered out of voting. *(verified by `ActiveProviderIds_*` tests.)*
6. {#LEG-LAW-6} **Quorum failure returns empty, it does not guess.** When quorum isn't reached, `QuorumReached == false` and `Consensus == ""`; escalation/retry is the caller's decision. Legion never silently downgrades the quorum.
7. {#LEG-LAW-7} **The breaker is shared and process-static.** "Claude is down" means the same thing to every `LegionClient` in the process; transient 5xx/429/network errors retry with backoff, then the per-provider breaker opens for a cooldown.
8. {#LEG-LAW-8} **Psychometric scoring is deterministic in code; the model only answers items.** Profiles are computed from in-character item responses, not asked-for directly, and persisted as one faithful JSON profile per persona.
9. {#LEG-LAW-9} **Personas are unique within a panel.** `PersonaLibrary` holds exactly 1024 personas with unique ids and names; panels sample WITHOUT replacement so no voter repeats inside a batch. *(verified by `All_PersonasHaveUniqueIds`, `All_PersonasHaveUniqueNames`.)*

## 6. Verified state {#LEG-§6}

Build: `dotnet build MindAttic.Legion.slnx -c Release` — ✅ **clean** (0 warnings, 0 errors; SDK 10.0.300, target `net10.0`; verified 2026-06-07).
Tests: `dotnet test MindAttic.Legion.Tests` (NUnit 4). Offline run ✅ **441 passed / 0 failed / 0 skipped** (verified 2026-06-07, ~6s). The suite separates **offline** tests (default) from **live** tests gated by category (`LiveKeys`, `LiveKeysTrusted`, `LiveApi`, `LivePsychometrics`) that require real provider keys + network. Offline run command:
`dotnet test MindAttic.Legion.Tests -c Release --filter "Category!=LiveKeys&Category!=LiveKeysTrusted&Category!=LiveApi&Category!=LivePsychometrics"`.

Proven-working units (offline, fakes via `TestSupport/FakeLlmHandlers.cs`): quorum arithmetic, provider catalog shape & tier overrides, active-provider resolution, circuit breaker open/reset, per-provider wire shapes (Claude/OpenAI/Gemini/Cohere headers & payloads), model-discovery extraction, health-check classification & actionable URLs, persona library uniqueness, voter round-robin distribution, psychometric instruments/answer-parsing/scoring, and the CLI command smoke/aggregation tests.

The live categories (real keys) cover end-to-end consensus, key validation, and live psychometrics; their status is environment-dependent and therefore **not asserted ✅ here** — see [USER_STORIES](#) statuses.

## 7. Active frontier {#LEG-§7}

- Design notes live in `docs/rfc/`. See `docs/rfc/0001-codex-documentation-standard.md` (the documentation standard itself).
- Backlog and epic status: `docs/USER_STORIES.md`.

## 8. Quality bar {#LEG-§8}

A feature is **done** only when:
1. It has NUnit coverage; offline behavior is proven with `TestSupport` fakes (no live keys required to prove the logic).
2. Anything touching a vendor wire shape has a wire-level test (headers + payload) under fakes.
3. It respects the Laws in §5 (portability, Vault credentials, exact quorum, tiers-not-ids, active-set, fail-empty, shared breaker).
4. Public API has XML doc comments; the README example for the feature compiles conceptually.
5. `dotnet build -c Release` is clean and the offline test filter passes.
6. `pwsh tools/codex.ps1 doctor` passes.

## 9. Glossary {#LEG-§9}

- **Panel** — the set of active voters (provider × persona) participating in a vote.
- **Quorum** — agreement threshold: `Plurality` (any winner) · `SimpleMajority` (>50%) · `TwoThirds` (≥66.7%) · `Unanimous` (100%).
- **Voter** — one `VoterProfile`: a provider id + optional persona + optional key override.
- **Persona** — a markdown system-prompt worldview from the 1024-member `PersonaLibrary`.
- **Tier** — a `ModelTier` (Low…Highest) the catalog maps to a concrete model per provider.
- **Trusted four** — claude, openai, gemini, deepseek; the default allowed/active set.
- **Judge** — the LLM that synthesizes free-form votes into a consensus (`JudgeProviderId`).
- **Consensus / dissent** — the agreed answer and the recorded minority positions.
- **Circuit breaker** — per-provider fast-fail after repeated failures, shared process-wide.
- **Psychometric instrument** — one of five trait batteries (OCEAN, HEXACO, MBTI-style, Enneagram-style, DISC-style) scored deterministically from in-character answers.
