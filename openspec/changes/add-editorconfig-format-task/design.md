# Design: Add `.editorconfig` and `dotnet format` task

## Technical Approach

Codify the observed dominant C# style in a root `.editorconfig`, expose it through two `mise` tasks, and gate it in CI before tests. `dotnet format` is built into .NET SDK 10 (pinned via `mise.toml` — no `dotnet-tools.json`). The change is formatting-only: no analyzer activation, no severity bumps that would interact with `-warnaserror` on `publish-linux-x64`. Mirrors the proposal's Approach 1 and two-commit strategy.

## Architecture Decisions

| # | Option | Tradeoff | Decision |
|---|--------|----------|----------|
| 1 | Codify OBSERVED style (Allman braces, `camelCase` private fields, no underscore) vs proposal's stated "K&R + `_camelCase`" | Observed minimizes first-run churn and matches "codify, don't invent"; proposal text appears mischaracterized | **Codify OBSERVED.** Divergence flagged in Open Questions. |
| 2 | Include `dotnet_naming_*` rules (PascalCase public / `_camelCase` private) vs omit them | Naming rules emit `IDE1006` diagnostics and require a `severity` to function — violates the hard constraint ("no analyzer rules/severity", no `-warnaserror` interaction); a `_camelCase` rule would flag every existing field | **Omit naming rules.** Defer to a follow-up analyzer-tuning change. |
| 3 | `format-check` `depends=["restore"]` (mise idiomatic) vs explicit `mise run restore` CI step | `depends` keeps one source of truth; proposal wants explicit restore | **`format-check` depends on `restore`.** CI calls `mise run format-check`; `--no-restore` skips a redundant restore. |
| 4 | All `csharp_*`/`dotnet_*` severities at `:suggestion`/`:none` (never `:warning`/`:error`) | Keeps `dotnet format` functional without elevating any diagnostic into the `-warnaserror` path | **Use `:suggestion`/`:none`.** Documented boundary. |
| 5 | Two commits: chore (format churn) → feat (tooling) vs single commit | Split keeps reviewer focus; commit 1 large but mechanical | **Two commits, ordering matters** (format must land before the gate). |

## Data Flow

    mise run format-check ──► depends: restore ──► dotnet format RetroBox.slnx
                                                          │  --verify-no-changes
                                                          │  --no-restore
                                                          ▼
                                                  exit 0 (clean) | exit 1 (drift) ──► CI blocks

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `.editorconfig` | Create | Root rules: `[*]` charset/lf/eol/newline + `[*.cs]` 4-space, Allman braces, outside-namespace usings, file-scoped namespaces, primary constructors. All at `:suggestion`/`:none`. |
| `mise.toml` | Modify | Add `[tasks.format]` and `[tasks.format-check]` mirroring `restore`/`test` style; `format-check` `depends=["restore"]`. |
| `.github/workflows/build-retrobox.yml` | Modify | Insert `Check formatting` step (`mise run format-check`) between `Set up mise` and `Run tests`. |
| `src/**/*.cs`, `tests/**/*.cs` | Modify | First-run formatting only (Commit 1). |

## Interfaces / Contracts

`.editorconfig` (codifies observed style):

```editorconfig
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4
dotnet_sort_system_directives_first = true
csharp_preferred_using_directive_placement = outside_namespace:suggestion
csharp_new_line_before_open_brace = all              # observed Allman
csharp_prefer_braces = true:suggestion
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_style_primary_constructors = true:suggestion
csharp_indent_case_labels = false
csharp_indent_switch_labels = true
csharp_indent_braces = false
csharp_space_after_keywords_in_control_flow_statements = true
csharp_space_before_colon_in_inheritance = true
csharp_preserve_single_line_blocks = true
csharp_preserve_single_line_statements = true
```

`mise.toml` additions (mirror `restore`/`test`):

```toml
[tasks.format]
description = "Apply dotnet formatting rules to the solution"
run = "dotnet format RetroBox.slnx"

[tasks.format-check]
description = "Verify formatting compliance without changing files"
depends = ["restore"]
run = "dotnet format RetroBox.slnx --verify-no-changes --no-restore"
```

CI step (insert before `Run tests`):

```yaml
      - name: Check formatting
        run: mise run format-check
```

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Manual | `mise run format-check` exits 0 on clean tree | Run on freshly formatted checkout |
| RED (drift) | `mise run format-check` exits non-zero on drift | Introduce a stray tab/whitespace in a `*.cs`, run task, assert non-zero |
| CI | `format-check` blocks PRs on drift | Push a drift commit, assert CI fails before tests run |
| Regression | `test` and `publish-linux-x64` unchanged | Run both; confirm no warnaserror surface added |

## Threat Matrix

| Boundary | Applicability | Design response | Planned RED tests |
|---|---|---|---|
| Documentation-like paths | N/A — no executable md/cmake | — | — |
| Git repo selection | N/A — no `git -C` in tasks (CI runs at repo root) | — | — |
| Commit state | N/A — two-commit is a human convention, no git scripting | — | — |
| Push state | N/A — no push automation | — | — |
| PR commands | N/A — no `gh`/PR CLI | — | — |
| Shell task / subprocess invocation (`mise` → `dotnet format`) | Applicable | `--no-restore` only valid after restore (enforced via `depends=["restore"]`); non-zero exit propagates (`mise`/GitHub Actions fail the step) | Drift → exit 1; CI step fails |

## Migration / Rollout

Two-commit rollout within one PR (ordering matters):

1. **Commit 1 — `chore: apply dotnet format to existing sources`** — `src/**/*.cs`, `tests/**/*.cs` only. Large whitespace/using/brace diff, zero logic change. `obj/` is gitignored → not formatted.
2. **Commit 2 — `feat(ci): add .editorconfig and dotnet format task`** — `.editorconfig`, `mise.toml`, `.github/workflows/build-retrobox.yml`. Small, decision-bearing.

Rollback: revert all three config files; the formatted source remains valid (additive).

## Open Questions

- [ ] **Brace style**: proposal text says "K&R braces"; observed code is **Allman** (next-line braces for class, method, and control blocks). Design codifies OBSERVED (Allman). Confirm before implementation — choosing K&R instead would multiply Commit 1 churn.
- [ ] **Private field naming**: proposal says `_camelCase`; observed is plain `camelCase` (e.g., `private readonly string rootPath`). Design codifies OBSERVED (no underscore). Confirm — `_camelCase` would require renames across all references (not formatting-only) and is deferred with naming rules (Open Q 3).
- [ ] **Naming rules omitted**: proposal In-Scope lists `dotnet_naming_*` for PascalCase public / `_camelCase` private fields. This design OMITS them because naming rules need a `severity` to function and activate `IDE1006` — violating the "no analyzer rules/severity, no `-warnaserror` interaction" constraint. Confirm defer to a follow-up analyzer-tuning change.