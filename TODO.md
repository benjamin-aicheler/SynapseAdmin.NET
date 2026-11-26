# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-26

## 🔄 In Progress


## ⏭️ Next Up

- [ ] [1.1] Device and whois request/response DTOs (AC-23)
- [ ] [1.2] `IMatrixGateway.DeviceId` and the four gateway methods; Synapse implementation, Generic refusals, test fakes, wire tests (AC-23)
- [ ] [2.1] `UserDeviceViewModel`, `UserConnectionViewModel`, `UserDeviceMappingExtensions` with property tests
- [ ] [2.2] `IUserDeviceService` / `UserDeviceService`, DI, characterization tests (rename validation, own-device refusal, no IPs in logs)
- [ ] [3.1] Resources EN/DE/FR
- [ ] [3.2] `RenameDeviceDialog` and tests
- [ ] [3.3] `UserDevicesPanel` and tests (AC-23, AC-5)
- [ ] [3.4] `UserConnectionsPanel` and tests
- [ ] [3.5] Devices and Recent connections tabs on `UserDetails`
- [ ] [4.1–4.3] Finish: drop `[Planned]` tags, decision log, README/ARCHITECTURE/AGENTS.md lists, reset PLAN.md and TODO.md

## ⛔ Blocked

## 📋 Backlog

## 🐛 Bugs / Follow-ups Found During Work

## ✅ Done

- [x] 2026-11-26 – [0.1] SPEC.md `[Planned]` entries for FR-4, AC-23, §8, §10, §13
- [x] 2026-11-26 – [0.2] PLAN.md approved; TODO.md filled

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
