# Cover Art and Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement phase 5 of the web panel with ScreenScraper search/download, manual cover upload, scraper Settings, and immediate Spanish/English selection.

**Architecture:** Add scraper persistence and a `IRetroBoxCoverSource` abstraction in the web/core boundary, with a real ScreenScraper HTTP implementation and deterministic cover selection. Keep all network and file operations behind testable services; expose them through AOT-compatible Minimal API endpoints and the existing embedded static frontend.

**Tech Stack:** .NET 10, C# 13, ASP.NET Core Minimal APIs, YamlDotNet source generation, `HttpClient`, xUnit, embedded HTML/CSS/JS.

**Spec:** `docs/superpowers/specs/2026-09-03-web-panel-design.md`

## Global Constraints

- The panel remains a single Native AOT binary; no Blazor, MVC, SignalR, Node build, or runtime CDN dependencies.
- Scraper secrets are write-only over HTTP; untouched secret fields are omitted from updates and explicit empty values clear them.
- Search and cover download require all four credentials; manual cover upload does not.
- Search returns only results with a usable `box-2D` image and uses configured region/language priorities.
- Covers are cached locally and never hotlinked; failed replacement leaves the previous cover usable.
- Accepted manual uploads are JPG/JPEG, PNG, or WebP; GIF is rejected.
- The language selector is in full-screen Settings, changes immediately, and persists in `localStorage`.
- Use `mise run test` and `mise run format-check` for verification.

---

### Task 1: Scraper catalog model and persistence

**Files:**
- Modify: `src/RetroBox.Core/...` existing catalog model/store/context files found by the implementer
- Create: `src/RetroBox.Web/...` scraper settings model/store files
- Test: `tests/RetroBox.Tests/...` scraper persistence tests

**Interfaces:**
- Produces a settable `RetroBoxScraperSettings` with four secret fields plus `RegionPriority` and `LanguagePriority`.
- Produces load/save behavior for `/data/retrobox/scraper.yaml`, defaulting to empty settings when absent.
- Produces DTOs that expose `configured`, `developerConfigured`, and `userConfigured` without secret values.

- [ ] **Step 1: Write failing tests** for absent settings, YAML round-trip, default priorities, all-four-credentials configuration, partial configuration flags, and explicit empty-secret clearing.
- [ ] **Step 2: Run the focused xUnit tests** with `mise run test -- --filter FullyQualifiedName~Scraper` and verify they fail for the missing model/store.
- [ ] **Step 3: Implement source-generated YAML registration and the settings store** using the repository's existing catalog-store conventions, preserving file mode 0600 where supported.
- [ ] **Step 4: Run the focused tests** and verify they pass.
- [ ] **Step 5: Run `mise run format-check`** for the changed files.

### Task 2: ScreenScraper client and cover selection

**Files:**
- Create: `src/RetroBox.Web/...` `IRetroBoxCoverSource`, ScreenScraper DTOs/client, and cover-selection service
- Modify: `src/RetroBox.Web/...` project registration and AOT JSON context
- Test: `tests/RetroBox.Tests/...` fake cover source/client tests

**Interfaces:**
- `IRetroBoxCoverSource.SearchAsync(string query, CancellationToken)` returns only results containing a usable `box-2D` image.
- `IRetroBoxCoverSource.GetGameAsync(string screenScraperId, CancellationToken)` returns media candidates.
- A selector walks `RegionPriority`, then `LanguagePriority`, then the first available `box-2D` candidate.
- All HTTP calls use bounded cancellation/timeouts and never expose credentials in exceptions or DTOs.

- [ ] **Step 1: Write failing tests** for required credentials, URL/query construction including `systemeid=135`, filtering missing `box-2D`, priority ordering, fallback ordering, timeout propagation, and ScreenScraper error propagation.
- [ ] **Step 2: Run the focused tests** and verify failure.
- [ ] **Step 3: Implement the fake-backed abstraction and real `HttpClient` client** with source-generated JSON DTOs and no dynamic serialization.
- [ ] **Step 4: Implement deterministic media selection** using configured priority arrays and a stable first-result fallback.
- [ ] **Step 5: Run the focused tests** and verify pass.
- [ ] **Step 6: Run `mise run format-check`**.

### Task 3: Safe cover cache replacement and Settings/scraper endpoints

**Files:**
- Create: `src/RetroBox.Web/...` cover cache and endpoint registration files
- Modify: `src/RetroBox.Web/...` application composition and JSON context
- Test: `tests/RetroBox.Tests/...` endpoint tests using `WebApplicationFactory`

**Interfaces:**
- `GET /api/settings/scraper` returns flags and priorities, never secrets.
- `PUT /api/settings/scraper` accepts optional secret fields plus validated closed priority lists; omitted secrets remain unchanged.
- `POST /api/settings/scraper/test` requires all four credentials and returns `scraper-not-configured` otherwise.
- `GET /api/scraper/search?q=` requires all four credentials, filters unusable results, and returns stable result DTOs.
- `POST /api/games/{id}/cover` confirms a selected ScreenScraper id, downloads the selected `box-2D`, stores it, replaces the old cover, and persists `screenScraperId`.
- Cover replacement writes the new file and catalog safely before removing the previous file; failures preserve the old state.

- [ ] **Step 1: Write failing endpoint tests** for secret redaction, omitted-vs-empty secrets, invalid priority values, missing credentials, search filtering, cover replacement, and failure rollback.
- [ ] **Step 2: Run the endpoint tests** and verify failure.
- [ ] **Step 3: Implement the settings/search endpoints** with flat error codes and AOT-registered request/response DTOs.
- [ ] **Step 4: Implement the cover cache transaction** and ScreenScraper confirmation endpoint.
- [ ] **Step 5: Run endpoint tests** and verify pass.

### Task 4: Manual cover upload endpoint

**Files:**
- Modify: `src/RetroBox.Web/...` cover cache and endpoint registration
- Test: `tests/RetroBox.Tests/...` upload endpoint tests

**Interfaces:**
- `POST /api/games/{id}/cover/upload` accepts one multipart image and returns the active cover metadata.
- Accepted extensions/content are JPG/JPEG, PNG, and WebP; GIF, unknown extensions, malformed images, and oversized requests return stable error codes.
- A valid upload clears `screenScraperId`; a failed upload leaves the previous cover and catalog unchanged.

- [ ] **Step 1: Write failing tests** for each accepted format, GIF rejection, malformed content, size cap, missing game, replacement, and rollback.
- [ ] **Step 2: Run the focused tests** and verify failure.
- [ ] **Step 3: Implement bounded multipart streaming and image validation** without loading unbounded request bodies into memory.
- [ ] **Step 4: Reuse the safe cover replacement service**, clearing `screenScraperId` only after the new cover is valid.
- [ ] **Step 5: Run the focused tests** and verify pass.

### Task 5: Settings UI, gear navigation, and cover workflows

**Files:**
- Modify: `src/RetroBox.Web/.../index.html`
- Modify: `src/RetroBox.Web/.../app.js`
- Modify: `src/RetroBox.Web/.../app.css`
- Test: `tests/RetroBox.Tests/...` web-host/static-asset endpoint tests where applicable

**Interfaces:**
- The library has a gear button that navigates to a full-screen Settings view; Settings has “Volver”.
- Settings displays language, region priority, scraper credential fields, configured flags, save, and credential-test actions.
- Secret inputs start blank/untouched, never render returned secret values, and are omitted from the update payload until edited.
- Language changes immediately, persists to `localStorage`, and updates all `data-i18n` strings.
- Game UI offers ScreenScraper search/confirm and manual upload; manual upload and confirmed ScreenScraper replacement are distinct actions.

- [ ] **Step 1: Add failing/static behavior tests or deterministic DOM checks** for gear navigation, Settings back navigation, secret omission, language persistence, and upload/search controls.
- [ ] **Step 2: Implement the Settings view and navigation** in the embedded assets without external CSS/JS dependencies.
- [ ] **Step 3: Implement closed, reorderable region/language priority controls** with move-up/move-down buttons and validation feedback.
- [ ] **Step 4: Implement scraper search-confirm and manual upload UI** with Spanish/English translations and API error-code rendering.
- [ ] **Step 5: Run the web tests, `mise run test`, and `mise run format-check`**.

### Task 6: Integration verification and documentation

**Files:**
- Modify: `docs/architecture.md` or the relevant web-panel documentation if the implementation differs from the approved design
- Modify: `docs/superpowers/specs/2026-09-03-web-panel-design.md` only if implementation reveals a concrete contract correction

- [ ] **Step 1: Run `mise run test`** and record/fix any failures.
- [ ] **Step 2: Run `mise run format-check`** and fix formatting failures.
- [ ] **Step 3: Run `mise run publish-linux-x64`** if the host/toolchain permits; otherwise record the expected macOS Native AOT limitation without changing the workflow.
- [ ] **Step 4: Review the diff for secret leakage, unbounded uploads, external asset dependencies, and stale spec language.**
