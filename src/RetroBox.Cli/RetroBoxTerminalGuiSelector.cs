using System.Collections.ObjectModel;
using RetroBox.Core;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace RetroBox.Cli;

public sealed class RetroBoxTerminalGuiSelector : IRetroBoxBootSelectorUi
{
    public RetroBoxBootSelectionDecision Select(
        IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines,
        string? defaultVmId)
    {
        using IApplication app = Application.Create();
        app.Init();

        var items = new ObservableCollection<string>(virtualMachines.Select(entry =>
            $"{entry.Key} — {entry.Value.Label}"));
        var list = new ListView<string>();
        list.SetSource(items);
        if (defaultVmId is not null)
        {
            var defaultIndex = virtualMachines
                .Select((entry, index) => (entry.Key, index))
                .FirstOrDefault(item => item.Key == defaultVmId)
                .index;
            list.Index = defaultIndex;
        }

        var result = new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Cancel);
        using var window = new Window
        {
            Title = "RetroBox VM selector (F12)",
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        list.X = 1;
        list.Y = 1;
        list.Width = Dim.Fill(2);
        list.Height = Dim.Fill(5);
        window.Add(list);

        void Complete(RetroBoxBootSelectionAction action)
        {
            if (list.Index is int index && index >= 0 && index < virtualMachines.Count)
            {
                result = new RetroBoxBootSelectionDecision(
                    action,
                    virtualMachines[index].Key);
                app.RequestStop();
            }
        }

        var run = new Button { Text = "Run", X = 2, Y = Pos.Bottom(list) + 1};
        run.Accepted += (_, _) => Complete(RetroBoxBootSelectionAction.Run);

        var setDefault = new Button { Text = "Run and set default", X = Pos.Right(run) + 2, Y = Pos.Bottom(list) + 1, IsDefault = true };
        setDefault.Accepted += (_, _) => Complete(RetroBoxBootSelectionAction.RunAndSetDefault);

        var cancel = new Button { Text = "Cancel", X = Pos.Right(setDefault) + 2, Y = Pos.Bottom(list) + 1 };
        cancel.Accepted += (_, _) => app.RequestStop();

        window.Add(run, setDefault, cancel);

        app.Run(window);
        return result;
    }
}
