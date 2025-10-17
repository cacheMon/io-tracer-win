using IOTracesCORE.trace;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.snapper
{
    internal class FilesystemSnapper
    {
        private WriterManager wm;
        private bool interrupted;
        private readonly int hashLen = 16;
        private string scanRoot = "";
        private bool anonymouse;
        public FilesystemSnapper(WriterManager wm, bool anonymouse = false)
        {
            this.wm = wm;
            interrupted = false;
            this.anonymouse = anonymouse;
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
                    scanRoot = drive.Name;
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
                    string filepath = PathHasher.HashFilePath(fileInfo.FullName, scanRoot, anonymouse ,hashLen);
                    FilesystemInfo fi = new FilesystemInfo(
                        path: filepath.Replace("\\", "/"), 
                        size: fileInfo.Length, 
                        creationDate: fileInfo.CreationTime, 
                        modificationDate: fileInfo.LastWriteTime
                    );
                    //Debug.WriteLine($"Path: {filepath}");
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
