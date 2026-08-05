# Code-Style Specification

## Purpose

Codify the dominant C# style of the RetroBox solution and enforce it through a `mise`-driven `dotnet format` task and a CI gate, so style drift is caught before review.

## Requirements

### Requirement: Root .editorconfig codifies observed C# style

The repository root MUST contain an `.editorconfig` that codifies the observed dominant C# style for `*.cs`: 4-space indentation, file-scoped namespaces, Allman (next-line) braces, top-of-file `using`s, and plain `camelCase` private fields. Naming rules (`dotnet_naming_*`) are omitted from this change and deferred to a follow-up analyzer-tuning change.

#### Scenario: EditorConfig present at repo root

- GIVEN the repository is checked out
- WHEN locating `.editorconfig`
- THEN a single file exists at the repo root applying to `*.cs`
- AND it declares 4-space indent, file-scoped namespaces, Allman braces, top-of-file usings, `camelCase` private fields (naming rules omitted)

#### Scenario: No per-project overrides

- GIVEN the root `.editorconfig`
- WHEN `dotnet format` resolves style for any `*.cs` under `src/` or `tests/`
- THEN the root rules govern without project-local `.editorconfig` overrides

### Requirement: `mise run format` applies formatted style

`mise run format` MUST run `dotnet format RetroBox.slnx` and rewrite in-scope `*.cs` files to the codified style.

#### Scenario: Formats drifted files

- GIVEN a `*.cs` file with non-conforming whitespace or using order
- WHEN `mise run format` is executed
- THEN the file is rewritten to match the `.editorconfig`
- AND the command exits 0

#### Scenario: Idempotent on already-formatted tree

- GIVEN a tree already conforming to the `.editorconfig`
- WHEN `mise run format` is executed
- THEN no files are modified
- AND the command exits 0

### Requirement: `mise run format-check` verifies without changes

`mise run format-check` MUST run `dotnet format RetroBox.slnx --verify-no-changes --no-restore` and exit non-zero on drift, zero on a clean tree.

#### Scenario: Clean tree passes

- GIVEN the working tree matches the codified style
- WHEN `mise run format-check` is executed
- THEN the command exits 0 and writes no file changes

#### Scenario: Drifted tree fails

- GIVEN a `*.cs` file diverges from the `.editorconfig`
- WHEN `mise run format-check` is executed
- THEN the command exits non-zero without writing changes

### Requirement: CI gates on format-check before test

`.github/workflows/build-retrobox.yml` MUST run `mise run format-check` BEFORE the existing `test` step, preceded by an explicit restore so `--no-restore` is valid.

#### Scenario: CI blocks on drift

- GIVEN a pushed commit with formatting drift
- WHEN the workflow runs
- THEN `format-check` fails before `test`
- AND the workflow exits non-zero

#### Scenario: CI passes on clean commit

- GIVEN a pushed commit conforming to the `.editorconfig`
- WHEN the workflow runs
- THEN `format-check` passes and `test` executes

### Requirement: First-run formatting applied as a chore

All existing `*.cs` files under `src/` and `tests/` MUST be formatted to the codified style, committed as a separate chore commit distinct from the tooling commit.

#### Scenario: Source tree conforms after first run

- GIVEN the `.editorconfig` is in place and the chore commit is applied
- WHEN `mise run format-check` runs on the formatted tree
- THEN it exits 0 across all `src/` and `tests/` `*.cs` files

#### Scenario: No logic change in chore commit

- GIVEN the first-run formatting commit
- WHEN the diff is reviewed
- THEN it contains only whitespace and using-order changes

### Requirement: Format tooling does not regress existing tasks

Adding the format tooling MUST NOT break `mise run test` or `mise run publish-linux-x64`.

#### Scenario: Tests still pass

- GIVEN the tooling commit is applied
- WHEN `mise run test` is executed
- THEN it passes with the same test count as before the change

#### Scenario: Publish still succeeds

- GIVEN the tooling commit is applied
- WHEN `mise run publish-linux-x64` is executed
- THEN it completes successfully