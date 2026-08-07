# 0004. NFC tags carry raw bytes, not NDEF

Date: 2026-07-28
Status: Accepted

## Context

A physical floppy needs a machine-readable label that identifies which cataloged
image it maps to. NTAG21x / MIFARE Ultralight tags natively support NDEF
records, which is the "standard" choice for phone-readable NFC.

## Decision

Tags are written with **raw `<id>,<mode>` bytes into pages 4 through 11** —
8 pages × 4 bytes = 32 bytes maximum, zero-padded. This is deliberately *not* an
NDEF record.

## Consequences

- Tags are not phone-readable as text, which is fine: they are read by the
  floppy controller firmware, never by a phone.
- Payloads are limited to 32 bytes (`<catalog-id>,ro` fits comfortably).
- The writer and reader stay simple; no NDEF library dependency in firmware.
- Reads stop at the first `0x00` byte and trim trailing whitespace.
