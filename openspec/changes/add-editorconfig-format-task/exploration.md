## Exploration: add-editorconfig-format-task

### Current State
The RetroBox solution has no centralized formatting or analyzer policy. `mise.toml` pins `dotnet = "10"` and exposes `restore`, `test`, `cli`, and `publish-linux-x64` tasks, but no `format` task. None of the four projects (`Cli`, `Core`, `Daemon`, `Tests`) enable `EnableNETAnalyzers`, set an `AnalysisLevel`, or use `TreatWarningsAsErrors`. There is no `.editorconfig`, `.globalconfig`, `Directory.Build.props`, `Directory.Packages.props`, `.config/dotnet-tools.json`, or `.gitattributes` in the repository. CI (`build-retrobox.yml`) currently runs `mise run test` and `mise run publish-linux-x64`; formatting is not gated.

### Affected Areas
- `mise.toml` — needs a new `format` task and possibly a `format-check`/`lint` task; tool pinning is already `dotnet = "10"`.
- `.editorconfig` (new, repo root) — will define indentation, namespace style, brace style, using placement, naming rules, and analyzer severity for all C# projects.
- `src/RetroBox.Cli/RetroBox.Cli.csproj` — may need `<EnableNETAnalyzers>` / `<AnalysisLevel>` if the proposal couples formatting with code-quality analyzers.
- `src/RetroBox.Core/RetroBox.Core.csproj` — same; also references `Vecc.YamlDotNet.Analyzers.StaticGenerator`, which must continue to work without conflict.
- `src/RetroBox.Daemon/RetroBox.Daemon.csproj` — same analyzer opt-in decision.
- `tests/RetroBox.Tests/RetroBox.Tests.csproj` — xUnit test style must be preserved (snake_case test names are conventional here).
- `.github/workflows/build-retrobox.yml` — likely needs a format/check step added to the PR/push gate.
- All `*.cs` files — a first `dotnet format` run is expected to produce whitespace/ordering churn across many files.

### Approaches

1. **EditorConfig only + `dotnet format` task (no new analyzers)**
   - Add a root `.editorconfig` that normalizes the observable style (4 spaces, file-scoped namespaces, K&R braces, top-of-file usings, PascalCase public members, `_camelCase` private fields, etc.). Add a single `mise` task `format` that runs `dotnet format RetroBox.slnx --no-restore` (or without `--no-restore` if restore is cheap). Optionally add `format-check` with `--verify-no-changes` for CI.
   - Pros: Low blast radius; no risk of analyzer warnings becoming build errors; fast to implement; matches the repo's current "no analyzer" posture.
   - Cons: Does not catch code-quality issues or enforce Roslyn analyzer rules; future style drift can still happen if only formatting rules are enforced.
   - Effort: Low

2. **EditorConfig + built-in .NET analyzers + format task**
   - Add `.editorconfig` and enable `<EnableNETAnalyzers>true</EnableNETAnalyzers>` with a conservative `AnalysisLevel` (e.g. `latest-minimum` or `latest-recommended`) in each `.csproj` (or a shared `Directory.Build.props`). Add `mise run format` and `mise run format-check`. Wire `format-check` into CI before `test`.
   - Pros: Catches bugs and style in one pass; aligns with modern .NET conventions; `dotnet format` can apply analyzer code-style fixes (`--severity`) in addition to whitespace.
   - Cons: Higher initial churn; some analyzer suggestions may conflict with existing patterns (e.g. test naming, primary constructors, `sealed` usage, or xUnit-specific rules) and must be tuned or suppressed; `publish-linux-x64` already passes `-warnaserror`, so new warnings can break the AOT publish step.
   - Effort: Medium

### Recommendation
Choose **Approach 1** first: add a `.editorconfig` that codifies the existing dominant style and a `mise run format` task using the built-in .NET 10 `dotnet format`. Add a `format-check` task with `--verify-no-changes` and include it in `build-retrobox.yml` before the test step. Defer enabling `EnableNETAnalyzers` to a follow-up change because it requires tuning analyzer severity against the existing `-warnaserror` publish flag and the xUnit test conventions. This keeps the PR focused on the stated problem (style drift) and avoids conflating formatting with code-quality analyzer adoption.

### Risks
- **First-run churn**: `dotnet format` will likely modify many source files (whitespace, using-directive ordering, blank-line normalization). The PR will be large unless the formatter is first run in a separate commit or slice.
- **AOT publish break**: `publish-linux-x64` uses `-warnaserror`. If analyzers are enabled later, any new warning becomes a hard error during publish.
- **CI time**: Adding `format-check` adds a solution-level restore/format pass to every PR. Using `--no-restore` after an explicit restore step mitigates this.
- **Test naming / xUnit conventions**: Analyzer-driven naming rules could flag the existing snake_case `Fact` method names. Any analyzer rollout must suppress or configure `CA1707`/`VSTHRD200`-style rules for the test project.
- **SDK availability**: `dotnet format` is built into .NET SDK 6+; the pinned `dotnet = "10"` already includes it, so no `dotnet-tools.json` manifest is required.

### Ready for Proposal
Yes. The scope is clear: add `.editorconfig`, add `format`/`format-check` tasks to `mise.toml`, and wire `format-check` into `build-retrobox.yml`. The orchestrator should tell the user that the recommended path is formatting-only first, with analyzer enablement deferred to a later change because of the `-warnaserror` AOT publish risk.
