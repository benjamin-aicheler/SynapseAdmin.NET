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
**Last updated:** 2026-11-27

## 🔄 In Progress

- [ ] 

## ⏭️ Next Up

- [ ] [5.1] SPEC.md `[Planned]` removed; AGENTS.md, README; reset PLAN.md / TODO.md

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] Follow-ups (not this task): "Join to room" on the user page; alias and `server_name` input; conditional Tuwunel Make room admin (PLAN §3 F)

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-27 – [4.1] `RoomDetails`: Make room admin / Join user buttons, dialog flow, reload; 14 page tests + both buttons in the disabled-actions tests, mutation-checked
- [x] 2026-11-27 – [3.2] `RoomUserAction` enum, `RoomUserActionDialog`; 15 dialog tests, mutation-checked, also under de_DE
- [x] 2026-11-27 – [3.1] 16 room recovery keys in EN/DE/FR (DE "Berechtigungsstufe", FR "niveau de pouvoir")
- [x] 2026-11-27 – [2.2] `RoomService.MakeRoomAdminAsync` / `JoinUserToRoomAsync`; 3 table rows, 18 specific tests, mutation-checked
- [x] 2026-11-27 – [2.1] `MatrixIdHelper.IsUserId` / `IsLocalUserId` (exact server name); 27 helper tests
- [x] 2026-11-27 – [1.2] Tuwunel join correction (join rule read, Client-Server invite, then join); 11 wire tests, mutation-checked
- [x] 2026-11-27 – [1.1] Room recovery DTOs and gateway calls; Generic and Tuwunel refuse make room admin; 10 wire tests
- [x] 2026-11-27 – [0] SPEC.md `[Planned]` entries (FR-5, AC-26, §4, §8, §10, §13, §15) and PLAN.md (Approved)

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
