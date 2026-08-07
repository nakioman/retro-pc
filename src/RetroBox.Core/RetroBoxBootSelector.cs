namespace RetroBox.Core;

public enum RetroBoxBootSelectionAction
{
    Run,
    RunAndSetDefault,
    Cancel,
}

public sealed record RetroBoxBootSelectionDecision(
    RetroBoxBootSelectionAction Action,
    string? VmId = null);

public interface IRetroBoxBootSelectorUi
{
    RetroBoxBootSelectionDecision Select(
        IReadOnlyList<KeyValuePair<string, RetroBoxVm>> virtualMachines,
        string? defaultVmId);
}

public sealed class RetroBoxBootSelector(RetroBoxConfigStore store, IRetroBoxBootSelectorUi selectorUi)
{
    public RetroBoxBootSelectionDecision Resolve(
        string? explicitVmId = null,
        bool selectorRequested = false,
        bool persistDefault = true,
        bool quitOnCancel = false)
    {
        return Resolve(store.Load(), explicitVmId, selectorRequested, persistDefault, quitOnCancel);
    }

    public RetroBoxBootSelectionDecision Resolve(
        RetroBoxCatalogData catalog,
        string? explicitVmId = null,
        bool selectorRequested = false,
        bool persistDefault = true,
        bool quitOnCancel = false)
    {
        var virtualMachines = catalog.Vms
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

        if (virtualMachines.Length == 0)
        {
            throw new RetroBoxCatalogException("No virtual machines are configured.");
        }

        if (explicitVmId is not null)
        {
            ValidateVmId(explicitVmId, catalog);
            return new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, explicitVmId);
        }

        var defaultVmId = string.IsNullOrWhiteSpace(catalog.Config.DefaultVm)
            ? null
            : catalog.Config.DefaultVm;

        if (!selectorRequested && defaultVmId is not null)
        {
            return new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, defaultVmId);
        }

        var decision = selectorUi.Select(virtualMachines, defaultVmId);
        if (decision.Action == RetroBoxBootSelectionAction.Cancel)
        {
            if (quitOnCancel)
            {
                return decision;
            }

            if (defaultVmId is not null)
            {
                return new RetroBoxBootSelectionDecision(RetroBoxBootSelectionAction.Run, defaultVmId);
            }

            throw new RetroBoxCatalogException("VM selection was cancelled and no default VM is configured.");
        }

        if (decision.VmId is null)
        {
            throw new RetroBoxCatalogException("VM selection did not specify a VM.");
        }

        ValidateVmId(decision.VmId, catalog);
        if (decision.Action == RetroBoxBootSelectionAction.RunAndSetDefault && persistDefault)
        {
            store.UpdateDefaultVm(decision.VmId);
        }

        return decision;
    }

    private static void ValidateVmId(string vmId, RetroBoxCatalogData catalog)
    {
        if (!RetroBoxCatalogRules.IsValidId(vmId) || !catalog.Vms.ContainsKey(vmId))
        {
            throw new RetroBoxCatalogException($"Unknown VM '{vmId}'.");
        }
    }
}
