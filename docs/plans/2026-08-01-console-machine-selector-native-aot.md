---
title: Console machine selector and Native AOT startup
date: 2026-08-01
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: ce-plan-bootstrap
execution: code
---

# Console machine selector and Native AOT startup

## Goal Capsule

Replace the Terminal.Gui VM selector with a plain console menu that renders on the appliance's physical Linux virtual terminal and over SSH.

Publish the RetroBox CLI as a Linux x64 Native AOT executable so the appliance starts quickly from its HDD.

The work preserves the optional `config.yaml` contract: installation still creates no initial default VM.

## Product Contract

### Summary

The current Terminal.Gui selector accepts keyboard input on the appliance's physical `tty`, but fails to render there; it works through SSH.

The appliance needs a selector that only relies on normal console output and key input, which are available in both environments.

### Requirements

- R1. `retrobox boot --selector` renders a plain-text machine selector without Terminal.Gui, alternate-screen buffers, color capability detection, or terminal-emulator-specific behavior.
- R2. The selector begins with the user-provided RetroBox ASCII banner, the centered title `Machine Selector`, separator lines, and a stable numbered list of catalog VMs.
- R3. Each entry displays its one-based numeric key, label, and `(default)` when it is the configured default VM.
- R4. Pressing a displayed number starts that VM without changing `config.yaml`.
- R5. Pressing `D` enters default-selection mode; the next valid VM number writes that VM as `defaultVm` and immediately starts it.
- R6. `Esc` cancels selection. Preserve the existing behavior: when a default exists, cancellation runs it; when none exists, return the existing clear error.
- R7. Invalid keys or numbers produce a concise retry message without crashing or writing configuration.
- R8. The selector clears the screen before its initial render, then uses ordinary console writes for the menu and prompts.
- R9. The published Linux x64 `retrobox` artifact is Native AOT and remains self-contained at `/opt/retrobox/retrobox`.
- R10. Native AOT publishing must fail on unresolved AOT/trimming compatibility warnings; catalog loading, VM boot, daemon, and CLI help behavior remain functional.

### Scope Boundaries

- Keep F12 as the trigger for opening the selector; changing the hotkey behavior is out of scope.
- Keep YAML catalogs and the optional `config.yaml` model; do not seed a default VM during installation.
- Do not change 86Box, SDL, the fullscreen boot service, or VM profile paths in this work.
- Remove `vt.global_cursor_default=0` from the generated GRUB command line as a small accompanying console-usability fix, but do not make the selector depend on cursor visibility.

### Acceptance Examples

- AE1. Given three catalog VMs and `pentium100` as default, the screen shows `1. Pentium 100 (default)` and selecting `2` runs the second stable catalog entry without changing `config.yaml`.
- AE2. Given no `config.yaml`, the screen lists all catalog VMs; `D`, then `1`, writes the selected default and launches it.
- AE3. Given no configured default, pressing `Esc` returns `VM selection was cancelled and no default VM is configured.`
- AE4. Given a configured default, pressing `Esc` resolves and runs that default.
- AE5. Given the Linux x64 publish task, the output is a Native AOT executable that runs `--help` and `boot --dry-run` on Linux.

## Planning Contract

- KTD1. **Use a minimal `System.Console` selector rather than another TUI framework.** The physical Linux VT already proves normal output and key input work, while Terminal.Gui's rendering fails there. (session-settled: user-directed — chosen over retaining Terminal.Gui/driver configuration: it must work on the appliance console.)
- KTD2. **Numeric direct selection plus two-step `D` default selection.** Numeric keys launch immediately; `D` followed by a number persists and launches the VM. This retains the existing `Run` and `RunAndSetDefault` selection semantics. (session-settled: user-directed — chosen over a save-only default action: the selected default must start immediately.)
- KTD3. **Native AOT is the production Linux publish mode.** The `publish-linux-x64` task and CI artifact use `PublishAot=true`; compilation prerequisites are installed in the build environment, and AOT compatibility is treated as a build gate. (session-settled: user-directed — chosen over the existing single-file JIT publish: reduce cold-start time on the appliance HDD.)

### Technical Design

The CLI owns presentation and keeps `RetroBoxBootSelector` in Core as the policy layer.

The new CLI selector receives an abstraction over screen clearing, text output, and key input so tests can drive it without a real TTY.

It renders only ASCII characters, calls `Console.Clear()` once before the first menu, and reads `ConsoleKeyInfo` in a retry loop.

`CliCommandFactory` continues to inject the selector into `RetroBoxBootSelector`, so current selection-policy tests remain valid and presentation tests isolate the console adapter.

Native AOT will remove the Terminal.Gui dependency, enable AOT analyzers for the publish graph, and exercise the existing YAML catalog path under an AOT Linux smoke test.

YamlDotNet 18.1 provides `StaticContext` and static serializer/deserializer builders; the implementation must use that AOT-safe path if the analyzer or publish smoke test identifies runtime reflection as incompatible.

### Sequencing

U1 establishes the console selector and its tests first, removing the rendering dependency.

U2 makes the resulting smaller CLI Native AOT-compatible and updates CI/publish validation.

U3 applies the GRUB cursor correction and documentation after the executable contract is settled.

## Implementation Units

### U1. Replace the graphical selector with a testable console menu

- **Goal:** Render and operate the ASCII machine selector on a physical VT and SSH with the same selection semantics as today.
- **Requirements:** R1-R8; AE1-AE4.
- **Files:** `src/RetroBox.Cli/RetroBoxTerminalGuiSelector.cs` (replace or rename), `src/RetroBox.Cli/CliCommandFactory.cs`, `src/RetroBox.Cli/RetroBox.Cli.csproj`, `tests/RetroBox.Tests/RetroBoxBootSelectorTests.cs`, new focused selector-rendering test file under `tests/RetroBox.Tests/`.
- **Approach:** Remove Terminal.Gui references and package dependency. Introduce a small injectable console adapter for `Clear`, `WriteLine`, `Write`, and `ReadKey`. Render the fixed banner/title/instructions and catalog entries in stable order. Parse only displayed numeric keys, `D`/`d`, and `Esc`; feed the resulting `Run`, `RunAndSetDefault`, or `Cancel` decision to the existing Core selection policy.
- **Test scenarios:** Banner/title/separators and default marker render correctly; numeric selection maps to the intended stable VM; `D` then valid number returns `RunAndSetDefault`; invalid input retries without a decision; `Esc` returns `Cancel`; CLI injection still passes the result through the existing boot path.
- **Verification:** `mise run test`.

### U2. Publish the CLI with Native AOT

- **Goal:** Produce and validate a self-contained Native AOT Linux x64 executable used by the existing Actions artifact and appliance installer.
- **Requirements:** R9-R10; AE5.
- **Files:** `mise.toml`, `src/RetroBox.Cli/RetroBox.Cli.csproj`, potentially `src/RetroBox.Core/RetroBoxConfigStore.cs` and catalog model files if YamlDotNet static contexts are required, `.github/workflows/build-retrobox.yml`, relevant tests under `tests/RetroBox.Tests/`.
- **Approach:** Change the Linux publish task to `PublishAot=true` and enable AOT compatibility analysis. Install the documented Linux compiler prerequisites (`clang`, `zlib1g-dev`) in the build job. Remove obsolete `PublishSingleFile` reliance because Native AOT produces the native application artifact directly. Make any serializer changes needed to eliminate AOT/trimming warnings while preserving YAML schema and validation behavior. Add a Linux-only smoke step that invokes the published artifact's help and `boot --dry-run` against a fixture catalog.
- **Test scenarios:** AOT publish completes without warnings treated as unresolved compatibility; artifact is executable and has no `Terminal.Gui` runtime dependency; help succeeds; dry run resolves each fixture VM and does not create `config.yaml`; normal unit suite continues to pass.
- **Verification:** `mise run test`; `mise run publish-linux-x64`; CI Linux artifact smoke; inspect the artifact with `file` and run its documented smoke commands on Linux.

### U3. Restore normal console cursor defaults and document the interaction

- **Goal:** Stop globally hiding the hardware console cursor and document the selector's direct-console contract.
- **Requirements:** R1-R2, R8.
- **Files:** `appliance/installer/lib/grub-install.sh`, `appliance/installer/README.md`, `appliance/README.md` if its boot description names the old GUI behavior.
- **Approach:** Remove `vt.global_cursor_default=0` from the generated normal boot kernel command line. Update appliance instructions to describe the ASCII selector, numeric launch, `D` + number behavior, and F12 trigger.
- **Test scenarios:** Generated GRUB defaults omit the global cursor-disable parameter; documentation does not claim a graphical/Terminal.Gui selector.
- **Verification:** Shell syntax check for installer scripts; `systemd-analyze verify` remains clean for the unchanged units; documentation review.

## Verification Contract

| Gate | Applies to | Evidence |
| --- | --- | --- |
| Unit tests | U1-U2 | `mise run test` passes with selector policy, rendering adapter, catalog, and CLI coverage. |
| Native publish | U2 | `mise run publish-linux-x64` completes as AOT on Linux with its compiler prerequisites present. |
| Published smoke | U2 | Linux CI executes the published binary's help and two `boot --dry-run --select <id>` cases against a fixture catalog. |
| Appliance shell lint | U3 | ShellCheck validates the installer entry scripts and sourced libraries. |
| Physical smoke | U1-U3 | On the appliance VT, `retrobox boot --selector` visibly renders, number starts a VM, `D` + number persists/starts, and `Esc` follows the documented default behavior. SSH behavior remains equivalent. |

## Definition of Done

- Terminal.Gui is absent from the production CLI dependency graph.
- The selector is visible and usable from both appliance `tty` and SSH.
- The first selector render clears the screen and uses the specified RetroBox banner and numbered layout.
- `D` plus a VM number persists that VM as default and launches it.
- No initial `config.yaml` is added to the appliance payload or installer.
- Linux x64 CI publishes a Native AOT artifact that the appliance workflow can consume unchanged.
- The generated appliance GRUB configuration no longer disables the cursor globally.
- All verification-contract gates pass and no experimental fallback code remains.

## Sources

- `src/RetroBox.Cli/RetroBoxTerminalGuiSelector.cs` currently initializes Terminal.Gui and is the physical-VT failure point.
- `src/RetroBox.Core/RetroBoxBootSelector.cs` already owns `Run`, `RunAndSetDefault`, and cancellation semantics.
- `mise.toml` and `.github/workflows/build-retrobox.yml` define the artifact consumed by the appliance workflow.
- [Native AOT deployment guidance](https://github.com/dotnet/docs/blob/main/docs/core/deploying/native-aot/index.md) documents `PublishAot`, AOT compatibility analysis, and Linux toolchain prerequisites.
