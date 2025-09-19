using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    internal class FilesystemInfo
    {
        public string path { get; set; }
        public long size { get; set; }       // bytes
        public DateTime? CreationDate { get; set; }

        public DateTime modificationDate { get; set; }

        public FilesystemInfo(string path, long size, DateTime? creationDate, DateTime modificationDate)
        {
            this.path = path;
            this.size = size;
            this.CreationDate = creationDate;
            this.modificationDate = modificationDate;
        }
    }
}
