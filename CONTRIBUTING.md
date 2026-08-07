# Contributing to RetroBox

Thanks for contributing! This guide covers the workflows, command conventions,
and review expectations for this repository.

## Command interface

Use **`mise` tasks** as the project command interface. Do not invoke `dotnet`
directly for normal project workflows — `mise.toml` pins the .NET SDK version and
wraps every common operation.

| Task | Purpose |
| --- | --- |
| `mise install` | Install pinned tools (dotnet 10, arduino-cli). |
| `mise run restore` | Restore .NET dependencies. |
| `mise run test` | Run the xUnit test suite. |
| `mise run format` | Apply `dotnet format` to the solution. |
| `mise run format-check` | Verify formatting without changing files. |
| `mise run cli -- <args>` | Run the `retrobox` CLI from source. |
| `mise run publish-linux-x64` | Publish the Native AOT Linux x64 binary. |
| `mise run firmware-compile` | Compile the ESP8266 firmware. |
| `mise run firmware-upload -- <port>` | Compile and upload the firmware. |

`mise.toml` is the source of truth for the .NET tool version and commands. If a
command needs to change, update `mise.toml` first and keep `AGENTS.md` aligned.

## Development loop

1. Create a feature branch off `main` (see below for branch cleanup).
2. Write or update tests alongside code (xUnit under `tests/RetroBox.Tests`).
3. Run the gates before opening a PR:

   ```bash
   mise run test
   mise run format-check
   ```

   If `mise run test` fails because the sandbox blocks local build IPC, rerun
   the same `mise` task with the required permissions instead of switching to
   direct `dotnet` commands.

4. Commit with a Conventional Commit message (see below).
5. Open a PR against `main` using the PR template.

## Commit conventions

Use [Conventional Commits](https://www.conventionalcommits.org/). Scope by area
where useful:

- `feat(cli):`, `fix(core):`, `feat(daemon):`, `feat(firmware):`,
  `feat(appliance):`, `docs(appliance):`, `chore:`, `refactor(core):`

Examples from the history:

```text
feat(cli): implement retrobox nfc read/write and catalog status (#16) (#51)
fix(appliance): gate daemon service on serial device and pass --serial-port (#53)
chore: apply dotnet format to existing sources
```

Reference the issue or PR number in the commit body when applicable.

## Code style

- C# 13, nullable enable, implicit usings; English identifiers and comments.
- Flat `RetroBox*`-prefixed file-per-concern classes (see `AGENTS.md`).
- Formatting is enforced by `.editorconfig` + `mise run format-check` in CI.
- No comments unless they explain non-obvious decisions.

## Architecture decisions

Substantial design choices are recorded as ADRs in `docs/decisions/`
(`NNNN-slug.md`). When you change behavior that later agents or maintainers
should be able to reconstruct, add or update an ADR. See the
[ADR template](docs/decisions/README.md).

## Releases

Releases are published from the `release` job in
`.github/workflows/build-usb-installer.yml` on pushes to `main`. Tags follow
`appliance-YYYYMMDD-<run>`. Release notes are generated from
`git log --no-merges <previous-tag>..HEAD`, grouped by conventional commit type
(Features / Fixes / Other). Keep commit messages meaningful — they become the
public changelog.

## Branch cleanup

Stale remote branches accumulate as PR branches merge. When you close a PR or
finish a feature branch, delete the remote branch:

```bash
git push origin --delete <branch-name>
```

Branches already merged into `main` are safe to delete (`git branch -r --merged
main`). The maintainer periodically prunes superseded `nakioman/*` prototype
branches; ask before deleting anything with unmerged commits.
