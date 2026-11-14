# 🤖 Code Review Report

## 📋 Metadata

* **Date:** [YYYY-MM-DD HH:MM:SS]
* **Target / Scope:** [Branch, Commit, PR, or File Set]
* **Reviewed Files/Modules:** [List of reviewed files]
* **Main Purpose of Code:** [Short summary of what this code does in 1-2 sentences]
* **Review Status:** [Pass | Needs Work | Critical Issues]

## 🚨 1. Security & Vulnerabilities

*Focus: Authentication & Cookie Bridge (`SessionBridgeService`, `AuthController`, `MediaController`), rate limiting (`ILoginRateLimiter`, `"auth"` policy), ASP.NET Core Data Protection & key encryption at rest, XSS/CSRF/SSRF & media MIME sanitization, URI encoding (`Uri.EscapeDataString`) in gateways, reverse-proxy `ForwardedHeaders` trust, log forging (`.SanitizeForLogging()`), and secret/error-detail leakage.*

**Issues Found:**
* **[[High | Medium | Low]] [Issue Title] (`path/to/File.cs:line`):** [Description of the vulnerability and its impact, or "No issues found."]

**Recommended Fixes:**
* **[Issue Title]:** [Specific, actionable remediation or code suggestion]

## 🐛 2. Bugs & Logic Errors

*Focus: Null-reference risks, race conditions, async/await pitfalls, `CancellationToken` propagation, Blazor component lifecycle/disposal (`IDisposable` / `CancellationTokenSource`), timezone conversion via `ITimeZoneService` (never `DateTime.Now` / `TimeZoneInfo.Local`), and Synapse/Tuwunel API quirks:*
* *`next_token` number-to-string coercion via `ClientHttpClient.GetAsync` + `SynapseCompatibilityJsonOptions` (never `GetFromJsonAsync` on inconsistent endpoints)*
* *Field naming differences (`avatar` in Room Admin API vs `avatar_url` in User Admin API)*
* *Timestamp unit differences (`creation_ts` in milliseconds on `/_synapse/admin/v3/users` vs seconds on `/_synapse/admin/v2/users/{userId}`)*
* *Room size semantics (`order_by=size` sorts by member count, not byte size)*
* *HTTP `DELETE` cancellation via `HttpRequestMessage(HttpMethod.Delete, url)` + `SendAsync(reqMessage, cancellationToken)`*

**Issues Found:**
* **[[High | Medium | Low]] [Bug Title] (`path/to/File.cs:line`):** [Description of the bug or edge case, or "No issues found."]

**Recommended Fixes:**
* **[Bug Title]:** [Specific, actionable remediation or code suggestion]

## ⚡ 3. Performance & Efficiency

*Focus: Blazor Server rendering efficiency (unnecessary `StateHasChanged`, missing `@key`, large DOM trees), SignalR circuit memory leaks (unsubscribed events, undisposed streams/timers), server-side pagination/virtualization in `MudDataGrid`, `IMemoryCache` usage, N+1 homeserver API calls, and streaming vs. buffering large media payloads.*

**Observations:**
* **[[High | Medium | Low]] [Observation Title] (`path/to/File.cs:line`):** [Performance bottleneck description, or "No issues found."]

**Optimization Suggestions:**
* **[Observation Title]:** [Specific suggestion for improvement]

## 🏗️ 4. Code Quality & Architecture

*Focus: Adherence to SynapseAdmin.NET N-Tier architecture, SOLID/DRY principles, localization rules, MudBlazor/theming conventions, and unit test coverage:*
* *Presentation Layer (`.razor` / `.razor.cs`): Code-behind pattern required (no inline `@code` blocks), binds only to `ViewModels` (no raw SDK/DTO models), checks capabilities via `IMatrixGateway.SupportsAdminApi` (never a concrete gateway type check), uses MudBlazor components & `IAppTheme`.*
* *Application Layer (`Services/` & `Interfaces/`): Every service has a matching interface in `Interfaces/` and communicates only through `IMatrixGateway` / `IMatrixAuthGateway`.*
* *Gateway Layer (`Infrastructure/Gateways/`): Encapsulates all wire-level HTTP, URI encoding, and JSON concerns; returns structured DTOs/models (never raw `HttpResponseMessage`); implements Synapse Admin endpoints in `SynapseCompatibleAdminGateway` (Synapse and Tuwunel) without modifying the `LibMatrix` submodule.*
* *Localization (`EN`, `DE`, `FR`): Services localize business outcomes; `.razor.cs` localizes interaction logic only (dialogs, dynamic titles); `.razor` localizes static layout text.*

**Assessment & Remarks:**
* **Readability & Modularity:** [Good | Fair | Poor — Remarks on structure, naming, and separation of concerns]
* **N-Tier & Gateway Compliance:** [Remarks on layer boundaries, ViewModels, and gateway usage]
* **Blazor, MudBlazor & Localization Conventions:** [Remarks on code-behind usage, MudBlazor components, and `IStringLocalizer` placement]
* **Unit Test Coverage:** [Remarks on xUnit test coverage in `tests/SynapseAdmin.Tests/` for new or changed logic]

## 🛡️ 5. Error Handling & Logging

*Focus: Standardized `OperationResult` / `OperationResult<T>` service return types, silent cancellation (`OperationCanceledException` -> `OperationResult.Cancelled()` with `Severity.Normal`, and UI suppressing error Snackbars on `Severity.Normal`), safe localized user-facing error messages (never raw `ex.Message` or stack traces), and structured server-side logging via `ILogger<T>` with `.SanitizeForLogging()`.*

**Remarks:**
* **OperationResult & Silent Cancellation:** [Assessment of service return patterns and `OperationCanceledException` handling]
* **Logging & Safe Error Messages:** [Assessment of `ILogger<T>` usage, log sanitization, and user-facing message safety]

## 🎯 6. Conclusion & Action Items

*Summary of the most important to-dos prioritized by importance.*

### 🔴 High Priority (Blocker)
* [ ] `[path/to/File.cs:line]` [Critical security, data-loss, or breaking bug fix]

### 🟡 Medium Priority (Bugs & Refactoring)
* [ ] `[path/to/File.cs:line]` [Important functional, architectural, or error-handling adjustment]

### 🟢 Low Priority (Nitpicks & Style)
* [ ] `[path/to/File.cs:line]` [Minor style, localization, or cleanup improvement]
