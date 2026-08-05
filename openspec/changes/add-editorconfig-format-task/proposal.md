# Proposal: Add .editorconfig and `dotnet format` task to close style-drift risk

## Intent

The RetroBox solution has no centralized formatting policy and no CI gate for style. Contributors using different editors produce whitespace, using-order, and brace-style drift that accumulates silently. This change codifies the observed dominant style and adds an automated format check so drift is caught in CI before review.

## Scope

### In Scope
- Root `.editorconfig` codifying the observed C# style (4 spaces, file-scoped namespaces, K&R braces, top-of-file usings, PascalCase public / `_camelCase` private fields)
- Two `mise` tasks: `format` (`dotnet format RetroBox.slnx`) and `format-check` (`dotnet format RetroBox.slnx --verify-no-changes --no-restore`)
- CI gate: `format-check` step in `.github/workflows/build-retrobox.yml` before the existing `test` step, using `--no-restore` after an explicit restore
- First-run formatting applied to all existing `*.cs` files (separate commit)

### Out of Scope
- Enabling `EnableNETAnalyzers` / `AnalysisLevel` — deferred; interacts with `-warnaserror` on `publish-linux-x64` and xUnit snake_case test naming (`CA1707`)
- Analyzer severity tuning or `.globalconfig`
- Coverage thresholds or coverlet configuration
- `Directory.Build.props`, `.config/dotnet-tools.json`, or `.gitattributes`

## Capabilities

### New Capabilities
None — tooling/CI change only, no domain behavior change.

### Modified Capabilities
None.

## Approach

Formatting-only, per exploration Approach 1. The `.editorconfig` captures the dominant style already present in the codebase — no opinion changes. `dotnet format` is built into .NET SDK 10 (already pinned in `mise.toml`), so no tool manifest is needed.

**Two-commit strategy** to keep review focused:
1. **Commit 1 (chore)**: apply `dotnet format` to all existing files — large whitespace/using-order diff, no logic change
2. **Commit 2 (feat)**: add `.editorconfig`, `mise` tasks, and CI wiring

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `.editorconfig` (new) | New | Root formatting rules for C# |
| `mise.toml` | Modified | Add `format` and `format-check` tasks |
| `.github/workflows/build-retrobox.yml` | Modified | Add `format-check` step before `test` |
| `src/**/*.cs`, `tests/**/*.cs` | Modified | First-run formatting (whitespace/usings only) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Large first-run diff obscures review | High | Split into two commits; commit 1 is pure formatting |
| CI time increase from format-check | Low | Use `--no-restore` after explicit restore step |
| Accidental logic change from formatter | Low | `dotnet format` whitespace/usings only; review diff for non-formatting changes |

## Rollback Plan

Revert the three config files (`.editorconfig`, `mise.toml`, `.github/workflows/build-retrobox.yml`). The formatted source files remain valid — formatting is additive, not breaking. No data migration or runtime behavior change.

## Dependencies

- .NET SDK 10 (already pinned via `mise.toml`)
- `RetroBox.slnx` solution file (already exists)

## Success Criteria

- [ ] `.editorconfig` exists at repo root with the codified rules
- [ ] `mise run format-check` exits 0 on a clean tree
- [ ] `mise run format-check` exits non-zero when a file has formatting drift
- [ ] CI `build-retrobox.yml` runs `format-check` before `test` and blocks on drift
- [ ] `mise run test` and `mise run publish-linux-x64` continue to pass unchanged
