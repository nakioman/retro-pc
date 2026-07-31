using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxBootSelectorTests
{
    [Fact]
    public void Valid_default_runs_without_opening_selector()
    {
        var root = CreateRoot("pentium100");
        var ui = new FakeSelectorUi(new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));

        var result = new RetroBoxBootSelector(new RetroBoxConfigStore(root), ui).Resolve();

        Assert.Equal("pentium100", result.VmId);
        Assert.Equal(0, ui.Calls);
    }

    [Fact]
    public void Missing_default_requires_selector_and_run_does_not_persist()
    {
        var root = CreateRoot("");
        var ui = new FakeSelectorUi(new RetroBoxBootSelectionDecision(
            RetroBoxBootSelectionAction.Run, "386sx16"));

        var result = new RetroBoxBootSelector(new RetroBoxConfigStore(root), ui).Resolve();

        Assert.Equal("386sx16", result.VmId);
        Assert.Equal(1, ui.Calls);
        Assert.DoesNotContain("defaultVm:", File.ReadAllText(Path.Combine(root, "config.yaml")));
    }

    [Fact]
    public void Missing_config_file_forces_selector()
    {
        var root = CreateRoot("pentium100");
        File.Delete(Path.Combine(root, "config.yaml"));
        var ui = new FakeSelectorUi(new RetroBoxBootSelectionDecision(
            RetroBoxBootSelectionAction.Run, "386sx16"));

        var result = new RetroBoxBootSelector(new RetroBoxConfigStore(root), ui).Resolve();

        Assert.Equal("386sx16", result.VmId);
        Assert.Equal(1, ui.Calls);
    }

    [Fact]
    public void Run_and_set_default_persists_before_returning()
    {
        var root = CreateRoot("pentium100");
        var ui = new FakeSelectorUi(new RetroBoxBootSelectionDecision(
            RetroBoxBootSelectionAction.RunAndSetDefault, "386sx16"));

        var result = new RetroBoxBootSelector(new RetroBoxConfigStore(root), ui).Resolve(selectorRequested: true);

        Assert.Equal("386sx16", result.VmId);
        Assert.Contains("defaultVm: 386sx16", File.ReadAllText(Path.Combine(root, "config.yaml")));
    }

    [Fact]
    public void Dry_run_selection_does_not_persist_new_default()
    {
        var root = CreateRoot("pentium100");
        var before = File.ReadAllText(Path.Combine(root, "config.yaml"));
        var ui = new FakeSelectorUi(new RetroBoxBootSelectionDecision(
            RetroBoxBootSelectionAction.RunAndSetDefault, "386sx16"));

        _ = new RetroBoxBootSelector(new RetroBoxConfigStore(root), ui).Resolve(
            selectorRequested: true, persistDefault: false);

        Assert.Equal(before, File.ReadAllText(Path.Combine(root, "config.yaml")));
    }

    [Fact]
    public void Explicit_selection_bypasses_selector_and_validates_id()
    {
        var root = CreateRoot("pentium100");
        var ui = new FakeSelectorUi(new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var selector = new RetroBoxBootSelector(new RetroBoxConfigStore(root), ui);

        Assert.Equal("386sx16", selector.Resolve("386sx16").VmId);
        Assert.Equal(0, ui.Calls);
        var error = Assert.Throws<RetroBoxCatalogException>(() => selector.Resolve("missing"));
        Assert.Contains("Unknown VM 'missing'", error.Message);
    }

    [Fact]
    public void Cancellation_falls_back_to_default_or_fails_without_one()
    {
        var withDefault = CreateRoot("pentium100");
        var result = new RetroBoxBootSelector(new RetroBoxConfigStore(withDefault),
            new FakeSelectorUi(new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel)))
            .Resolve(selectorRequested: true);
        Assert.Equal("pentium100", result.VmId);

        var withoutDefault = CreateRoot("");
        var error = Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxBootSelector(
            new RetroBoxConfigStore(withoutDefault),
            new FakeSelectorUi(new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel)))
            .Resolve(selectorRequested: true));
        Assert.Contains("cancelled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hotkey_detector_accepts_f12_ignores_other_keys_and_times_out()
    {
        var f12 = new FakeConsoleInput(new ConsoleKeyInfo('x', ConsoleKey.F12, false, false, false));
        Assert.True(new RetroBoxBootHotkeyDetector(f12, new FakeClock()).IsSelectorRequested());

        var other = new FakeConsoleInput(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
        var otherClock = new FakeClock();
        Assert.False(new RetroBoxBootHotkeyDetector(other, otherClock).IsSelectorRequested());
        Assert.Equal(TimeSpan.FromSeconds(1), otherClock.Elapsed);
    }

    [Fact]
    public void Empty_catalog_is_rejected_with_clear_error()
    {
        var root = CreateRoot("");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), "vms: {}\n");

        var error = Assert.Throws<RetroBoxCatalogException>(() => new RetroBoxBootSelector(
            new RetroBoxConfigStore(root),
            new FakeSelectorUi(new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel)))
            .Resolve());

        Assert.Contains("No virtual machines", error.Message);
    }

    private static string CreateRoot(string defaultVm)
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-boot-selector-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.yaml"), string.IsNullOrEmpty(defaultVm) ? "{}\n" : $"defaultVm: {defaultVm}\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), """
            vms:
              pentium100:
                label: "Pentium 100"
                path: "/data/vms/pentium100"
              386sx16:
                label: "386SX-16"
                path: "/data/vms/386sx16"
            """);
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
        return root;
    }

    private sealed class FakeSelectorUi(RetroBoxBootSelectionDecision decision) : IRetroBoxBootSelectorUi
    {
        public int Calls { get; private set; }

        public RetroBoxBootSelectionDecision Select(
            IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines,
            string? defaultVmId)
        {
            Calls++;
            return decision;
        }
    }

    private sealed class FakeConsoleInput(params ConsoleKeyInfo[] keys) : IRetroBoxConsoleInput
    {
        private readonly Queue<ConsoleKeyInfo> remaining = new(keys);

        public bool KeyAvailable => remaining.Count > 0;

        public ConsoleKeyInfo ReadKey(bool intercept) => remaining.Dequeue();
    }

    private sealed class FakeClock : IRetroBoxBootClock
    {
        public DateTimeOffset Now { get; private set; }

        public TimeSpan Elapsed => Now - DateTimeOffset.MinValue;

        public void Sleep(TimeSpan duration) => Now += duration;
    }
}
