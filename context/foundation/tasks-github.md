# Plan: Migrate `roadmap.md` → GitHub Issues

## Problem

`context/foundation/roadmap.md` defines 9 backlog-ready items (2 foundations + 7 slices) with explicit dependency chains, PRD refs, outcomes, and risks. We want a 1:1 mirror in GitHub Issues so the roadmap becomes actionable as work-tracking artifacts, while the `.md` remains the design source of truth.

## Task management system

- **Repository:** `Sarnapa/cho-na-bojo` (per env context).
- **System chosen:** **GitHub Issues** in the repo, labeled and linked. No Milestones, no Projects board (kept lightweight per user choice — dependency order already lives in the roadmap).
- **Auth:** Confirmed — `gh auth status` shows logged in as `Sarnapa` with ADMIN on `Sarnapa/cho-na-bojo`; token scopes include `repo` and `workflow`. Migration is unblocked.

## Format (approved by user)

### Title
`[<Roadmap ID>] <change-id>: <short outcome>`
Example: `[F-01] data-layer-foundation: Supabase Postgres + migrations + seeded sports & Warsaw venues`

### Body template

```markdown
> Source: [`context/foundation/roadmap.md`](../blob/master/context/foundation/roadmap.md) — section <Roadmap ID>
> Change ID: `<change-id>`

## Outcome
<verbatim "Outcome" bullet from roadmap>

## PRD refs
<verbatim "PRD refs" line from roadmap>

## Prerequisites
Roadmap: <verbatim "Prerequisites" value from roadmap, e.g. "F-02" or "—">

Blocked by:
- [ ] #<issue-number-of-prereq>   <!-- one checkbox per prereq; omit "Blocked by" subsection only when roadmap value is "—" (e.g. F-01) -->

## Unlocks
<verbatim "Unlocks" line if present, else omit>

## Parallel with
<verbatim if present, else omit>

## Risk
<verbatim "Risk" bullet from roadmap>

## Unknowns
<verbatim if non-empty>

---
Roadmap status at issue creation: `<status>`
```

### Labels (created if missing)

| Label | Color | Description |
|---|---|---|
| `type:foundation` | `#5319e7` | Foundation enabler (F-NN). |
| `type:slice` | `#0e8a16` | Vertical user-visible slice (S-NN). |
| `stream:A` | `#1d76db` | Matchmaking loop (must-have path). |
| `stream:B` | `#0366d6` | Around the loop (notifications + lifecycle). |
| `status:ready` | `#c2e0c6` | Ready for `/10x-plan`. |
| `status:preparing` | `#fbca04` | In preparation — research/plan underway (pre-implementation). |
| `status:prepared` | `#d4c5f9` | Implementation plan ready; next step is `/10x-implement`. |
| `status:implementing` | `#0052cc` | Implementation underway (plan approved, coding in progress). |
| `status:proposed` | `#fef2c0` | Waiting on prerequisites. |
| `north-star` | `#b60205` | The slice that closes the matchmaking loop (S-05 only). |

### Per-issue label assignment

| Issue | Labels |
|---|---|
| F-01 | `type:foundation`, `stream:A`, `status:ready` |
| F-02 | `type:foundation`, `stream:A`, `status:proposed` |
| S-01 | `type:slice`, `stream:A`, `status:proposed` |
| S-02 | `type:slice`, `stream:A`, `status:proposed` |
| S-03 | `type:slice`, `stream:A`, `status:proposed` |
| S-04 | `type:slice`, `stream:A`, `status:proposed` |
| S-05 | `type:slice`, `stream:A`, `status:proposed`, `north-star` |
| S-06 | `type:slice`, `stream:B`, `status:proposed` |
| S-07 | `type:slice`, `stream:B`, `status:proposed` |

## Migration approach

Issues are created **in dependency order** (F-01 → F-02 → S-01 → ... → S-07) so each prerequisite's GitHub issue number is known by the time a dependent issue references it via the `Prerequisites (blocked by)` task-list checkbox.

Two-phase execution per issue:
1. Create labels (idempotent — skip if exists).
2. For each roadmap item, in order: build body file from template (substituting captured prereq issue numbers), create issue via `gh issue create --title ... --body-file ... --label ...`, capture returned issue number, save to a session-local mapping table.

A small PowerShell driver script (held in session `files/`, not committed) reads the mapping table and creates one issue at a time so a failure mid-run can resume cleanly.

## Todos

1. **doctor-check** — Confirm target repo is `Sarnapa/cho-na-bojo` and list any existing issues whose titles match `[F-NN]`/`[S-NN]` prefixes; if duplicates found, stop and ask. (Auth already verified.)
2. **create-labels** — Create the 7 labels above (idempotent: `gh label create ... || gh label edit ...`).
3. **build-bodies** — Generate 9 issue body Markdown files in session `files/issues/` from the template, one per roadmap item, leaving `<prereq-number>` placeholders.
4. **create-F-01** — `gh issue create` for F-01, capture number, save to mapping.
5. **create-F-02** — Substitute F-01's number into F-02 body, create, capture number.
6. **create-S-01..S-07** — Same pattern, in strict dependency order. S-06 references S-05; S-07 also references S-05 (per roadmap "Parallel with: S-06" — S-07's blocker is S-05, not S-06; the roadmap's `Prerequisites: S-06` line for S-07 is included verbatim in the body for traceability, so S-07 task-list will check S-06).
7. **verify** — `gh issue list --label type:foundation`, `gh issue list --label type:slice`, spot-check 2 issues for correct cross-links and labels.
8. **report** — Print mapping table (Roadmap ID → issue number → URL) back to user.

## Notes & considerations

- **Source of truth:** `roadmap.md` stays canonical. Issues are a mirror; roadmap is not deleted or summarized. No edits to `roadmap.md` as part of this task.
- **No deletion / no destructive actions:** if issues already exist with the same titles, the plan stops and asks rather than re-creating duplicates.
- **Dependencies as task-lists:** the `- [ ] #N` syntax under the `Blocked by:` subheading is what GitHub uses to render the *Tracked by* / *Tracks* relationship on the issue sidebar. The verbatim roadmap `Prerequisites:` value (e.g. `F-02`) is preserved above it for traceability back to the roadmap.
- **S-07 prerequisite nuance:** Roadmap lists S-07's `Prerequisites: S-06` and `Parallel with: S-06`. We faithfully encode `Prerequisites: S-06` as the blocked-by checkbox; the "Parallel with" stays as a separate body section.
- **No iOS, no secrets, no `context/archive/` touches** — all repo hard rules respected; nothing here writes to the codebase or commits anything.
- **Re-runnability:** if the user runs migration twice, the doctor step lists existing issues whose titles match the planned `[F-01] ...` prefixes and asks before proceeding.
