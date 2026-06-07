---
codex: 1
project: MindAttic.Legion
code: LEG
layer: stories
status: living
updated: 2026-06-07
---

# MindAttic.Legion — User Stories

> ✅ done (shipped & tested) · 🟡 partial · ⬜ planned · 🗑️ cut. Every ✅ cites the test that proves it.
> "Verified" here means an **offline** NUnit test (fakes via `TestSupport/FakeLlmHandlers.cs`) proves the logic without live keys, unless noted. Stories that can only be proven with live provider keys are marked 🟡 (logic proven offline; end-to-end unproven in this environment).

## Epic A — Consensus voting

- **LEG-US-A1 ✅** As an integrator, I can ask a panel a question under a quorum and get a consensus or an explicit "no quorum", so I never act on a split panel. *Given a free-form question, When a single voter answers under a loose quorum, Then quorum is reached; When strict quorum can't be met, Then `QuorumReached==false`.* *(verified by `VoteAsync_FreeForm_SingleVoter_ReachesAnyQuorum`, `VoteAsync_FreeForm_NoNarrativeSynthesis_FailsStrictQuorum`, `VoteAsync_NoVoters_ReturnsQuorumNotReached`.)*
- **LEG-US-A2 ✅** As an integrator, I can rely on exact quorum thresholds, so a 2-of-4 tie never counts as a majority and a 2-of-3 always clears two-thirds. *Given vote tallies, When evaluated, Then integer arithmetic decides the boundary exactly.* *(verified by `SimpleMajority_RejectsExactTie`, `TwoThirds_IsSatisfiedBy_AdmitsTwoOfThree_RejectsOneOfTwo`, `Plurality_IsSatisfiedBy_NeedsAtLeastOne`, `Unanimous_IsSatisfiedBy_NeedsEveryVoter`, `IsSatisfiedBy_ZeroTotal_AlwaysFalse`.)* (See [LEG-LAW-3](BIBLE.md#LEG-LAW-3).)
- **LEG-US-A3 🟡** As an integrator, I can run a real multi-provider vote end-to-end against live keys. *Logic proven offline; live path is gated under category `LiveApi` (`LiveApiIntegrationTests`) and not asserted here without keys.*

## Epic B — Decisions & scoring

- **LEG-US-B1 🟡** As an automated workflow, I can call `DecideAsync(question, options)` to pick one option with confidence and reasoning, so my code doesn't hard-code a branch. *Logic exercised via the voting-service tests; dedicated decision assertions are 🟡 pending a named `Decide*` offline test.*
- **LEG-US-B2 🟡** As a reviewer, I can score content across rubric dimensions (1–10) and receive failing dimensions + improvement directives. *Exercised through `LlmVotingServiceTests`; mark ✅ once a dedicated `Score*` offline test is named.*

## Epic C — Providers, tiers & catalog

- **LEG-US-C1 ✅** As an integrator, I can request a model by tier and survive vendor model-id rotation, so my callers never name a drifting model id. *(verified by `BuildHighTierModelOverrides_*`, `BuildTierModelOverrides_*`, `BlankModel_FallsBackToProviderDefaultModel`.)* (See [LEG-LAW-4](BIBLE.md#LEG-LAW-4).)
- **LEG-US-C2 ✅** As an integrator, I get a stable provider catalog with the expected provider count and known ids. *(verified by `All_HasExpectedProviderCount`, `All_KnownIds`.)*
- **LEG-US-C3 ✅** As an integrator, a provider counts as active only with a non-empty key and membership in the allowed set (default trusted four). *(verified by `ActiveProviderIds_DefaultAllowedSet_IsTheTrustedFour`, `ActiveProviderIds_BlankKey_IsNotActive`, `ActiveProviderIds_KeyForUntrustedProvider_IsFilteredOut`, `ActiveProviderIds_NoKeys_ReturnsEmpty`.)* (See [LEG-LAW-5](BIBLE.md#LEG-LAW-5).)
- **LEG-US-C4 ✅** As an operator, I can discover a provider's live model list and parse vendor-specific shapes. *(verified by `LlmModelDiscoveryExtractTests` / `LlmModelDiscoveryTests`, e.g. `AnthropicShape_DataArrayWithIdProperty`, `CohereShape_ModelsArrayWithNameProperty`, `BareArray_OfStrings_Works`.)*

## Epic D — Transport & resilience

- **LEG-US-D1 ✅** As an integrator, `LegionClient.CallAsync` dispatches the correct wire shape per vendor (headers + payload), so each provider is called correctly. *(verified by `Claude_SendsXApiKeyHeader_AndAnthropicVersion`, `Claude_SystemPrompt_PostedAsTopLevelSystemField`, `CallAsync_ExplicitKey_DispatchesOpenAiShape`, `CallAsync_ExplicitKey_DispatchesGeminiShape`, `CallAsync_ExplicitKey_DispatchesCohereShape`, `Cohere_HitsV2Endpoint_AndExtractsMessageContent`.)*
- **LEG-US-D2 ✅** As an integrator, missing/unknown providers fail clearly and HTTP errors propagate. *(verified by `CallAsync_MissingKey_Throws`, `CallAsync_UnknownProvider_Throws`, `CallAsync_HttpError_Propagates`, `CallAsync_BlankModel_FallsBackToDefault`.)*
- **LEG-US-D3 ✅** As an integrator, a sick provider trips a shared circuit breaker so calls fail fast and reset on recovery. *(verified by `CircuitBreaker_OpensAfterThreshold`, `CircuitBreaker_ResetsOnSuccess`, `CircuitBreakerOpenException_IsCircuitOpen`.)* (See [LEG-LAW-7](BIBLE.md#LEG-LAW-7).)
- **LEG-US-D4 ✅** As an operator, a failed key gives an actionable diagnosis pointing at the right dashboard/keys URL. *(verified by `ActionableDiagnosis_LinksToCorrectUrl`, `AuthInvalid_TellsUserToGenerateNewKey`, `CircuitOpen_TellsUserToUseDifferentProvider`, `CheckOneAsync_*` health-check tests.)*
- **LEG-US-D5 ✅** As an integrator, transient transport faults unwrap and classify correctly. *(verified by `AggregateException_UnwrapsToFirstInner`, `ArgumentException_IsBadRequest`, and `ResilienceTests`.)*

## Epic E — Personas & panels

- **LEG-US-E1 ✅** As an integrator, I get exactly 1024 personas with unique ids and names. *(verified by `All_PersonasHaveUniqueIds`, `All_PersonasHaveUniqueNames`, `PersonaLibraryShapeTests`.)* (See [LEG-LAW-9](BIBLE.md#LEG-LAW-9).)
- **LEG-US-E2 ✅** As an integrator, `VoterFactory` spreads N voters round-robin across providers with distinct personas, reproducibly with a seed. *(verified by `AssignRoundRobin_HundredVoters_FourProviders_TwentyFiveEach`, `AssignRoundRobin_OneProvider_AllVotersGoThere`, `AssignRoundRobin_PreservesOrderForReproducibility`, `AssignRoundRobin_ResolvesTierModelPerAssignment`.)*
- **LEG-US-E3 ✅** As an integrator, persona profiles round-trip and align by index/id. *(verified by `AllDetails_AlignWithAllByIndexAndId`, `PersonaStoreTests`.)*

## Epic F — Psychometrics

- **LEG-US-F1 ✅** As an analyst, the five instruments parse in-character answers and score deterministically in code. *(verified by `PsychometricInstrumentsTests`, `PsychometricAnswerParsingTests`, `PsychometricScorerTests`, `PsychometricModelTests`.)* (See [LEG-LAW-8](BIBLE.md#LEG-LAW-8).)
- **LEG-US-F2 🟡** As an analyst, I can score the whole library against live models. *Gated under category `LivePsychometrics` (`PsychometricLiveTests`); not asserted here without keys.*

## Epic G — CLI (`legion.exe`)

- **LEG-US-G1 ✅** As a shell/CI user, `legion.exe ask` returns a consensus and `poll` returns a sorted distribution + plurality winner. *(verified by `Ask_SmokeTest_ReturnsConsensusAtLowTier`, `PollCommandTests` aggregation tests `Aggregate_SortsByDescendingCount`, `Aggregate_GroupsCaseInsensitively`, `Aggregate_SkipsFailedAndEmptyOutcomes`, `Aggregate_AllFailed_ReturnsEmpty`.)*
- **LEG-US-G2 ✅** As a shell user, `ask` auto-collects `CLAUDE.md`/README context and frames voters as architects. *(verified by `CollectAutoContextAsync_IncludesClaudeMdAndReadmeWhenPresent`, `CollectAutoContextAsync_NoFiles_ReturnsEmpty`, `BuildArchitectFraming_ContainsTheFiveHeuristics`.)*
- **LEG-US-G3 ✅** As a shell user, `generate` fans out per-provider batches and `tiers` probes the (provider, tier) matrix. *(verified by `GenerateCommandTests`, `TiersCommandTests`.)*

## Epic H — Credentials (via MindAttic.Vault)

- **LEG-US-H1 ✅** As an integrator, keys resolve through the documented order and the Vault-backed config store contributes when registered. *(verified by `MindAtticCredentialStoreConfigurationTests`, `VotingConfigurationTests`, `ActiveProviderIds_SharedCredentialsEnabled_StoreContributes`, `ActiveProviderIds_SharedCredentialsDisabled_OnlyExplicitKeysCount`.)* (See [LEG-LAW-2](BIBLE.md#LEG-LAW-2).)

## Priority backlog

1. **LEG-US-B1** — add a dedicated offline `Decide*` test and promote to ✅.
2. **LEG-US-B2** — add a dedicated offline `Score*` rubric test and promote to ✅.
3. **LEG-US-A3 / F2** — wire a CI secret-bearing job (or documented local run) so the live categories can be exercised and reported, promoting these from 🟡.

### Audit log

No stories have been rewritten since adoption of the Codex standard; original asks were derived directly from README.md and the test suite. Any future change to a story above must preserve the original wording here, marked "(original spec — audit log)".
