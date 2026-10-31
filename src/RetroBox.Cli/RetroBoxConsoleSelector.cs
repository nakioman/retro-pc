using RetroBox.Core;

namespace RetroBox.Cli;

public interface IRetroBoxConsole
{
    void Clear();

    void WriteLine(string value = "");

    ConsoleKeyInfo ReadKey(bool intercept);
}

public sealed class SystemRetroBoxConsole : IRetroBoxConsole
{
    public void Clear() => Console.Clear();

    public void WriteLine(string value = "") => Console.WriteLine(value);

    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
}

public sealed class RetroBoxConsoleSelector(
    IRetroBoxConsole? console = null,
    Func<IReadOnlyList<string>>? ipAddresses = null) : IRetroBoxBootSelectorUi
{
    private const string Banner = """
         ██████  ███████ ████████ ██████   ██████  ██████   ██████  ██   ██
         ██   ██ ██         ██    ██   ██ ██    ██ ██   ██ ██    ██  ██ ██
         ██████  █████      ██    ██████  ██    ██ ██████  ██    ██   ███
         ██   ██ ██         ██    ██   ██ ██    ██ ██   ██ ██    ██  ██ ██
         ██   ██ ███████    ██    ██   ██  ██████  ██████   ██████  ██   ██
        """;

    private readonly IRetroBoxConsole terminal = console ?? new SystemRetroBoxConsole();
    private readonly Func<IReadOnlyList<string>> getIpAddresses = ipAddresses ?? RetroBoxNetworkAddresses.GetLanIPv4Addresses;

    public RetroBoxBootSelectionDecision Select(
        IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines,
        string? defaultVmId)
    {
        if (virtualMachines.Count > 9)
        {
            throw new RetroBoxCatalogException("The console selector supports at most nine virtual machines.");
        }

        terminal.Clear();
        Render(virtualMachines, defaultVmId);

        while (true)
        {
            var key = terminal.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                return new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel);
            }

            if (char.ToUpperInvariant(key.KeyChar) == 'D')
            {
                terminal.WriteLine("Set default VM: press its number, or Esc to cancel.");
                return SelectDefault(virtualMachines);
            }

            if (TryGetVmIndex(key, virtualMachines.Count, out var index))
            {
                return Starting(RetroBoxBootSelectionAction.Run, virtualMachines[index]);
            }

            terminal.WriteLine("Invalid selection. Press a listed number, D, or Esc.");
        }
    }

    private RetroBoxBootSelectionDecision SelectDefault(IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines)
    {
        while (true)
        {
            var key = terminal.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                return new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel);
            }

            if (TryGetVmIndex(key, virtualMachines.Count, out var index))
            {
                return Starting(RetroBoxBootSelectionAction.RunAndSetDefault, virtualMachines[index]);
            }

            terminal.WriteLine("Invalid default VM. Press a listed number, or Esc to cancel.");
        }
    }

    // The splash was quit to show this menu, so nothing else covers the console while the
    // emulator loads; without this screen the menu just sits there looking unresponsive.
    private RetroBoxBootSelectionDecision Starting(
        RetroBoxBootSelectionAction action,
        KeyValuePair<string, RetroBoxVm> selected)
    {
        terminal.Clear();
        terminal.WriteLine(Banner);
        terminal.WriteLine();
        terminal.WriteLine($"Starting {selected.Value.Label}...");
        terminal.WriteLine();
        terminal.WriteLine("Please wait while the emulator loads.");
        return new RetroBoxBootSelectionDecision(action, selected.Key);
    }

    private void Render(IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines, string? defaultVmId)
    {
        terminal.WriteLine(Banner);
        terminal.WriteLine();
        terminal.WriteLine("                          Machine Selector");
        terminal.WriteLine();
        terminal.WriteLine(FormatIpLine(getIpAddresses()));
        terminal.WriteLine();
        terminal.WriteLine("================================================");
        for (var index = 0; index < virtualMachines.Count; index++)
        {
            var (id, vm) = virtualMachines[index];
            var marker = id == defaultVmId ? " (default)" : string.Empty;
            terminal.WriteLine($"{index + 1}. {vm.Label}{marker}");
        }

        terminal.WriteLine("================================================");
        terminal.WriteLine("Press a number to start. Press D to set the default VM. Esc cancels.");
    }

    private static string FormatIpLine(IReadOnlyList<string> addresses) =>
        addresses.Count == 0 ? "IP: not connected" : $"IP: {string.Join(", ", addresses)}";

    private static bool TryGetVmIndex(ConsoleKeyInfo key, int count, out int index)
    {
        index = key.KeyChar - '1';
        return index >= 0 && index < count;
    }
}
