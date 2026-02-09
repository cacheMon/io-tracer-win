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
        private Random random = new Random();
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
                    Debug.WriteLine($"Scanning Drive: {drive.Name}");
                    scanRoot = drive.Name;
                    TraverseDirectory(drive.RootDirectory.FullName);
                    Thread.Sleep(500);
                }
            }
        }

        private void TraverseDirectory(string rootPath)
        {
            Stack<string> dirs = new Stack<string>();
            dirs.Push(rootPath);
            DateTime ts = DateTime.Now;
            while (dirs.Count > 0)
            {
                if (interrupted) return;
                string currentDir = dirs.Pop();

                try
                {
                    DirectoryInfo di = new DirectoryInfo(currentDir);
                    foreach (FileSystemInfo fsi in di.EnumerateFileSystemInfos())
                    {
                        if (interrupted) return;

                        if ((fsi.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            dirs.Push(fsi.FullName);
                        }
                        else
                        {
                            FileInfo fileInfo = (FileInfo)fsi;
                            string filepath = anonymouse ? PathHasher.HashFilePath(fileInfo.FullName, scanRoot, anonymouse, hashLen) : fileInfo.FullName;
                            FilesystemInfo fi = new FilesystemInfo(
                                timestamp: ts,
                                path: filepath.Replace("\\", "/"),
                                size: fileInfo.Length,
                                creationDate: fileInfo.CreationTime,
                                modificationDate: fileInfo.LastWriteTime,
                                lastAccessTime: fileInfo.LastAccessTime,
                                attributes: fileInfo.Attributes,
                                extension: fileInfo.Extension,
                                isReadOnly: fileInfo.IsReadOnly
                            );
                            wm.Write(fi);
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[ACCESS DENIED] {currentDir}");
                }
                catch (DirectoryNotFoundException)
                {
                    Debug.WriteLine($"[NOT FOUND] {currentDir}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERROR] {currentDir}: {ex.Message}");
                }
            }
        }
    }
}
