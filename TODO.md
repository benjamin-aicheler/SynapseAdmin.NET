# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-24

## 🔄 In Progress

- [ ] [4.1] AGENTS.md: new names, contract rule, Room Size Semantics, Tuwunel & Generic Fallback

## ⏭️ Next Up

- [ ] [4.2] ARCHITECTURE.md: new names in §2, §3, §5, §6, §10; ADR 0006 → Accepted; ADR 0001 note
- [ ] [4.3] Reset PLAN.md and TODO.md to the templates from `main`

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] Step 2 (separate PR): `NotSupportedException` → localized "not supported" message in `RunGatewayCallAsync` (SPEC.md AC-14, §10)
- [ ] Step 3 (separate PR): #542

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-24 – PLAN.md approved
- [x] 2026-11-24 – [1.1] `GetAuthenticated_RoomListDir_FollowsTheProbedServer` (6 rows) passes on `main`'s code; in-memory mutations caught: no reversal (2 Tuwunel rows fail), case-sensitive brand check (`tuwunel` row fails)
- [x] 2026-11-24 – [2.1, 2.2] Gateway and test class renamed with `git mv` (7 files, 59 lines); no string references to the old names in `tests/`; build 0 errors, app project 0 warnings; 1233 tests pass
- [x] 2026-11-24 – [3.1–3.3] `TuwunelAdminGateway` overrides `GetRoomListAsync`; brand check removed from `SynapseAdminGateway`; factory picks the class; `GetAuthenticated_TuwunelBrand_GetsTheTuwunelGateway` (3 casings); `RoomListDirs` split into `GetRoomList_SendsTheDirItIsGiven` (4) and `Tuwunel_GetRoomList_SendsTheDirThatGivesTheDocumentedOrder` (8); [1.1] test passes unchanged; no `ServerBrand` comparison left; 1236 tests pass (EN and DE)

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
