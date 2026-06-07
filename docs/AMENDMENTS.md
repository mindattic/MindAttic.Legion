---
codex: 1
project: MindAttic.Legion
code: LEG
layer: amendments
status: living
updated: 2026-06-07
---

# MindAttic.Legion — Amendments (append-only; an amendment wins over the bible)

> Append-only change log. Never rewrite an amendment — supersede it with a new one. Beyond ~25 entries, fold accepted changes into `docs/BIBLE.md` and start a new epoch (note the git tag); history stays in git.

## LEG-A1 — Adopt the Codex documentation standard (supersedes —) {#LEG-A1}

Established the Codex canonical-documentation layout for this repo: `docs/BIBLE.md` (L0), `docs/AMENDMENTS.md` (L1), `docs/USER_STORIES.md` (L2), `docs/rfc/` (design notes), the generated `docs/BIBLE.digest.md`, `tools/codex.ps1` (doctor + digest), and a `SessionStart` hook (`.claude/hooks/inject-digest.ps1`) registered in `.claude/settings.json`.

- **Why:** give Legion a single source of truth with stable IDs, inherited house rules, and tooling that fails CI when docs drift from code.
- **House rules:** inherited and linked from [BIBLE §5](BIBLE.md#LEG-§5) → `../../MindAttic.HouseRules.md`. Not restated, not modified.
- **Migration:** none — there were no pre-existing canon docs (`docs/`, `game_bible.md`, `ARCHITECTURE.md`, `FOUNDATION_*`, `user_stories.md`) to fold in. All Codex docs are new; README.md and source code were left untouched.
- **No L5 canon-as-data:** Legion is a `library`. Its structured catalogs (the provider catalog, the 1024-persona library, psychometric instruments) live in source/embedded resources and are exercised by tests, so no `docs/data/*.json` was extracted. Re-evaluate if a catalog needs to be authored as prose-free data.
