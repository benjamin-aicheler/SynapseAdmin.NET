# Code Review Template

How to review SynapseAdmin.NET and write the report. The report skeleton is in [Report Template](#report-template) at the end. Saving the report and acting on it follows AGENTS.md "Automated Code Review".

## 1. Scope and Preparation

- Review the diff against `main` (`git diff main...HEAD`) unless you were asked for a PR, a file set or the whole codebase. Record the commit SHA: the line numbers in the report refer to it.
- Read [AGENTS.md](./AGENTS.md) first. Its sections are the checklist (see [What to Check](#4-what-to-check)), so this template doesn't repeat them. Read the parts of [SPEC.md](./SPEC.md) and [ARCHITECTURE.md](./ARCHITECTURE.md) that cover the code under review.
- Run `dotnet build` and `dotnet test` and record the results. If the diff changes only Markdown files, skip them and record "not run, Markdown only". If you can't run them, say why.
- Don't review `LibMatrix/` (git submodule). Read it only to confirm how the SDK behaves.

## 2. Severity and Review Status

Severity measures impact, not category: a bug can be High or Low. AGENTS.md requires approval before High and Medium fixes, so pick the level carefully.

| Severity | Meaning | Examples |
|---|---|---|
| **High** | Blocks merging. Exploitable security flaw, data loss, an action on the wrong entity, a crash or broken core feature, or a broken "🚫 Never" rule in AGENTS.md. | Token in a URL or log, SSRF past the destination policy, delete applied to a room the admin didn't confirm |
| **Medium** | Wrong behavior in a realistic case, a resource leak, or a broken AGENTS.md convention that leads to bugs. Changed logic without tests. | Missing `CancellationToken`, undisposed `MediaDownload`, a cache key without the homeserver, an error snackbar on cancellation, a key missing from `SharedResources.resx` |
| **Low** | No wrong behavior. Readability, naming, small duplication, comments, a doc gap. | An unused parameter, a copy of a shared helper, an outdated comment |

The review status follows from the findings:

- **Critical Issues:** at least one High.
- **Needs Work:** at least one Medium, no High.
- **Pass:** only Low findings, or none.

## 3. Findings

Write each finding in this format:

```markdown
**BUG-01 [High | Medium | Low] [Title]** (`path/to/File.cs:line`)
* **Problem:** What is wrong and what it causes.
* **Scenario:** Concrete input or state → wrong result. For example: open `/rooms/A`, navigate to `/rooms/B` before A has loaded → A's details are shown under B's ID.
* **Verified:** Yes (how: a test, a run, the callers read) | Plausible (what is still unconfirmed)
* **Fix:** The specific change, with a code sketch if it helps.
```

- **IDs:** one prefix per section (`SEC`, `BUG`, `PERF`, `ARCH`, `ERR`, `TEST`), numbered from `01` in each report. The action items and any follow-up commit or PR refer to findings by ID.
- **Evidence:** a finding needs a scenario. If you can't name one, it goes under Open Questions or is left out. Before you report something as missing, check that nothing else handles it: the callers, the middleware pipeline in `Program.cs`, framework defaults, `_Imports.razor`.
- **One root cause, one finding:** list further locations of the same problem under that finding. Put each finding in the section of its main impact. Within a section, order findings from most to least severe.
- **Safe to share:** never paste access tokens, `DP_PASSPHRASE` values, real user IDs or raw event JSON into a report. Use placeholders.

## 4. What to Check

Each area names the AGENTS.md sections it checks against, plus points AGENTS.md doesn't cover.

- **Security:** "Security & Data Protection", "🚫 Never". Also: `[Authorize]` on controller actions that use the session or media (`AuthController` and `CultureController` are anonymous on purpose), local-only redirects (`Url.IsLocalUrl`), HTML encoding of everything passed to a `MarkupString`, `Uri.EscapeDataString` for every value placed in a gateway URL, which proxies `ForwardedHeaders` trusts, and untrusted text in logs (log forging).
- **Bugs:** "Known Pitfalls", and from "Code Conventions": "Detail Pages & Destructive Actions", "Null Gateway Results", "Login Failures", "Timezone & Themes". Also: null references, state changed after an `await` on a disposed component, `async void` outside event handlers, missing `CancellationToken`s, undisposed `CancellationTokenSource`s, timers, event subscriptions and JS object references.
- **Performance:** server-side paging with `MudTable` `ServerData` rather than loading whole lists, N+1 homeserver calls, `IMemoryCache` use and its keys, needless `StateHasChanged`, missing `@key` on lists that change, media streamed rather than buffered, and `JsonSerializerOptions` kept in `static readonly` fields.
- **Architecture & code quality:** "Code Conventions" ("N-Tier Separation", "ViewModel Mapping", "Interfaces & DI", "Gateway Pattern & Capability Checks", "Blazor & MudBlazor UI", "Razor Markup Hygiene", "Localization", "Shared Helpers", "Tuning Values"). If a private member was renamed or removed, check "Tests Bound to Private Names". If code was deleted as unused, check "Dynamically Wired Code".
- **Error handling & logging:** "Error Handling & Silent Cancellation", "Null Gateway Results", "Logging".
- **Tests & documentation:**
  - Changed logic has tests: xUnit, bUnit through `DetailPageTestBase`, the service's `*ServiceCharacterizationTests.cs`, and a `MappingExtensionsTests` test for each new mapper. bUnit tests follow "bUnit Component Tests" and "Test Doubles".
  - Every new string is in `SharedResources.resx`, `.de.resx` and `.fr.resx` ([LOCALIZATION.md](./LOCALIZATION.md)).
  - SPEC.md, ARCHITECTURE.md and AGENTS.md are updated as "Where Documentation Goes" describes. `[Planned]` tags are resolved once a feature is done.
  - Behavior-preserving changes follow [REFACTOR_RULES.md](./REFACTOR_RULES.md).
  - "⚠️ Ask first" changes were agreed: NuGet packages, `.github/workflows/`, Docker security settings, the `LibMatrix/` submodule.

## 5. Deliberate Choices: Don't Flag

These look like mistakes but are decided and documented. If you think one is wrong, raise it under Open Questions, not as a finding.

- `Download` and `Preview` set `X-Content-Type-Options: nosniff` in addition to the global middleware (#396).
- The two `creation_ts` conversions: milliseconds on `/_synapse/admin/v3/users`, seconds on `/_synapse/admin/v2/users/{userId}`.
- The different unsorted defaults: `"f"` for rooms and users, `"b"` for event reports and federation destinations.
- The different `StringComparison` values of the MIME checks, and the case-sensitive alias server name check in `RoomAliasesDialog`.
- Code without a static caller that "Dynamically Wired Code" lists.
- `MatrixSessionService` keeping its own catch blocks instead of `RunGatewayCallAsync`.
- The `AuthenticationStateProvider` registration next to `IMatrixAuthStateService` in `Program.cs`.
- Tuning values as named constants rather than configuration.
- The floating `10.0` .NET versions (".NET Version Policy").
- Everything else in the standing decisions of the [archived refactor plan](./docs/archive/2026-11-refactor-plan.md#62-standing-decisions-constraints-for-every-task) (§6.2).

## Report Template

Copy the block below to `reviews/YYYY-MM-DD_HH-MM-SS_review.md`, fill it in and delete the placeholder text. A section without findings says "No issues found."

````markdown
# 🤖 Code Review Report

## 📋 Metadata

* **Date:** [YYYY-MM-DD HH:MM:SS, local time, as in the file name]
* **Reviewer:** [Agent and model, or person]
* **Scope:** [Branch, PR #n, file set, or whole codebase]
* **Commit:** [Short SHA that the line numbers refer to]
* **Reviewed Files/Modules:** [Files or globs]
* **Not Reviewed:** [What was left out and why, or "Nothing"]
* **Main Purpose of Code:** [What this code does, in 1–2 sentences]
* **Build & Tests:** `dotnet build` [passed | failed], `dotnet test` [n passed, n failed, n skipped | not run, because …]
* **Review Status:** [Pass | Needs Work | Critical Issues]
* **Findings:** [n High, n Medium, n Low]

## 📝 Summary

[2–4 sentences: the overall state of the code, the most important risks, and what is done well.]

## 🚨 1. Security & Vulnerabilities

[Findings SEC-01, SEC-02, … in the format of §3, or "No issues found."]

## 🐛 2. Bugs & Logic Errors

[Findings BUG-01, …, or "No issues found."]

## ⚡ 3. Performance & Efficiency

[Findings PERF-01, …, or "No issues found."]

## 🏗️ 4. Architecture & Code Quality

[Findings ARCH-01, …, or "No issues found."]

## 🛡️ 5. Error Handling & Logging

[Findings ERR-01, …, or "No issues found."]

## 🧪 6. Tests & Documentation

[Findings TEST-01, …, or "No issues found."]

## ❓ 7. Open Questions

* [A question about intent or design that the review can't settle, with its location. Or "None."]

## 🎯 8. Action Items

### 🔴 High (blocks merging)
* [ ] **SEC-01** [One-line summary of the fix]

### 🟡 Medium
* [ ] **BUG-01** [One-line summary of the fix]

### 🟢 Low
* [ ] **ARCH-01** [One-line summary of the fix]
````
