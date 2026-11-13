# Specification: SynapseAdmin.NET

| Field | Value |
|---|---|
| **Status** | Implemented |
| **Author(s)** | Benjamin Aicheler (AI-assisted specification derived from codebase) |
| **Stakeholders** | Benjamin Aicheler, Self-Hosted Matrix Homeserver Administrators |
| **Version / Scope** | Living System Specification (baseline `v1.15.0-beta`, extended per feature) |
| **Created** | 2026-11-13 |
| **Last updated** | 2026-11-13 |

> **How this spec is maintained:** `SPEC.md` is the single living specification for `SynapseAdmin.NET`. When adding a feature, extend the relevant sections below (`FR-*`, `AC-*`, UI routes, edge cases, risks) with `[Planned]` tags, execute via [PLAN.md](./PLAN.md) and [TODO.md](./TODO.md), and remove the `[Planned]` tags and log decisions once implemented.

## 1. Summary

SynapseAdmin.NET is a self-hosted .NET 10 Blazor Server web application for administering Matrix Synapse and Synapse-Admin-API-compatible homeservers (such as Tuwunel >= 1.8.1). It gives homeserver administrators a modern, localized, browser-based console for managing users, rooms, media, moderation reports, event forensics, registration tokens, federation queues, server notices, and database background updates without requiring direct CLI or database access.

## 2. Background & Problem

Managing a Matrix Synapse (or compatible) homeserver natively relies on raw HTTP Admin API requests, command-line scripts, or direct database inspection—workflows that are slow, error-prone, and difficult to use safely for routine moderation and media auditing. Additionally, exposing raw homeserver admin endpoints directly to browser SPAs can introduce CORS, credential-exposure, or reverse-proxy complexity, while newer compatible homeservers (such as Tuwunel >= 1.8.1) require tolerant API handling beyond Synapse-only SDK assumptions.

SynapseAdmin.NET addresses this by acting as a dedicated, containerized administration server that proxies Matrix and Synapse Admin requests server-side, encrypts session material at rest, enforces strict SSRF and cookie security policies, and presents high-density Material Design workflows via MudBlazor.

## 3. Goals

- Deliver complete browser-based homeserver administration across users, rooms, room directory, media moderation/cleanup, event reports, raw event forensics, registration tokens, federation destinations, server notices, and background database jobs.
- Support both Synapse and compatible homeservers (e.g., Tuwunel >= 1.8.1), while gracefully detecting non-admin Matrix servers and informing the user when admin APIs are unavailable.
- Protect administrator credentials, target homeservers, and internal networks via SSRF-checked discovery (`HOMESERVER_ALLOWLIST`), AES-256-GCM encrypted Data Protection keys (`DP_PASSPHRASE`), `Secure` HTTP-only cookies, browser-bound session handoff, and login rate limiting.
- Provide responsive, secure media inspection (streaming previews, downloads, HTTP byte-range seeking, quarantine/protection controls) without buffering large files on the server.
- Support internationalized administration (`en-US`, `de-DE`, `fr-FR`), browser-local timezone interpretation, and customizable UI themes.

### Non-Goals (explicitly out of scope)

- **General Matrix end-user client functionality:** End-user messaging, room participation, or E2EE key management for non-admin Matrix users are out of scope.
- **Direct homeserver database access:** All operations communicate exclusively through Matrix Client-Server, Federation version, and Synapse Admin HTTP APIs.
- **Local user database or separate RBAC system:** SynapseAdmin.NET stores no local accounts or permissions; authentication and admin authorization are enforced by the target homeserver.
- **Fixed multi-tenant SLAs or offline/air-gapped bundling:** No uptime/concurrency SLAs are mandated, and external font CDN loading (`fonts.googleapis.com`) is accepted for now without offline asset bundling.

## 4. Users & Use Cases

| User / role | Need | Use case |
|---|---|---|
| Homeserver Administrator | Authenticate safely without exposing tokens in URLs or risking SSRF | Log in with Matrix username/password or access token against an allowlisted or public HTTPS homeserver |
| Homeserver Administrator | Monitor server health and resource hotspots at a glance | View server brand/version, user/room/report totals, latest users/reports, largest rooms, and top media uploaders on the dashboard |
| Homeserver Administrator | Manage user lifecycle, permissions, and uploaded media | Search, create, deactivate/erase, renew validity, shadow-login, toggle experimental features, and audit/quarantine/delete user media |
| Homeserver Administrator | Moderate rooms, messages, and media | Inspect room metadata/state/members/messages, purge history, block/delete rooms, manage public directory visibility/aliases, and browse media galleries |
| Homeserver Administrator | Investigate abuse and regulate server growth | Review/dismiss event reports, inspect raw unredacted events by ID, send server notices, and manage registration tokens |
| Homeserver Administrator | Maintain federation and server storage/DB health | Inspect failing federation destinations and reset retry timeouts; pause/resume/trigger DB background updates; purge remote media cache and old local media |

### User Stories

- As a **Homeserver Administrator**, I want to log in using either username/password or an access token so that I can administer my homeserver regardless of how my admin account is provisioned.
- As a **Homeserver Administrator**, I want to inspect and stream user or room media (including video/audio seeking) and quarantine, protect, or delete specific files so that I can moderate content safely without leaking quarantined objects.
- As a **Homeserver Administrator**, I want to inspect unredacted raw event JSON and purge room history up to a message or timestamp in my local timezone so that I can investigate and remediate moderation incidents accurately.
- As a **Homeserver Administrator**, I want destructive actions on detail pages to validate that the displayed entity matches the confirmed target so that rapid navigation never affects the wrong user or room.

## 5. Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-1 | **Authentication & Cookie Bridge:** Support login via Matrix username/password (`m.login.password`) or access token; resolve `.well-known/matrix/client` and `/server`; persist encrypted session info in `ProtectedLocalStorage`; synchronize authentication to `.AspNetCore.Cookies` via a single-use, 60-second, antiforgery-bound session bridge (`POST /Auth/SignIn`); support logout via `/Auth/SignOutAction`. | Must |
| FR-2 | **Homeserver Destination Policy & Rate Limiting:** Enforce HTTPS and refuse internal/private/loopback/link-local IP addresses (including delegated `.well-known` hops, redirects, and resolved socket IPs) unless allowlisted in `HOMESERVER_ALLOWLIST` (failing closed if configured with no valid entries). Throttle password logins per IP and per `(homeserver, username)` (5/min), token logins per IP (5/min), and `/Auth/SignIn` HTTP requests (`"auth"` policy, 10/min). | Must |
| FR-3 | **Capability Detection & Dashboard (`/`):** Probe server brand/version (`Synapse`, `Tuwunel >= 1.8.1`, or `Generic`) and admin API availability (`SupportsAdminApi`). Display homeserver info, totals (users, rooms, reports), latest 5 users, latest 5 reports, largest rooms by estimated DB size, and top 10 media uploaders. | Must |
| FR-4 | **User Management (`/users`, `/users/{UserId}`):** Provide server-side paginated, sortable, searchable user list; create users (`UserCreateDialog`); inspect user details (status, admin/guest role, appservice ID, consent info, memberships); deactivate/erase users; renew account validity (`RenewAccountDialog`); obtain shadow login tokens (`LoginAsUser`); view and toggle experimental features; view server-side paginated user media with separate page vs overall upload totals; quarantine single or all user media, unquarantine, protect/unprotect, and delete individual uploads. | Must |
| FR-5 | **Room Management & History Purge (`/rooms`, `/rooms/{RoomId}`):** Provide server-side paginated, sortable, searchable room list (including Room Type / Spaces); inspect room details (avatar, canonical alias, version, tombstone/replacement room link, join rules, guest access, history visibility, encryption); browse room messages (linked to Event Forensics and message-anchored purge); search/sort room members; inspect state events; block/unblock or delete rooms; purge history up to an event ID or local-timezone timestamp (`PurgeHistoryDialog`) with live purge status polling. | Must |
| FR-6 | **Room Media Auditing & Gallery (`/rooms/{RoomId}`, `/rooms/{RoomId}/gallery`):** List deduplicated local and remote room media (exact ordinal `mxc://` matching); switch between table view and card gallery view or open the dedicated gallery page; filter by search text, source (`all`/`local`/`remote`), and type (`all`/`image`/`video`/`audio`); lazy-load items via `IntersectionObserver` infinite scroll; preview, download, quarantine/unquarantine, protect/unprotect, or delete items; bulk-quarantine room media. | Must |
| FR-7 | **Public Room Directory & Aliases (`/room-directory`):** Browse, search, and token-paginate public directory rooms; publish/remove rooms (`PublishRoomDialog`); add and remove local room aliases (`RoomAliasesDialog`). | Should |
| FR-8 | **Authorized Media Streaming & Bulk Maintenance (`/Media/*`, `/media-maintenance`):** Stream avatars, previews, and downloads via `MediaController` using `.AspNetCore.Cookies` with `Cache-Control: private, no-cache`, existence-checked `ETag` (`304`), upstream HTTP range forwarding (`206`/`416`), `X-Content-Type-Options: nosniff`, and sandboxed CSP for SVG/HTML/non-media previews. Provide bulk remote media cache purging (by age) and local media deletion (by age, minimum size MB, and optional profile preservation). | Must |
| FR-9 | **Event Reports & Event Forensics (`/reports`, `/event-forensics`):** Provide server-side paginated, searchable event reports list with report dismissal and forensics links; fetch and display raw unredacted Matrix event payloads by Event ID with metadata links (sender, room, timestamp) and JSON clipboard copy. | Must |
| FR-10 | **Registration Tokens (`/tokens`):** List, create (`RegistrationTokenDialog` with optional custom token, max uses, and expiration), edit, copy to clipboard, and delete registration tokens. | Should |
| FR-11 | **Federation Destinations (`/federation`):** Display server-side paginated, sortable, searchable federation destinations showing OK/Failing status, last retry, retry interval, and failure time; reset connection retry timeout for failing destinations. | Should |
| FR-12 | **Server Notices (`/notices`):** Validate inputs and send server notice messages (`m.text`) to a specified Matrix user ID. | Should |
| FR-13 | **Background Updates (`/background-updates`):** Display DB background updates enabled/paused state and active update metrics; pause/resume updates and manually trigger `populate_stats_process_rooms` and `regenerate_directory` jobs. | Should |
| FR-14 | **Localization, Timezone & Theming:** Support `en-US` (default), `de-DE`, and `fr-FR` via `CultureController.Set` (`.AspNetCore.Culture` cookie) and RTL layout handling; detect browser IANA timezone (`TimeZoneService`) for rendering timestamps and DST-safe input conversion; support 12 reflection-discovered MudBlazor themes (`IAppTheme`) plus dark/light mode persisted in `ProtectedLocalStorage`. | Should |

## 6. Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-1 | Performance | Media streams must be relayed from upstream response headers without full-file server buffering (`ResponseOwningStream`), honoring upstream `Range` requests. Large tables (`/users`, `/rooms`, `/reports`, `/federation`, user media) must use server-side pagination, and route changes must cancel superseded in-flight requests. |
| NFR-2 | Security / privacy | Authentication cookies must set `HttpOnly`, `SameSite=Lax`, and `CookieSecurePolicy.Always`. Data Protection keys on disk must be encrypted with AES-256-GCM when `DP_PASSPHRASE` is set. Untrusted strings must be sanitized before logging (`.SanitizeForLogging()`), and technical exception details must never be shown to users. Container runs as non-root `app` (`UID 1654`). |
| NFR-3 | Availability | Application restarts must preserve valid user sessions when `./keys` and `DP_PASSPHRASE` are persisted. Multi-replica deployments must support sticky sessions and shared `/app/keys`. Non-admin homeservers must degrade gracefully with localized capability alerts rather than crashing. |
| NFR-4 | Accessibility | UI must use MudBlazor Material Design components, semantic headings with focus management (`FocusOnNavigate`), RTL support (`MudRTLProvider`), and responsive breakpoints across desktop and mobile viewports. |
| NFR-5 | Compatibility | Targets .NET 10 (`net10.0`), modern desktop/mobile browsers with WebSockets and ES modules, `linux/amd64` and `linux/arm64` Docker images, Matrix Synapse, and Tuwunel >= 1.8.1 (including JSON/timestamp/field-naming compatibility workarounds). |

## 7. Acceptance Criteria

- **AC-1 (FR-1, Auth Bridge):** Given an authenticated login in a Blazor circuit, when `AuthCookieService.EstablishCookieSessionAsync` creates a bridge key, then `SessionBridgeService` binds it to the circuit's antiforgery token, `POST /Auth/SignIn` redeems it atomically at most once within 60 seconds via request body (never URL query), and issues `.AspNetCore.Cookies` (`AuthCookieServiceTests`).
- **AC-2 (FR-2, SSRF Policy):** Given an empty `HOMESERVER_ALLOWLIST`, when a user enters a public `https://` homeserver, discovery proceeds; when a user enters or a `.well-known`/redirect/DNS resolution targets `http://` or an internal/private/loopback/link-local IP, discovery fails with `HomeserverNotAllowedException` and shows a localized error without leaking technical details (`HomeserverDestinationPolicyTests`).
- **AC-3 (FR-2, Rate Limiting):** Given 5 failed password login attempts within 1 minute for the same IP or `(homeserver, username)`, when a 6th attempt occurs, then `LoginRateLimiter` blocks the attempt and returns `TooManyLoginAttempts`; token logins throttle per IP only (`SecurityInfrastructureTests`).
- **AC-4 (FR-4, User Media Pagination & Totals):** Given a user with uploaded media, when the User Details Media tab loads, then it requests page `from = page * pageSize` ordered `created_ts`/`b`, queries overall totals via localpart search on `/_synapse/admin/v1/statistics/users/media`, and labels page statistics separately from overall totals (`UserMediaPaginationTests`, `UserDetailsMediaTabTests`).
- **AC-5 (FR-5, FR-4, Stale-Selection Guard):** Given `RoomDetails` or `UserDetails` navigating from entity A to entity B, when A's request finishes late or B's load fails, then A's data and action buttons are never displayed and destructive confirmations target only the currently displayed entity ID (`RoomDetailsStaleSelectionTests`, `UserDetailsStaleSelectionTests`).
- **AC-6 (FR-6, Media Deduplication):** Given a room whose `/_synapse/admin/v1/room/{roomId}/media` response contains duplicate `mxc://` URIs, when rendered in `RoomDetails` or `RoomGalleryGrid`, then each unique `mxc://` URI (compared ordinally) appears once preserving first-seen order (`RoomMediaDeduplicationTests`, `RoomGalleryDuplicateMediaTests`).
- **AC-7 (FR-8, Conditional & Ranged Media):** Given a request to `/Media/Download`, `/Media/Preview`, or `/Media/Avatar`, when `If-None-Match` matches (including `*`), the controller validates authorization and upstream media existence before returning `304 Not Modified` (returning `404` if missing/quarantined); when a valid single `bytes` `Range` is supplied, it forwards the range upstream and relays `206 Partial Content` with `Content-Range` (`MediaControllerConditionalTests`, `MediaRangeForwardingTests`).
- **AC-8 (FR-14, Timezone & DST):** Given an IANA browser timezone, `TimeZoneService` converts timestamps to local time, advances invalid DST spring-forward times by 1 hour, and resolves ambiguous DST fall-back times to the earlier occurrence (`TimeZoneAndPurgeTimestampTests`).
- **AC-9 (NFR-5, Synapse Quirks):** Given numeric `next_token` values, millisecond `creation_ts` on v3 user lists vs second `creation_ts` on v2 user details, and `avatar` keys on room admin responses, deserialization and mapping complete without errors (`JsonStringConverterTests`, `MappingExtensionsTests`).

## 8. User Experience / Interface

- **Layout (`MainLayout` & `NavMenu`):** Persistent MudBlazor top app bar with drawer toggle, `ThemeSelector`, `LanguageSelector`, and dark/light mode toggle; collapsible navigation drawer linking to all functional areas plus Login/Logout and About.
- **Page Routes (`src/SynapseAdmin/Components/Pages/`):**
  - Public/Auth: `/login`, `/about`, `/Error`, `/not-found`
  - Authenticated Admin/Portal Routes (`@attribute [Authorize]`): `/`, `/users`, `/users/{UserId}`, `/rooms`, `/rooms/{RoomId}`, `/rooms/{RoomId}/gallery`, `/room-directory`, `/reports`, `/event-forensics`, `/federation`, `/notices`, `/tokens`, `/background-updates`, `/media-maintenance`
- **Modal Dialogs:** `UserCreateDialog`, `RenewAccountDialog`, `PurgeHistoryDialog`, `PublishRoomDialog`, `RoomAliasesDialog`, `RegistrationTokenDialog`, `MediaPreviewDialog`, plus MudBlazor confirmation dialogs for destructive actions.
- **HTTP Controller Endpoints:** `POST /Auth/SignIn`, `GET /Auth/SignOutAction`, `GET /Media/Avatar`, `GET /Media/Preview`, `GET /Media/Download`, `GET /Culture/Set`.

## 9. Data & Integrations

- **Data involved:** Matrix users, memberships, media metadata/binaries, rooms, room state/messages, public directory entries, event reports, raw Matrix events, registration tokens, federation destination metrics, background update statuses, session claims, and UI preferences (theme, dark mode, culture).
- **Systems affected / integrations:** Target Matrix homeservers (Synapse, Tuwunel >= 1.8.1, or generic Matrix servers), `LibMatrix` / `ArcaneLibs` SDK submodules, Google Fonts CDN (`fonts.googleapis.com`), and host filesystem volumes (`./keys`, `./logs`). Detailed data flows and caching TTLs are documented in [ARCHITECTURE.md](./ARCHITECTURE.md).
- **Personal data (PII) & GDPR considerations:** Matrix user IDs, display names, message/event contents, media files, access tokens, and client IP addresses transit the application. No local database is maintained; passwords are never stored; tokens/claims are encrypted via ASP.NET Core Data Protection (AES-256-GCM with `DP_PASSPHRASE`); logs default to 7-day daily rotation with untrusted fields sanitized. Any additional retention, log aggregation, or GDPR compliance controls are managed by the deploying homeserver administrator.

## 10. Edge Cases & Error Handling

- **Silent cancellation:** `OperationCanceledException` returns `OperationResult.Cancelled()` (`Severity.Normal`), suppressing error snackbars in Blazor components during navigation or rapid reloads.
- **Safe error messaging:** Exception messages, stack traces, and `HomeserverNotAllowedException` technical reasons are logged server-side only; users receive localized friendly messages from `SharedResources`.
- **Plain HTTP cookie rejection:** Non-localhost HTTP access cannot store `CookieSecurePolicy.Always` cookies; `NavMenu` warns the user (`HttpsRequiredForMediaWarning`) and skips cookie sync, while `AuthController.SignIn` logs a warning.
- **Missing client IP:** If a reverse proxy omits client IP (e.g., Unix socket) or is not in `TRUSTED_PROXIES`, `ClientIdentityCircuitHandler` and `MatrixSessionService` log a warning and fall back to the shared `"unknown"` rate-limit bucket rather than per-circuit random keys.
- **Media download endpoint fallback:** `MatrixGatewayBase.OpenMediaAsync` calls `/_matrix/client/v1/media/download` and falls back to `/_matrix/media/v3/download` only on `M_UNRECOGNIZED` (or bare `404`/`405`), never on `M_NOT_FOUND`.
- **Synapse/Tuwunel inconsistencies:** Handled via `SynapseCompatibilityJsonOptions` (`JsonStringConverter`), separate v3/v2 `creation_ts` mapping, `avatar` vs `avatar_url` DTO mapping, `RoomService` name caching for `/statistics/database/rooms`, localpart search for `/statistics/users/media`, and ordinal `mxc://` deduplication.

## 11. Dependencies & Constraints

- **Framework & packages:** `.NET 10` (`net10.0`), `MudBlazor` `9.12.0`, `MudBlazor.Translations` `3.6.0`, `Serilog.AspNetCore` `10.0.0`, `Serilog.Sinks.File` `7.0.0`.
- **Submodules:** `LibMatrix` (`SynapseAdmin.NET` branch) and `ArcaneLibs` (both AGPL-3.0); must not be modified locally for Synapse Admin quirks.
- **Deployment constraints:** Docker container runs as non-root `app` (`UID 1654`) requiring writable `./keys` and `./logs` mounts; remote deployments require a TLS-terminating reverse proxy forwarding `X-Forwarded-For` and `X-Forwarded-Proto`; multi-replica setups require sticky sessions and shared `/app/keys` with identical `DP_PASSPHRASE`.
- **Test dependencies:** xUnit `2.9.3`, bUnit `2.15.2`, `Microsoft.AspNetCore.Mvc.Testing` `10.0.0`.

## 12. Success Metrics

| Metric | Baseline | Target | How measured |
|---|---|---|---|
| Build & automated test suite | Current `tests/SynapseAdmin.Tests` suite | 100% build & test pass on every PR/push to `main` | `dotnet build` and `dotnet test` in `.github/workflows/dotnet.yml` |
| Static security analysis | CodeQL workflow enabled | Zero unresolved high/critical CodeQL alerts | `.github/workflows/codeql.yml` (`actions`, `csharp`, `javascript-typescript`) |
| Container supply chain | Multi-arch GHCR builds | Signed `linux/amd64` and `linux/arm64` images on `main`/releases | `.github/workflows/docker-image.yml` with Sigstore `cosign` |

## 13. Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Post-login `LibMatrix` `HttpClient` lacks connect-time IP re-checking | Low | Medium | Discovery and password login enforce connect-time IP checks via `HomeserverDiscoveryClient`; post-login requests use validated `WellKnownUris`. |
| Missing or weak `DP_PASSPHRASE` in deployment | Medium | High | Log startup warning if unset; document mandatory unique passphrase and rotation impact in `README.md` and `docker-compose.yml`. |
| Misconfigured `TRUSTED_PROXIES` behind reverse proxy | Medium | Medium | Default to loopback + RFC 1918 + RFC 4193 with `ForwardLimit = 1`; log warning when client IP is unavailable; document proxy header config. |
| Upstream Synapse / Tuwunel Admin API changes | Medium | Medium | Isolate protocol handling in `SynapseCompatibleAdminGateway` and verify quirks with unit and bUnit tests. |
| Outdated `LibMatrix` / `ArcaneLibs` git submodule | Low | Medium | Track upstream changes manually since Dependabot does not cover git submodules (`.github/dependabot.yml`). |

## 14. Open Questions

- None currently open for the baseline specification. (Use this section when planning future enhancements or breaking changes.)

## 15. Decision Log

| Date | Decision | Reason | Decided by |
|---|---|---|---|
| 2026-11-13 | Set specification scope to the current `SynapseAdmin.NET` project baseline (`Status: Implemented`). | Document the complete existing functional and non-functional contract alongside `ARCHITECTURE.md`. | Benjamin Aicheler |
| 2026-11-13 | Restrict intended target audience strictly to homeserver administrators (non-admin end-user workflows out of scope). | Aligns with product purpose as a server administration tool rather than a Matrix client. | Benjamin Aicheler |
| 2026-11-13 | Leave external hosting, log aggregation/monitoring, and GDPR retention governance to each deploying administrator without fixed SLA/scale mandates. | Self-hosted deployment model leaves infrastructure and compliance policies under operator control. | Benjamin Aicheler |
| 2026-11-13 | Retain external Google Fonts CDN (`fonts.googleapis.com`) in `App.razor` for now. | Offline/air-gapped deployments are not currently targeted. | Benjamin Aicheler |
