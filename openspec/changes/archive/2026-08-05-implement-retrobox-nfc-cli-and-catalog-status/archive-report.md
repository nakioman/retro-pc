# Archive Report: implement-retrobox-nfc-cli-and-catalog-status

**Archived**: 2026-08-05
**Archive location**: `openspec/changes/archive/2026-08-05-implement-retrobox-nfc-cli-and-catalog-status/`
**Archive status**: intentional-with-warnings (see Review Gate section — surfaced as fact, not fabricated allow)

## Change

- **Name**: `implement-retrobox-nfc-cli-and-catalog-status`
- **Proposal**: `proposal.md` — Issue #16 NFC provisioning workflow
- **Mode**: STRICT TDD (RED→GREEN per task), delivery as a single PR with maintainer-approved `size:exception`

## Scope (final)

### In scope (shipped)
- Core `BuildPingCommand()` → `PING`; `ParseResponse` → discriminated `NfcResponse` (`Pong` | `Ok` | `Error(msg)` | `Unknown(line)`); dead `BuildReadCommand()` removed
- `IRetroBoxNfcClient` + `RetroBoxNfcSerialClient` over `System.IO.Ports` (per-call open/write/read/close; internal ctor + `InternalsVisibleTo` port-stream factory, mirroring `RetroBoxFloppyControlClient`)
- `RetroBoxNfcWriter`: catalog lookup → read `Mode` → `WRITE <id>,<mode>` → on `OK` flip `Nfc=true` + atomic persist via `RetroBoxConfigStore`; `NfcWriteResult` = `Written` | `NotCataloged(id)` | `Error(msg)`; `NfcPortUnavailable` for busy/EACCES/timeout
- CLI parent `nfc` + `read`/`write` subcommands: `nfc read --port <p>` (PING→PONG connectivity check, exit 0 on PONG), `nfc write <id> --port <p> [--config-root]` (NO `--mode`; mode from catalog)
- `Nfc` bool on `RetroBoxFloppy` (default false), additive YAML persistence (key `nfc`), backward/forward compatible

### Non-goals honored (verified final state)
- ZERO firmware changes — `git diff 863c4e9..HEAD` touches 0 `firmware/` files (PING→PONG + WRITE already on main `863c4e9`, pulled via ff-merge)
- NO `--mode` CLI option
- NO firmware READ/STATUS
- NO mode-match validation
- NO separate `catalog status` subcommand
- NO daemon RPC/lockfile/serialization — detect-and-error only
- NO required PING pre-flight on `nfc write` (spec forbids; let WRITE surface dead-device failure)

## Decisions (final, per #14)

1. `nfc read` = connectivity check (PING→PONG), NOT tag-payload read. User: "el nfc read es para saber si esta andando o no el nfc, es un check".
2. `nfc write <id>` has NO `--mode`; mode read from catalog entry — catalog is source of truth.
3. Firmware scope = ZERO (PING + WRITE already on main `863c4e9`).
4. Serial vs daemon: detect-and-error with actionable message (stop daemon and retry).

## Final Evidence (per FINAL-STATE HANDOFF — outranks apply-progress/verify-report snapshots)

### Tests
- `mise run test` independently confirmed: **148/148 pass / 0 fail** (117 baseline + 31 new)
- 0 CRITICAL, 0 WARNING except AOT-tooling (expected on macOS), 1 SUGGESTION (optional baud-rate config)

### Publish / AOT
- `mise run publish-linux-x64`: compiles clean; macOS link fails (`llvm-objcopy` not found) — expected tooling limitation; Linux CI runner validates the link. No code trim warnings. No code fix needed.

### Commits (HEAD `248c458`)
| Commit | Scope |
|--------|-------|
| `c4c6a10` | protocol: `BuildPingCommand` + `ParseResponse`; removed dead `BuildReadCommand` |
| `c8ced54` | `Nfc` bool on `RetroBoxFloppy` |
| `48ac4c7` | `IRetroBoxNfcClient` + `RetroBoxNfcSerialClient` + `System.IO.Ports` + `RecordingNfcClient` |
| `feb4b9c` | `RetroBoxNfcWriter` |
| `248c458` | nfc read/write CLI subcommands |
| `863c4e9` (ff-merge) | firmware PING handler from main (pre-existing) |

### Lines
- **898 total changed lines** — accepted as **maintainer-approved `size:exception`** (single PR; user explicitly accepted). Over the 400-line review budget and the 480→reset→1000 runtime attempt budget.
- Recomposition: source/test diff `863c4e9..HEAD` = 877 additions + 9 deletions (886), plus 12 lines of tasks.md checkbox updates ≈ 898 authoritative figure.
- `openspec/` docs not counted in the code line figure (they are SDD artifacts).

### Runtime ledger
- Native runtime ledger **complete**: `settle` returned `state: complete` after reset + re-acquire at budget 1000.

## Verification Report Reference

- File: `verify-report.md` (archived alongside this report) — verdict PASS, 0 CRITICAL.
- Snapshot note: apply-progress (#19) recorded 886 changed lines and 148/148 at apply time; final state per handoff is 898 lines / 148/148 — no contradiction on tests; line count superseded by final handoff.

## Review Gate (honest status — NOT fabricated)

- Kill switch / receipt-driven development: **ON**. However, no native adversarial review ceremony was requested by the user this session; the orchestrator did not run `review.start`, and no native review artifacts exist (no `reviews/` dir; native status reports `reviewPolicy/reviewLedger/reviewReceipt/reviewBundle/reviewContext/reviewState` all `missing`).
- Native `gentle-ai sdd-status` reports `nextRecommended: resolve-review` with `blockedReasons: ["verify evidence cannot enter remediation: missing valid gentle-ai.verify-result/v1 envelope: the first non-empty content must be fenced yaml; bounded review transaction is missing"]`.
- `reviewGate.result`: **not present** (omitted until final archive gating runs). No receipt was produced or fabricated.
- Archive proceeds per orchestrator direction (user driving delivery as a single PR via `gh`; PR creation handled by orchestrator after this archive). This archive is marked **intentional-with-warnings** for the missing native review receipt, per the sdd-archive rule: record the exact reason and mark the archive rather than fabricate an allow.
- If a terminal receipt is required before merge, a native `review.start` + re-verify would be needed post-archive.

## Delta Specs as Archive-Record

This repo's openspec layout is **non-standard**: there is no `openspec/specs/` main-specs store and no prior archive folder. Per orchestrator instruction, the delta specs are preserved verbatim as the archive-record rather than creating a new specs store destructively:

- `specs/retrobox-nfc-cli/spec.md` — 9 requirements (NFC Read Connectivity Check, NFC Write Catalog-Driven, NFC Not-Imported Id, NFC Port Option, NFC Serial Contention, NFC Protocol Builders, NFC Protocol Parser, NFC Client Abstraction, NFC CLI Help Surface) — **full spec** (no pre-existing main spec; entirely ADDED)
- `specs/nfc-catalog-status/spec.md` — 1 requirement (Catalog Nfc Field) with 4 Given/When/Then scenarios — **full spec** (entirely ADDED)

Both deltas contain only ADDED requirements; no MODIFIED/REMOVED/RENAMED sections. No destructive merge was performed. If the repo later adopts `openspec/specs/`, these two files are the canonical delta sources to promote.

## Artifact Observation IDs (Engram traceability)

| Artifact | Engram ID |
|----------|-----------|
| proposal | #13 |
| decisions | #14 |
| spec | #15 |
| design | #16 |
| tasks | #18 |
| apply-progress | #19 |
| verify-report | #21 |
| archive-report | this report |

## SDD Cycle Status

**CLOSED** for archive purposes with the review-gate caveat above. No remediation was needed (verify found nothing to fix; no blockers).
