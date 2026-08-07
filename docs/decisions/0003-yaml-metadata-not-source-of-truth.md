# 0003. 86Box config is the hardware source of truth

Date: 2026-07-28
Status: Accepted

## Context

Each VM has native emulator hardware settings (CPU, RAM, video, sound, disks,
optical drives). RetroBox also keeps YAML catalogs. Two representations of the
same machine invite drift and duplicated validation.

## Decision

The **86Box `.cfg` in each VM profile is the authoritative hardware
description**. RetroBox YAML (`vms.yaml`) holds only metadata for display and
selection: a label and the profile path. `config.yaml` never encodes hardware
state. VM selection and boot read YAML for cataloging; 86Box reads the `.cfg`.

## Consequences

- No need to mirror emulator hardware in YAML or keep a schema for it.
- A VM profile is usable by 86Box even without RetroBox YAML.
- Hardware changes are edits to the `.cfg` only; YAML metadata changes are
  edits to `vms.yaml` only.
