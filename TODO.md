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

- [ ] [4.1] SPEC.md `[Planned]` removed; AGENTS.md, README; reset PLAN.md / TODO.md

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] 

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-27 – [3.1] `UserDetails`: "Join to room" (disabled for deactivated/suspended), dialog flow, reload; default `IRoomService` stub in the test base; 11 page tests, mutation-checked. Deviation: the dialog gets `FormatTarget(user, target)` (name + ID), as Edit profile does
- [x] 2026-11-27 – [2.2] `UserJoinRoomDialog` (typed ID or leave/invite/knock picks; refuses join/ban); 14 dialog tests, mutation-checked, also under de_DE
- [x] 2026-11-27 – [2.1] 7 keys in EN/DE/FR (`JoinToRoom*`, `InvalidRoomId`, `UserBannedFromRoom`)
- [x] 2026-11-27 – [1.1] `MatrixIdHelper.IsRoomId`; room ID check (first) and trim in `RoomService.JoinUserToRoomAsync`; 13 helper + 8 service tests, mutation-checked
- [x] 2026-11-27 – [0] SPEC.md `[Planned]` entries (FR-4, AC-27, §8, §10, §15) and PLAN.md (Approved)

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
