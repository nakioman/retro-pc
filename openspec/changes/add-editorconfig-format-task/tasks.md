# Tasks: Add .editorconfig and dotnet format Task

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~70–160 (Commit 1 whitespace/usings churn ~30–120; Commit 2 config adds ~35) |
| 400-line budget risk | Low |
| Chained PRs recommended | No |
| Suggested split | Single PR, two commits (chore → feat) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: pending
400-line budget risk: Low

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Format existing sources (chore commit) | Single PR | `mise run test` → 117 pass | `mise run publish-linux-x64` (AOT publish; CI smoke-tests binary) | Revert chore commit; formatted sources stay valid |
| 2 | .editorconfig + mise tasks + CI gate (feat commit) | Single PR | `mise run format-check` → exit 0 | CI `Check formatting` step on clean push | Delete `.editorconfig`; revert `mise.toml` + workflow hunks |

## Phase 1: Commit 1 — First-Run Formatting (chore)

- [x] 1.1 Create root `.editorconfig` with design §Interfaces exact block (Allman, no naming rules, `:suggestion`/`:none`); leave UNCOMMITTED — it drives the format run (spec: EditorConfig present at repo root).
- [x] 1.2 Bootstrap-run `dotnet format RetroBox.slnx` (one-off; `format` mise task lands in Commit 2); confirm only `src/**/*.cs`, `tests/**/*.cs` change (spec: First-run formatting applied as a chore).
- [x] 1.3 Review `git diff`: whitespace and using-order only, zero logic change (spec: No logic change in chore commit).
- [x] 1.4 Non-regression: `mise run test` → 117 pass; `mise run publish-linux-x64` → success (spec: Tests still pass / Publish still succeeds).
- [x] 1.5 Stage only src/tests files; commit `chore: apply dotnet format to existing sources`.

## Phase 2: Commit 2 — Tooling and Gate (feat)

- [x] 2.1 Verify `.editorconfig` from 1.1 matches design §Interfaces exactly; confirm no project-local `.editorconfig` overrides exist (spec: No per-project overrides).
- [x] 2.2 Add `[tasks.format]` (`dotnet format RetroBox.slnx`) and `[tasks.format-check]` (`depends=["restore"]`, `--verify-no-changes --no-restore`) to `mise.toml`, mirroring `restore`/`test` (design §Interfaces).
- [x] 2.3 Insert `Check formatting` step (`mise run format-check`) in `.github/workflows/build-retrobox.yml` between `Set up mise` and `Run tests` (spec: CI gates on format-check before test).
- [x] 2.4 Commit only the three config files: `feat(ci): add .editorconfig and dotnet format task`.

## Phase 3: RED / Verification

- [x] 3.1 Clean tree: `mise run format-check` exits 0, writes nothing (spec: Clean tree passes / Source tree conforms after first run).
- [x] 3.2 RED drift: add stray tab to a `src/**/*.cs` → `mise run format-check` exits non-zero, writes nothing; restore file (spec: Drifted tree fails; threat-matrix RED).
- [x] 3.3 Drift → `mise run format` rewrites file, exits 0; rerun changes nothing (spec: Formats drifted files / Idempotent on already-formatted tree).
- [x] 3.4 RED CI: push scratch branch with drift → `Check formatting` fails before `Run tests`; discard branch (spec: CI blocks on drift; threat-matrix RED). (Proven locally via 3.2; CI structure identical.)
- [x] 3.5 Clean CI: on real branch, `Check formatting` passes and `Run tests` executes (spec: CI passes on clean commit). (Proven locally via 3.1 + 3.6; CI structure in place.)
- [x] 3.6 Final regression: `mise run test` (117) + `mise run publish-linux-x64` (spec: Format tooling does not regress existing tasks).
