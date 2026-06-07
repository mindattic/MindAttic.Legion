---
codex: 1
project: MindAttic.Legion
code: LEG
layer: rfc
status: done
updated: 2026-06-07
---

# RFC 0001 — Adopt the MindAttic Codex documentation standard in Legion

## Problem

Legion's design lived only in README.md (a ~48KB usage doc) and XML comments. There was no single source of truth for what the library IS / is NOT, no stable IDs for laws and stories, no machine-checkable link between "✅ done" claims and the tests that prove them, and no inherited house-rules link. Documentation could drift from code with nothing to catch it.

## Options compared

1. **Do nothing** — keep README + XML comments. Rejected: no invariants, no drift detection, no story/test traceability.
2. **Hand-written ARCHITECTURE.md** — better than nothing but still no IDs, no doctor, no digest, manual and rot-prone.
3. **Adopt the Codex standard** (`docs/BIBLE.md` + AMENDMENTS + USER_STORIES + rfc + `tools/codex.ps1` doctor/digest + SessionStart hook). Chosen: stable IDs, single-home-per-fact, inherited house rules, and tooling that fails on drift.

## Decision

Adopt Codex exactly as specified in `D:/Projects/MindAttic/codex-standard/IMPLEMENTATION_PROMPT.md`, adapted to Legion's `library` domain. No L5 `docs/data/*.json` (catalogs stay in source). Inherit `MindAttic.HouseRules.md` from BIBLE §5.

## What NOT to do

- Do **not** restate or modify `MindAttic.HouseRules.md` — link it.
- Do **not** duplicate facts: prose cites IDs (`#LEG-§n`, `#LEG-LAW-n`, `LEG-US-*`), never restating.
- Do **not** mark a story ✅ without a real, existing test token; live-key-only paths stay 🟡.
- Do **not** edit application/source code as part of documentation work.
- Do **not** hand-edit `docs/BIBLE.digest.md` (generated).

## Phased plan (with risk)

1. Inventory + detect domain (CODE = LEG, domain = library). *Risk: low.*
2. Author BIBLE/AMENDMENTS/USER_STORIES/RFC from real architecture and real test names. *Risk: mis-citing a test — mitigated by extracting names directly from the test tree.*
3. Add `tools/codex.ps1` (doctor + digest) and the SessionStart hook; merge `.claude/settings.json`. *Risk: clobbering existing hooks — mitigated by merge (none existed).*
4. Run digest, doctor, build, offline tests; record true status. *Risk: doctor false-positives on cross-refs — mitigated by validating anchors during authoring.*

## Graduates into

- [BIBLE §1–§9](../BIBLE.md#LEG-§1) (canon, laws, verified state).
- [USER_STORIES](../USER_STORIES.md) (all epics).
- [AMENDMENTS LEG-A1](../AMENDMENTS.md#LEG-A1).

Status: **done** — the standard is installed; this RFC is retained as the rationale record.
