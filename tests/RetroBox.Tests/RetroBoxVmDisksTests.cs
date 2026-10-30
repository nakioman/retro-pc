using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxVmDisksTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "retrobox-vm-disk-tests", Guid.NewGuid().ToString("N"));

    public RetroBoxVmDisksTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("39, 4, 762, 0, ide", 60_862_464L)]
    [InlineData("63, 16, 4092, 0, ide", 2_111_864_832L)]
    [InlineData("63, 255, 2434, 0, ide", 20_020_331_520L)]
    public void ReadHardDisks_derives_size_from_geometry(string parameters, long expected)
    {
        var config = WriteConfig($"""
            [Hard disks]
            hdd_01_fn = hdd.raw
            hdd_01_ide_channel = 0:0
            hdd_01_parameters = {parameters}
            hdd_01_speed = CP30064
            """);

        var disk = Assert.Single(RetroBox86BoxConfig.ReadHardDisks(config));

        Assert.Equal("hdd.raw", disk.File);
        Assert.Equal(expected, disk.SizeBytes);
    }

    [Fact]
    public void ReadHardDisks_rejects_disk_without_valid_parameters()
    {
        var config = WriteConfig("""
            hdd_02_fn = second.raw
            hdd_02_parameters = 63, x, 10, 0, ide
            """);

        var error = Assert.Throws<RetroBoxCatalogException>(() => RetroBox86BoxConfig.ReadHardDisks(config));

        Assert.Contains("hdd_02_parameters", error.Message);
    }

    [Fact]
    public void Plan_uses_86box_geometry_when_size_is_omitted()
    {
        WriteConfig("hdd_01_fn = hdd.raw\nhdd_01_parameters = 39, 4, 762, 0, ide\n");
        var vm = Vm(RetroBoxVmEngines.EightySixBox, new RetroBoxVmDisk { File = "hdd.raw" });

        var plan = Assert.Single(RetroBoxVmDiskPlanner.Plan("386sx16", vm));

        Assert.Equal(Path.Combine(root, "hdd.raw"), plan.Path);
        Assert.Equal(60_862_464L, plan.SizeBytes);
        Assert.Equal(RetroBoxVmDiskFormats.Raw, plan.Format);
    }

    [Fact]
    public void Plan_accepts_86box_size_matching_geometry()
    {
        WriteConfig("hdd_01_fn = hdd.raw\nhdd_01_parameters = 39, 4, 762, 0, ide\n");
        var vm = Vm(RetroBoxVmEngines.EightySixBox, new RetroBoxVmDisk { File = "hdd.raw", Size = "60862464" });

        Assert.Equal(60_862_464L, Assert.Single(RetroBoxVmDiskPlanner.Plan("386sx16", vm)).SizeBytes);
    }

    [Fact]
    public void Plan_rejects_86box_size_that_disagrees_with_geometry()
    {
        WriteConfig("hdd_01_fn = hdd.raw\nhdd_01_parameters = 39, 4, 762, 0, ide\n");
        var vm = Vm(RetroBoxVmEngines.EightySixBox, new RetroBoxVmDisk { File = "hdd.raw", Size = "64M" });

        var error = Assert.Throws<RetroBoxCatalogException>(() => RetroBoxVmDiskPlanner.Plan("386sx16", vm));

        Assert.Contains("67108864", error.Message);
        Assert.Contains("60862464", error.Message);
    }

    [Fact]
    public void Plan_rejects_86box_disk_missing_from_config()
    {
        WriteConfig("hdd_01_fn = hdd.raw\nhdd_01_parameters = 39, 4, 762, 0, ide\n");
        var vm = Vm(RetroBoxVmEngines.EightySixBox, new RetroBoxVmDisk { File = "other.raw" });

        var error = Assert.Throws<RetroBoxCatalogException>(() => RetroBoxVmDiskPlanner.Plan("386sx16", vm));

        Assert.Contains("no hdd_NN_fn", error.Message);
    }

    [Fact]
    public void Plan_uses_declared_size_for_qemu_disks_without_reading_86box_config()
    {
        var vm = Vm(RetroBoxVmEngines.Qemu, new RetroBoxVmDisk { File = "xp.qcow2", Size = "20G", Format = "qcow2" });

        var plan = Assert.Single(RetroBoxVmDiskPlanner.Plan("winxp", vm));

        Assert.Equal(new RetroBoxVmDiskPlan(Path.Combine(root, "xp.qcow2"), 20L << 30, "qcow2"), plan);
    }

    [Fact]
    public void Create_makes_sparse_raw_file_of_exact_size()
    {
        var path = Path.Combine(root, "disks", "hdd.raw");
        var creator = new RetroBoxVmDiskCreator(new RecordingRunner(), "/missing/qemu-img");

        var outcome = creator.Create(new RetroBoxVmDiskPlan(path, 60_862_464L, "raw"));

        Assert.Equal(RetroBoxVmDiskOutcome.Created, outcome);
        Assert.Equal(60_862_464L, new FileInfo(path).Length);
    }

    [Fact]
    public void Create_keeps_existing_disk_untouched()
    {
        var path = Path.Combine(root, "hdd.raw");
        File.WriteAllText(path, "installed OS");
        var runner = new RecordingRunner();
        var creator = new RetroBoxVmDiskCreator(runner, "/missing/qemu-img");

        Assert.Equal(RetroBoxVmDiskOutcome.Kept, creator.Create(new RetroBoxVmDiskPlan(path, 1L << 20, "raw")));
        Assert.Equal(RetroBoxVmDiskOutcome.Kept, creator.Create(new RetroBoxVmDiskPlan(path, 1L << 20, "qcow2")));

        Assert.Equal("installed OS", File.ReadAllText(path));
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void Create_runs_qemu_img_for_qcow2_with_bundle_libraries()
    {
        var bin = Path.Combine(root, "opt", "bin");
        Directory.CreateDirectory(bin);
        var qemuImg = Path.Combine(bin, "qemu-img");
        File.WriteAllText(qemuImg, string.Empty);
        var runner = new RecordingRunner();
        var path = Path.Combine(root, "xp.qcow2");

        var outcome = new RetroBoxVmDiskCreator(runner, qemuImg).Create(new RetroBoxVmDiskPlan(path, 20L << 30, "qcow2"));

        Assert.Equal(RetroBoxVmDiskOutcome.Created, outcome);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(qemuImg, run.FileName);
        Assert.Equal(["create", "-f", "qcow2", path, "21474836480"], run.Arguments);
        Assert.Equal(Path.Combine(root, "opt", "lib"), run.Environment["LD_LIBRARY_PATH"]);
        Assert.True(run.DiscardStandardOutput);
    }

    [Fact]
    public void Create_reports_missing_qemu_img_clearly()
    {
        var creator = new RetroBoxVmDiskCreator(new RecordingRunner(), "/opt/qemu-retrobox/bin/qemu-img");

        var error = Assert.Throws<IOException>(() =>
            creator.Create(new RetroBoxVmDiskPlan(Path.Combine(root, "xp.qcow2"), 1L << 30, "qcow2")));

        Assert.Contains("qemu-img", error.Message);
        Assert.Contains("/opt/qemu-retrobox/bin/qemu-img", error.Message);
        Assert.False(File.Exists(Path.Combine(root, "xp.qcow2")));
    }

    [Fact]
    public void Create_reports_qemu_img_failure()
    {
        var qemuImg = Path.Combine(root, "qemu-img");
        File.WriteAllText(qemuImg, string.Empty);
        var creator = new RetroBoxVmDiskCreator(new RecordingRunner(exitCode: 1), qemuImg);

        var error = Assert.Throws<IOException>(() =>
            creator.Create(new RetroBoxVmDiskPlan(Path.Combine(root, "xp.qcow2"), 1L << 30, "qcow2")));

        Assert.Contains("exited with code 1", error.Message);
    }

    private RetroBoxVm Vm(string engine, params RetroBoxVmDisk[] disks) => new()
    {
        Label = "VM",
        Path = root,
        Engine = engine,
        Disks = [.. disks],
    };

    private string WriteConfig(string content)
    {
        var path = Path.Combine(root, "86box.cfg");
        File.WriteAllText(path, content);
        return path;
    }
}

internal sealed class RecordingRunner(int exitCode = 0) : IRetroBoxProcessRunner
{
    public List<RetroBoxProcessSpec> Runs { get; } = [];

    public List<string> Log { get; } = [];

    public int Run(RetroBoxProcessSpec spec)
    {
        Runs.Add(spec);
        Log.Add($"run {Path.GetFileName(spec.FileName)}");
        return exitCode;
    }

    public IDisposable Start(RetroBoxProcessSpec spec)
    {
        Runs.Add(spec);
        Log.Add($"start {Path.GetFileName(spec.FileName)}");
        return new Stopper(Log, Path.GetFileName(spec.FileName));
    }

    private sealed class Stopper(List<string> log, string name) : IDisposable
    {
        public void Dispose() => log.Add($"stop {name}");
    }
}
