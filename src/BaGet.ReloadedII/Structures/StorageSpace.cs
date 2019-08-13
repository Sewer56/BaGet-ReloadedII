using System;
using System.Collections.Generic;
using System.Text;

namespace BaGet.ReloadedII.Structures
{
    public class StorageSpace
    {
        public long bytesConsumed { get; set; }
        public long totalBytes { get; set; }

        public StorageSpace(long bytesConsumed, long totalBytes)
        {
            this.bytesConsumed = bytesConsumed;
            this.totalBytes = totalBytes;
        }

        public StorageSpace() { }
    }
}
