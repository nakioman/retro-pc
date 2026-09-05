# RetroBox web panel — handoff

**Written:** 2026-09-04. **Spec:** [`specs/2026-09-03-web-panel-design.md`](specs/2026-09-03-web-panel-design.md).

The spec is the authority. Plans argue from it, and when a plan and the spec
disagree, the spec wins — that happened once already in phase 3 and the plan
was the thing that got corrected.

## Where things stand

Phases 1 and 2 are merged. **Phase 3 is complete and open as a stack of seven
PRs** awaiting review. Phases 4 and 5 are unplanned.

Merge the phase 3 stack in this order — each is based on the one above it:

| PR | Branch | What |
|---|---|---|
| [#91](https://github.com/nakioman/retro-pc/pull/91) | `docs/phase-3-plan` | the phase 3 plan, two spec corrections, one exit-criterion fix |
| [#92](https://github.com/nakioman/retro-pc/pull/92) | `feat/serial-supervision` | the daemon retries the serial device instead of failing once |
| [#93](https://github.com/nakioman/retro-pc/pull/93) | `feat/drive-state-api` | `GET /api/drive` + SSE, and the atomic catalog snapshot |
| [#94](https://github.com/nakioman/retro-pc/pull/94) | `feat/command-quarantine` | late replies can no longer answer the next command |
| [#96](https://github.com/nakioman/retro-pc/pull/96) | `feat/nfc-write-endpoint` | `POST /api/nfc/write` |
| [#97](https://github.com/nakioman/retro-pc/pull/97) | `feat/assign-tag-ui` | the drive panel and assign flow, es/en |
| [#98](https://github.com/nakioman/retro-pc/pull/98) | `fix/phase-3-review` | what the whole-branch review found |

365 tests, `format-check` clean. `publish-linux-x64` runs in CI on Ubuntu.

### What the appliance can do once this merges

Upload a floppy through the panel → see it in the drive → assign it an NFC tag
→ insert the disk → it mounts. That last step is the whole point: before phase
3, a floppy uploaded through the panel was inert, because there was no way to
give it a tag.

## What remains

**Phase 4 — games grouping.** `games.yaml`, activating the `RetroBoxGame`
record, and the grouped UI. Depends on nothing outstanding.

**Phase 5 — cover art.** `scraper.yaml`, a ScreenScraper client behind
`IRetroBoxCoverSource`, and a search-and-confirm UI. Depends on phase 4.
Note the spec's assumption: this needs a ScreenScraper developer account
(`devid`/`devpassword`) that the owner holds or will request. Covers are
downloaded and cached, never hotlinked (spec decision D6). Language and region
are configurable, Spanish preferred.

## Carried debt

Each of these was deliberately deferred with a reason, not forgotten. Do not
re-derive them.

**The CLI does not record a tag's UID.** `RetroBoxNfcWriter` (`retrobox nfc
write`) sets `Nfc = true` but never `NfcUid`, because `IRetroBoxNfcClient`
exposes only `PingAsync`/`WriteAsync` and cannot read a UID. A tag written that
way is invisible to the panel's `NfcUid` ownership check. Phase 3 closed the
user-visible half of this by also consulting the drive tracker — the firmware
names a tag's owner in its `INSERT`, whatever the catalog recorded — so the
warning fires. What remains is that the CLI keeps creating entries with no UID,
and it is the last catalog write path that bypasses `RetroBoxFloppyLibrary`
entirely. Closing it means adding `ReadTagIdAsync` to `IRetroBoxNfcClient` and
having the writer go through `library.AssignTag`.

**The reassignment TOCTOU is narrowed, not closed.** `POST /api/nfc/write` now
carries the tag UID the 409 reported, and the server refuses if the tag changed.
The residual gap is the server's own `TAGID`→`WRITE` round trip — the same gap
every unconfirmed first write already has. Closing it fully needs a firmware
`WRITE-if-uid-matches`, which the protocol does not have.

**SSE fan-out has no coalescing.** Every open browser tab polls `TAGID` on the
same `SemaphoreSlim(1,1)` the write uses, so N tabs against a wedged controller
push refresh latency toward N×5s and a write queues behind them. The real fix is
one shared cached drive view fed by a single poller. Correct today, just slow.

**An SSE drop is silent** and indistinguishable from "no controller" — the drive
card collapses with no explanation. `EventSource` retries and the server re-sends
a first frame, so it is not permanently stale. Distinguishing them needs a
connection-state model.

**Five pre-existing real-time budgets in tests** — `RetroBoxDaemonTests.cs:276`,
`:332`, `:356`, `:471` and `RetroBoxWatchingCatalogSourceTests.cs:122`. None was
introduced by phase 3 and none has been observed flaking. Convert them with
`TimeProvider` when touching those files. (Phase 3 *did* introduce one such test
and it failed 1 run in 6; that one was fixed rather than deferred, because the
phase that introduced it should pay for it.)

**From earlier phases:** `RetroBoxGame` still has an unused `DefaultVm` and
`init` accessors — phase 4 activates the record and should drop the former and
make the latter settable, since the YamlDotNet static generator needs that. The
library's lock is in-process only, so `retrobox import` over SSH concurrent with
a panel write can still clobber an entry. `Program.cs` invokes synchronously, so
the daemon's `CancellationToken` is `None` in production and nothing awaits the
generic host's `WaitForShutdown` — SIGTERM may be absorbed until `TimeoutStopSec`.

## Constraints that bind every phase

These are not style preferences; each one has bitten.

- **Native AOT must keep publishing.** This is why the panel is **Minimal APIs
  only** — Blazor, Razor, MVC and SignalR are all unsupported — and why live
  updates are server-sent events rather than SignalR. New response types go on
  the source-generated JSON context and are serialized through it explicitly.
  `[UnconditionalSuppressMessage]` and new NuGet packages are both defects.
- **`mise run publish-linux-x64` cannot run on macOS.** It fails on a missing
  `llvm-objcopy`, and with `StripSymbols=false` on an invalid linker name. CI
  runs it on Ubuntu. Do not treat its absence from a local run as a problem.
- **All commands go through `mise`.** Never invoke `dotnet` directly.
- **No authentication and no TLS.** A recorded decision (D7) — the panel is
  LAN-trusted. Do not add either, and do not report their absence as a finding.
- **Panel assets are plain HTML/CSS/vanilla JS**, embedded as resources. No
  build step, no npm, no bundler, **no CDN** — the appliance may have no
  internet.
- **Spanish is the panel's primary language**, with English complete alongside
  it. Spanish uses proper accents and opening `¿`/`¡`. The i18n scaffolding
  already exists in `app.js` (a `STRINGS` object, `t()`, `data-i18n`
  attributes, a persisted picker) — extend it, never build a second mechanism.
- **No fixed-duration sleeps and no real-time budgets in tests.** A *generous
  failure deadline* that turns a hang into a legible failure is fine and is the
  established pattern (`WithFailureDeadline`, 30s). A duration a test waits out
  is not. These are different things and the distinction matters: a test that
  cannot hang is good, a test that costs two seconds on every green run is not.

## Two design facts you will not guess from the code

**A blank tag never raises an `INSERT`.** The firmware cannot read a payload
from an unwritten tag; it settles into `UNREADABLE` and announces nothing. So
the primary use case — a new floppy with a new tag — is invisible to the event
stream, and the panel must ask via `TAGID`. This is why `GET /api/drive` exists
at all rather than the panel just listening to events.

**The wire protocol has no request IDs.** A reply is just `OK`, `PONG`,
`Tag ID: <UID>` or `ERROR <msg>` arriving on the same line-oriented stream as
unsolicited events, so the router pairs a reply with whatever command is
pending. That breaks when a command times out: the late reply would be handed
to the *next* command. Phase 3 added a single expiring orphan slot that absorbs
it, and the next command waits for a clear slot before going on the wire. Note
it is a *slot*, not a counter — a counter is only correct if a cancelled
command's reply is guaranteed to arrive, which is false in exactly the case
that produces the timeout.

Also worth knowing: `blankTag` means "the tracker has not seen this tag's
`INSERT`", not "this tag is blank". After a controller reconnect the tracker
starts empty while a cataloged floppy may still be seated, so the panel can
briefly say `blankTag` about a disk that is already assigned. The UI must never
treat that reading as proof a tag is free.

## How this work is run

Spec → plan → execution, using the `superpowers` skills: `brainstorming` writes
the spec, `writing-plans` writes the plan, `subagent-driven-development`
executes it one task per subagent with a review after each and a whole-branch
review at the end.

The execution ledger lives at
`.superpowers/sdd/<plan-basename>/progress.md` (git-ignored). Phase 3's records
every ruling made during execution with its reasoning and what it costs if
wrong — read it before re-deciding anything it already settled. It is also the
recovery map if context is lost: the commits it names exist in git even when
nothing remembers creating them.

The owner wants **small, stacked PRs** so they can review incrementally. Five
tasks, five PRs, plus the plan and the review fixes.

## Process lessons that cost real time

Written down because each was paid for once already.

**Verify by mutation, not by reading.** Phase 3 shipped **five tests that passed
for reasons unrelated to their names**, two of which survived deleting the very
line they claimed to cover. Every one was found by breaking the production line
a test names and checking that the test actually fails. A test you have not
mutated is a test you have not verified. This applies hardest to tests that
"obviously" work.

**Plan-authored code and tests are not exempt.** Six defects in phase 3 came
from the plan's own snippets: tests that could not fail, a fallthrough that read
an already-consumed `Response` body, an exit criterion stricter than the spec,
and a line that told each endpoint group to construct its own
`RetroBoxFloppyLibrary` — which gave them separate locks and lost a
just-written tag assignment in **37 of 40** concurrent runs. Implementers should
say so when a brief contradicts itself rather than implementing it silently; two
of them did, and both were right.

**When deferring something, check the premise.** One deferral in phase 3 rested
on "this only affects tags written by the CLI". It did not — nothing had *ever*
written the field the check depended on, so the phase's headline safety
guarantee was inert for every tag in existence. The reviewer found it by
grepping for writers of that field, which is a thirty-second check that should
have happened before the deferral.

**Copy a git worktree without `.git` before mutating it.** `cp -R` of a worktree
brings a `.git` *file* pointing back at the real repository, so `git` commands
inside the copy are not sandboxed. That produced one misleading mutation result.
Use `tar --exclude=.git`.

**Large diffs and subagents.** Three review dispatches died on API errors during
phase 3's final review. Splitting the review into disjoint halves with explicit
line ranges helped, but the last one died before reading anything — so when
dispatches keep failing, verify the remaining claims directly rather than
spending a fourth attempt.
