# Cover search and download improvements

## Goal
Make ScreenScraper cover workflows usable for slow responses, identifiable through cover previews, and tolerant of larger legitimate cover files.

## Acceptance criteria
- ScreenScraper request/download timeout and maximum downloaded cover size are configurable and have safer defaults.
- Search results include a usable box-art thumbnail and the UI displays it beside the game title before selection.
- Oversized cover failures are avoided for the configured limit and remain safely bounded.
- Settings are persisted without exposing scraper secrets; existing behavior and rollback guarantees remain intact.
- `mise run test` and `mise run format-check` pass.

## Constraints/decisions
- Keep the native-AOT Minimal API and embedded frontend architecture.
- Reuse the existing box-2D media URL as the preview; no third-party runtime assets.
- Store timeout in seconds and download limit in megabytes in `scraper.yaml`.

## Tasks
- [x] Extend scraper settings, DTOs, persistence, and HTTP client configuration.
- [x] Return thumbnail metadata from search and render selectable previews.
- [x] Apply configurable timeout and download-size limits to cover downloads.
- [x] Add/update UI strings and settings controls.
- [x] Run review and final verification.

## Current phase
Complete; implementation and verification finished.

## Test commands
- `mise run test`
- `mise run format-check`

## Review findings
Review found no correctness issues in the final diff. Existing secret redaction and rollback paths remain unchanged.

## Final verification evidence
- `mise run format-check` — passed.
- `mise run test` — passed, 445/445 tests.
