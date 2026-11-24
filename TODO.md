# TODO

**Related:** [SPEC.md](./SPEC.md) · [PLAN.md](./PLAN.md) · [ARCHITECTURE.md](./ARCHITECTURE.md)
**Last updated:** 2026-11-24

## 🔄 In Progress

- [ ] [5.1] SPEC.md: remove `[Planned]`, name tests in AC-15, decision row

## ⏭️ Next Up

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
- [x] 2026-11-24 – [1.1] SPEC.md: FR-8 `[Planned]` sentence, AC-15, §10 Tuwunel edge case
- [x] 2026-11-24 – [2.1, 2.2] `DeleteLocal_Success_FormatsTheReportedTotal_WithoutLogging` (0, 2) red, then green with `string.Format(…, result.Total)`; `DeletedLocalMediaSuccess` has a single `{0}` in EN/DE/FR
- [x] 2026-11-24 – [3.1–3.3] `MediaMaintenanceTests`: label test first passed by accident (outlined fields render the label text twice), rewritten to count `<label>` elements, then red with both confirmation tests; refusal test passes already (step 2). Page, code-behind (`DeleteLocalMediaConfirmationMessage`) and resources changed, `UploadedBeforeDays` removed; resource test `CardText_IsTranslated_WithExactlyItsPlaceholders` placed in `MediaMaintenanceTests` instead of next to `NotSupportedWarning_IsTranslated`; 1316 tests pass (EN and DE)
- [x] 2026-11-24 – [4.1, 4.2] Synapse wire test (`keep_profiles` true/false, body `{}`) passes on unchanged code; Tuwunel refusal test red, then green with the override; no request sent

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
