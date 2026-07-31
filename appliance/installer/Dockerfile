# Builder image for the RetroBox USB appliance installer.
#
# The Debian image tooling (mmdebstrap, mksquashfs, xorriso, isolinux) only runs
# on Linux, so on macOS the build runs in this container. mmdebstrap needs to
# create device nodes and mounts, so run the container privileged. The image is
# amd64 (the appliance target); on Apple Silicon add --platform linux/amd64 so
# mmdebstrap builds natively under emulation instead of failing on foreign arch:
#
#   docker build --platform linux/amd64 -t retropc-builder appliance/installer
#   docker run --rm --platform linux/amd64 --privileged -v "$PWD:/work" \
#       retropc-builder /work/appliance/installer/build-usb-installer.sh
#
# CI builds on a native amd64 Linux runner instead (see
# .github/workflows/build-usb-installer.yml) and does not need this image.
FROM debian:trixie-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
        mmdebstrap \
        squashfs-tools \
        xorriso \
        isolinux \
        syslinux-common \
        dosfstools \
        ca-certificates \
        debian-archive-keyring \
        zstd \
        xz-utils \
        e2fsprogs \
        file \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /work
ENTRYPOINT ["/bin/bash"]
