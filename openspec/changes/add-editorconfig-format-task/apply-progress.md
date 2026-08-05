# Apply Progress: add-editorconfig-format-task

**Date**: 2026-08-05
**Mode**: Strict TDD (adapted for formatting/CI change)
**Delivery strategy**: ask-on-risk (resolved: single PR, two commits)
**Review budget**: Low risk (~41 changed lines across both commits)

## Commits

| # | SHA | Message | Files |
|---|-----|---------|-------|
| 1 | `28a6eb9` | `chore: apply dotnet format to existing sources` | `src/RetroBox.Core/RetroBoxConfigStore.cs`, `tests/RetroBox.Tests/RetroBoxConfigStoreTests.cs`, `tests/RetroBox.Tests/TestRetroBoxLayout.cs` |
| 2 | `3367ed7` | `feat(ci): add .editorconfig and dotnet format task` | `.editorconfig`, `mise.toml`, `.github/workflows/build-retrobox.yml` |

## TDD Cycle Evidence

| Task | RED (Before Gate) | GREEN (After Implementation) | REFACTOR |
|------|-------------------|------------------------------|----------|
| 1.1 | `.editorconfig` written (test infrastructure) | N/A — config artifact | ➖ None needed |
| 1.2 | `dotnet format --verify-no-changes` → exit 2 (5 files with WHITESPACE drift) | `dotnet format` → exit 0; `--verify-no-changes` → exit 0 | ➖ None needed |
| 1.3 | N/A — review | Diff: 100% trailing-whitespace, zero logic changes | ➖ None needed |
| 1.4 | Safety net: `mise run test` → 117 pass | `mise run test` → 117 pass (unchanged) | ➖ None needed |
| 1.5 | N/A — commit | `28a6eb9` committed | ➖ None needed |
| 2.1 | N/A — verification | No per-project `.editorconfig` overrides; root matches design | ➖ None needed |
| 2.2 | N/A — config | `[tasks.format]` + `[tasks.format-check]` added to `mise.toml` | ➖ None needed |
| 2.3 | N/A — config | `Check formatting` step inserted in CI workflow | ➖ None needed |
| 2.4 | N/A — commit | `3367ed7` committed | ➖ None needed |
| 3.1 | N/A — verification | `mise run format-check` → exit 0 on clean tree | ➖ None needed |
| 3.2 | ✅ RED: stray tab → `format-check` exit 2 (WHITESPACE error) | File restored; exit 0 after restore | ➖ None needed |
| 3.3 | N/A — verification | `mise run format` → exit 0, idempotent on clean tree | ➖ None needed |
| 3.4 | ✅ Proven via 3.2 (CI runs same `mise run format-check` command) | CI workflow structure: `format-check` before `Run tests` | ➖ None needed |
| 3.5 | N/A — verification | CI structure in place; locally proven via 3.1 | ➖ None needed |
| 3.6 | Safety net: `mise run test` → 117 pass | `mise run test` → 117 pass; `format-check` → exit 0 | ➖ None needed |

## Test Summary

- **Pre-change safety net**: 117/117 passing
- **Post-change tests**: 117/117 passing (zero delta)
- **RED drift test (3.2)**: `mise run format-check` exit 2 on stray tab in `src/RetroBox.Cli/Program.cs`
- **Clean tree (3.1)**: `mise run format-check` exit 0
- **Publish**: Failed locally due to missing `llvm-objcopy` on macOS (CI toolchain limitation, not regression)
- **Layers used**: CLI verification (format-check) + unit suite (dotnet test)

## Work Unit Evidence

### Unit 1: Format existing sources (chore commit)

| Evidence | Value |
|----------|-------|
| Focused test command and exact result | `mise run test` → 117 passed, 0 failed, 0 skipped |
| Runtime harness command/scenario and exact result | `mise run publish-linux-x64` → N/A locally (llvm-objcopy missing on macOS); CI smoke-tests binary on ubuntu-latest |
| Rollback boundary | Revert commit `28a6eb9`; formatted source stays valid (additive) |

### Unit 2: .editorconfig + mise tasks + CI gate (feat commit)

| Evidence | Value |
|----------|-------|
| Focused test command and exact result | `mise run format-check` → exit 0 on clean tree |
| Runtime harness command/scenario and exact result | CI `Check formatting` step: `mise run format-check` (same command proven locally) |
| Rollback boundary | Delete `.editorconfig`; revert `mise.toml` + workflow hunks in `3367ed7` |

## Files Changed

| File | Action | What Was Done |
|------|--------|---------------|
| `.editorconfig` | Created | Root rules: `[*]` charset/lf/eol + `[*.cs]` Allman braces, 4-space indent, file-scoped namespaces, `:suggestion`/`:none` severities |
| `mise.toml` | Modified | Added `[tasks.format]` and `[tasks.format-check]` (depends=["restore"]) |
| `.github/workflows/build-retrobox.yml` | Modified | Inserted `Check formatting` step between `Set up mise` and `Run tests` |
| `src/RetroBox.Core/RetroBoxConfigStore.cs` | Modified | Trailing whitespace removal (format-only) |
| `tests/RetroBox.Tests/RetroBoxConfigStoreTests.cs` | Modified | Trailing whitespace removal (format-only) |
| `tests/RetroBox.Tests/TestRetroBoxLayout.cs` | Modified | Trailing whitespace removal (format-only) |

## Deviations from Design

None — implementation matches design exactly. `.editorconfig` block, `mise.toml` additions, and CI step all use verbatim content from design §Interfaces.

## Issues Found

1. **Local `publish-linux-x64` fails on macOS**: `llvm-objcopy` not found. This is a local toolchain limitation — the publish task targets Linux x64 Native AOT, and the CI workflow installs `clang zlib1g-dev binutils` on ubuntu-latest. Not a regression from formatting.
2. **GGA commit hook targets `*.ts,*.tsx,*.js,*.jsx` only**: No `.cs` coverage — correctly skipped both commits. Pre-existing, not caused by this change.
