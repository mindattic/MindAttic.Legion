---
codex: 1
project: MindAttic.Legion
code: M.LEGN
layer: stories
status: living
updated: 2026-10-03
---
<!-- GENERATED from MindAttic.Workflow/data/workflow.db by `workflow db render`. Do not edit: this file is rewritten from the database; change stories and tickets with the workflow CLI. -->

# MindAttic.Legion — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned. Every ✅ cites the test that proves it.
> "Verified" here means an **offline** NUnit test (fakes via `TestSupport/FakeLlmHandlers.cs`) proves the logic without live keys, unless noted. Stories that can only be proven with live provider keys are marked 🟡 (logic proven offline; end-to-end unproven in this environment).

## Epic A — Consensus voting

- **M.LEGN-US-A1 ✅** As an integrator, I can ask a panel a question under a quorum and get a consensus or an explicit "no quorum", so I never act on a split panel. *Given a free-form question, When a single voter answers under a loose quorum, Then quorum is reached; When strict quorum can't be met, Then `QuorumReached==false`.* *(verified by `VoteAsync_FreeForm_SingleVoter_ReachesAnyQuorum`, `VoteAsync_FreeForm_NoNarrativeSynthesis_FailsStrictQuorum`, `VoteAsync_NoVoters_ReturnsQuorumNotReached`.)*
- **M.LEGN-US-A2 ✅** As an integrator, I can rely on exact quorum thresholds, so a 2-of-4 tie never counts as a majority and a 2-of-3 always clears two-thirds. *Given vote tallies, When evaluated, Then integer arithmetic decides the boundary exactly.* *(verified by `SimpleMajority_RejectsExactTie`, `TwoThirds_IsSatisfiedBy_AdmitsTwoOfThree_RejectsOneOfTwo`, `Plurality_IsSatisfiedBy_NeedsAtLeastOne`, `Unanimous_IsSatisfiedBy_NeedsEveryVoter`, `IsSatisfiedBy_ZeroTotal_AlwaysFalse`.)* (See [M.LEGN-LAW-3](BIBLE.md#M.LEGN-LAW-3).)
- **M.LEGN-US-A3 🟡** As an integrator, I can run a real multi-provider vote end-to-end against live keys. *Logic proven offline; live path is gated under category `LiveApi` (`LiveApiIntegrationTests`) and not asserted here without keys.*

## Epic B — Decisions & scoring

- **M.LEGN-US-B1 🟡** As an automated workflow, I can call `DecideAsync(question, options)` to pick one option with confidence and reasoning, so my code doesn't hard-code a branch. *Logic exercised via the voting-service tests; dedicated decision assertions are 🟡 pending a named `Decide*` offline test.*
- **M.LEGN-US-B2 🟡** As a reviewer, I can score content across rubric dimensions (1–10) and receive failing dimensions + improvement directives. *Exercised through `LlmVotingServiceTests`; mark ✅ once a dedicated `Score*` offline test is named.*

## Epic C — Providers, tiers & catalog

- **M.LEGN-US-C1 ✅** As an integrator, I can request a model by tier and survive vendor model-id rotation, so my callers never name a drifting model id. *(verified by `BuildHighTierModelOverrides_*`, `BuildTierModelOverrides_*`, `BlankModel_FallsBackToProviderDefaultModel`.)* (See [M.LEGN-LAW-4](BIBLE.md#M.LEGN-LAW-4).)
- **M.LEGN-US-C2 ✅** As an integrator, I get a stable provider catalog with the expected provider count and known ids. *(verified by `All_HasExpectedProviderCount`, `All_KnownIds`.)*
- **M.LEGN-US-C3 ✅** As an integrator, a provider counts as active only with a non-empty key and membership in the allowed set (default: the nine-provider trusted set in `LlmProviderCatalog.DefaultIds`). *(verified by `ActiveProviderIds_DefaultAllowedSet_IsTheTrustedProviders`, `ActiveProviderIds_BlankKey_IsNotActive`, `ActiveProviderIds_KeyForUntrustedProvider_IsFilteredOut`, `ActiveProviderIds_NoKeys_ReturnsEmpty`.)* (See [M.LEGN-LAW-5](BIBLE.md#M.LEGN-LAW-5).)
- **M.LEGN-US-C4 ✅** As an operator, I can discover a provider's live model list and parse vendor-specific shapes. *(verified by `LlmModelDiscoveryExtractTests` / `LlmModelDiscoveryTests`, e.g. `AnthropicShape_DataArrayWithIdProperty`, `CohereShape_ModelsArrayWithNameProperty`, `BareArray_OfStrings_Works`.)*

## Epic D — Transport & resilience

- **M.LEGN-US-D1 ✅** As an integrator, `LegionClient.CallAsync` dispatches the correct wire shape per vendor (headers + payload), so each provider is called correctly. *(verified by `Claude_SendsXApiKeyHeader_AndAnthropicVersion`, `Claude_SystemPrompt_PostedAsTopLevelSystemField`, `CallAsync_ExplicitKey_DispatchesOpenAiShape`, `CallAsync_ExplicitKey_DispatchesGeminiShape`, `CallAsync_ExplicitKey_DispatchesCohereShape`, `Cohere_HitsV2Endpoint_AndExtractsMessageContent`.)*
- **M.LEGN-US-D2 ✅** As an integrator, missing/unknown providers fail clearly and HTTP errors propagate. *(verified by `CallAsync_MissingKey_Throws`, `CallAsync_UnknownProvider_Throws`, `CallAsync_HttpError_Propagates`, `CallAsync_BlankModel_FallsBackToDefault`.)*
- **M.LEGN-US-D3 ✅** As an integrator, a sick provider trips a shared circuit breaker so calls fail fast and reset on recovery. *(verified by `CircuitBreaker_OpensAfterThreshold`, `CircuitBreaker_ResetsOnSuccess`, `CircuitBreakerOpenException_IsCircuitOpen`.)* (See [M.LEGN-LAW-7](BIBLE.md#M.LEGN-LAW-7).)
- **M.LEGN-US-D4 ✅** As an operator, a failed key gives an actionable diagnosis pointing at the right dashboard/keys URL. *(verified by `ActionableDiagnosis_LinksToCorrectUrl`, `AuthInvalid_TellsUserToGenerateNewKey`, `CircuitOpen_TellsUserToUseDifferentProvider`, `CheckOneAsync_*` health-check tests.)*
- **M.LEGN-US-D5 ✅** As an integrator, transient transport faults unwrap and classify correctly. *(verified by `AggregateException_UnwrapsToFirstInner`, `ArgumentException_IsBadRequest`, and `ResilienceTests`.)*

## Epic E — Personas & panels

- **M.LEGN-US-E1 ✅** As an integrator, I get exactly 1024 personas with unique ids and names. *(verified by `All_PersonasHaveUniqueIds`, `All_PersonasHaveUniqueNames`, `PersonaLibraryShapeTests`.)* (See [M.LEGN-LAW-9](BIBLE.md#M.LEGN-LAW-9).)
- **M.LEGN-US-E2 ✅** As an integrator, `VoterFactory` spreads N voters round-robin across providers with distinct personas, reproducibly with a seed. *(verified by `AssignRoundRobin_HundredVoters_FourProviders_TwentyFiveEach`, `AssignRoundRobin_OneProvider_AllVotersGoThere`, `AssignRoundRobin_PreservesOrderForReproducibility`, `AssignRoundRobin_ResolvesTierModelPerAssignment`.)*
- **M.LEGN-US-E3 ✅** As an integrator, persona profiles round-trip and align by index/id. *(verified by `AllDetails_AlignWithAllByIndexAndId`, `PersonaStoreTests`.)*

## Epic F — Psychometrics

- **M.LEGN-US-F1 ✅** As an analyst, the five instruments parse in-character answers and score deterministically in code. *(verified by `PsychometricInstrumentsTests`, `PsychometricAnswerParsingTests`, `PsychometricScorerTests`, `PsychometricModelTests`.)* (See [M.LEGN-LAW-8](BIBLE.md#M.LEGN-LAW-8).)
- **M.LEGN-US-F2 🟡** As an analyst, I can score the whole library against live models. *Gated under category `LivePsychometrics` (`PsychometricLiveTests`); not asserted here without keys.*

## Epic G — CLI (`legion.exe`)

- **M.LEGN-US-G1 ✅** As a shell/CI user, `legion.exe ask` returns a consensus and `poll` returns a sorted distribution + plurality winner. *(verified by `Ask_SmokeTest_ReturnsConsensusAtLowTier`, `PollCommandTests` aggregation tests `Aggregate_SortsByDescendingCount`, `Aggregate_GroupsCaseInsensitively`, `Aggregate_SkipsFailedAndEmptyOutcomes`, `Aggregate_AllFailed_ReturnsEmpty`.)*
- **M.LEGN-US-G2 ✅** As a shell user, `ask` auto-collects `CLAUDE.md`/README context and frames voters as architects. *(verified by `CollectAutoContextAsync_IncludesClaudeMdAndReadmeWhenPresent`, `CollectAutoContextAsync_NoFiles_ReturnsEmpty`, `BuildArchitectFraming_ContainsTheFiveHeuristics`.)*
- **M.LEGN-US-G3 ✅** As a shell user, `generate` fans out per-provider batches and `tiers` probes the (provider, tier) matrix. *(verified by `GenerateCommandTests`, `TiersCommandTests`.)*

## Epic H — Credentials (via MindAttic.Vault)

- **M.LEGN-US-H1 ✅** As an integrator, keys resolve through the documented order and the Vault-backed config store contributes when registered. *(verified by `MindAtticCredentialStoreConfigurationTests`, `VotingConfigurationTests`, `ActiveProviderIds_SharedCredentialsEnabled_StoreContributes`, `ActiveProviderIds_SharedCredentialsDisabled_OnlyExplicitKeysCount`.)* (See [M.LEGN-LAW-2](BIBLE.md#M.LEGN-LAW-2).)

## Priority backlog

1. **M.LEGN-US-B1** — add a dedicated offline `Decide*` test and promote to ✅.
2. **M.LEGN-US-B2** — add a dedicated offline `Score*` rubric test and promote to ✅.
3. **M.LEGN-US-A3 / F2** — wire a CI secret-bearing job (or documented local run) so the live categories can be exercised and reported, promoting these from 🟡.

## Tickets
> Permanent log of every change and fix — this repo's ticket system. Rows are append-only: never
> delete or reword one; only move its Status (open → closed / wont-do) and fill in Ref. Status is
> a word, not a glyph, so tickets don't count toward the story index. Keep this section last.

| ID | Date | Type | Status | Summary | Ref |
|---|---|---|---|---|---|
| M.LEGN-T1 | 2026-10-12 | docs | closed | Start the permanent ticket log in USER_STORIES.md | — |
| M.LEGN-T2 | 2026-10-13 | fix | closed | Restore LlmProviderCatalog to live-verified model ids (all 13 providers) and expand the default trusted/voting set from 4 to 9 providers | M.LEGN-LAW-5, ActiveProviderIds_DefaultAllowedSet_IsTheTrustedProviders, AskCommandTests.TrustedProviderIds_MatchesCatalogDefaultSet |
| M.LEGN-T3 | 2026-10-14 | chore | closed | NUnit tests for LegionConfig (Models/LegionConfig.cs): LoadFromDirectory walks up parent dirs to find legion.json (stops after 12 levels, null when absent or path invalid), LoadFromFile tolerates comments/trailing commas/case-insensitive keys and returns null on malformed JSON, ApplyTo replaces AllowedProviderIds only when voters is non-empty (case-insensitive set), sets JudgeProviderId only when non-blank, merges models/apiKeys skipping blank keys or values, applies maxConcurrency; this is how every project declares its voter panel and the CLI vote command depends on it | MindAttic.Legion.Tests/LegionConfigTests.cs (cdea439) |
| M.LEGN-T4 | 2026-10-14 | chore | open | NUnit tests for LlmProviderRuntimeConfigurationResolver (Services/LlmProviderRuntimeConfiguration.cs): Get lowercases/trims the provider id, reads type/model/maxTokens from providers.json raw entries, ignores non-string type/model and non-number maxTokens, swallows malformed or non-object JSON without throwing, returns null fields for unknown providers, HasApiKey false for blank keys, GetModel returns the override; use a temp credential dir like MindAtticCredentialStoreConfigurationTests so the status board never crashes on a bad providers.json | — |
| M.LEGN-T5 | 2026-10-14 | chore | open | NUnit tests for LlmVotingService.DecideAsync and CreatePanel: DecideAsync throws ArgumentException on null/empty options, defaults to Plurality, maps Consensus/NarrativeSummary/ConsensusStrength/QuorumReached/DissenterReasons into DecisionResult, Choice is empty when quorum fails, Options echo the ballot; CreatePanel spreads over GetActiveProviderIds and backfills the fallback provider; use FakeLlmHandlers, DecideAsync is the public decision API with zero coverage today | — |
| M.LEGN-T6 | 2026-10-14 | chore | open | NUnit tests for LlmVotingService multi-voter tallying and refill: TallyChoiceVote groups decisions case-insensitively, collects dissenter reasoning, blanks Consensus when quorum fails, all-failed returns EmptyResult with votes attached; RefillFailedVotersAsync replaces error slots round-robin with a different surviving provider (same provider only when it is the only one), never uses providers outside AllowedProviderIds, strips the persona, keeps the original error if the refill also fails; free-form judge path clamps agreement_count to 0..N, falls back to first answer with agreement 1 when the judge reply is garbage; GetJudgeProviderId prefers configured judge with a key else first active else claude | — |
| M.LEGN-T7 | 2026-10-14 | chore | open | NUnit tests for LegionClient Claude thinking helpers: ClaudeModelDefaultsAdaptiveThinking (true for claude-sonnet-5*/claude-haiku-5* and higher majors, false for fable/mythos, opus, sonnet-4, null/blank) and ClaudeModelRequiresBetweenToolsThinking (true for sonnet/haiku 5-5 and later incl. dated suffixes and 6-x, false for bare claude-sonnet-5 or non-numeric segments), plus a wire test via FakeLlmHandlers asserting the Claude payload sends thinking type disabled vs between_tools vs omitted; a wrong answer here yields 400s or empty replies from new Claude models | — |
| M.LEGN-T8 | 2026-10-14 | chore | open | NUnit tests for LegionClient.CallWithDocumentAsync and ExtractDocumentText: argument validation (blank apiKey throws InvalidOperationException, empty bytes or blank prompt throw ArgumentException), blank model falls back to the claude default, PDF is sent as a base64 document block with the pdfs beta header, text/* is UTF-8 decoded with BOM stripped, DOCX reads word/document.xml with tags stripped (empty when missing), EPUB concatenates .xhtml/.html entries in name order, temperature omitted for models that deprecate it; build small zip fixtures in-memory and capture the request with a fake handler | — |
| M.LEGN-T9 | 2026-10-14 | chore | open | NUnit tests for CircuitBreaker (Services/CircuitBreaker.cs) as a unit: stays closed below threshold, opens at threshold and ThrowIfOpen throws CircuitBreakerOpenException carrying ProviderId and positive TimeUntilProbe, closes again after the cooldown elapses, provider ids are case-insensitive, RecordSuccess and Reset clear failure count and open state, unknown providers are never open, per-provider isolation, concurrent RecordFailure counts are exact; existing ResilienceTests only exercise it indirectly through LegionClient | — |
| M.LEGN-T10 | 2026-10-14 | chore | open | NUnit tests for LlmPsychometricAssessor.AssessAsync without a live API (fake HttpMessageHandler behind LlmVotingProvider): one call per PsychometricInstruments instrument at temperature 0 with the tier model pinned, persona markdown in the system prompt (generic prompt when blank), throws InvalidOperationException when fewer than half the items parse (refusal/prose/truncated reply) instead of persisting a midpoint profile, RawAnswers keyed by instrument, profile records provider/model/timestamp; currently only covered by the live PsychometricLiveTests | — |
| M.LEGN-T11 | 2026-10-14 | chore | open | NUnit tests for PsychometricsCommand (Cli/PsychometricsCommand.cs) against a temp --store dir: no args returns 1 and help returns 0, unknown subcommand returns 1, init seeds the store, score rejects untrusted --provider and missing API key with exit 1, show/history usage errors and missing persona return 1, show --json emits the profile, stats returns 1 when empty and aggregates MBTI/DISC/Enneagram counts and mean OCEAN/HEXACO, diff rejects non-integer run ids and runs with no shared personas and reports mean absolute drift and type-change percentages for seeded runs | — |
| M.LEGN-T12 | 2026-10-14 | chore | open | NUnit tests for LegionCli top-level dispatch (Cli/LegionCli.cs): no args/help prints usage and returns 0, unknown command returns 1, an exception inside a subcommand returns 2, models with no arg or unknown provider returns 1, personas rejects a non-integer count, panel rejects missing count and parses --diverse/--store/provider positionals (diverse with an empty store returns 1), status rejects bad --timeout, vote parses --options/--quorum/--max-tokens/--no-narrative, fails with 1 on unreadable --context-file and when no provider has a key, and applies a legion.json found above the cwd; capture Console output | — |
| M.LEGN-T13 | 2026-10-14 | chore | open | NUnit tests for ServiceCollectionExtensions DI registration: AddLLMVoting(config) and AddLLMVoting(factory) both resolve LlmVotingService, LlmVotingProvider, LegionClient and LlmHealthCheck from a built ServiceProvider, LlmHealthCheck and LegionClient are transient (not captured singletons), and the LegionClient key resolver prefers VotingConfiguration.ApiKeys, falls back to the shared store only when UseSharedCredentials is true, and returns null otherwise; AddLegionClient alone resolves LegionClient and LlmHealthCheck without voting types | — |
| M.LEGN-T14 | 2026-10-15 | chore | closed | Remove duplicate command copies (/deploy, /quickload, /quicksave) now centralized at the user level (~/.claude/commands) | M.FLOW-T14 |
| M.LEGN-T15 | 2026-10-16 | chore | closed | Tracked .githooks/commit-msg runs the shared MindAttic commit rule: every commit names an existing workflow ticket | M.FLOW agent-standard commit hook |
| M.LEGN-T16 | 2026-10-16 | chore | closed | install.ps1 adds this repo's 8 .prose/commands stubs for the shared global commands, so any LLM client finds them | mindattic-agent-standard/install.ps1 run 2026-10-16 |
| M.LEGN-T17 | 2026-10-16 | chore | open | Bug hunt: run an exhaustive /bughunt over the whole repo and fix every real bug found (each fix under its own fix ticket), then hunt again; close this only when a full pass finds nothing | user request 2026-10-16: approved repo sweep |
| M.LEGN-T18 | 2026-10-16 | chore | open | Run the full unit test suite and fix every failure (each under its own fix ticket) until it passes; put the passing run in --ref. No test project: close this wont-do saying so | user request 2026-10-16: approved repo sweep |
| M.LEGN-T19 | 2026-10-16 | chore | open | Clean up code and docs: remove dead code, stale comments and outdated or duplicated docs, and simplify where behaviour doesn't change (/simplify); tests stay green | user request 2026-10-16: approved repo sweep |
| M.LEGN-T20 | 2026-10-16 | docs | open | Bring README.md up to date with the code as it is now (what it is, setup, usage, commands, current state), following codex-standard/README-STYLE.md; README.htm regenerated if the repo has one | user request 2026-10-16: approved repo sweep |
| M.LEGN-T21 | 2026-10-16 | feature | open | Log to MindAttic.Log (3.0.0): every app/CLI entry point writes to the standalone MindAttic.Log.db Vault assigns this app (VaultPaths.LoggingDbPath, MindAttic.Vault 6.0.0) through Serilog's WriteToMindAtticLog, tagged with its Component; log startup, user actions that change data, failures and unhandled exceptions (UI thread, AppDomain, unobserved tasks); tests point the log at a temp file. Pattern: MindAttic.Workflow WorkflowLog (M.FLOW-T92), MindAttic.Orchestrator OrchestratorLog | M.FLOW-T92 |
| M.LEGN-T22 | 2026-10-16 | docs | closed | Update USER_STORIES.md | — |
| M.LEGN-T23 | 2026-10-17 | fix | open | Renew Groq API key: legion health returns 401 Invalid API Key against api.groq.com/openai/v1 as of 2026-10-17 | legion health |
| M.LEGN-T24 | 2026-10-17 | fix | open | Renew OpenRouter API key: legion health returns 401 User not found against openrouter.ai/api/v1 as of 2026-10-17 | legion health |
| M.LEGN-T25 | 2026-10-17 | fix | open | Renew Kimi (Moonshot AI) API key: legion health returns 401 Invalid Authentication against api.moonshot.cn/v1 as of 2026-10-17 | legion health |
| M.LEGN-T26 | 2026-10-17 | fix | open | Investigate Mistral API key: legion health returns 429 rate_limited (not 401) on every call as of 2026-10-17, check console.mistral.ai for plan/quota status or renew if key is dead | legion health |
| M.LEGN-T27 | 2026-10-17 | chore | open | Add a Perplexity API key to the shared credential store: legion health reports missing-key, never configured | legion health |
| M.LEGN-T28 | 2026-10-17 | feature | open | Add Cerebras Cloud as a Legion provider: OpenAI-compatible API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T29 | 2026-10-17 | feature | open | Add SambaNova Cloud as a Legion provider: OpenAI-compatible API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T30 | 2026-10-17 | feature | open | Add DeepInfra as a Legion provider: OpenAI-compatible API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T31 | 2026-10-17 | feature | open | Add Hyperbolic as a Legion provider: OpenAI-compatible API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T32 | 2026-10-17 | feature | open | Add Novita AI as a Legion provider: OpenAI-compatible API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T33 | 2026-10-17 | feature | open | Add AI21 Labs (Jamba) as a Legion provider: OpenAI-compatible chat API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T34 | 2026-10-17 | feature | open | Add Zhipu AI / GLM direct API as a Legion provider: OpenAI-compatible endpoint, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T35 | 2026-10-17 | feature | open | Add Alibaba Qwen (DashScope International) as a Legion provider: OpenAI-compatible endpoint, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T36 | 2026-10-17 | feature | open | Add Nebius AI Studio as a Legion provider: OpenAI-compatible API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T37 | 2026-10-17 | feature | open | Add Reka AI as a Legion provider: own chat-completions API, add to LlmProviderCatalog + credential store + health check | — |
| M.LEGN-T38 | 2026-10-18 | docs | closed | Update USER_STORIES.md | — |
