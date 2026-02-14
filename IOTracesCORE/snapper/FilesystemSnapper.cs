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
        private bool snapshotCompleted;
        private readonly int hashLen = 16;
        private string scanRoot = "";
        private bool anonymouse;
        private Random random = new Random();
        public FilesystemSnapper(WriterManager wm, bool anonymouse = false)
        {
            this.wm = wm;
            interrupted = false;
            snapshotCompleted = false;
            this.anonymouse = anonymouse;
            PrivilegeManager.EnableBackupPrivileges();
        }

        public void Stop()
        {
            interrupted = true;
        }

        public bool IsSnapshotComplete()
        {
            return snapshotCompleted;
        }

        public void Run()
        {
            DriveInfo[] drives = DriveInfo.GetDrives();
            Console.WriteLine("Starting filesystem snapshot...");
            foreach (DriveInfo drive in drives)
            {
                if (interrupted) return;

                if (drive.IsReady)
                {
                    Debug.WriteLine($"Scanning Drive: {drive.Name}");
                    scanRoot = drive.Name;
                    TraverseDirectory(drive.RootDirectory.FullName);
                    Thread.Sleep(500);
                }
            }

            // Only mark as complete if we weren't interrupted
            if (!interrupted)
            {
                snapshotCompleted = true;
                Debug.WriteLine("Filesystem snapshot completed.");
                wm.FinalizeFilesystemSnapshot(true);
            }
            else
            {
                Debug.WriteLine("Filesystem snapshot was interrupted and is incomplete.");
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
                            if ((fsi.Attributes & FileAttributes.ReparsePoint) != FileAttributes.ReparsePoint)
                            {
                                dirs.Push(fsi.FullName);
                            }
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
