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

public sealed class RetroBoxConsoleSelector(IRetroBoxConsole? console = null) : IRetroBoxBootSelectorUi
{
    private const string Banner = """
         ██████  ███████ ████████ ██████   ██████  ██████   ██████  ██   ██
         ██   ██ ██         ██    ██   ██ ██    ██ ██   ██ ██    ██  ██ ██
         ██████  █████      ██    ██████  ██    ██ ██████  ██    ██   ███
         ██   ██ ██         ██    ██   ██ ██    ██ ██   ██ ██    ██  ██ ██
         ██   ██ ███████    ██    ██   ██  ██████  ██████   ██████  ██   ██
        """;

    private readonly IRetroBoxConsole terminal = console ?? new SystemRetroBoxConsole();

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
                return new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, virtualMachines[index].Key);
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
                return new RetroBoxBootSelectionDecision(
                    RetroBoxBootSelectionAction.RunAndSetDefault,
                    virtualMachines[index].Key);
            }

            terminal.WriteLine("Invalid default VM. Press a listed number, or Esc to cancel.");
        }
    }

    private void Render(IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines, string? defaultVmId)
    {
        terminal.WriteLine(Banner);
        terminal.WriteLine();
        terminal.WriteLine("                          Machine Selector");
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

    private static bool TryGetVmIndex(ConsoleKeyInfo key, int count, out int index)
    {
        index = key.KeyChar - '1';
        return index >= 0 && index < count;
    }
}
