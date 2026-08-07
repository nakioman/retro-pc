# 0001. Single Native AOT Linux binary

Date: 2026-07-28
Status: Accepted

## Context

The `retrobox` CLI and daemon run on the appliance — a minimal Debian 13 host
with a read-only root and no .NET runtime installed. We need a single deployable
artifact that the installer can place at `/opt/retrobox/retrobox`.

## Decision

Publish the CLI as a **Native AOT** self-contained Linux x64 binary
(`mise run publish-linux-x64`). The binary links most of the runtime into one
executable; `System.IO.Ports` cannot be statically linked and ships as
`libSystem.IO.Ports.Native.so` next to the binary.

## Consequences

- Single file to deploy, no runtime prerequisites on the appliance.
- Fast startup, appropriate for the appliance's boot-to-VM path.
- AOT requires the Linux toolchain (`clang`, `zlib1g-dev`, `binutils`) in CI;
  on macOS the publish task fails at `llvm-objcopy`, which is expected and not a
  regression.
- System.IO.Ports stays a runtime-loaded native library, so the artifact is a
  binary + one `.so`.
