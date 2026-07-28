# AGENTS.md

## Project Commands

Use `mise` tasks as the project command interface. Do not invoke `dotnet` directly for normal project workflows.

- Restore dependencies: `mise run restore`
- Run tests: `mise run test`
- Run the CLI: `mise run cli -- <args>`
- Publish Linux x64: `mise run publish-linux-x64`

`mise.toml` is the source of truth for the .NET tool version and project commands. If a command needs to change, update `mise.toml` first and keep this file aligned.

## Verification

Before claiming tests pass, run:

```bash
mise run test
```

If the command fails because the sandbox blocks local build IPC, rerun the same `mise` task with the required permissions instead of switching to direct `dotnet` commands.
