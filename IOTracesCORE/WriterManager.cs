using IOTracesCORE.trace;
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
        private readonly StringBuilder fs_sb;
        private readonly StringBuilder ds_sb;
        private readonly StringBuilder mr_sb;
        private readonly static int maxKB = 10000;
        private static int amount_compressed_file = 0;

        public WriterManager(string dirpath)
        {
            amount_compressed_file = 0;

            fs_sb = new StringBuilder();
            ds_sb = new StringBuilder();
            mr_sb = new StringBuilder();

            dir_path = $"{dirpath}\\run_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
            fs_filepath = GenerateFilePath("fs");
            ds_filepath = GenerateFilePath("ds");
            mr_filepath = GenerateFilePath("mr");
            fs_snap_filepath = $"{dir_path}\\snapshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt";

            string? fs_folder = Path.GetDirectoryName(fs_filepath) ?? throw new Exception("Invalid directory path.");
            string? ds_folder = Path.GetDirectoryName(ds_filepath) ?? throw new Exception("Invalid directory path.");
            string? mr_folder = Path.GetDirectoryName(mr_filepath) ?? throw new Exception("Invalid directory path.");
            if (!Directory.Exists(fs_folder))
            {
                Directory.CreateDirectory(fs_folder);
            }
            if(!Directory.Exists(ds_folder))
            {
                Directory.CreateDirectory(ds_folder);
            }
            //if(!Directory.Exists(mr_folder))
            //{
            //    Directory.CreateDirectory(mr_folder);
            //}
            Console.WriteLine("File output: {0}", dirpath);
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
            string temp_str = sb.ToString();
            sb.Clear();

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
            else
            {
                return;
            }

            using (StreamWriter writeText = new(old_fp, true))
            {
                writeText.Write(temp_str);
            }
            CompressFile(old_fp);
            WriteStatus();

        }

        private static bool IsTimeToFlush(StringBuilder sb)
        {
            int maxChars = maxKB * 1024 / sizeof(char);

            return sb.Length > 1000000;
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

        public void CompressAll()
        {
            FlushWrite(fs_sb, fs_filepath, "filesystem");
            FlushWrite(ds_sb, ds_filepath, "disk");
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

        public void TraverseFilesystem()
        {
            DriveInfo[] drives = DriveInfo.GetDrives();
            Console.WriteLine("Starting filesystem snapshot...");
            foreach (DriveInfo drive in drives)
            {
                if (drive.IsReady)
                {
                    Console.WriteLine($"=== Drive: {drive.Name} ===");
                    TraverseDirectory(drive.RootDirectory.FullName);
                    Console.WriteLine();
                }
            }
            CompressFile(fs_snap_filepath);
        }

        private void TraverseDirectory(string dirPath)
        {
            try
            {

                string[] files = Directory.GetFiles(dirPath);
                using (StreamWriter writeText = new StreamWriter(fs_snap_filepath, true))
                {
                    foreach (string file in files)
                    {
                        writeText.WriteLine(file);
                    }
                }
                

                string[] directories = Directory.GetDirectories(dirPath);
                foreach (string directory in directories)
                {
                    TraverseDirectory(directory);
                }
            }
            catch (UnauthorizedAccessException)
            {
                using StreamWriter writeText = new(fs_snap_filepath, true);
                writeText.WriteLine($"[ACCESS DENIED] {dirPath}");
            }
            catch (DirectoryNotFoundException)
            {
                using StreamWriter writeText = new(fs_snap_filepath, true);
                writeText.WriteLine($"[NOT FOUND] {dirPath}");
            }
            catch (Exception ex)
            {
                using StreamWriter writeText = new(fs_snap_filepath, true);
                writeText.WriteLine($"[ERROR] {dirPath}: {ex.Message}");
            }
        }
    }
}
