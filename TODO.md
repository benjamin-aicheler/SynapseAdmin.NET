# TODO

<!--
HOW TO USE THIS FILE (instructions for the AI agent):
- This is the SINGLE-TASK execution checklist derived from the approved PLAN.md (ignore if PLAN.md is blank template or Done).
- Work on ONE task at a time, top to bottom within "Next Up", unless the user says otherwise.
- Before starting a task: move it to "In Progress".
- After finishing a task: verify it (`dotnet build` & `dotnet test`), tick the checkbox, move it to "Done"
  with the date and a short note (e.g. commit hash or files changed).
- If you discover new work, add it to "Backlog" or "Bugs / Follow-ups" – do not silently expand the current task.
- If a task is blocked, move it to "Blocked" with the reason and what is needed.
- Keep entries short. Reference PLAN step IDs (e.g. [1.2]) and spec IDs (e.g. FR-15, AC-10).
- ON COMPLETION (before merging the PR, or before starting the next task): finalize `[Planned]` items in SPEC.md and clear/reset this file (and PLAN.md) back to the blank template from `main` (optionally preserving any real unresolved follow-ups in Backlog, or moving them to GitHub Issues).
-->

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-21

## 🔄 In Progress

- [ ] [1.1] Characterization tests for unfiltered loading, source-only filtering and search by `mxc://` URI (AC-10)

## ⏭️ Next Up

- [ ] [2.1] Failing tests: Images and file-name search find item 21; Files excludes items without a lookup or a known type; ≤ 5 lookup batches per load; stale filter discarded (AC-10)
- [ ] [2.2] Classification in order with a cursor and bounded lookups in `RoomGalleryGrid.razor.cs` (FR-6)
- [ ] [3.1] Failing tests: "Checked x of y items", "Check more", "No media found" only when complete, unknown-type note (AC-10)
- [ ] [3.2] Markup, `CheckMore` handler, EN/DE/FR resource keys (FR-6)
- [ ] [4.1] Finalize SPEC.md `[Planned]` entries and the Decision Log; reset PLAN.md and TODO.md

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] 

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-21 – SPEC.md `[Planned]` entries and PLAN.md approved – df23bee

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
