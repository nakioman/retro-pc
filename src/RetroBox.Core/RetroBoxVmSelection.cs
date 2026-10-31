namespace RetroBox.Core;

public sealed class RetroBoxVmSelection(RetroBoxConfigStore store)
{
    public IReadOnlyList<KeyValuePair<string, RetroBoxVm>> List()
    {
        // Catalog (vms.yaml) order, as in the boot selector.
        return store.Load().Vms.ToArray();
    }

    public string GetDefaultVmId()
    {
        return store.Load().Config.DefaultVm;
    }

    public void SetDefaultVm(string vmId)
    {
        if (!RetroBoxCatalogRules.IsValidId(vmId))
        {
            throw new RetroBoxCatalogException($"Invalid VM ID '{vmId}'.");
        }

        var catalog = store.Load();
        if (!catalog.Vms.ContainsKey(vmId))
        {
            throw new RetroBoxCatalogException($"Unknown VM '{vmId}'.");
        }

        store.UpdateDefaultVm(vmId);
    }
}
