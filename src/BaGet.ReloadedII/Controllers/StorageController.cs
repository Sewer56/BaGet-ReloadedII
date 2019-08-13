using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BaGet.ReloadedII.Structures;
using Microsoft.AspNetCore.Mvc;

namespace BaGet.ReloadedII.Controllers
{
    /// <summary>
    /// The NuGet Service Index. This aids NuGet client to discover this server's services.
    /// </summary>
    public class StorageController : Controller
    {
        // GET v3/index
        [HttpGet]
        public StorageSpace Get()
        {
            var executingDisk = GetExecutingDisk();
            var bytesConsumed = executingDisk.TotalSize - executingDisk.AvailableFreeSpace;
            return new StorageSpace(bytesConsumed, executingDisk.TotalSize);
        }

        public static DriveInfo GetExecutingDisk()
        {
            var fileInfo = new FileInfo(Assembly.GetExecutingAssembly().Location);
            return new DriveInfo(fileInfo.Directory.Root.FullName);
        }

    }
}
