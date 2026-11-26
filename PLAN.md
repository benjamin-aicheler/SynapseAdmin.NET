# Implementation Plan: <!-- Feature / Task Name -->

<!--
HOW TO USE THIS FILE (instructions for the AI agent):
- This is a SINGLE-TASK working document describing HOW a proposed SPEC.md change will be implemented.
- Before writing: read SPEC.md (including [Planned] deltas), ARCHITECTURE.md and AGENTS.md, and explore the relevant code.
- Overwrite/fill this file for the active task only, propose the plan, and wait for the user's review. Do NOT start coding until Status is "Approved".
- Break work into small, independently verifiable phases/steps. Each step should leave the codebase compiling (`dotnet build`) and tests passing (`dotnet test`).
- Reference concrete files, functions and modules. Mark assumptions clearly.
- Transfer actionable steps into TODO.md once the plan is approved.
- Remove placeholder HTML comments while working on the active plan.
- ON COMPLETION (before merging the PR, or before starting the next task): finalize `[Planned]` changes in SPEC.md (and ARCHITECTURE.md if architecture/flows changed), then reset this file and TODO.md back to their clean templates from `main` (git history preserves finished plans).
-->

| Field | Value |
|---|---|
| **Status** | Draft <!-- Inactive/Template when Draft with placeholders or Done; Active when Approved / In Progress --> |
| **Spec** | [SPEC.md](./SPEC.md) |
| **Issue / Branch** | <!-- #number / ai/feature/... --> |
| **Author(s)** | |
| **Created** | <!-- YYYY-MM-DD --> |
| **Last updated** | <!-- YYYY-MM-DD --> |

## 1. Summary of Approach

<!-- 3–5 sentences: the chosen technical solution in a nutshell. -->

## 2. Current State Analysis

<!-- Relevant existing code, components and patterns. What can be reused? What must change? -->

| Area / file | Current behaviour | Required change |
|---|---|---|
| | | |

## 3. Alternatives Considered

| Option | Pros | Cons | Decision |
|---|---|---|---|
| A: | | | ✅ chosen / ❌ rejected |
| B: | | | |

## 4. Technical Design

### Components affected

- 

### Data model changes

<!-- schema changes, migrations, backward compatibility -->

### API / interface changes

<!-- new/changed endpoints, events, contracts; breaking changes? -->

### Configuration / feature flags

- 

## 5. Implementation Steps

<!-- Small, ordered, verifiable steps. Each step: what, where, how to verify. -->

### Phase 1: <!-- name -->

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 1.1 | | | <!-- e.g. unit tests pass, endpoint returns 200 --> |
| 1.2 | | | |

### Phase 2: <!-- name -->

| # | Step | Files / modules | Verification |
|---|---|---|---|
| 2.1 | | | |

## 6. Testing Strategy

- **Unit & bUnit component tests:** <!-- xUnit services/mappers & DetailPageTestBase bUnit checks (culture-independent) -->
- **Integration tests:** <!-- WebApplicationFactory endpoints, auth bridge, media streaming -->
- **Manual / homeserver tests:** <!-- e.g. Synapse / Tuwunel verification -->
- **Test data:** <!-- no real user PII or production access tokens -->
- **Mapping to acceptance criteria:** <!-- AC-* → test X -->

## 7. Rollout & Rollback

- **Deployment approach:** <!-- e.g. feature flag, staged rollout -->
- **Migration steps:**
- **Rollback plan:**
- **Monitoring after release:**

## 8. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| | |

## 9. Open Questions

- [ ] 

## 10. Progress Notes

<!-- Short dated log of deviations from the plan, discoveries and decisions during implementation. -->

| Date | Note |
|---|---|
| | |
