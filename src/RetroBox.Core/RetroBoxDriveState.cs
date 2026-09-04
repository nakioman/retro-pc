namespace RetroBox.Core;

public abstract record RetroBoxDriveState
{
    /// <summary>No controller attached, or no event seen yet.</summary>
    public sealed record Unknown() : RetroBoxDriveState;

    public sealed record Empty() : RetroBoxDriveState;

    public sealed record Loaded(string FloppyId, string Mode) : RetroBoxDriveState;
}

public interface IRetroBoxDriveState
{
    RetroBoxDriveState Current { get; }
}
