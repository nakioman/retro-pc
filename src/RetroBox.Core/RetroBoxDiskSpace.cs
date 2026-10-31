namespace RetroBox.Core;

public static class RetroBoxDiskSpace
{
    // DriveInfo on Linux runs statvfs against the given path, so any directory on the /data
    // filesystem reports that filesystem's space; it does not need to be the mount point itself.
    public static long? GetAvailableFreeBytes(string path)
    {
        try
        {
            return new DriveInfo(path).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
