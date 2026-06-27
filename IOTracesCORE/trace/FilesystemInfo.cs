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
        public DateTime LastAccessTime { get; set; }
        public FileAttributes Attributes { get; set; }
        public string Extension { get; set; }
        public bool IsReadOnly { get; set; }

        // Row buffer/writer are provided per-thread by CsvRowBuffer (no per-row allocation).

        public FilesystemInfo(string path, long size, DateTime creationDate, DateTime modificationDate, DateTime lastAccessTime, FileAttributes attributes, string extension, bool isReadOnly, DateTime timestamp)
        {
            this.path = path;
            this.size = size;
            this.CreationDate = creationDate;
            this.modificationDate = modificationDate;
            this.LastAccessTime = lastAccessTime;
            this.Attributes = attributes;
            this.Extension = extension;
            this.IsReadOnly = isReadOnly;

            // CsvWriter is no longer built per row here — formatting uses the per-thread
            // CsvRowBuffer (see FormatAsCsv). Per-row writer construction was the dominant cost.
            this.timestamp = timestamp;
        }

        public string FormatAsCsv()
        {
            var csv = CsvRowBuffer.Begin(out var buffer);
            csv.WriteField(timestamp.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(path);
            csv.WriteField(size);
            csv.WriteField(CreationDate.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(modificationDate.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff"));
            csv.WriteField(Attributes.ToString());
            csv.WriteField(Extension);
            csv.WriteField(IsReadOnly);
            csv.NextRecord();
            return buffer.ToString();
        }
    }
}
