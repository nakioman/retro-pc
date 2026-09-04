using System.CommandLine;
using RetroBox.Cli;
using RetroBox.Core;

namespace RetroBox.Tests;

[Collection(CliConsoleTestCollection.Name)]
public sealed class CliBootSessionTests
{
    [Fact]
    public void After_vm_exit_selector_opens_and_runs_selected_vm_until_cancel()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var ui = new ScriptedSelectorUi(null,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, "386sx16"),
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey());

        var exitCode = command.Parse(["boot", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["pentium100", "386sx16"], runs);
        Assert.Equal(2, ui.Calls);
    }

    [Fact]
    public void Cancel_after_vm_exit_does_not_relaunch_default_vm()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var ui = new ScriptedSelectorUi(null,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey());

        var exitCode = command.Parse(["boot", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["pentium100"], runs);
        Assert.Equal(1, ui.Calls);
    }

    [Fact]
    public void Explicit_select_runs_once_without_opening_selector()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var ui = new ScriptedSelectorUi(null,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey());

        var exitCode = command.Parse(["boot", "--select", "386sx16", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["386sx16"], runs);
        Assert.Equal(0, ui.Calls);
    }

    [Fact]
    public void Non_zero_vm_exit_still_returns_to_selector()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var ui = new ScriptedSelectorUi(null,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey(), exitCode: 1);

        var exitCode = command.Parse(["boot", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["pentium100"], runs);
        Assert.Equal(1, ui.Calls);
    }

    [Fact]
    public void Selector_flag_loops_after_selected_vm_exits()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var ui = new ScriptedSelectorUi(null,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, "386sx16"),
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey());

        var exitCode = command.Parse(["boot", "--selector", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["386sx16"], runs);
        Assert.Equal(2, ui.Calls);
    }

    [Fact]
    public void Auto_run_covers_splash_before_vm_then_quits_when_selector_returns()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var log = new List<string>();
        var ui = new ScriptedSelectorUi(log,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey(), splashLog: log);

        var exitCode = command.Parse(["boot", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["Cover", "Quit", "Select"], log);
    }

    [Fact]
    public void Selector_quits_splash_before_each_selection_and_covers_before_vm()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var log = new List<string>();
        var ui = new ScriptedSelectorUi(log,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, "386sx16"),
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey(), splashLog: log);

        var exitCode = command.Parse(["boot", "--selector", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["Quit", "Select", "Cover", "Quit", "Select"], log);
    }

    [Fact]
    public void Explicit_select_covers_splash_without_selector()
    {
        var root = CreateRoot("pentium100");
        var runs = new List<string>();
        var log = new List<string>();
        var ui = new ScriptedSelectorUi(log,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey(), splashLog: log);

        var exitCode = command.Parse(["boot", "--select", "386sx16", "--config-root", root]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.Equal(["386sx16"], runs);
        Assert.Equal(["Cover"], log);
    }

    [Fact]
    public void Boot_failure_quits_splash_so_terminal_is_not_stuck()
    {
        var missing = Path.Combine(Path.GetTempPath(), "retrobox-boot-missing", Guid.NewGuid().ToString("N"));
        var log = new List<string>();
        var ui = new ScriptedSelectorUi(log,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand([], ui, new NoHotkey(), splashLog: log);

        var exitCode = command.Parse(["boot", "--config-root", missing]).Invoke();

        Assert.Equal(1, exitCode);
        Assert.Equal(["Quit"], log);
    }

    private static RootCommand CreateBootCommand(
        List<string> runs,
        IRetroBoxBootSelectorUi ui,
        IRetroBoxBootHotkeyDetector hotkey,
        int exitCode = 0,
        List<string>? splashLog = null)
    {
        var log = splashLog ?? [];
        return CliCommandFactory.CreateRootCommand(
            bootRunner: request =>
            {
                runs.Add(request.VmId);
                return exitCode;
            },
            hotkeyDetector: hotkey,
            selectorUi: ui,
            bootSplash: new RecordingSplash(log));
    }

    private static string CreateRoot(string defaultVm)
    {
        var root = Path.Combine(Path.GetTempPath(), "retrobox-cli-boot-tests", Guid.NewGuid().ToString("N"));
        var pentiumPath = Path.Combine(root, "pentium100");
        var sx16Path = Path.Combine(root, "386sx16");
        Directory.CreateDirectory(pentiumPath);
        Directory.CreateDirectory(sx16Path);
        File.WriteAllText(Path.Combine(pentiumPath, "86box.cfg"), string.Empty);
        File.WriteAllText(Path.Combine(sx16Path, "86box.cfg"), string.Empty);
        File.WriteAllText(Path.Combine(root, "config.yaml"), string.IsNullOrEmpty(defaultVm) ? "{}\n" : $"defaultVm: {defaultVm}\n");
        File.WriteAllText(Path.Combine(root, "vms.yaml"), $"""
            vms:
              pentium100:
                label: "Pentium 100"
                path: "{pentiumPath}"
              386sx16:
                label: "386SX-16"
                path: "{sx16Path}"
            """);
        File.WriteAllText(Path.Combine(root, "floppies.yaml"), "floppies: {}\n");
        return root;
    }

    private sealed class NoHotkey : IRetroBoxBootHotkeyDetector
    {
        public bool IsSelectorRequested() => false;
    }

    private sealed class RecordingSplash(List<string> log) : IBootSplash
    {
        public void Quit() => log.Add("Quit");

        public void Cover() => log.Add("Cover");
    }

    private sealed class ScriptedSelectorUi(List<string>? log, params RetroBoxBootSelectionDecision[] decisions) : IRetroBoxBootSelectorUi
    {
        private readonly List<string>? log = log;
        private readonly RetroBoxBootSelectionDecision[] decisions = decisions;
        private int index;

        public int Calls { get; private set; }

        public RetroBoxBootSelectionDecision Select(
            IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines,
            string? defaultVmId)
        {
            Calls++;
            log?.Add("Select");
            return decisions[Math.Min(index++, decisions.Length - 1)];
        }
    }
}
