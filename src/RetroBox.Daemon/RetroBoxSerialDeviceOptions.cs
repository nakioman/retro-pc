namespace RetroBox.Daemon;

public sealed record RetroBoxSerialDeviceOptions(string Port, int Baud)
{
    public const int DefaultBaud = 115200;
}
