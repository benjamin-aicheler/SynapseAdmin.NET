# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-25

## 🔄 In Progress

- [ ] [0.3] Check Tuwunel's handlers (v1.8.2, v2.0.0) for the requests of phases 1–4 (FR-4, AC-19 to AC-22)

## ⏭️ Next Up

- [ ] [1.1] Map `Suspended` into `UserDetailViewModel`; remove the threepid placeholder comment (AC-19)
- [ ] [1.2] Suspended and Shadow-banned chips; Active ignores shadow-ban (AC-19)
- [ ] [2.1] Type `UpdateUserAsync`'s request as `SynapseAdminUserUpsertRequest`; update the two hand-written gateway doubles
- [ ] [2.2] Gateway `SetUserSuspendedAsync`, `SetUserShadowBannedAsync`; Generic refuses; Tuwunel refuses shadow-ban (AC-20)
- [ ] [2.3] Service `SetUserLockedAsync`, `SetUserAdminAsync`, `SetUserSuspendedAsync`, `SetUserShadowBannedAsync` (AC-20)
- [ ] [2.4] Actions card toggles; own-account actions disabled with a hint, Deactivate included (AC-20)
- [ ] [3.1] Gateway `ResetPasswordAsync`; Generic refuses; Tuwunel refuses the signed-in admin's own ID (AC-21)
- [ ] [3.2] Service `ResetPasswordAsync`, `ReactivateUserAsync`; no password in logs (AC-21)
- [ ] [3.3] `ResetPasswordDialog` (logout off by default, own-account hint), `ReactivateUserDialog`, page wiring (AC-21)
- [ ] [4.1] Service `UpdateUserProfileAsync`: only changed fields, no change sends nothing (AC-22)
- [ ] [4.2] `UserProfileDialog` and page wiring (AC-22)
- [ ] [4.3] Finish: drop `[Planned]` tags, decision log, AGENTS.md "What Tuwunel Refuses", reset PLAN.md and TODO.md

## ⛔ Blocked

## 📋 Backlog

- [ ] `user_type` editing (left out of #584; needs a request that keeps omit, clear and set apart; Tuwunel ignores the field)

## 🐛 Bugs / Follow-ups Found During Work

## ✅ Done

- [x] 2026-11-25 – [0.1] SPEC.md `[Planned]` entries for FR-4, AC-19 to AC-22, §8, §10, §13
- [x] 2026-11-25 – [0.2] PLAN.md approved; TODO.md filled

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
