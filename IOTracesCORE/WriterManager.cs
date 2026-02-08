using IOTracesCORE.cloudstorage;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Text;
using ZstdSharp;

namespace IOTracesCORE
{
    class WriterManager
    {
        private string dir_path;
        private string fs_filepath;
        private string ds_filepath;
        private string mr_filepath;
        private string nw_filepath;
        private string fs_snap_filepath;
        private string process_snap_filepath;

        private readonly StringBuilder fs_sb;
        private readonly StringBuilder ds_sb;
        private readonly StringBuilder mr_sb;
        private readonly StringBuilder nw_sb;
        private readonly StringBuilder fs_snap_sb;
        private readonly StringBuilder process_snap_sb;

        private ObjectStorageHandler obj_storage;

        private const double MEMORY_PRESSURE_RATIO = 0.01;
        private const long ABSOLUTE_MAX_BYTES = 256L * 1024 * 1024; // 256 MB
        private static readonly TimeSpan MIN_FLUSH_INTERVAL = TimeSpan.FromSeconds(10);
        private static DateTime _lastFlushUtc = DateTime.UtcNow;

        private bool is_anonymous;
        private bool is_upload_automatically;
        public static int amount_compressed_file = 0;
        public static int disk_event_counter = 0;
        public static int file_event_counter = 0;
        public static TimeSpan active_session = TimeSpan.FromSeconds(0);
        public static TimeSpan trace_duration = TimeSpan.FromSeconds(0);

        public WriterManager(string dirpath, bool is_anonymous, bool upload, ObjectStorageHandler obj)
        {
            amount_compressed_file = 0;

            fs_sb = new StringBuilder();
            ds_sb = new StringBuilder();
            mr_sb = new StringBuilder();
            nw_sb = new StringBuilder();
            fs_snap_sb = new StringBuilder();
            process_snap_sb = new StringBuilder();

            obj_storage = obj;
            is_upload_automatically = upload;


            dir_path = $"{dirpath}\\{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            fs_filepath = GenerateFilePath("fs");
            ds_filepath = GenerateFilePath("ds");
            mr_filepath = GenerateFilePath("mr");
            nw_filepath = GenerateFilePath("nw");
            process_snap_filepath = GenerateFilePath("process");
            fs_snap_filepath = GenerateFilePath("filesystem_snapshot");
            this.is_anonymous = is_anonymous;

            StartEventRateDetector();
        }

        public void InitiateDirectory()
        {
            EnsureDirectoryExists(dir_path);
            string? fs_folder = Path.GetDirectoryName(fs_filepath) ?? throw new Exception("Invalid directory path.");
            string? ds_folder = Path.GetDirectoryName(ds_filepath) ?? throw new Exception("Invalid directory path.");
            string? mr_folder = Path.GetDirectoryName(mr_filepath) ?? throw new Exception("Invalid directory path.");
            string? nw_folder = Path.GetDirectoryName(nw_filepath) ?? throw new Exception("Invalid directory path.");
            string? proc_snap_folder = Path.GetDirectoryName(process_snap_filepath) ?? throw new Exception("Invalid directory path.");
            string? fs_snap_folder = Path.GetDirectoryName(fs_snap_filepath) ?? throw new Exception("Invalid directory path.");


            EnsureDirectoryExists(fs_folder);
            EnsureDirectoryExists(ds_folder);
            EnsureDirectoryExists(proc_snap_folder);
            EnsureDirectoryExists(fs_snap_folder);
            EnsureDirectoryExists(nw_folder);
            //if(!Directory.Exists(mr_folder))
            //{
            //    Directory.CreateDirectory(mr_folder);
            //}
            Console.WriteLine("File output: {0}", this.dir_path);
        }

        private void StartEventRateDetector()
        {
            Debug.WriteLine("Starting event rate detector thread...");
            Thread eventRateThread = new(EventRateDetector)
            {
                IsBackground = true
            };
            eventRateThread.Start();
        }

        private void EventRateDetector()
        {
            while (true)
            {
                int initial_count = disk_event_counter;
                Thread.Sleep(1000);
                int final_count = disk_event_counter;
                int events_in_interval = final_count - initial_count;
                disk_event_counter = 0;
                //Debug.WriteLine($"Rate: {events_in_interval}");
                if (events_in_interval > 100)
                {
                    active_session += TimeSpan.FromSeconds(1);
                }
            }
        }

        private static string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return "\"\"";

            if (field.Contains(',') || field.Contains('\n') || field.Contains('"'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return $"\"{field}\"";
        }

        public void Write(FilesystemInfo fs)
        {
            fs_snap_sb.Append(fs.FormatAsCsv());
            if (IsTimeToFlush(fs_snap_sb, true))
            {
                FlushWrite(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot");
            }
        }

        public void Write(ProcessInfo pc)
        {
            if (pc.Name.Equals("IOTracesCORE"))
            {
                return;
            }

            process_snap_sb.Append(pc.FormatAsCsv());
            if (IsTimeToFlush(process_snap_sb))
            {
                FlushWrite(process_snap_sb, process_snap_filepath, "process");
            }
        }

        public void Write(FilesystemTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE"))
            {
                return;
            }

            string process_name = EscapeCsvField(data.Comm);

            int size = data.TraceSize;
            if (process_name.Equals("IOTracesCORE"))
            {
                return;
            }
            file_event_counter += 1;
            //event_counter += 1;
            DebugLogger.LogRaw(data.FormatAsCsv(is_anonymous));
            fs_sb.Append(data.FormatAsCsv(is_anonymous));
            if (IsTimeToFlush(fs_sb))
            {
                FlushWrite(fs_sb, fs_filepath, "filesystem");
            }
        }

        public void Write(DiskTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE"))
            {
                return;
            }

            string process_name = EscapeCsvField(data.Comm);

            if (process_name.Equals("IOTracesCORE"))
            {
                return;
            }
            disk_event_counter += 1;
            ds_sb.Append(data.FormatAsCsv());

            if (IsTimeToFlush(ds_sb))
            {
                FlushWrite(ds_sb, ds_filepath, "disk");
            }
        }

        public void Write(NetworkTrace data)
        {
            if (data.Comm.Equals("IOTracesCORE"))
            {
                return;
            }

            string process_name = EscapeCsvField(data.Comm);

            if (process_name.Equals("IOTracesCORE"))
            {
                return;
            }
            //event_counter += 1;
            nw_sb.Append(data.FormatAsCsv());

            if (IsTimeToFlush(nw_sb))
            {
                FlushWrite(nw_sb, nw_filepath, "network");
            }
        }

        public void Write(MemoryTrace data)
        {
            DateTime ts = data.Ts;
            int pid = data.Pid;
            string process_name = EscapeCsvField(data.Comm);
            string type = EscapeCsvField(data.Type);

            mr_sb.AppendFormat("{0},{1},{2},{3}\n", ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), pid, process_name, type);

            if (IsTimeToFlush(mr_sb))
            {
                FlushWrite(mr_sb, mr_filepath, "memory");
            }
        }

        public void FlushWrite(StringBuilder sb, string filepath, string tracetype)
        {
            string old_fp;

            if (tracetype.Equals("filesystem"))
            {
                old_fp = fs_filepath;
                fs_filepath = GenerateFilePath("fs");
            }
            else if (tracetype.Equals("disk"))
            {
                old_fp = ds_filepath;
                ds_filepath = GenerateFilePath("ds");
            }
            else if (tracetype.Equals("memory"))
            {
                old_fp = mr_filepath;
                mr_filepath = GenerateFilePath("mr");
            }
            else if (tracetype.Equals("process"))
            {
                old_fp = process_snap_filepath;
                process_snap_filepath = GenerateFilePath("process");
            }
            else if (tracetype.Equals("filesystem_snapshot"))
            {
                old_fp = fs_snap_filepath;
                fs_snap_filepath = GenerateFilePath("filesystem_snapshot");
            }
            else if (tracetype.Equals("network"))
            {
                old_fp = nw_filepath;
                nw_filepath = GenerateFilePath("nw");
            }
            else
            {
                return;
            }
            _lastFlushUtc = DateTime.UtcNow;
            using (var writer = new StreamWriter(old_fp, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(sb);
            }

            sb.Clear();

            try
            {
                string compressed_fp = CompressFile(old_fp);
                if (is_upload_automatically)
                {
                    obj_storage.QueueFile(compressed_fp);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error compressing file {old_fp}: {ex.Message}");
            }
            WriteStatus();
        }

        public void DirectWrite(string file_out_path, string input)
        {
            string out_path = $"{dir_path}\\system_spec\\{file_out_path}";

            string? dir = Path.GetDirectoryName(out_path);
            if (!string.IsNullOrEmpty(dir))
            {
                EnsureDirectoryExists(dir);
            }

            using (var writer = new StreamWriter(out_path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(input);
            }

            amount_compressed_file++;

            if (is_upload_automatically)
            {
                obj_storage.QueueFile(out_path);
            }

        }

        private static bool IsTimeToFlush(StringBuilder sb, bool isSnap = false)
        {
            long sbBytes = sb.Length * sizeof(char);

            var gcInfo = GC.GetGCMemoryInfo();
            long availableMemory = gcInfo.TotalAvailableMemoryBytes;

            long adaptiveLimit = (long)(availableMemory * MEMORY_PRESSURE_RATIO);

            if (isSnap)
                adaptiveLimit /= 4;

            adaptiveLimit = Math.Min(adaptiveLimit, ABSOLUTE_MAX_BYTES);

            if (sbBytes >= adaptiveLimit)
                return true;

            if (DateTime.UtcNow - _lastFlushUtc >= MIN_FLUSH_INTERVAL &&
                sbBytes > adaptiveLimit / 4)
            {
                return true;
            }

            return false;
        }


        public static string CompressFile(string filepath)
        {
            try
            {
                if (!File.Exists(filepath))
                {
                    throw new FileNotFoundException("Input file does not exist.", filepath);
                }

                string compressed_fp = $"{filepath}.zst";

                using (var input = File.OpenRead(filepath))
                using (var output = File.Create(compressed_fp))
                using (var compressor = new CompressionStream(output))
                {
                    input.CopyTo(compressor);
                }

                amount_compressed_file++;

                File.Delete(Path.GetFullPath(filepath));

                return compressed_fp;
            }
            catch
            {
                throw;
            }
        }



        public void CompressRun()
        {
            if (is_upload_automatically)
            {
                Directory.Delete(dir_path, true);
                return;
            }

            string zipPath = $"{dir_path}_temp.zip";
            string output_dir = $"{dir_path}.zip.zst";

            try
            {
                if (!Directory.Exists(dir_path))
                {
                    Debug.WriteLine($"Directory not found: {dir_path}");
                    return;
                }

                ZipFile.CreateFromDirectory(dir_path, zipPath);

                byte[] zipData = File.ReadAllBytes(zipPath);

                using (var compressor = new Compressor())
                {
                    var compressedData = compressor.Wrap(zipData);
                    File.WriteAllBytes(output_dir, compressedData.ToArray());
                }

                Debug.WriteLine($"Compressed entire run to {output_dir}");

                Directory.Delete(dir_path, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Compression failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (File.Exists(zipPath))
                    {
                        File.Delete(zipPath);
                    }
                }
                catch
                {
                }
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (Directory.Exists(path))
                return;

            if (File.Exists(path))
            {
                Debug.WriteLine($"Warning: File exists at directory path '{path}', renaming...");
                string backupPath = path + ".backup_" + DateTime.UtcNow.Ticks;
                File.Move(path, backupPath);
                Debug.WriteLine($"Moved conflicting file to: {backupPath}");
            }

            Directory.CreateDirectory(path);
        }

        public async Task CompressAllAsync()
        {
            Debug.WriteLine("Compressing all remaining data...");

            FlushWrite(fs_sb, fs_filepath, "filesystem");
            FlushWrite(ds_sb, ds_filepath, "disk");
            FlushWrite(process_snap_sb, process_snap_filepath, "process");
            FlushWrite(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot");
            FlushWrite(nw_sb, nw_filepath, "network");
            Debug.WriteLine("Flushed all StringBuilders.");

            WriteStatus();

            await obj_storage.ClearQueue();
            ConfigClasses.SaveTracemetaConfiguration(active_session + trace_duration, file_event_counter);

            CompressRun();
        }


        private string GenerateFilePath(string type)
        {
            string fs_name = $".\\{type}\\{type}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{PathHasher.deviceId}.csv";
            return Path.Combine(dir_path, fs_name);
        }

        private static void WriteStatus()
        {
            //Console.Clear();
            //Console.WriteLine("Press CTRL + C to exit, or close the console window!");
            string stat = $"{DateTime.Now} | File Compressed: {amount_compressed_file}";
            Console.WriteLine(stat);
        }
    }
}
