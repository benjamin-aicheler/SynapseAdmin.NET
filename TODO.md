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
**Last updated:** 2026-11-24

## 🔄 In Progress

- [ ] 

## ⏭️ Next Up

- [ ] [3.1] `RunGatewayCallAsync`: optional `logRefusal` callback, with `GatewayCallExtensionsTests`
- [ ] [3.2] Refused reads log `Debug` (event report list, largest rooms, background update status, experimental features, user list, user details' safe feature fetch); #559 lookup uses `logRefusal`; service tests
- [ ] [3.3] Dashboard: total users from its own `name` request; refused latest-users read leaves the card empty without a snackbar; bUnit test
- [ ] [3.4] "Unknown" creation date in `Users`, `UserDetails`, `Home` (new resource key EN/DE/FR); bUnit tests; `dotnet build`, `dotnet test`
- [ ] [4.1] Remove `[Planned]` tags; Tuwunel >= 1.8.2 in AGENTS.md, ARCHITECTURE.md, README.md; Known Pitfall for Tuwunel refusals
- [ ] [4.2] Reset PLAN.md and TODO.md; PR (no issue)

## ⛔ Blocked

- [ ] <!-- Task – blocked by: <reason> – needs: <what/who> -->

## 📋 Backlog

- [ ] 

## 🐛 Bugs / Follow-ups Found During Work

- [ ] 

## ✅ Done

- [x] 2026-11-24 – [2.1–2.3] Tuwunel refusals (16 methods, user list sort), `creation_ts` 0 → unknown, Tuwunel >= 1.8.2; wire, probe and mapper tests. The "unknown" resource key and the three page cells from [3.4] came along (needed to compile)
- [x] 2026-11-24 – [1.1] SPEC.md `[Planned]`: Tuwunel >= 1.8.2, AC-17, five §10 bullets, three Decision Log rows

## Definition of Done (applies to every task)

- [ ] Code implemented according to [PLAN.md](./PLAN.md) and [AGENTS.md](./AGENTS.md) conventions
- [ ] Unit, bUnit, and/or integration tests added/updated and passing (`dotnet test`)
- [ ] Solution builds cleanly with no warnings/errors introduced (`dotnet build`)
- [ ] [SPEC.md](./SPEC.md) `[Planned]` tags finalized (and [ARCHITECTURE.md](./ARCHITECTURE.md) / localization updated if applicable)
- [ ] No secrets, tokens, or real user PII committed
