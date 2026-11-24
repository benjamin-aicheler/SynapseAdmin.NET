# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-24

## 🔄 In Progress

- [ ] [1.1] Tests: `RunGatewayCallAsync` (both overloads) maps `OperationNotSupportedByHomeserverException` to `OperationNotSupportedByHomeserver` (`Warning`, logged once); a plain `NotSupportedException` keeps the failure key; `Operation` is the caller's name (AC-14)

## ⏭️ Next Up

- [ ] [1.2] Add `Models/OperationNotSupportedByHomeserverException.cs` and the catch in both overloads
- [ ] [1.3] Resource key `OperationNotSupportedByHomeserver` in EN, DE, FR + a three-culture resource test
- [ ] [2.1] Reflection test: every method `GenericMatrixGateway` declares throws the new exception with its own name
- [ ] [2.2] Replace the 49 throws in `GenericMatrixGateway`
- [ ] [3.1] `MediaServiceCharacterizationTests`: `DeleteLocalMediaAsync` refusal → `OperationNotSupportedByHomeserver`, `Warning`
- [ ] [4.1] SPEC.md: remove `[Planned]` from §3 Goals, AC-14, §10; name the tests in AC-14
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

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
