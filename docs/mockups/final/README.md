# RetroBox desktop mockup

This folder is the consolidated UX reference for the future React / TypeScript / Vite application with routing and i18n. It combines the seven sibling HTML mockups, including their later fixes. It is a standalone, interactive prototype, not the production web application.

## Open and share

Open `index.html` in a current desktop browser. No installation, build, server, API, or network connection is required. Keep all four runtime files together:

| File | Responsibility |
| --- | --- |
| `index.html` | Application shell, navigation, forms, and dialogs. |
| `styles.css` | Windows 3.1 visual tokens, layouts, tables, controls, and states. |
| `data.js` | Disposable game, floppy, VM, backup, and settings fixtures. |
| `app.js` | In-memory state, list rendering, navigation, and simulated interactions. |

Give the next agent this entire folder, including this README and `DESIGN.md`. The original mockups remain alongside it for provenance. All presentation rules are in CSS; there are no inline styles or inline event handlers. Dynamic list HTML is rendered by JavaScript; it is not a React component implementation.

## Confirmed scope

- Desktop only, with a Windows 3.1 gray window, navy title bars, teal desktop, beveled controls, and bordered tables. The application name is **RetroBox**.
- Two main sections: floppy/game library and virtual machines. Settings and individual operations use dialogs, matching the original mockups.
- Create a VM by entering its name and pasting an existing `86box.cfg`. There are no hardware selectors or generated hardware configurations.
- Edit a VM's name and CFG without recreating it. The stable ID, execution indicator, and backups survive a rename.
- Show which VM is running, if any. No power controls and no bulk backup action.
- Manage backups individually for each VM: list, create, download, and delete. Restore was not part of the supplied flow.
- Settings include Spanish/English selection and the existing ScreenScraper developer/user configuration. No COM port or WebNFC settings.
- Actions are simulated. Changes last only until reload. Translation behavior is intentionally omitted; all UI copy remains Spanish even when English is selected.

## Source reconciliation

| Original mockup | Included in this consolidation |
| --- | --- |
| `initial-mockup.html` | Grouped floppy tables, upload, read/write mode, NFC, disk deletion, adding disks. |
| `settings-dialog-addition.html` | Application menu and settings dialog; serial settings replaced with the existing ScreenScraper settings per user direction. |
| `cover-art-view.html` | Cover thumbnail, local image selection, search, preview, selection, accept/cancel. |
| `vms-tab-addition.html` | Main section navigation and VM execution status. Hardware selectors, power actions, and bulk backups explicitly excluded. |
| `vms-tab-creation-fix.html` | Name plus pasted CFG creation workflow. |
| `vms-backup-administration-fix.html` | CFG editing and per-VM backup administration. |
| `fix-backup-tables-borders.html` | Full table borders, including backup headers and cells. |

The name-editing workflow is the additional feature requested for this consolidation.

## UX walkthrough

1. **Library:** search by game or floppy filename. The matching game remains visible with its disks for context. “Modo actual” shows a closed/open lock with an accessible label and hover/focus tooltip for read-only/read-write. All disk buttons live in Actions: change mode, write NFC, rename, and delete. “Renombrar…” changes the display name while retaining the stable disk ID, mode, and NFC association. This does not rename an image file on disk. Delete asks for confirmation; deleting the last disk keeps the group with an empty-state message.
2. **Upload:** choose “Subir disquetes…”, enter a game/software name, choose one or more `.img`, `.ima`, or `.dsk` files, and confirm. Files create a new group. “Agregar disco…” attaches files to an existing group. New disks start read/write and without an NFC assignment, consistent with the supplied mocks.
3. **NFC:** choose “Escribir NFC…”, simulate disk detection, then write. Writing stays disabled until detection. The centered NFC column shows a synthetic Tag ID for assigned disks and “Sin NFC” otherwise. Tag IDs are demo fixtures, not readings from physical hardware. No actual serial or NFC operation occurs.
4. **Covers:** open “Carátula…”, choose a local PNG/JPEG/WebP or run a simulated search, select a result, and accept. Cancel/Escape discards the draft. Local images remain in browser memory. Typographic cover samples are deliberate offline placeholders, not real artwork or scraper results.
5. **VM creation:** switch to VMs, choose “Crear VM…”, supply a nonempty name and CFG, and create. The mockup checks required text only; it does not validate 86Box syntax or allocate disks.
6. **VM editing:** choose “Editar nombre / CFG…”. Change either field, then save or cancel. Editing the name preserves the VM's ID and backup list. CFG fixture contents are illustrative, not guaranteed bootable 86Box profiles.
7. **Backups:** choose “Administrar backups…”. An empty VM has an explicit empty state. “Crear backup…” opens an inline workflow dialog for a label; its confirmation inserts a synthetic timestamp and size. The floppy-disk icon downloads; trash icons delete. Icon buttons have accessible labels and hover titles, and action groups align right. Download creates a clearly named `*-DEMO.txt` receipt, not a backup archive. Deletion requires confirmation and affects only that VM's demo list.
8. **Settings:** open “Opciones…”. Use dummy developer ID/password and ScreenScraper ID/password. “Probar credenciales” checks that all four fields are populated, then reports simulated success. Region and metadata-language arrows reorder priorities. Accept retains settings for the page session; Cancel/Escape discards edits.

Navigation uses `#/floppies` and `#/vms`, including browser back/forward. The initial view is the library. Dialogs use native `showModal()` for keyboard containment and Escape behavior. Confirmation dialogs initially focus Cancel. Data is escaped before insertion into dynamic HTML.

## Production handoff

Preserve the visual flows, but replace the demo store and DOM rendering with typed React components and real services. Suggested component boundaries are `AppShell`, `LibraryPage`, `GameGroup`, `FloppyTable`, `VmPage`, `VmEditorDialog`, `VmBackupsDialog`, `CoverDialog`, `NfcWriteDialog`, `SettingsDialog`, and `ConfirmDialog`. These are organizational suggestions, not committed architecture or a mandated router library.

Use stable entity IDs as route parameters and React keys; labels must be independently editable. Keep form drafts separate from persisted state. Map the two section URLs to the selected router; decide dialog URL behavior during implementation. Do not copy global active IDs, event delegation, HTML strings, or fixture-only state flags into production architecture.

For i18n, move static labels, notifications, validation errors, accessible names, counts, and dialog titles into message catalogs. Use interpolation, plural rules, and locale-aware date/number formatting. Maintain an extensible UI locale registry starting with `es` and `en`. UI language and ScreenScraper metadata language priority are separate concepts. Do not translate filenames, VM IDs, user-entered names, or CFG contents. No i18n library is selected by this mockup.

### Existing repository integration points

Check the actual contracts before implementation; this prototype calls none of these endpoints.

| Concern | Existing source / route |
| --- | --- |
| Catalog | `src/RetroBox.Web/RetroBoxCatalogEndpoints.cs`, `GET /api/catalog`. |
| Game groups | `RetroBoxGameEndpoints.cs`: `POST /api/games`, `PATCH` / `DELETE /api/games/{id}`. |
| Floppies | `RetroBoxLibraryEndpoints.cs`: `POST /api/floppies`, `PATCH` / `DELETE /api/floppies/{id}`. |
| Physical disk state | `RetroBoxDriveEndpoints.cs`: `GET /api/drive`, drive events. |
| NFC write | `RetroBoxNfcEndpoints.cs`: `POST /api/nfc/write`. |
| Cover selection/upload | `RetroBoxCoverEndpoints.cs`: `POST /api/games/{id}/cover` and `/cover/upload`. |
| Scraper | `RetroBoxScraperEndpoints.cs`: `GET` / `PUT /api/settings/scraper`, `POST /api/settings/scraper/test`, `GET /api/scraper/search?q=…`. |
| Current settings form | `src/RetroBox.Web/wwwroot/index.html`, `src/RetroBox.Web/wwwroot/app.js`. |
| Settings rules | `src/RetroBox.Core/RetroBoxScraperSettings.cs`. |

ScreenScraper field names match the current settings shape: `devId`, `devPassword`, `ssId`, `ssPassword`, `regionPriority`, `languagePriority`, `requestTimeoutSeconds`, and `maxDownloadMegabytes`. Initial region order is `sp, wor, eu, us`; metadata languages are `es, en`; timeout is 60 seconds (5–600); maximum download is 16 MB (1–64). Production credential test behavior must follow the real endpoint, which tests saved settings. The mockup tests the current form for flow demonstration only. Never prepopulate secrets by copying this fixture approach; respect the backend's credential-presence flags and update semantics.

The production catalog can include ungrouped disks even though the source mockups demonstrate game groups. Plan that real-data state during implementation. The production NFC contract stores `nfc` and `mode: ro|rw`; the demo's `tagged` and `readOnly` are presentation-only fixtures. Tags contain `<id>,<mode>` bytes; the displayed `tagId` represents the separate physical UID. The current catalog does not establish persistent per-floppy UID storage; confirm the backend contract for this display during implementation. A real mode change must follow the existing backend/hardware rules, rather than simply toggling this demo boolean.

VM/backups HTTP contracts are **not established by this mockup**. Before connecting them, confirm name-update semantics, CFG application timing for a running VM, backup consistency during execution, deletion behavior for a running VM and its backups, archive format, and progress/error reporting. The simulated operations do not settle these product/backend decisions. In particular, deleting a VM removes its synthetic backups here only to keep the disposable demo state coherent; it does not authorize that production deletion policy.

Refer to `docs/architecture.md`, `docs/vm-profiles.md`, and the root `AGENTS.md` for domain rules. The mockup does not change the appliance or its current frontend.

## Verification and limitations

The prototype targets desktop windows at least 1000 CSS pixels wide. The library scrolls inside the application window; large dialogs scroll when the viewport height is limited. No mobile layout is included.

Browser checks covered library/VM navigation, local floppy selection and upload into a new group, VM creation, rename with existing backups preserved, backup creation/deletion, NFC detection/write, simulated cover search/selection, and missing-credential feedback. JavaScript syntax and the repository's `mise run format-check` were checked. These are prototype checks, not evidence that the production API or 86Box integrations work.

No external image dependencies, web fonts, browser storage, network requests, React dependencies, or installed packages are needed. There is no connection failure simulator, real backup generation, hardware access, or actual translation. Reload restores the sample data.

## Large-library navigation

The library paginates by game (10 per page), never splitting a game's disks. Search filters the entire library before pagination and resets to page one. The last page and empty results clamp correctly; previous/next buttons disable at the boundaries. Twelve groups, including ten explicitly synthetic software samples, make the second page available immediately.

Click a game title to collapse/expand its disk table. Cover, title, disk count, and group actions stay visible. “Expandir todo” and “Colapsar todo” affect all games across pages. Individual collapse state survives page changes and searches for this session. Settings includes “Iniciar con los juegos colapsados”; changing it applies to the current library on Accept, while Cancel discards the draft. Groups start expanded. As with all mockup settings, reload resets the preference. Production should persist that UI preference separately from ScreenScraper settings.
