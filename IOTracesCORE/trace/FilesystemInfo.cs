using CsvHelper;
using CsvHelper.Configuration;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.trace
{
    internal class FilesystemInfo
    {
        public DateTime timestamp { get; set; }

        public string path { get; set; }
        public long size { get; set; }       // bytes
        public DateTime CreationDate { get; set; }

        public DateTime modificationDate { get; set; }

        private readonly StringWriter buffer = new StringWriter();
        private readonly CsvWriter csv;

        public FilesystemInfo(string path, long size, DateTime creationDate, DateTime modificationDate, DateTime timestamp)
        {
            this.path = path;
            this.size = size;
            this.CreationDate = creationDate;
            this.modificationDate = modificationDate;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                NewLine = "\n"
            };

            this.csv = new CsvWriter(buffer, config);
            this.timestamp = timestamp;
        }

        public string FormatAsCsv()
        {
            buffer.GetStringBuilder().Clear();
            csv.WriteField(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(path);
            csv.WriteField(size);
            csv.WriteField(CreationDate.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.WriteField(modificationDate.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            csv.NextRecord();
            return buffer.ToString();
        }
    }
}
