using System.Runtime.InteropServices;

namespace RetroBox.Core;

public enum RetroBoxOpticalMedia
{
    Present,
    Absent,
    TrayOpen,

    /// <summary>A disc was inserted and the drive is still spinning it up.</summary>
    NotReady,
}

public interface IRetroBoxOpticalDriveProbe
{
    RetroBoxOpticalMedia Probe(string devicePath);
}

/// <summary>Reads the drive's state with the Linux CDROM_DRIVE_STATUS ioctl; opening it needs the cdrom group.</summary>
public sealed partial class RetroBoxLinuxOpticalDriveProbe : IRetroBoxOpticalDriveProbe
{
    private const int OpenReadOnlyNonBlocking = 0x800;
    private const nuint CdromDriveStatus = 0x5326;
    private const nint CurrentSlot = int.MaxValue;

    public RetroBoxOpticalMedia Probe(string devicePath)
    {
        // O_NONBLOCK: a plain open of an empty drive fails with ENOMEDIUM instead of reporting it.
        var descriptor = Open(devicePath, OpenReadOnlyNonBlocking);
        if (descriptor < 0)
        {
            throw new IOException($"Cannot open optical drive '{devicePath}': errno {Marshal.GetLastPInvokeError()}.");
        }

        try
        {
            return IoControl(descriptor, CdromDriveStatus, CurrentSlot) switch
            {
                < 0 => throw new IOException($"Cannot read the status of optical drive '{devicePath}': errno {Marshal.GetLastPInvokeError()}."),
                1 => RetroBoxOpticalMedia.Absent,
                2 => RetroBoxOpticalMedia.TrayOpen,
                3 => RetroBoxOpticalMedia.NotReady,
                // 4 is CDS_DISC_OK; 0 (CDS_NO_INFO) is a drive that cannot tell, so try the disc.
                _ => RetroBoxOpticalMedia.Present,
            };
        }
        finally
        {
            Close(descriptor);
        }
    }

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoControl(int descriptor, nuint request, nint argument);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int descriptor);
}
