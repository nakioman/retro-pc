using System.Globalization;

namespace RetroBox.Core;

public sealed record RetroBoxVmDiskPlan(string Path, long SizeBytes, string Format);

public enum RetroBoxVmDiskOutcome
{
    Created,
    Kept,
}

public static class RetroBoxVmDiskPlanner
{
    /// <summary>
    /// Resolves a VM's declared disks to paths and sizes. For 86Box the .cfg geometry is
    /// authoritative (ADR 0009): a declared size must match it.
    /// </summary>
    public static IReadOnlyList<RetroBoxVmDiskPlan> Plan(string vmId, RetroBoxVm vm)
    {
        if (vm.Disks.Count == 0)
        {
            return [];
        }

        var configDisks = vm.Engine == RetroBoxVmEngines.EightySixBox ? Read86BoxDisks(vmId, vm) : null;
        var plans = new List<RetroBoxVmDiskPlan>();
        foreach (var disk in vm.Disks)
        {
            var path = ResolvePath(vm.Path, disk.File);
            long? declared = disk.Size is not null && RetroBoxVmCatalogRules.TryParseDiskSize(disk.Size, out var bytes)
                ? bytes
                : null;

            long size;
            if (configDisks is not null)
            {
                var configDisk = configDisks.FirstOrDefault(candidate => ResolvePath(vm.Path, candidate.File) == path)
                    ?? throw new RetroBoxCatalogException(
                        $"VM '{vmId}' disk '{disk.File}' has no hdd_NN_fn entry in {RetroBox86BoxConfig.FileName}.");
                size = configDisk.SizeBytes;
                if (declared is not null && declared != size)
                {
                    throw new RetroBoxCatalogException(
                        $"VM '{vmId}' disk '{disk.File}' size {declared} bytes does not match hdd_{configDisk.Id}_parameters ({size} bytes); the 86Box geometry is authoritative.");
                }
            }
            else
            {
                size = declared ?? throw new RetroBoxCatalogException($"VM '{vmId}' disk '{disk.File}' needs a size.");
            }

            plans.Add(new RetroBoxVmDiskPlan(path, size, disk.Format));
        }

        return plans;
    }

    private static IReadOnlyList<RetroBox86BoxHardDisk> Read86BoxDisks(string vmId, RetroBoxVm vm)
    {
        var configPath = Path.Combine(vm.Path, RetroBox86BoxConfig.FileName);
        if (!File.Exists(configPath))
        {
            throw new RetroBoxCatalogException($"VM '{vmId}' profile is invalid: '{configPath}' does not exist.");
        }

        return RetroBox86BoxConfig.ReadHardDisks(configPath);
    }

    private static string ResolvePath(string profilePath, string file) =>
        Path.GetFullPath(Path.Combine(profilePath, file));
}

/// <summary>Creates missing VM disks; an existing path is never opened, so installed systems are safe.</summary>
public sealed class RetroBoxVmDiskCreator(IRetroBoxProcessRunner runner, string qemuImgPath)
{
    public RetroBoxVmDiskOutcome Create(RetroBoxVmDiskPlan plan)
    {
        if (Path.Exists(plan.Path))
        {
            return RetroBoxVmDiskOutcome.Kept;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plan.Path) ?? ".");
        if (plan.Format == RetroBoxVmDiskFormats.Qcow2)
        {
            CreateQcow2(plan);
        }
        else
        {
            using var stream = new FileStream(plan.Path, FileMode.CreateNew, FileAccess.Write);
            stream.SetLength(plan.SizeBytes);
        }

        return RetroBoxVmDiskOutcome.Created;
    }

    // falloc reserves the full virtual size on /data up front (near-instant via fallocate), so a
    // guest can never hit ENOSPC on the host mid-install the way a growing qcow2 can.
    private void CreateQcow2(RetroBoxVmDiskPlan plan)
    {
        if (!File.Exists(qemuImgPath))
        {
            throw new IOException(
                $"Cannot create qcow2 disk '{plan.Path}': qemu-img was not found at '{qemuImgPath}'. Install the QEMU package (/opt/qemu-retrobox) or pass --qemu-img.");
        }

        var exitCode = runner.Run(new RetroBoxProcessSpec(
            qemuImgPath,
            ["create", "-f", RetroBoxVmDiskFormats.Qcow2, "-o", "preallocation=falloc", plan.Path, plan.SizeBytes.ToString(CultureInfo.InvariantCulture)])
        {
            Environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LD_LIBRARY_PATH"] = RetroBoxQemuBundle.LibraryDirectory(qemuImgPath),
            },
            DiscardStandardOutput = true,
        });
        if (exitCode != 0)
        {
            throw new IOException($"qemu-img exited with code {exitCode} while creating '{plan.Path}'.");
        }
    }
}
