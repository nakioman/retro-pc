# nfc-catalog-status Specification

## Purpose

Adds an `Nfc` boolean to `RetroBoxFloppy` catalog entries, persisted to `floppies.yaml`, so a successful NFC `WRITE` is recorded as `nfc: true`. The field is additive and backward-compatible: older binaries ignore it.

## ADDED Requirements

### Requirement: Catalog Nfc Field

`RetroBoxFloppy` SHALL gain an `Nfc` boolean, default `false`. `RetroBoxConfigStore` MUST persist it to `floppies.yaml` (YamlDotNet) as `Nfc: true`/`Nfc: false`. Adding the field MUST be additive and backward-compatible: an older binary reading the same `floppies.yaml` SHALL ignore the `Nfc` key without error.

#### Scenario: Default value on new floppy

- WHEN a floppy is imported without setting `Nfc`
- THEN its `Nfc` value defaults to `false`

#### Scenario: Persist Nfc true

- GIVEN a floppy with `Nfc` set to `true`
- WHEN `RetroBoxConfigStore` saves `floppies.yaml`
- THEN the file contains `Nfc: true` for that entry

#### Scenario: Backward compatibility

- GIVEN `floppies.yaml` containing a `Nfc: true` key for a floppy
- WHEN an older binary that predates the `Nfc` field reads the file
- THEN it ignores the key without error and loads the floppy normally

#### Scenario: Forward compatibility on absence

- GIVEN `floppies.yaml` written by an older binary (no `Nfc` key)
- WHEN the new binary reads it
- THEN each floppy's `Nfc` defaults to `false`