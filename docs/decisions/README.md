# Architecture Decision Records

Substantial design decisions are recorded here as ADRs so that future agents
and maintainers can reconstruct *why* the code works the way it does.

## Conventions

- One file per decision: `NNNN-slug.md`, where `NNNN` is the next free
  four-digit number starting at `0001`.
- Keep each ADR short: context, decision, and consequences. Status is one of
  `Accepted`, `Superseded by NNNN`, `Proposed`, or `Deprecated`.
- When a new decision changes behavior that agents should understand, add it
  here and link it from `AGENTS.md` / `docs/architecture.md`.

## Index

- [0001-native-aot-binary.md](0001-native-aot-binary.md) — single Native AOT Linux binary.
- [0002-read-only-root-and-data.md](0002-read-only-root-and-data.md) — read-only root, persistent `/data`.
- [0003-yaml-metadata-not-source-of-truth.md](0003-yaml-metadata-not-source-of-truth.md) — 86Box config is authoritative.
- [0004-nfc-raw-bytes-not-ndef.md](0004-nfc-raw-bytes-not-ndef.md) — tags carry raw bytes.
- [0005-add-wifi-support.md](0005-add-wifi-support.md) — WiFi via systemd-networkd + first-boot dialog.
- [0006-squashfs-overlay-root.md](0006-squashfs-overlay-root.md) — immutable squashfs root + overlayfs; supersedes the ext4-ro mechanism in 0002.

## Template

```markdown
# NNNN. Title

Date: YYYY-MM-DD
Status: Accepted

## Context

The problem or constraint that forced the decision.

## Decision

What was decided, in concrete terms.

## Consequences

Positive and negative outcomes, and anything this decision rules out.
```
