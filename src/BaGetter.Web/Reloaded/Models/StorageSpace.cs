using System;
using System.IO;

namespace BaGetter.Web.Reloaded.Models;

public struct LocalStorageSpace
{
    private static readonly string _thisAssemblyDirectory = AppContext.BaseDirectory;

    public long BytesConsumed { get; }
    public long TotalBytes { get; }

    public string BytesConsumedGB => ToGigaBytesString(BytesConsumed);
    public string TotalBytesGB => ToGigaBytesString(TotalBytes);

    public LocalStorageSpace()
    {
        try
        {
            var drive = new DriveInfo(new DirectoryInfo(_thisAssemblyDirectory).Root.FullName);
            if (drive.IsReady)
            {
                BytesConsumed = drive.TotalSize - drive.AvailableFreeSpace;
                TotalBytes = drive.TotalSize;
            }
            else
            {
                BytesConsumed = 0;
                TotalBytes = 0;
            }
        }
        catch (IOException)
        {
            BytesConsumed = 0;
            TotalBytes = 0;
        }
        catch (ArgumentException)
        {
            BytesConsumed = 0;
            TotalBytes = 0;
        }
    }

    private static string ToGigaBytesString(long bytes) => (bytes / 1000000000.0f).ToString("#.00");
}
