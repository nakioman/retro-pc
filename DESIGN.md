---
name: RetroBox
description: A nostalgic Windows 3.1-inspired control panel for managing a tactile 86Box retro-PC appliance.
colors:
  desktop-navy: "#0f172a"
  panel-slate: "#1e293b"
  inset-navy: "#131c31"
  line-slate: "#334155"
  text-slate: "#e2e8f0"
  muted-slate: "#94a3b8"
  terminal-green: "#34d399"
  warning-amber: "#fbbf24"
  danger-red: "#f87171"
typography:
  display:
    fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
    fontSize: "1.125rem"
    fontWeight: 700
    lineHeight: 1.5
  body:
    fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"
    fontSize: "0.75rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "0.08em"
rounded:
  xs: "0.25rem"
  sm: "0.375rem"
  md: "0.5rem"
spacing:
  xs: "0.25rem"
  sm: "0.5rem"
  md: "0.75rem"
  lg: "1rem"
  xl: "1.25rem"
  page: "1.5rem"
components:
  button-primary:
    backgroundColor: "{colors.inset-navy}"
    textColor: "{colors.terminal-green}"
    rounded: "{rounded.xs}"
    padding: "0.4rem 0.75rem"
  card:
    backgroundColor: "{colors.panel-slate}"
    textColor: "{colors.text-slate}"
    rounded: "{rounded.md}"
    padding: "1.25rem"
  input:
    backgroundColor: "{colors.inset-navy}"
    textColor: "{colors.text-slate}"
    rounded: "{rounded.xs}"
    padding: "0.4rem 0.6rem"
---

# Design System: RetroBox

## Overview

**Creative North Star: “The Windows 3.1 Manager”**

RetroBox is a nostalgic control surface for people who enjoy configuring real machines. Its visual language should feel like a lovingly maintained late-80s/early-90s system utility: dense but legible, monochrome-leaning, explicit about state, and built from practical panels rather than decorative marketing surfaces.

The current HTML/CSS implementation establishes a dark DOS-adjacent baseline: a navy desktop, slate panels, thin dividers, monospace text, and terminal-green action emphasis. The planned Vite/React interface should preserve that identity while expanding the UI into richer library, VM, drive, NFC, backup, and configuration workflows. Nostalgia should come from the palette, typography, labels, and panel grammar—not from sacrificing clarity or accessibility.

**Key Characteristics:**

- Windows 3.1 Manager / DOS utility inspiration
- Monospace, compact, technical typography
- Dark navy desktop with slate panel layering
- Green primary action and system-status accent
- Thin borders, restrained radii, and explicit state
- Tactile, inspectable, local-appliance personality

## Colors

The palette is a dark, cool utility surface with a single high-signal green accent and amber/red semantic states.

### Primary

- **Terminal Green** (#34d399): Primary actions, focus borders, healthy drive/tag states, and the RetroBox mark.

### Secondary

- **Warning Amber** (#fbbf24): Untagged media and attention-needed states.
- **Danger Red** (#f87171): Errors and destructive-state feedback.

### Neutral

- **Desktop Navy** (#0f172a): Page background and deepest utility surface.
- **Inset Navy** (#131c31): Inputs, secondary controls, nested forms, and recessed content.
- **Panel Slate** (#1e293b): Cards and primary containers.
- **Line Slate** (#334155): Borders, dividers, and inactive outlines.
- **Text Slate** (#e2e8f0): Primary readable content.
- **Muted Slate** (#94a3b8): Hints, subtitles, labels, and secondary metadata.

### Named Rules

**The Signal-Color Rule.** Green, amber, and red communicate state or action; do not use them as decoration.

## Typography

**Display Font:** ui-monospace, SFMono-Regular, Menlo, Consolas, monospace

**Body Font:** ui-monospace, SFMono-Regular, Menlo, Consolas, monospace

**Label/Mono Font:** Same monospace stack; labels use compact sizing and occasional uppercase treatment.

**Character:** The type system is deliberately machine-like and utilitarian. Hierarchy comes from size, weight, muted color, uppercase labels, and spacing rather than a mix of expressive fonts.

### Hierarchy

- **Display** (700, 1.125rem, 1.5): Product name and brand identity.
- **Headline** (400, 0.8125rem, 1.5, uppercase, 0.08em): Card and section headings.
- **Title** (600, 0.875rem, 1.5): Library items and game names.
- **Body** (400, 14px, 1.5): Controls, content, and operational copy.
- **Label** (400, 0.75rem, 1.5): Hints, field labels, metadata, and supporting status text.

## Layout

The page uses a centered operational workspace with a maximum width of 60rem and a 1.5rem page gutter. The header is a flexible two-sided bar: brand identity on the left, search and settings on the right. Main content is a single-column grid with 1.25rem vertical gaps.

Cards are the primary grouping primitive. Within cards, content uses compact 0.5–1rem gaps, with flex rows for actions and grid layouts for forms. At widths below 34rem, the page gutter reduces to 1rem, header tools become full width, and two-column credential/priority grids collapse to one column.

Future React screens should retain this panel-based scanability while introducing richer navigation only when it materially improves VM, media, drive, backup, or settings workflows.

## Elevation & Depth

The system is flat and layered rather than shadow-driven. Depth is conveyed through tonal changes between desktop navy, panel slate, and inset navy, plus thin slate borders. Avoid ornamental shadows and gradients; a stateful accent border is preferred when a surface needs emphasis.

### Named Rules

**The Tonal-Layer Rule.** Use background and inset changes to establish hierarchy; reserve shadows for a clearly justified interaction or overlay state.

## Shapes

Shapes are compact and restrained: 0.25rem controls and fields, 0.375rem nested groups and badges, and 0.5rem primary cards. Borders are 1px slate lines. Dashed borders are reserved for upload/drop targets. The drive status card uses a 3px green left rail as a signature state marker.

## Components

### Buttons

- **Shape:** Compact 0.25rem radius with 1px border.
- **Primary:** Inset navy background, green border/text, bold monospace label, `0.4rem 0.75rem` padding.
- **Hover / Focus:** Border shifts to terminal green; focus must remain visibly outlined without removing keyboard affordance.
- **Secondary:** Inset navy background with slate border and text; it becomes green-accented on hover.

### Cards / Containers

- **Corner Style:** 0.5rem for primary cards; 0.375rem for nested groups.
- **Background:** Panel slate for primary containers; inset navy for nested forms and list rows.
- **Shadow Strategy:** No default shadow; use tonal layering and borders.
- **Border:** 1px line slate; dashed for upload/drop surfaces.
- **Internal Padding:** 1.25rem primary cards, 0.75rem compact nested groups.

### Inputs / Fields

- **Style:** Inset navy background, 1px line slate border, 0.25rem radius, monospace text, compact padding.
- **Focus:** Terminal-green border with the browser’s visible focus behavior retained or an equally clear replacement.
- **Error / Disabled:** Red text for errors; disabled controls reduce opacity and use a progress/disabled cursor where appropriate.

### Navigation

The current surface uses a compact header with brand, search, and settings access rather than a persistent sidebar. A future React navigation may grow into a utility-style manager shell, but should remain compact, explicit, and easy to operate from keyboard and small screens.

### Drive Status Rail

The drive card is a signature component: a 3px terminal-green left rail communicates an active physical integration state, while the body exposes status, detail, and assignment actions.

## Do's and Don'ts

### Do:

- **Do** use monospace typography and explicit labels for operational controls.
- **Do** make green, amber, and red meaningful status signals.
- **Do** use borders and tonal layers to make panels easy to scan.
- **Do** preserve the physical-machine vocabulary: drives, floppies, tags, images, VMs, and media.
- **Do** keep controls keyboard-operable and status/error messages programmatically exposed.
- **Do** let the Vite/React migration add capability without losing the compact nostalgic utility character.

### Don't:

- **Don't** turn the interface into a generic modern SaaS dashboard with bright gradients, oversized hero sections, or decorative glass effects.
- **Don't** use accent colors without semantic meaning.
- **Don't** replace clear machine-state language with vague or purely friendly copy.
- **Don't** use excessive rounded corners, heavy shadows, or ornamental illustrations that weaken the Windows 3.1/DOS control-panel metaphor.
- **Don't** make nostalgia an excuse for inaccessible contrast, tiny targets, or mouse-only interactions.
