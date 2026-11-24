# Implementation Plan: "Not Supported by This Homeserver" Message (ADR 0006, Step 2)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) §3 Goals, AC-14, §10 "Operation not supported by the homeserver" (all `[Planned]`) |
| **Issue / Branch** | none / `ai/feature/not-supported-message` |
| **Author(s)** | Benjamin Aicheler (AI-assisted) |
| **Created** | 2026-11-24 |
| **Last updated** | 2026-11-24 |

## 1. Summary of Approach

Gateways signal an operation they can't carry out with Synapse's result by throwing a dedicated `OperationNotSupportedByHomeserverException`, which derives from `NotSupportedException`. `GatewayCallExtensions.RunGatewayCallAsync` catches exactly this type and returns the new localized message `OperationNotSupportedByHomeserver`, in EN, DE and FR, instead of the method's own failure key. `GenericMatrixGateway` switches its 49 `NotSupportedException` throws to the new type. Services, pages and the existing failure keys don't change. Step 3 (#542) will be the first `TuwunelAdminGateway` override to throw it.

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Extensions/GatewayCallExtensions.cs` (both overloads) | No session → `NotAuthenticated`. `OperationCanceledException` → `Cancelled()`. Any other exception → `logFailure(ex)` and `Failure(L[failureKey])` (`Severity.Error`). | Add a catch for `OperationNotSupportedByHomeserverException` between the two existing catches: it calls `logFailure(ex)` and returns `Failure(L["OperationNotSupportedByHomeserver"], Severity.Warning)`. |
| `Infrastructure/Gateways/GenericMatrixGateway.cs` | 49 admin methods `=> throw new NotSupportedException()`; two of them have an English message ("Admin APIs are only supported for Synapse.", "Background updates are only supported for Synapse."). | Each throws `new OperationNotSupportedByHomeserverException()`. The two messages go away; the exception names the operation itself. |
| new `Models/OperationNotSupportedByHomeserverException.cs` | — | `public sealed class OperationNotSupportedByHomeserverException([CallerMemberName] string operation = "") : NotSupportedException(…)`. It has an `Operation` property, and its message names the operation, for the log only. Next to `HomeserverNotAllowedException`, the other exception type the service layer handles. |
| `Resources/SharedResources{,.de,.fr}.resx` | — | Key `OperationNotSupportedByHomeserver`: EN "This operation is not supported by this homeserver." · DE "Dieser Vorgang wird von diesem Homeserver nicht unterstützt." · FR "Cette opération n'est pas prise en charge par ce homeserver." (the FR resources already say "homeserver"). |
| Plain `NotSupportedException` from elsewhere | `ResponseOwningStream`, `System.Text.Json` (an unsupported type in a response), `LoginRateLimiter.IsAllowed` and LibMatrix can throw it for reasons that have nothing to do with the homeserver. | Unchanged: it still logs and returns the method's failure key. This is why the catch takes the subclass only. |
| Services (9 files using `RunGatewayCallAsync`), pages, `MatrixSessionService`, `MediaController` | Report what the service returns. | No change. The pages already show the result's message through `SnackbarExtensions`. `MatrixSessionService` and `MediaController` call no admin operation. |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Catch a dedicated subclass of `NotSupportedException` | Only a gateway's deliberate refusal gets the new message. It still is a `NotSupportedException`, as the 2026-11-24 decision says. The operation name reaches the log. | One new type. | ✅ chosen |
| B: Catch every `NotSupportedException` | No new type. | A non-seekable stream, an unsupported JSON type or a LibMatrix limitation would tell the admin the homeserver doesn't support the operation, which is wrong and hides a bug. | ❌ rejected |
| C: Gateways return a "not supported" result instead of throwing | No exception for an expected case. | Changes the return type of every `IMatrixGateway` method, and contradicts the decision. | ❌ rejected |
| D: Log a refusal through a logger in `RunGatewayCallAsync` at `Warning` instead of `logFailure` | Lower log level for an expected outcome. | Needs a new parameter at about 50 call sites and loses each method's log template (which user, which room). | ❌ rejected: `logFailure` keeps the context. |

## 4. Technical Design

### Components affected

- `GatewayCallExtensions`, `GenericMatrixGateway`, the new exception, and the three `.resx` files.

### Data model changes

- None.

### API / interface changes

- **New public exception type**, `OperationNotSupportedByHomeserverException`. `IMatrixGateway` doesn't change. Its documentation says that a gateway throws this type for an operation it can't carry out with Synapse's result.
- **Behavior change:** an admin operation on a homeserver without the admin API now shows "This operation is not supported by this homeserver." instead of the operation's own error message (SPEC.md §10). The pages normally hide these operations (`SupportsAdminApi`), so this is rarely visible today. Step 3 makes it visible for Tuwunel's bulk local media deletion.
- **Severity:** `Warning`, not `Error`, because nothing failed: the server just doesn't offer the operation. Like every non-`Normal` result, it still shows a snackbar.

### Configuration / feature flags

- None.

## 5. Implementation Steps

The tests are written first and fail until the code below exists (TDD). Each commit builds and passes the tests.

### Phase 1: Exception and call policy

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Tests in `GatewayCallExtensionsTests`, for both overloads (with and without data). The new exception → message `OperationNotSupportedByHomeserver`, `Severity.Warning`, logged once. A plain `NotSupportedException` → the failure key, `Severity.Error` (unchanged). Also: the exception's `Operation` is the caller's member name. | `tests/SynapseAdmin.Tests/GatewayCallExtensionsTests.cs` | The tests fail (red). |
| 1.2 | Add the exception type and the catch in both overloads. Update the XML summary of `GatewayCallExtensions`. | `Models/OperationNotSupportedByHomeserverException.cs`, `Extensions/GatewayCallExtensions.cs` | 1.1 passes. |
| 1.3 | Add the resource key in EN, DE and FR. Add a test that the key resolves to a non-key value in `en-US`, `de-DE` and `fr-FR` through the real `ResourceManager`. | `Resources/SharedResources*.resx`, a resource test next to the existing localization tests | The test passes in all three cultures. |

### Phase 2: Generic gateway

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | Add a test that calls every method `GenericMatrixGateway` declares through reflection, with default arguments, against a homeserver URL that isn't contacted. It expects `OperationNotSupportedByHomeserverException`, whose `Operation` is the method's name. Today the methods throw a plain `NotSupportedException`, so the test fails. | new `tests/SynapseAdmin.Tests/GenericMatrixGatewayTests.cs` | The test fails (red). |
| 2.2 | Replace the 49 throws. | `Infrastructure/Gateways/GenericMatrixGateway.cs` | 2.1 passes; `grep "new NotSupportedException" GenericMatrixGateway.cs` finds none. |

### Phase 3: End to end through a service

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | In `MediaServiceCharacterizationTests`, `DeleteLocalMediaAsync` against a gateway stub faulted with the new exception (`Stub.Fault`) → failure with `OperationNotSupportedByHomeserver`, `Severity.Warning`. This is the operation step 3 will refuse on Tuwunel. | `tests/SynapseAdmin.Tests/MediaServiceCharacterizationTests.cs` | Passes. |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | SPEC.md: remove `[Planned]` from §3 Goals, AC-14 and §10. Name the tests in AC-14. | `SPEC.md` | No `[Planned]` left for this feature. |
| 4.2 | AGENTS.md: in "Error Handling & Silent Cancellation", add that `OperationNotSupportedByHomeserverException` returns `OperationNotSupportedByHomeserver` (`Warning`). "Synapse Admin API as the Contract" names the exception type instead of saying the message is planned. ARCHITECTURE.md: the same in §8 Error handling and in the layering rule. | `AGENTS.md`, `ARCHITECTURE.md` | — |
| 4.3 | Reset PLAN.md and TODO.md to the templates from `main`. | `PLAN.md`, `TODO.md` | `git diff main -- PLAN.md TODO.md` is empty. |

## 6. Testing Strategy

- **Unit tests:** `GatewayCallExtensionsTests` (policy, both overloads, the subclass versus a plain `NotSupportedException`), `GenericMatrixGatewayTests` (every declared method refuses, with its own name), `MediaServiceCharacterizationTests` (one service end to end), and the resource test (three cultures).
- **bUnit tests:** none new. The pages show `result.Message` unchanged, and `SnackbarExtensions` tests already cover `Warning`.
- **Manual / homeserver tests:** none; no server is involved until step 3.
- **Test data:** stubs and fake URLs only.
- **Acceptance criteria:** AC-14 → 1.1, 2.1 and 3.1. The fact that "the page shows the same labels" follows from nothing in the UI changing.

## 7. Rollout & Rollback

- **Deployment approach:** normal release.
- **Migration steps:** none.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** refusals appear in the log under each operation's existing failure template, with the exception's operation name.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| A real bug that throws a plain `NotSupportedException` would be reported as "not supported by this homeserver". | Only the dedicated subclass is caught; a test asserts that a plain `NotSupportedException` keeps the failure key. |
| A refusal is logged at `Error` through `logFailure`, which is noisy for an expected outcome. | Accepted (alternative D). Refusals are rare: generic servers hide admin pages, and step 3 adds a single refusal. |
| A new `GenericMatrixGateway` override throws the old plain exception. | The reflection test covers every method the class declares. |

## 9. Open Questions

- [x] Is `Severity.Warning` right for the message? Yes (2026-11-24).
- [x] Is the wording right in all three languages? Yes (2026-11-24).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-24 | Plan drafted and approved: `Warning`, wording as proposed. |
