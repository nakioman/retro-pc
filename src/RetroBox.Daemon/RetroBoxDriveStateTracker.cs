using RetroBox.Core;

namespace RetroBox.Daemon;

public sealed class RetroBoxDriveStateTracker : IRetroBoxDriveState
{
    private volatile RetroBoxDriveState current = new RetroBoxDriveState.Unknown();

    public RetroBoxDriveState Current => current;

    public void Observe(RetroBoxArduinoSerialEvent serialEvent)
    {
        current = serialEvent switch
        {
            RetroBoxArduinoInsertEvent insert => new RetroBoxDriveState.Loaded(insert.Id, insert.Mode),
            RetroBoxArduinoEjectEvent => new RetroBoxDriveState.Empty(),
            RetroBoxArduinoInitEvent => new RetroBoxDriveState.Unknown(),
            _ => current,
        };
    }
}
