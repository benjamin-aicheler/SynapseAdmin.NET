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

- [ ] [4.1] Finalize SPEC.md `[Planned]` entries and the Decision Log; reset PLAN.md and TODO.md

## ⏭️ Next Up

- [ ] 

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] 

## 🐛 Bugs / Follow-ups Found During Work

- [x] 2026-11-21 – A load started by the JS sentinel didn't render after `_isBatchLoading` was reset, so a status that depends on it stayed stale. Fixed in `LoadNextBatch`'s `finally` and covered by `SentinelLoad_StoppingAtTheCap_RendersCheckMore`.

## ✅ Done

- [x] 2026-11-21 – SPEC.md `[Planned]` entries and PLAN.md approved – df23bee
- [x] 2026-11-21 – [1.1] Characterization tests (unfiltered, source filter, search by URI) – `RoomGalleryFilterHydrationTests`
- [x] 2026-11-21 – [2.1]/[2.2] Classification in order with bounded lookups – `RoomGalleryGrid.razor.cs`
- [x] 2026-11-21 – [3.1]/[3.2] Status, "Check more", unknown-type note, EN/DE/FR strings – `RoomGalleryGrid.razor`, resx

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
