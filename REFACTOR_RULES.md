# Strict Refactoring Ruleset

When executing tasks from `REFACTOR_PLAN.md`, you must strictly follow these rules.

## 1. Scope & Execution Boundaries
- **Single-Task Lock:** Execute ONLY the specific task ID assigned in the current prompt. Never perform "drive-by" cleanups in unrelated files.
- **Diff Size Cap:** Keep changes small and focused (target < 200 changed lines per pass). If a task exceeds this, stop and split it into smaller sub-tasks.
- **Separate Mechanical from Structural:** Never mix formatting/import cleanup with logic restructuring in the same pass.

## 2. Behavior & Contract Preservation
- **Public Contracts Are Immutable:** Do NOT change public method signatures, function parameter orders, return types, REST/RPC payloads, CLI flags, database schemas, or config keys unless explicitly authorized in the task's scope.
- **Preserve Defensive Logic:** Never delete null checks, bounds checks, retry loops, fallback paths, or error logging to make code look shorter.
- **No Paradigm Rewrites:** Do NOT rewrite working imperative code into functional pipelines, swap state management patterns, or introduce new third-party dependencies.

## 3. Dead Code & Dynamic Wiring Safety
- **Never Guess on Dynamic Usage:** Do NOT delete classes, interfaces, methods, or endpoints that lack direct static callers if the codebase uses dependency injection, reflection, runtime serialization, event buses, or convention-based routing. Flag them for human review instead.
- **Only Remove Proven Dead Code:** Only delete unreferenced `private` helpers, unused local variables, and unused imports within the target file.

## 4. Test-Gated Workflow (Mandatory)
1. **Pre-Flight Check:** Run the repository's build and test suite BEFORE making any edits. If tests are currently failing, stop and report the failure immediately.
2. **Characterization First:** If the target module lacks unit/integration tests and the task involves structural changes (Phase 2 or Phase 3), write characterization tests that lock in current inputs, outputs, and edge-case behaviors BEFORE modifying production code.
3. **Post-Edit Verification:** After applying changes, run the full build, type-checker, linter, and test suite.
4. **Revert on Failure:** If any test fails or compiler warnings increase, fix the regression immediately or revert your changes—never weaken or delete existing assertions to make tests pass.