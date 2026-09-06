# RetroBox mockup visual reference

This document applies only to `docs/mockups/final`. The approved Windows 3.1 mockups are the visual authority for this artifact. The repository-root `DESIGN.md` describes the existing dark frontend and must not override this mockup's explicitly confirmed appearance.

The surface is an operational desktop utility: compact task controls, readable tables, an observable running-VM state, and dialogs for edits and confirmations. Preserve the user's existing Windows 3.1 composition when migrating to React.

| Element | Implementation |
| --- | --- |
| Desktop | Teal `#008080`, 18px outside the main window. |
| Window and controls | Gray `#c0c0c0`, rectangular, white top/left bevels and black or gray bottom/right bevels. |
| Title bars | Navy `#000080`, white text, compact close button on dialogs. |
| Content | White inset scrolling workspace; gray game/VM groups and white data rows. |
| Tables | Collapsed 1px gray `#808080` borders on every cell; gray headers. |
| Typography | MS Sans Serif / Tahoma / Geneva / sans-serif, 13px base; 15px item titles; 11–12px supporting information. |
| CFG editor | Courier New / monospace, 13px with 1.5 line height, preserved whitespace. |
| Status | Dark green `#006400` with white text for running; pale yellow `#fff0b3` and dark brown for missing NFC. Labels always explain state. |
| Navigation | Two rectangular section tabs; selected tab joins the gray workspace frame. |
| Dialogs | Native modal semantics with custom Windows-style chrome; vertical action column at right. |
| Focus | Black dotted outline; standard keyboard operation. |

The typographic cover samples use Georgia inside small bounded book-cover-shaped rectangles. They are placeholders marked “MUESTRA”, not branding or genuine cover artwork. Their navy, red, and gray backgrounds are limited to sample cover content. A user-selected local image replaces them.

Do not add mobile adaptations, rounded cards, gradients, power controls, hardware selectors, or a bulk-backup action. Running status is informational. Preserve full backup-table borders and independently editable VM names. All CSS lives in `styles.css`; dialogs and repeated data rows share that visual system.

Floppy names are editable through a dedicated rename dialog. The current write mode is a closed/open lock with hover/focus tooltip and accessible label. All disk action buttons are in the right-aligned Actions column. NFC values are centered synthetic Tag IDs for assigned disks. Action groups and table action headers align right. Trash and download controls use consistent 16px outlined SVG icons (trash can and floppy disk), with accessible labels and hover titles.

Game title buttons disclose the disk table and expose expanded state. The bottom paginator shows 10 games per page, current page, matching game count, and previous/next controls. Expand/collapse-all controls remain in the library toolbar.
