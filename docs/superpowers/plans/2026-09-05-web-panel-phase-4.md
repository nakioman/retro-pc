# RetroBox Web Panel Phase 4 — Games Grouping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a validated `games.yaml` catalog and let the web panel create, edit, delete, and display floppy groups.

**Architecture:** Extend the atomic catalog snapshot with games, load and save `games.yaml` alongside the existing YAML set, and keep all mutations behind the shared `RetroBoxFloppyLibrary` lock. The web API returns grouped catalog DTOs while the existing vanilla JS renders groups and an explicit ungrouped section.

**Tech Stack:** .NET 10, C# 13, YamlDotNet static serialization, ASP.NET Core Minimal APIs, source-generated `System.Text.Json`, xUnit, embedded HTML/CSS/vanilla JS.

**Spec:** `docs/superpowers/specs/2026-09-03-web-panel-design.md`

## Global Constraints

- Use `mise` tasks only; never invoke `dotnet` directly.
- Preserve Native AOT compatibility and register every new YAML/HTTP DTO in the source-generated contexts.
- Keep Minimal APIs, embedded static assets, no npm/CDN/build step, and no authentication/TLS.
- API JSON remains camelCase with stable error codes; Spanish is primary and English must have complete key parity.
- Covers are metadata only in this phase; do not validate or serve cover files yet.
- Every PR ends with `mise run test` and `mise run format-check`.
- Tests must use failure deadlines rather than fixed-duration sleeps.

---

### Task 1: Activate the game catalog and validation

**Branch:** `feat/web-games-model` (base: current phase-3 tip)

**Files:**
- Modify: `src/RetroBox.Core/RetroBoxCatalogModels.cs`
- Modify: `src/RetroBox.Core/RetroBoxConfigStore.cs`
- Modify: `src/RetroBox.Core/RetroBoxYamlContext.cs`
- Modify: `src/RetroBox.Core/RetroBoxCatalogSource.cs`
- Test: `tests/RetroBox.Tests/RetroBoxConfigStoreTests.cs`
- Test: `tests/RetroBox.Tests/RetroBoxWatchingCatalogSourceTests.cs`

**Interfaces:**
- Produces `RetroBoxCatalogData(..., IReadOnlyDictionary<string, RetroBoxGame> Games)` and `RetroBoxGame` with settable `Label`, nullable `Cover`, nullable `ScreenScraperId`, and `List<string> FloppyIds`.
- `games.yaml` uses `RetroBoxGameCatalog.Games` and is optional when absent.

- [ ] Write tests for loading an absent games file as empty, round-tripping game metadata, invalid game IDs/labels, unknown floppy references, and duplicate floppy membership.
- [ ] Run `mise run test` and confirm the new tests fail for the missing model/store behavior.
- [ ] Add the game records, remove `DefaultVm`, load/save `games.yaml`, and include games in the atomic `SaveYamlSet` operation.
- [ ] Add validation enforcing the exact rules from the spec while allowing ungrouped floppies and ignoring missing covers.
- [ ] Register all new YAML records in `RetroBoxYamlContext` and update every `RetroBoxCatalogData` construction site.
- [ ] Run focused tests, then `mise run test` and `mise run format-check`.
- [ ] Commit as `feat(core): activate games catalog`.

### Task 2: Add grouped catalog API and game mutations

**Branch:** `feat/web-games-api` (base: `feat/web-games-model`)

**Files:**
- Create: `src/RetroBox.Web/RetroBoxGameEndpoints.cs`
- Modify: `src/RetroBox.Web/RetroBoxCatalogEndpoints.cs`
- Modify: `src/RetroBox.Web/RetroBoxWebContracts.cs`
- Modify: `src/RetroBox.Web/RetroBoxWebHost.cs`
- Modify: `src/RetroBox.Core/RetroBoxFloppyLibrary.cs`
- Modify: `src/RetroBox.Web/RetroBoxWebJsonContext.cs` or its existing JSON context file
- Test: `tests/RetroBox.Tests/RetroBoxWebHostTests.cs`

**Interfaces:**
- `GET /api/catalog` returns games with nested floppy views plus `ungroupedFloppies`.
- `POST /api/games` accepts `{ id, label }` and returns the created game.
- `PATCH /api/games/{id}` accepts `{ label, floppyIds }`; membership is replaced atomically.
- `DELETE /api/games/{id}` removes only the group and leaves its floppies cataloged.

- [ ] Add integration tests covering grouped/ungrouped catalog JSON, create, membership replacement, delete, duplicate membership rejection, unknown floppy rejection, and invalid catalog errors.
- [ ] Run `mise run test` to establish the failing API baseline.
- [ ] Add focused library operations that load one catalog snapshot, modify games, validate, and save through the existing shared lock.
- [ ] Implement endpoint error mapping with stable codes such as `invalid-request`, `unknown-game`, `unknown-floppy`, `duplicate-membership`, and `catalog-unavailable`.
- [ ] Register every request/response DTO in the source-generated JSON context and map endpoints in the host.
- [ ] Run `mise run test` and `mise run format-check`.
- [ ] Commit as `feat(web): add games grouping API`.

### Task 3: Render grouped games in the panel

**Branch:** `feat/web-games-ui` (base: `feat/web-games-api`)

**Files:**
- Modify: `src/RetroBox.Web/wwwroot/index.html`
- Modify: `src/RetroBox.Web/wwwroot/app.js`
- Modify: `src/RetroBox.Web/wwwroot/app.css`
- Test: `tests/RetroBox.Tests/RetroBoxStaticAssetsTests.cs` or the existing web asset tests

**Interfaces:**
- Consume the grouped `GET /api/catalog` shape from Task 2.
- Use the existing `STRINGS`, `t()`, `data-i18n`, language picker, and error-code mapping; do not introduce another localization mechanism.

- [ ] Add asset-level tests asserting both language dictionaries contain every new key and the embedded resources remain available.
- [ ] Run the relevant tests and confirm they fail before adding the grouped markup/strings.
- [ ] Add Spanish-default and English-complete strings for game creation, editing, membership, deletion, grouped headings, and ungrouped disks.
- [ ] Render each game as a distinct section with its disks, plus “Sin agrupar”/“Ungrouped”; preserve search, upload, NFC badges, and existing drive behavior.
- [ ] Add minimal controls to create a group, edit its label/membership, and delete/ungroup it, with confirmation and API error presentation.
- [ ] Add responsive hand-written CSS matching the existing palette and offline constraints.
- [ ] Run `mise run test` and `mise run format-check`.
- [ ] Commit as `feat(web): add grouped games panel`.

### Task 4: End-to-end hardening and documentation

**Branch:** `feat/web-games-verification` (base: `feat/web-games-ui`)

**Files:**
- Modify: `docs/architecture.md`
- Modify: relevant panel/API documentation under `docs/`
- Test: `tests/RetroBox.Tests/CliHelpSmokeTests.cs` or existing end-to-end web tests

- [ ] Add an end-to-end scenario that starts the real web host with a temporary catalog, creates a game, assigns disks, reloads the catalog, and verifies grouped output and ungrouped disks.
- [ ] Mutate the production path named by the test (temporarily remove the save or membership validation line) and verify the test fails, then restore the implementation.
- [ ] Document `games.yaml`, the API routes, membership semantics, and the fact that deleting a game does not delete floppies.
- [ ] Run `mise run test`, `mise run format-check`, and inspect the final diff for AOT/source-generation regressions, missing i18n keys, CDN references, and fixed sleeps.
- [ ] Commit as `docs(web): document games grouping`.

## Self-review

- The plan covers the spec's game model, YAML persistence, validation, grouped API, UI, localization, and phase-4 verification.
- Covers and ScreenScraper remain intentionally deferred to phase 5.
- No task introduces a new package, authentication, TLS, or a second catalog lock.
- All cross-task names are fixed above: `Games`, `RetroBoxGameCatalog`, grouped catalog DTOs, and the three game routes.
