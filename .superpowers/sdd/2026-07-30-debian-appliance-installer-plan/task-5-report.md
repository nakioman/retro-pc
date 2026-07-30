# Task 5 report — Automate the build in GitHub Actions

## Delivered

- Added `.github/workflows/build-appliance-installer.yml` with the required
  manual and scoped-push triggers, read-only repository permissions, Ubuntu
  runner, checkout, mise setup, xorriso installation, test execution, and
  invocation of `appliance/installer/build-installer.sh`.
- Configured a seven-day `actions/upload-artifact@v4` artifact named
  `retro-pc-debian-installer-${{ github.sha }}` containing the BIOS ISO, SHA-256
  sidecar, and JSON metadata sidecar.
- Added a credential-free job summary for the selected `86BOX_VERSION` and ISO
  checksum.
- Extended the installer shell contract with workflow assertions and updated
  installer documentation to describe the available artifact workflow.

## Test-first evidence

The workflow contract assertion was added before the workflow existed and
failed as expected with `installer build workflow is missing`. After adding the
workflow, `bash tests/installer/installer-contract.sh` passed.

## Verification

- `bash tests/installer/installer-contract.sh` — passed.
- Local Ruby YAML parse of `.github/workflows/build-appliance-installer.yml` —
  passed.
- `git diff --check` — passed.
- `mise run test` — passed: 91 tests, 0 failures.

## Self-review

Reviewed the complete workflow, documentation, and contract-test diff against
the Task 5 brief. No actionable findings. The workflow delegates ISO creation
and its checksum/metadata sidecars exclusively to `build-installer.sh`; no
builder changes were required.
