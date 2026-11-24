# Implementation Plan: One Gateway Class per Server Type (ADR 0006, Step 1)

| Field | Value |
|---|---|
| **Status** | Approved |
| **Spec** | [SPEC.md](./SPEC.md) §15 (2026-11-24 rows), [ARCHITECTURE.md](./ARCHITECTURE.md) ADR 0006 |
| **Issue / Branch** | none (user's decision) / `ai/feature/split-gateways-per-server` |
| **Author(s)** | Benjamin Aicheler (AI-assisted) |
| **Created** | 2026-11-24 |
| **Last updated** | 2026-11-24 |

## 1. Summary of Approach

This is the first of the three PRs the 2026-11-24 decisions call for. It doesn't change behavior, and follows [REFACTOR_RULES.md](./REFACTOR_RULES.md).

- **Rename:** `SynapseCompatibleAdminGateway` becomes `SynapseAdminGateway`, the class that implements the Synapse Admin API contract.
- **New class:** `TuwunelAdminGateway` derives from it and overrides only `GetRoomListAsync`. That override is the #541 sort-direction fix, which today is a brand check inside the shared gateway.
- **Factory:** `MatrixAuthGateway` picks the class after the probe, using the same brand comparison the gateway uses today. Every homeserver therefore sends the same requests as before.
- **Not in this PR:** the `NotSupportedException` handling (step 2), #542 (step 3) and per-operation version checks (SPEC.md §14).

## 2. Current State Analysis

| Area / file | Current behaviour | Required change |
|---|---|---|
| `Infrastructure/Gateways/SynapseCompatibleAdminGateway.cs` | Public class serving Synapse, Tuwunel and servers of unknown brand that answer the admin API. Its constructor takes `(homeserver, serverBrand = SynapseBrand, serverVersion = UnknownVersion)`. `GetRoomListAsync` reverses `dir` for `RoomSortKeys.IsLargestFirst` keys when `ServerBrand` equals `TuwunelBrand` (`OrdinalIgnoreCase`); this is the only brand check in the gateway code. | Rename the class and file to `SynapseAdminGateway`. Remove the brand check, so `GetRoomListAsync` always sends the `dir` it was given. |
| `Infrastructure/Gateways/MatrixAuthGateway.cs` (`CreateAuthenticatedGatewayAsync`) | A discovered Synapse returns `new SynapseCompatibleAdminGateway(synapse, SynapseBrand, version)`. Any other server whose probe confirms the admin API returns `new SynapseCompatibleAdminGateway(hs, brand, version)`, where `brand` is the federation-reported name or `GenericBrand`. Otherwise it returns `GenericMatrixGateway`. | The Synapse path returns `SynapseAdminGateway`. The admin path returns `TuwunelAdminGateway` when `brand` equals `TuwunelBrand` (`OrdinalIgnoreCase`, the same comparison as today), otherwise `SynapseAdminGateway`. |
| new `Infrastructure/Gateways/TuwunelAdminGateway.cs` | — | `internal sealed class TuwunelAdminGateway(homeserver, serverBrand = TuwunelBrand, serverVersion = UnknownVersion) : SynapseAdminGateway`. It keeps the brand casing the server reports, which the dashboard shows. It overrides `GetRoomListAsync`: reverse `dir` for largest-first keys, then call `base`. |
| `Infrastructure/Helpers/RoomSortKeys.cs`, `Infrastructure/Gateways/GatewayConstants.cs` | Their doc comments name `SynapseCompatibleAdminGateway`. | Update the comments. |
| `tests/…/SynapseCompatibleGatewayCharacterizationTests.cs` (45 references) | Wire-level tests of the gateway. `GetRoomList_SendsTheDirThatGivesTheDocumentedOrder` builds the shared gateway with a brand string, including a lower-case `"tuwunel"` row. | Rename the class and file to `SynapseAdminGatewayCharacterizationTests`. Tuwunel rows build `TuwunelAdminGateway`. Synapse and generic-brand rows build `SynapseAdminGateway`. The lower-case row moves to the factory test (3.1), because the factory now does the brand comparison. |
| `tests/…/MatrixAuthGatewayProbeTests.cs` | `Assert.IsType<SynapseCompatibleAdminGateway>` in 3 places; the default `FakeHomeserver` is a Tuwunel 1.9.0. | Rename the type in the assertions. Add the class assertions and the end-to-end room-list test (1.1). |
| `tests/…/UserMediaPaginationTests.cs` | `GatewayProbe : SynapseCompatibleAdminGateway`, and calls to the static `Build*Url` test hooks. | Rename only. |
| AGENTS.md, ARCHITECTURE.md | 7 and 10 mentions of the old name. The contract rule says the Tuwunel corrections are "brand checks inside `SynapseCompatibleAdminGateway`" until the split. ADR 0006 has status "Accepted (implementation planned)". | Update on completion (Phase 4). `docs/archive/` and `reviews/` keep the old name, because they are historical records. |

Not affected:
- **No configuration, DI registration or logger category** uses the class name: the factory constructs it, and `Program.cs` registers only `MatrixAuthGateway`.
- **No persisted data** contains it: the gateway is cached only in `IMemoryCache`.
- **"Dynamically Wired Code"** doesn't list it.
- **The UI** checks only `SupportsAdminApi`, which stays `true` for both classes.

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: Inheritance, `TuwunelAdminGateway : SynapseAdminGateway` | Overrides exactly the deviating methods; everything else is inherited; matches the existing `MatrixGatewayBase` virtual methods. | A subclass can override a method and break the contract; the tests have to catch that. | ✅ chosen (ADR 0006) |
| B: Decorator around `IMatrixGateway` | Could combine corrections for several servers. | Forwards about 60 members to change one, and no correction is shared between servers. | ❌ rejected (ADR 0006) |
| C: Keep `SynapseCompatibleAdminGateway` as the name | Smaller diff. | The name says "compatible", but the class is now the Synapse contract itself, and the subclass is the compatible one. | ❌ rejected (ADR 0006 names `SynapseAdminGateway`) |
| D: Make `TuwunelAdminGateway` public | Matches the base class. | Nothing outside the assembly needs it; `GenericMatrixGateway` is `internal` as well; tests reach it through `InternalsVisibleTo`. | ❌ rejected |

## 4. Technical Design

### Components affected

- `MatrixAuthGateway` (factory), `SynapseAdminGateway` (renamed), `TuwunelAdminGateway` (new), and the doc comments of `RoomSortKeys` and `GatewayConstants`.

### Data model changes

- None.

### API / interface changes

- **`IMatrixGateway` doesn't change.** The public type `SynapseCompatibleAdminGateway` is renamed. Only the factory and the tests refer to it; nothing outside the assembly does.
- **Constructor:** `SynapseAdminGateway` keeps the current signature, including the `serverBrand` parameter, so the dashboard keeps showing the federation-reported brand. It doesn't use the brand for any decision anymore.
- **What a caller would notice:** `new SynapseAdminGateway(hs, "Tuwunel")` no longer reverses `dir`. Only tests construct it with a Tuwunel brand, and they switch to `TuwunelAdminGateway`.
- **Request helpers:** the private helpers stay `private`. The Tuwunel override needs none of them, so they become `protected` only when an override needs one.

### Configuration / feature flags

- None.

## 5. Implementation Steps

Each phase is one commit, and each commit builds and passes `dotnet test`. The REFACTOR_RULES require separating mechanical and structural changes, so Phases 2 and 3 are separate commits. The byte-identical DLL check doesn't apply, because the type name changes.

### Phase 1: Characterization tests on `main`'s code

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | Add an end-to-end test through the factory. `FakeHomeserver` in `MatrixAuthGatewayProbeTests` records the target of `GET /_synapse/admin/v1/rooms` and answers with an empty list. A theory over the probe outcomes sorts the result by `joined_members` with `dir=f` and asserts the `dir` sent. Expected: `b` when the federation reports `Tuwunel` or `tuwunel`; `f` when it reports `Synapse`, when the discovery reports Synapse, and when the federation probe fails (unknown brand). | `tests/SynapseAdmin.Tests/MatrixAuthGatewayProbeTests.cs` | The test passes against the current code. Then make it fail on purpose, by removing the reversal in memory and restoring it in memory, never with `git checkout`, to show that it guards the behavior. |

### Phase 2: Mechanical rename

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | `git mv` the gateway file and rename the class `SynapseCompatibleAdminGateway` → `SynapseAdminGateway` in `src/` (gateway, factory, the two doc comments) and `tests/`. | the 7 files from §2 | `dotnet build` has 0 warnings and errors; `dotnet test` passes unchanged; `grep -rn SynapseCompatibleAdminGateway src tests` is empty. |
| 2.2 | `git mv` the test class and file to `SynapseAdminGatewayCharacterizationTests`. Before renaming, search `tests/` for the old name in quotes (AGENTS.md "Tests Bound to Private Names"). | `tests/SynapseAdmin.Tests/SynapseCompatibleGatewayCharacterizationTests.cs` | Same test count as before. |

### Phase 3: Structural split

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 3.1 | Add `TuwunelAdminGateway` with the `GetRoomListAsync` override, and remove the brand check from `SynapseAdminGateway.GetRoomListAsync`. | `Infrastructure/Gateways/TuwunelAdminGateway.cs`, `SynapseAdminGateway.cs` | — |
| 3.2 | The factory returns `TuwunelAdminGateway` for the Tuwunel brand and `SynapseAdminGateway` otherwise. | `MatrixAuthGateway.cs` | The 1.1 test passes unchanged. |
| 3.3 | Tests: the probe tests assert the exact class. Tuwunel (both casings) gets `TuwunelAdminGateway`. Synapse via federation or discovery, and an unknown brand, get `SynapseAdminGateway`. Without the admin API, the result stays `GenericMatrixGateway`. Split `RoomListDirs` by class as described in §2. | `MatrixAuthGatewayProbeTests.cs`, `SynapseAdminGatewayCharacterizationTests.cs` | `dotnet test` passes. `grep -rn ServerBrand src/SynapseAdmin` finds no comparison, only the declarations and the dashboard's display. |

### Phase 4: Documentation

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 4.1 | AGENTS.md: rename the class everywhere. Rewrite the end of "Synapse Admin API as the Contract": a server's deviation goes into an override in its own gateway class (`TuwunelAdminGateway`), and the factory picks the class. "Room Size Semantics" and "Tuwunel & Generic Fallback" name `TuwunelAdminGateway`. The `SynapseCompatibleGatewayCharacterizationTests` reference becomes the new name. | `AGENTS.md` | No `SynapseCompatible` left outside `docs/archive/` and `reviews/`. |
| 4.2 | ARCHITECTURE.md: rename the class in §2, §3 (diagram, table, layering rules), §5 (sequence diagram, capability flow), §6 and §10. ADR 0006 becomes "Accepted". ADR 0001 says that today's `SynapseAdminGateway` is not the SDK-based class removed in #397. | `ARCHITECTURE.md` | Links and anchors still resolve. |
| 4.3 | Reset PLAN.md and TODO.md to the templates from `main`. | `PLAN.md`, `TODO.md` | `git diff main -- PLAN.md TODO.md` is empty. |

## 6. Testing Strategy

- **Unit tests:** the factory test (1.1) runs the probe end to end against a fake homeserver and is independent of the class names, so it shows the behavior before and after. The class assertions (3.3) and the split `RoomListDirs` rows check the new structure. All other gateway tests run unchanged against the renamed class.
- **bUnit / integration tests:** none affected. The UI and controllers see only `IMatrixGateway`.
- **Manual / homeserver tests:** none needed for a change that keeps the same requests. If a Tuwunel instance is available, sort the room list by Members both ways and check that the order is still correct.
- **Test data:** fake homeservers on `127.0.0.1` only; no real tokens or user data.
- **Acceptance criteria:** AC-13 (room list sorting, #541) stays covered by 1.1 and the `RoomListDirs` rows.

## 7. Rollout & Rollback

- **Deployment approach:** normal release.
- **Migration steps:** none. A gateway cached in `IMemoryCache` doesn't survive a restart, and nothing persists the type name.
- **Rollback plan:** revert the PR.
- **Monitoring after release:** none beyond the usual logs.

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| The factory compares the brand differently from the old check (e.g. case-sensitive) and a Tuwunel server gets `SynapseAdminGateway`, so the Members sort is reversed again. | Use the same `string.Equals(…, OrdinalIgnoreCase)`; 1.1 covers `Tuwunel` and `tuwunel` end to end. |
| A server of unknown brand (federation disabled) behaved like Synapse before and must still do so. | 1.1 and 3.3 cover the unresolved brand. |
| A test reaches a renamed member by string and fails only at run time. | Search `tests/` for the old names in quotes before renaming (2.2). |
| The rename diff grows past the 200-line guideline of REFACTOR_RULES. | About 55 renamed lines in `src/` and `tests/`, plus 17 doc mentions in Phase 4; separate commits per phase. |

## 9. Open Questions

- [x] Should a GitHub issue be created for this PR (and step 2)? No: the PR goes without one (2026-11-24).
- [x] Rename the test class to `SynapseAdminGatewayCharacterizationTests` (2.2)? Yes (2026-11-24).

## 10. Progress Notes

| Date | Note |
|---|---|
| 2026-11-24 | Plan drafted and approved; no issue, test class renamed. |
