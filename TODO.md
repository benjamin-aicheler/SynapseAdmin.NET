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

- [x] 2026-11-27 – [3.3] `RoomDetails` (a switch on action and result type) and `UserDetails` pass the servers; page tests check them and that a result of another type sends nothing
- [x] 2026-11-27 – [3.2] `RoomJoinRequestViewModel`, `Components/Shared/JoinServerInput` (split on commas and whitespace, first invalid server named); `UserJoinRoomDialog` takes an alias, servers field disabled with the note for an alias (no servers returned, no error shown); `RoomUserActionDialog` Join servers field; the servers error is a computed `Error`/`ErrorText`, so it follows the room field
- [x] 2026-11-27 – [3.1] Resources EN/DE/FR: `RoomIdOrAlias`, `JoinServers`, `JoinServersHint`, `JoinServersAliasNote`, `InvalidServerName`, `JoinServersNeedRoomId`; `InvalidRoomId` and `JoinToRoomRoomIdHint` name aliases; `JoinToRoomDescription` unchanged (the reference's conditions)
- [x] 2026-11-27 – [2.1] `RoomService.JoinUserToRoomAsync(roomIdOrAlias, userId, serverNames, token)`: room (ID or alias), alias with servers → `JoinServersNeedRoomId`, first invalid server named (`InvalidServerName`), then the user; servers trimmed, blanks and ordinal duplicates dropped; the result and log name the answer's `room_id`; logs count the servers; pages pass `token:` by name (servers in Phase 3)
- [x] 2026-11-27 – [1.3] Tuwunel: alias resolved via the Client-Server directory (`RoomAliasUrl` made protected), room details' `joined_local_members` / `404` before the join rule read; 8 new wire tests, #587 sequences updated, mutation-checked
- [x] 2026-11-27 – [1.2] Join signature (`roomIdOrAlias`, `serverNames`), alias `Uri.EscapeDataString`, `server_name` query; 3 wire tests; service passes no servers yet (Phase 2)
- [x] 2026-11-27 – [1.1] `MatrixIdHelper.IsRoomAlias` / `IsServerName` (`\z`, not `$`); 40 helper tests
- [x] 2026-11-27 – [0] SPEC.md `[Planned]` entries (FR-4, FR-5, AC-28, §10, §15) and PLAN.md (Approved; `server_name` and the room page servers field confirmed)

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
