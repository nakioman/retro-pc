#!/usr/bin/env bash
# Check that the host can run QEMU VMs (KVM, the VMware backdoor for the XP
# guest's 3D driver, retrobox's device groups) and append the results to the
# install report. Nothing here fails the install: 86Box VMs need none of it.
# See hardware-detect.md.
#
# Runs after install_services (which installs the modprobe option) and
# create_accounts (which adds the groups).

CPUINFO_PATH="/proc/cpuinfo"
KVM_DEVICE="/dev/kvm"
KVM_MODPROBE_CONF="/etc/modprobe.d/retrobox-kvm.conf"

# The installer runs on the appliance's own hardware, so the live system shows
# whether the target can use KVM. The kernel hides vmx/svm when firmware has
# virtualization disabled.
_detect_kvm() {
    if grep -qw vmx "$CPUINFO_PATH" 2>/dev/null; then
        KVM_CPU_FLAG="vmx"
    elif grep -qw svm "$CPUINFO_PATH" 2>/dev/null; then
        KVM_CPU_FLAG="svm"
    else
        KVM_CPU_FLAG="NOT_FOUND"
    fi
    if [ -c "$KVM_DEVICE" ]; then KVM_DEVICE_STATUS="PRESENT"; else KVM_DEVICE_STATUS="MISSING"; fi
    if [ "$KVM_CPU_FLAG" != "NOT_FOUND" ] && [ "$KVM_DEVICE_STATUS" = "PRESENT" ]; then
        KVM_STATUS="USABLE"
    else
        KVM_STATUS="UNAVAILABLE"
    fi
}

_detect_vmware_backdoor() {
    if grep -Eq '^[[:space:]]*options[[:space:]]+kvm([[:space:]].*)?[[:space:]]enable_vmware_backdoor=(1|Y|y)([[:space:]]|$)' \
        "$TARGET_MNT$KVM_MODPROBE_CONF" 2>/dev/null; then
        KVM_VMWARE_BACKDOOR="CONFIGURED"
    else
        KVM_VMWARE_BACKDOOR="MISSING"
    fi
}

# Prints MEMBER or MISSING for each group, one per line.
_retrobox_group_membership() {
    local groups group
    groups=" $(in_target id -nG "$RETROBOX_USER" 2>/dev/null || true) "
    for group in "$@"; do
        case "$groups" in
            *" $group "*) printf '%s=MEMBER\n' "$group" ;;
            *) printf '%s=MISSING\n' "$group" ;;
        esac
    done
}

check_qemu_host() {
    local report="$TARGET_MNT/data/retrobox/install-report.txt" line group state

    log "Checking QEMU host support (KVM, VMware backdoor, retrobox groups)"
    _detect_kvm
    _detect_vmware_backdoor

    case "$KVM_STATUS" in
        USABLE) log "KVM: usable ($KVM_CPU_FLAG, $KVM_DEVICE)" ;;
        *) warn "KVM unavailable (CPU flag: $KVM_CPU_FLAG, $KVM_DEVICE: $KVM_DEVICE_STATUS); enable VT-x in the firmware setup. QEMU VMs will not start; 86Box VMs are unaffected." ;;
    esac
    [ "$KVM_VMWARE_BACKDOOR" = "CONFIGURED" ] \
        || warn "kvm enable_vmware_backdoor=1 missing from $KVM_MODPROBE_CONF; the XP guest's VMware SVGA driver will disable Direct3D."

    mkdir -p "$(dirname "$report")"
    {
        printf 'kvm.cpu_flag=%s\n' "$KVM_CPU_FLAG"
        printf 'kvm.device=%s\n' "$KVM_DEVICE_STATUS"
        printf 'kvm.status=%s\n' "$KVM_STATUS"
        printf 'kvm.vmware_backdoor=%s\n' "$KVM_VMWARE_BACKDOOR"
    } >> "$report"

    while IFS= read -r line; do
        group="${line%%=*}"
        state="${line#*=}"
        printf 'retrobox.group.%s=%s\n' "$group" "$state" >> "$report"
        [ "$state" = "MEMBER" ] && continue
        case "$group" in
            kvm) warn "$RETROBOX_USER is not in the kvm group; QEMU VMs cannot open $KVM_DEVICE." ;;
            *) warn "$RETROBOX_USER is not in the $group group; QEMU cannot use the GPU for 3D." ;;
        esac
    done < <(_retrobox_group_membership kvm render)
    ok "QEMU host checks recorded in the install report"
}
