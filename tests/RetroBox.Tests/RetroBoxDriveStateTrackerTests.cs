using RetroBox.Core;
using RetroBox.Daemon;

namespace RetroBox.Tests;

public sealed class RetroBoxDriveStateTrackerTests
{
    [Fact]
    public void Current_starts_unknown_before_any_event()
    {
        Assert.IsType<RetroBoxDriveState.Unknown>(new RetroBoxDriveStateTracker().Current);
    }

    [Fact]
    public void Observe_records_an_inserted_floppy()
    {
        var tracker = new RetroBoxDriveStateTracker();

        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        var loaded = Assert.IsType<RetroBoxDriveState.Loaded>(tracker.Current);
        Assert.Equal("disk1", loaded.FloppyId);
        Assert.Equal(RetroBoxFloppyCatalogRules.ReadOnlyMode, loaded.Mode);
    }

    [Fact]
    public void Observe_records_an_eject()
    {
        var tracker = new RetroBoxDriveStateTracker();
        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        tracker.Observe(new RetroBoxArduinoEjectEvent());

        Assert.IsType<RetroBoxDriveState.Empty>(tracker.Current);
    }

    [Fact]
    public void Observe_leaves_the_state_alone_for_other_events()
    {
        var tracker = new RetroBoxDriveStateTracker();
        tracker.Observe(new RetroBoxArduinoInsertEvent("disk1", RetroBoxFloppyCatalogRules.ReadOnlyMode));

        tracker.Observe(new RetroBoxArduinoErrorEvent("no-tag-detected"));

        Assert.IsType<RetroBoxDriveState.Loaded>(tracker.Current);
    }
}
