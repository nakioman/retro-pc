# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

The primary user is a person who enjoys retro PCs, is comfortable building computers, and is not afraid to configure and maintain hardware and software themselves.

## Product Purpose

RetroBox is a complete retro-PC appliance distribution that provides a pre-configured 86Box environment for creating, managing, and backing up virtual machines, while integrating NFC-labeled floppy disks for a tactile old-computer experience without requiring an original vintage PC. Success means the user can operate and maintain a convincing retro-computing setup through both the physical appliance and its local web control surface.

## Positioning

RetroBox combines a ready-to-use 86Box distribution, VM lifecycle management, appliance configuration, and real NFC floppy hardware into one self-hosted system. Its distinctive mechanism is the bridge between virtual retro PCs and physical, NFC-labeled floppy media that can be inserted and managed like the original hardware.

## Operating Context

RetroBox runs as a Debian-based appliance that boots into a fullscreen 86Box virtual machine. The user may build or configure the host hardware, manage VM profiles and floppy images, use a physical CD-ROM, prepare NFC-labeled floppy disks, and operate the system through local command-line and web interfaces. Persistent application state lives under the appliance data directory, while the system root is read-only.

## Capabilities and Constraints

- 86Box is pre-configured for retro PC profiles and can run fullscreen like a console.
- Users can create, manage, select, and back up virtual machines and their associated cataloged media.
- A modified floppy drive and ESP8266 NFC controller read labels from physical floppy shells and map them to floppy images and modes.
- The appliance supports physical CD-ROM passthrough and first-boot WiFi configuration.
- The web interface supports local library management, floppy-image upload, drive state and assignment workflows, game metadata creation, scraper configuration, and Spanish/English language selection.
- NFC labels use the raw `<id>,<mode>` format, not NDEF.
- The product is intended for local/self-hosted appliance use and must preserve the existing physical-hardware and 86Box workflows.

## Evidence on Hand

The repository contains the RetroBox appliance, CLI, daemon, core domain, firmware, hardware, and web implementation. Product behavior is documented in `README.md`, `docs/`, `appliance/`, and `firmware/retrofloppy-esp8266/`. No external customer, testimonial, benchmark, or marketing evidence is established; future work must not fabricate any.

## Product Principles

- Preserve the tactile ritual and delight of retro-computer use.
- Make advanced hardware and VM configuration approachable to technically confident builders.
- Keep the physical and virtual representations of media synchronized and trustworthy.
- Favor local ownership, inspectability, and recoverability over opaque hosted services.
- Treat the appliance as a complete product, not just an emulator launcher.

## Accessibility & Inclusion

No product-specific accessibility standard has been established. Future web work should preserve accessible controls, keyboard operation, clear status and error feedback, and usable contrast as baseline requirements.
