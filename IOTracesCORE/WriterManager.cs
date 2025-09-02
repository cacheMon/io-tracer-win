using IOTracesCORE.trace;
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
        private readonly StringBuilder fs_sb;
        private readonly StringBuilder ds_sb;
        private readonly StringBuilder mr_sb;
        private readonly static int maxKB = 10000;
        private readonly static int maxMB = 200;
        private static int amount_compressed_file = 0;

        public WriterManager(string dirpath)
        {
            amount_compressed_file = 0;

            fs_sb = new StringBuilder();
            ds_sb = new StringBuilder();
            mr_sb = new StringBuilder();

            dir_path = dirpath;
            fs_filepath = GenerateFilePath("fs");
            ds_filepath = GenerateFilePath("ds");
            mr_filepath = GenerateFilePath("mr");

            string? folder = Path.GetDirectoryName(fs_filepath) ?? throw new Exception("Invalid directory path.");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Console.WriteLine("File output: {0}", dirpath);
            fs_sb.AppendLine("timestamp,operation,pid,process,filename,size");
            ds_sb.AppendLine("timestamp,pid,processname,lba,operation,size");
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

            fs_sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n", ts.ToFileTimeUtc(), operation_type, pid, process_name, filename, size);

            if (IsTimeToFlush(fs_sb))
            {
                FlushWrite(fs_sb, fs_filepath);
                if (IsTimeToCompress(fs_filepath))
                {
                    CompressWrite("filesystem");
                }

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

            ds_sb.AppendFormat("{0},{1},{2},{3},{4},{5}\n", ts.ToFileTimeUtc(), pid, process_name, sector, operation, size);

            if (IsTimeToFlush(ds_sb))
            {
                FlushWrite(ds_sb, ds_filepath);
                if (IsTimeToCompress(ds_filepath))
                {
                    CompressWrite("disk");
                }
                
            }
        }
        
        public void Write(MemoryTrace data)
        {
            DateTime ts = data.Ts;
            int pid = data.Pid;
            string process_name = data.Comm;
            string type = data.Type;

            mr_sb.AppendFormat("{0},{1},{2},{3}\n", ts.ToFileTimeUtc(), pid, process_name, type);

            if (IsTimeToFlush(mr_sb))
            {
                FlushWrite(mr_sb, mr_filepath);
                if (IsTimeToCompress(mr_filepath))
                {
                    CompressWrite("memory");
                }
                
            }
        }

        public static void FlushWrite(StringBuilder sb, string filepath)
        {
            string temp_str = sb.ToString();
            sb.Clear();

            using (StreamWriter writeText = new StreamWriter(filepath, true))
            {
                writeText.Write(temp_str);
            }
            WriteStatus();
            //Console.WriteLine("Flushed!");
        }

        private static bool IsTimeToFlush(StringBuilder sb)
        {
            int maxChars = maxKB * 1024 / sizeof(char);

            return sb.Length > maxChars;
        }

        private static bool IsTimeToCompress(string filepath)
        {
            FileInfo fileInfo = new FileInfo(Path.GetFullPath(filepath));
            double fileSizeInMBFS = fileInfo.Length / (1024 * 1024);

            return fileSizeInMBFS > maxMB;
        }

        public void CompressWrite(string tracetype)
        {
            string old_fp;
            string compressed_fp;
            if (tracetype.Equals("filesystem"))
            {
                old_fp = fs_filepath;
                fs_filepath = GenerateFilePath("fs");
                FlushWrite(fs_sb, old_fp);
            }
            else if (tracetype.Equals("disk"))
            {
                old_fp = ds_filepath;
                ds_filepath = GenerateFilePath("ds");
                FlushWrite(ds_sb, old_fp);
            }
            else if (tracetype.Equals("memory"))
            {
                old_fp = mr_filepath;
                mr_filepath = GenerateFilePath("mr");
                FlushWrite(mr_sb, old_fp);
            }
            else
            {
                return;
            }
            compressed_fp = $"{dir_path}\\{tracetype}_{DateTime.UtcNow:yyyyMMdd_HHmmss}" + ".zst";



            Console.WriteLine($"Writing to new file: {fs_filepath}");

            Console.WriteLine($"Compressing {old_fp}");
            using (var input = File.OpenRead(old_fp))
            using (var output = File.Create(compressed_fp))
            using (var compressor = new CompressionStream(output))
            {
                input.CopyTo(compressor);
            }
            Console.WriteLine($"Compressed {old_fp} -> {compressed_fp}");
            amount_compressed_file++;

            string full_path_old = Path.GetFullPath(old_fp);
            if (File.Exists(full_path_old))
            {
                File.Delete(full_path_old);
                Console.WriteLine("File deleted successfully.");
            }
            else
            {
                Console.WriteLine("File not found.");
            }
        }

        public void CompressAll()
        {
            CompressWrite("filesystem");
            CompressWrite("disk");
            //CompressWrite("memory");
            WriteStatus();
        }

        private string GenerateFilePath(string type)
        {
            string fs_name = $"{type}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return Path.Combine(dir_path, fs_name);
        }


        private static void WriteStatus()
        {
            string stat = $"{DateTime.Now} | File Compressed: {amount_compressed_file}";
            Console.WriteLine(stat);
        }
    }
}
