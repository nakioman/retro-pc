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

    [Fact]
    public void Qemu_vm_boots_through_runner_with_engine_and_qemu_binary()
    {
        var root = CreateRoot("pentium100");
        var winxp = AddQemuVm(root, writeArgs: true);
        RetroBoxBootCommandRequest? captured = null;
        var command = CliCommandFactory.CreateRootCommand(
            bootRunner: request =>
            {
                captured = request;
                return 0;
            },
            hotkeyDetector: new NoHotkey(),
            selectorUi: new ScriptedSelectorUi(null),
            bootSplash: new RecordingSplash([]));

        var exitCode = command.Parse([
            "boot", "--select", "winxp", "--config-root", root, "--qemu-binary", "/custom/qemu-system-i386",
        ]).Invoke();

        Assert.Equal(0, exitCode);
        Assert.NotNull(captured);
        Assert.Equal(RetroBoxVmEngines.Qemu, captured.Engine);
        Assert.Equal("/custom/qemu-system-i386", captured.QemuBinaryPath);
        Assert.Equal(winxp, captured.VmPath);
    }

    [Fact]
    public void Eighty_six_box_vm_reports_engine_and_default_qemu_binary()
    {
        var root = CreateRoot("pentium100");
        RetroBoxBootCommandRequest? captured = null;
        var command = CliCommandFactory.CreateRootCommand(
            bootRunner: request =>
            {
                captured = request;
                return 0;
            },
            hotkeyDetector: new NoHotkey(),
            selectorUi: new ScriptedSelectorUi(null),
            bootSplash: new RecordingSplash([]));

        Assert.Equal(0, command.Parse(["boot", "--select", "pentium100", "--config-root", root]).Invoke());
        Assert.Equal(RetroBoxVmEngines.EightySixBox, captured?.Engine);
        Assert.Equal(RetroBoxQemuBundle.DefaultSystemBinaryPath, captured?.QemuBinaryPath);
    }

    [Fact]
    public void Qemu_vm_without_args_file_fails_and_quits_splash()
    {
        var root = CreateRoot("pentium100");
        AddQemuVm(root, writeArgs: false);
        var runs = new List<string>();
        var log = new List<string>();
        var error = new StringWriter();
        var command = CreateBootCommand(runs, new ScriptedSelectorUi(log), new NoHotkey(), splashLog: log);
        var parseResult = command.Parse(["boot", "--select", "winxp", "--config-root", root]);
        parseResult.InvocationConfiguration.Error = error;

        Assert.Equal(1, parseResult.Invoke());
        Assert.Empty(runs);
        Assert.Equal(["Quit"], log);
        Assert.Contains("qemu.args", error.ToString());
    }

    [Fact]
    public void Eighty_six_box_vm_without_config_fails()
    {
        var root = CreateRoot("pentium100");
        File.Delete(Path.Combine(root, "pentium100", "86box.cfg"));
        var error = new StringWriter();
        var command = CreateBootCommand([], new ScriptedSelectorUi(null), new NoHotkey());
        var parseResult = command.Parse(["boot", "--select", "pentium100", "--config-root", root]);
        parseResult.InvocationConfiguration.Error = error;

        Assert.Equal(1, parseResult.Invoke());
        Assert.Contains("86box.cfg", error.ToString());
    }

    [Fact]
    public void Selector_loop_runs_86box_and_qemu_vms_alike()
    {
        var root = CreateRoot("pentium100");
        AddQemuVm(root, writeArgs: true);
        var runs = new List<string>();
        var log = new List<string>();
        var ui = new ScriptedSelectorUi(log,
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, "winxp"),
            new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel));
        var command = CreateBootCommand(runs, ui, new NoHotkey(), splashLog: log);

        Assert.Equal(0, command.Parse(["boot", "--config-root", root]).Invoke());
        Assert.Equal(["pentium100", "winxp"], runs);
        Assert.Equal(["Cover", "Quit", "Select", "Cover", "Quit", "Select"], log);
    }

    [Fact]
    public void Qemu_session_command_is_hidden_and_requires_its_options()
    {
        var help = new StringWriter();
        var root = CliCommandFactory.CreateRootCommand().Parse(["--help"]);
        root.InvocationConfiguration.Output = help;
        root.Invoke();

        Assert.DoesNotContain("qemu-session", help.ToString());
        Assert.NotEmpty(CliCommandFactory.CreateRootCommand().Parse(["qemu-session"]).Errors);
    }

    private static string AddQemuVm(string root, bool writeArgs)
    {
        var winxp = Path.Combine(root, "winxp");
        Directory.CreateDirectory(winxp);
        if (writeArgs)
        {
            File.WriteAllText(Path.Combine(winxp, "qemu.args"), "-m\n3072\n");
        }

        File.AppendAllText(Path.Combine(root, "vms.yaml"), $"""

              winxp:
                label: "Windows XP"
                path: "{winxp}"
                engine: qemu
            """);
        return winxp;
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
