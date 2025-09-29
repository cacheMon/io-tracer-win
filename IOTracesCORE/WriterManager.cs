using IOTracesCORE.trace;
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
        private string fs_snap_filepath;
        private string process_snap_filepath;

        private readonly StringBuilder fs_sb;
        private readonly StringBuilder ds_sb;
        private readonly StringBuilder mr_sb;
        private readonly StringBuilder fs_snap_sb;
        private readonly StringBuilder process_snap_sb;

        private readonly static int maxKB = 500000;
        private readonly static int maxSnapKB = 1000000;
        private static int amount_compressed_file = 0;

        public WriterManager(string dirpath)
        {
            amount_compressed_file = 0;

            fs_sb = new StringBuilder();
            ds_sb = new StringBuilder();
            mr_sb = new StringBuilder();
            fs_snap_sb = new StringBuilder();
            process_snap_sb = new StringBuilder();

            dir_path = $"{dirpath}\\run_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            fs_filepath = GenerateFilePath("fs");
            ds_filepath = GenerateFilePath("ds");
            mr_filepath = GenerateFilePath("mr");
            process_snap_filepath = GenerateFilePath("process");
            fs_snap_filepath = GenerateFilePath("filesystem_snapshot");

            string? fs_folder = Path.GetDirectoryName(fs_filepath) ?? throw new Exception("Invalid directory path.");
            string? ds_folder = Path.GetDirectoryName(ds_filepath) ?? throw new Exception("Invalid directory path.");
            string? mr_folder = Path.GetDirectoryName(mr_filepath) ?? throw new Exception("Invalid directory path.");
            string? proc_snap_folder = Path.GetDirectoryName(process_snap_filepath) ?? throw new Exception("Invalid directory path.");
            string? fs_snap_folder = Path.GetDirectoryName(fs_snap_filepath) ?? throw new Exception("Invalid directory path.");
            if (!Directory.Exists(fs_folder))
            {
                Directory.CreateDirectory(fs_folder);
            }
            if(!Directory.Exists(ds_folder))
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
            //if(!Directory.Exists(mr_folder))
            //{
            //    Directory.CreateDirectory(mr_folder);
            //}
            Console.WriteLine("File output: {0}", dirpath);
        }

        public void Write(FilesystemInfo fs)
        {
            DateTime ts = DateTime.Now;
            string name = fs.path;
            long size = fs.size;       // bytes
            DateTime? creationDate = fs.CreationDate;
            DateTime modificationDate = fs.modificationDate;
            fs_snap_sb.AppendFormat("{0},{1},{2},{3},{4}\n", ts.ToString(), name, size, creationDate, modificationDate);
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
            string name = pc.Name;
            string cmd = pc.CommandLine;
            ulong virtualSize = pc.VirtualSize;      // bytes
            ulong workingSetSize = pc.WorkingSetSize;    // bytes
            DateTime? creationDate = pc.CreationDate;

            process_snap_sb.AppendFormat("{0},{1},{2},{3},{4},{5},{6}\n", ts.ToString(), pid, name, cmd, virtualSize, workingSetSize, creationDate);

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
            string operation_type = data.Op;
            int pid = data.Pid;
            string process_name = data.Comm;
            string filename = data.Filename;
            int size = data.TraceSize;
            fs_sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n", ts.ToString(), operation_type, pid, process_name, filename, size);

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
            string process_name = data.Comm;
            long sector = data.Sector;
            string operation = data.Operation;
            int size = data.TraceSize;

            ds_sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n", ts.ToString(), pid, process_name, sector, operation, size);

            if (IsTimeToFlush(ds_sb))
            {
                FlushWrite(ds_sb, ds_filepath, "disk");
            }
        }
        
        public void Write(MemoryTrace data)
        {
            DateTime ts = data.Ts;
            int pid = data.Pid;
            string process_name = data.Comm;
            string type = data.Type;

            mr_sb.AppendFormat("{0},{1},{2},{3}\n", ts.ToString(), pid, process_name, type);

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
            else
            {
                return;
            }

            using (var writer = new StreamWriter(old_fp, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(sb);
            }

            sb.Clear();

            CompressFile(old_fp);
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

        public static void CompressFile(string filepath)
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
                Console.WriteLine("File not found.");
            }
        }

        public void CompressRun()
        {
            string zipPath = $"{dir_path}_temp.zip";
            string output_dir = $"{dir_path}_compressed.zip.zst";

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
            //FlushWrite(mr_sb, mr_filepath, "memory");
            WriteStatus();
            CompressRun();
        }

        private string GenerateFilePath(string type)
        {
            string fs_name = $".\\{type}\\{type}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
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
