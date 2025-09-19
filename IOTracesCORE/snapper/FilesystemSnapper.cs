using IOTracesCORE.trace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.snapper
{
    internal class FilesystemSnapper
    {
        private WriterManager wm;
        private bool interrupted;
        public FilesystemSnapper(WriterManager wm)
        {
            this.wm = wm;
            interrupted = false;
        }

        public void Stop()
        {
            interrupted = true;
        }

        public void Run()
        {
            DriveInfo[] drives = DriveInfo.GetDrives();
            Console.WriteLine("Starting filesystem snapshot...");
            foreach (DriveInfo drive in drives)
            {
                if (drive.IsReady)
                {
                    Console.WriteLine($"Scanning Drive: {drive.Name}");
                    TraverseDirectory(drive.RootDirectory.FullName);
                    Console.WriteLine();
                }
            }
        }

        private void TraverseDirectory(string dirPath)
        {
            try
            {
                if (interrupted) return;

                string[] files = Directory.GetFiles(dirPath);
                foreach (string file in files)
                {
                    FileInfo fileInfo = new FileInfo(file);
                    FilesystemInfo fi = new FilesystemInfo(
                        path: fileInfo.FullName, 
                        size: fileInfo.Length, 
                        creationDate: fileInfo.CreationTime, 
                        modificationDate: fileInfo.LastWriteTime
                    );
                    wm.Write(fi);
                }


                string[] directories = Directory.GetDirectories(dirPath);
                foreach (string directory in directories)
                {
                    TraverseDirectory(directory);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"[ACCESS DENIED] {dirPath}");
            }
            catch (DirectoryNotFoundException)
            {
                Console.WriteLine($"[NOT FOUND] {dirPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {dirPath}: {ex.Message}");
            }
        }
    }
}
