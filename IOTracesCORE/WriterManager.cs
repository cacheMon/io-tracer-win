using IOTracesCORE.cloudstorage;
using IOTracesCORE.trace;
using IOTracesCORE.utils;
using System.Diagnostics;
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

        private readonly static int maxKB = 500000;
        private readonly static int maxSnapKB = 1000000;
        private bool is_anonymous;
        private bool is_upload_automatically;
        public static int amount_compressed_file = 0;

        public WriterManager(string dirpath, bool is_anonymous, bool upload,ObjectStorageHandler obj)
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


            dir_path = $"{dirpath}\\run_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            fs_filepath = GenerateFilePath("fs");
            ds_filepath = GenerateFilePath("ds");
            mr_filepath = GenerateFilePath("mr");
            nw_filepath = GenerateFilePath("nw");
            process_snap_filepath = GenerateFilePath("process");
            fs_snap_filepath = GenerateFilePath("filesystem_snapshot");
            this.is_anonymous = is_anonymous; 
        }

        public void InitiateDirectory()
        {
            string? fs_folder = Path.GetDirectoryName(fs_filepath) ?? throw new Exception("Invalid directory path.");
            string? ds_folder = Path.GetDirectoryName(ds_filepath) ?? throw new Exception("Invalid directory path.");
            string? mr_folder = Path.GetDirectoryName(mr_filepath) ?? throw new Exception("Invalid directory path.");
            string? nw_folder = Path.GetDirectoryName(nw_filepath) ?? throw new Exception("Invalid directory path.");
            string? proc_snap_folder = Path.GetDirectoryName(process_snap_filepath) ?? throw new Exception("Invalid directory path.");
            string? fs_snap_folder = Path.GetDirectoryName(fs_snap_filepath) ?? throw new Exception("Invalid directory path.");
            if (!Directory.Exists(fs_folder))
            {
                Directory.CreateDirectory(fs_folder);
            }
            if (!Directory.Exists(ds_folder))
            {
                Directory.CreateDirectory(ds_folder);
            }
            if (!Directory.Exists(proc_snap_folder))
            {
                Directory.CreateDirectory(proc_snap_folder);
            }
            if (!Directory.Exists(fs_snap_folder))
            {
                Directory.CreateDirectory(fs_snap_folder);
            }
            if (!Directory.Exists(nw_folder))
            {
                Directory.CreateDirectory(nw_folder);
            }
            //if(!Directory.Exists(mr_folder))
            //{
            //    Directory.CreateDirectory(mr_folder);
            //}
            Console.WriteLine("File output: {0}", this.dir_path);
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
            string name = EscapeCsvField(fs.path);
            long size = fs.size;       // bytes
            DateTime? creationDate = fs.CreationDate;
            DateTime modificationDate = fs.modificationDate;
            fs_snap_sb.AppendFormat("{0},{1},{2},{3}\n", name, size, creationDate, modificationDate);
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
            DateTime ts = DateTime.Now;
            uint pid = pc.ProcessId;
            string name = EscapeCsvField(pc.Name);
            string cmd = EscapeCsvField(pc.CommandLine);
            ulong virtualSize = pc.VirtualSize;      // bytes
            ulong workingSetSize = pc.WorkingSetSize;    // bytes
            DateTime? creationDate = pc.CreationDate;
            double procUsage = pc.CpuUsage;

            process_snap_sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7}\n", ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), pid, name, cmd, virtualSize, workingSetSize, creationDate, procUsage);
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

            DateTime ts = data.Ts;
            string operation_type = EscapeCsvField(data.Op);
            int pid = data.Pid;
            string process_name = EscapeCsvField(data.Comm);
            string or_fp = Path.GetFileName(data.Filename.Trim('"'));
            string filename = EscapeCsvField(is_anonymous ? PathHasher.HashFileName(or_fp) : or_fp);
            //Debug.WriteLine(filename);

            int size = data.TraceSize;
            if (process_name.Equals("IOTracesCORE"))
            {
                return;
            }
            fs_sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n", ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), operation_type, pid, process_name, filename, size);
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

            DateTime ts = data.Ts;
            int pid = data.Pid;
            string process_name = EscapeCsvField(data.Comm);
            long sector = data.Sector;
            string operation = EscapeCsvField(data.Operation);
            int size = data.TraceSize;

            if (process_name.Equals("IOTracesCORE"))
            {
                return;
            }

            ds_sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n", ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), pid, process_name, sector, operation, size);

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

            DateTime ts = data.Ts;
            int pid = data.Pid;
            string process_name = EscapeCsvField(data.Comm);
            string saddr = EscapeCsvField(is_anonymous? PathHasher.Hash(data.Saddr, 10) : data.Saddr); 
            string daddr = EscapeCsvField(is_anonymous? PathHasher.Hash(data.Daddr,10) : data.Daddr);
            int sport = data.Sport;
            int dport = data.Dport;
            int bytes = data.Bytes;
            string type = EscapeCsvField(data.Type);

            if (process_name.Equals("IOTracesCORE"))
            {
                return;
            }

            nw_sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6},{7},{8}\n", ts.ToString("yyyy-MM-dd HH:mm:ss.fff"), pid, process_name, saddr, daddr, sport, dport, bytes, type);

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
            } else if (tracetype.Equals("network"))
            {
                old_fp = nw_filepath;
                nw_filepath = GenerateFilePath("nw");
            }
            else
            {
                return;
            }

            using (var writer = new StreamWriter(old_fp, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(sb);
            }

            sb.Clear();

            string compressed_fp = CompressFile(old_fp);
            if (is_upload_automatically)
            {
                obj_storage.QueueFile(compressed_fp);
            }
            WriteStatus();
        }

        public void DirectWrite(string file_out_path ,string input)
        {
            string out_path = $"{dir_path}\\{file_out_path}";

            using (var writer = new StreamWriter(out_path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(input);
            }
        }

        private static bool IsTimeToFlush(StringBuilder sb, bool isSnap = false)
        {
            int limit = 0;
            if (isSnap)
            {
                limit = maxSnapKB;
            } else
            {
                limit = maxKB;
            }

                return sb.Length > limit;
        }

        public static string CompressFile(string filepath)
        {
            //Console.WriteLine($"Compressing {filepath}");
            string compressed_fp = $"{filepath}.zst";
            using (var input = File.OpenRead(filepath))
            using (var output = File.Create(compressed_fp))
            using (var compressor = new CompressionStream(output))
            {
                input.CopyTo(compressor);
            }
            //Console.WriteLine($"Compressed {filepath} -> {compressed_fp}");
            amount_compressed_file++;
            

            string full_path_old = Path.GetFullPath(filepath);
            if (File.Exists(full_path_old))
            {
                File.Delete(full_path_old);
                //Console.WriteLine("File deleted successfully.");
            }
            else
            {
                Debug.WriteLine("File not found.");
            }

            return compressed_fp;
        }

        public void CompressRun()
        {
            string zipPath = $"{dir_path}_temp.zip";
            string output_dir = $"{dir_path}_{PathHasher.deviceId}_compressed.zip.zst";

            try
            {
                ZipFile.CreateFromDirectory(dir_path, zipPath);

                byte[] zipData = File.ReadAllBytes(zipPath);

                using (var compressor = new Compressor())
                {
                    var compressedData = compressor.Wrap(zipData);
                    File.WriteAllBytes(output_dir, compressedData.ToArray());
                }

                File.Delete(zipPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during compression: {ex.Message}");
                return;
            }

            Console.WriteLine($"Compressed entire run to {output_dir}");
            Directory.Delete(dir_path, true);
        }

        public void FlushSnapper()
        {
            FlushWrite(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot");
        }

        public void CompressAll()
        {
            FlushWrite(fs_sb, fs_filepath, "filesystem");
            FlushWrite(ds_sb, ds_filepath, "disk");
            FlushWrite(process_snap_sb, process_snap_filepath, "process");
            FlushWrite(fs_snap_sb, fs_snap_filepath, "filesystem_snapshot");
            FlushWrite(nw_sb, nw_filepath, "network");
            //FlushWrite(mr_sb, mr_filepath, "memory");
            WriteStatus();
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
