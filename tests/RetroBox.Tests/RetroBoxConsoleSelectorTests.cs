using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

public sealed class RetroBoxConsoleSelectorTests
{
    private static readonly IReadOnlyList<KeyValuePair<string, RetroBoxVm>> VirtualMachines =
    [
        new("pentium100", new RetroBoxVm { Label = "Pentium 100", Path = "/data/vms/pentium100" }),
        new("386sx16", new RetroBoxVm { Label = "386SX-16", Path = "/data/vms/386sx16" }),
    ];

    [Fact]
    public void Renders_banner_stable_numbered_vms_and_default_marker()
    {
        var terminal = new FakeConsole(Escape());

        _ = new RetroBoxConsoleSelector(terminal).Select(VirtualMachines, "pentium100");

        Assert.Equal(1, terminal.ClearCalls);
        Assert.Contains("Machine Selector", terminal.Output);
        Assert.Contains("1. Pentium 100 (default)", terminal.Output);
        Assert.Contains("2. 386SX-16", terminal.Output);
        Assert.Contains("Press a number to start.", terminal.Output);
    }

    [Fact]
    public void Number_starts_selected_vm_without_changing_default()
    {
        var terminal = new FakeConsole(Key('2'));

        var result = new RetroBoxConsoleSelector(terminal).Select(VirtualMachines, "pentium100");

        Assert.Equal(RetroBoxBootSelectionAction.Run, result.Action);
        Assert.Equal("386sx16", result.VmId);
    }

    [Fact]
    public void D_then_number_sets_default_and_starts_selected_vm()
    {
        var terminal = new FakeConsole(Key('d'), Key('2'));

        var result = new RetroBoxConsoleSelector(terminal).Select(VirtualMachines, null);

        Assert.Equal(RetroBoxBootSelectionAction.RunAndSetDefault, result.Action);
        Assert.Equal("386sx16", result.VmId);
        Assert.Contains("Set default VM", terminal.Output);
    }

    [Fact]
    public void Invalid_key_retries_before_selection()
    {
        var terminal = new FakeConsole(Key('9'), Key('1'));

        var result = new RetroBoxConsoleSelector(terminal).Select(VirtualMachines, null);

        Assert.Equal("pentium100", result.VmId);
        Assert.Contains("Invalid selection", terminal.Output);
    }

    [Fact]
    public void Escape_cancels_selection()
    {
        var terminal = new FakeConsole(Escape());

        var result = new RetroBoxConsoleSelector(terminal).Select(VirtualMachines, null);

        Assert.Equal(RetroBoxBootSelectionAction.Cancel, result.Action);
        Assert.Null(result.VmId);
    }

    private static ConsoleKeyInfo Key(char value) => new(value, ConsoleKey.NoName, false, false, false);

    private static ConsoleKeyInfo Escape() => new('\0', ConsoleKey.Escape, false, false, false);

    private sealed class FakeConsole(params ConsoleKeyInfo[] keys) : IRetroBoxConsole
    {
        private readonly Queue<ConsoleKeyInfo> keys = new(keys);
        private readonly StringWriter output = new();

        public int ClearCalls { get; private set; }

        public string Output => output.ToString();

        public void Clear() => ClearCalls++;

        public void WriteLine(string value = "") => output.WriteLine(value);

        public ConsoleKeyInfo ReadKey(bool intercept) => keys.Dequeue();
    }
}
