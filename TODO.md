# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-24

## 🔄 In Progress

- [ ] [4.1] SPEC.md: remove `[Planned]` from §3 Goals, AC-14, §10; name the tests in AC-14

## ⏭️ Next Up

- [ ] [4.2] AGENTS.md (Error Handling, Contract rule) and ARCHITECTURE.md (§8, layering rule)
- [ ] [4.3] Reset PLAN.md and TODO.md to the templates from `main`

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] Step 3 (separate PR): #542

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-24 – PLAN.md approved (`Warning`, wording as proposed)
- [x] 2026-11-24 – [1.1–1.3] Policy tests red on behavior (2 rows) with the exception type in place, green after the catch in both overloads; resource key in EN/DE/FR with `NotSupportedWarning_IsTranslated` (written together with the key); 1242 tests pass
- [x] 2026-11-24 – [2.1, 2.2] `GenericMatrixGatewayTests` (49 methods by signature) red with plain `NotSupportedException`, green after switching the 49 throws; 1292 tests pass
- [x] 2026-11-24 – [3.1] `GatewayRefuses_ReturnsTheNotSupportedWarning_AndLogsIt` over all 9 media operations (plan named only `DeleteLocalMediaAsync`); disabling the catch in memory fails all 9

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
