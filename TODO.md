# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-24

## 🔄 In Progress

- [ ] [1.1] SPEC.md `[Planned]`: FR-8 wording, AC-15 (#542), §10 Tuwunel refusal

## ⏭️ Next Up

- [ ] [2.1] `MediaServiceCharacterizationTests`: success message formatted with `Total` (0 and 2) – red
- [ ] [2.2] `string.Format(L["DeletedLocalMediaSuccess"], result.Total)` + single-placeholder text EN/DE/FR
- [ ] [3.1] bUnit `MediaMaintenanceTests`: label, description, switch, both confirmation variants, success snackbar via real `MediaService` – red
- [ ] [3.2] Page, code-behind and resources per PLAN §4; remove `UploadedBeforeDays`
- [ ] [3.3] Resource test: translations and placeholders of the changed and new keys
- [ ] [4.1] Wire tests: Synapse `DeleteLocalMediaAsync` request (characterization); Tuwunel refuses with no request – red
- [ ] [4.2] `TuwunelAdminGateway.DeleteLocalMediaAsync` override
- [ ] [5.1] SPEC.md: remove `[Planned]`, name tests in AC-15, decision row
- [ ] [5.2] AGENTS.md Known Pitfalls entry for `POST /_synapse/admin/v1/media/delete`
- [ ] [5.3] Reset PLAN.md and TODO.md to the templates from `main`

## ⛔ Blocked

- [ ] 

## 📋 Backlog

- [ ] 

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-24 – PLAN.md approved (wording as in §4, one success message for every total)

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
