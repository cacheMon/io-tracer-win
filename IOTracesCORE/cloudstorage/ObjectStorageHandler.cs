using Amazon.S3;
using Amazon.S3.Model;
using IOTracesCORE.utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.cloudstorage
{
    internal class ObjectStorageHandler
    {

        private R2Client r2Client;
        private ConcurrentQueue<string> uploadQueue = new();
        public static int UploadedFiles = 0;
        private TimeSpan LastActiveHours = TimeSpan.FromSeconds(0);
        public static int LastFileEvent;


        public ObjectStorageHandler()
        {
            r2Client = new();
        }

        public async Task UploadFile(string filepath)
        {
#if DEBUG
            Debug.WriteLine($"[DEBUG] Upload disabled in development mode. Skipping: {filepath}");
            return;
#endif
            //double deltaSeconds = WriterManager.active_session.TotalSeconds - LastActiveHours.TotalSeconds;
            //int deltaFileEvent = WriterManager.file_event_counter - LastFileEvent;
            //Debug.WriteLine($"Uploading file with delta seconds: {deltaSeconds}, delta file events: {deltaFileEvent}");

            FileInfo fi = new FileInfo(filepath);
            await r2Client.PutObject(fi);
            File.Delete(filepath);

            LastFileEvent = WriterManager.file_event_counter;
            LastActiveHours = WriterManager.active_session;
        }

        public void QueueFile(string filepath)
        {
            uploadQueue.Enqueue(filepath);
        }

        public async Task ClearQueue()
        {
            Debug.WriteLine("Clearing upload queue");
            while (uploadQueue.Count > 0)
            {
                if (uploadQueue.TryDequeue(out var filepath))
                {
                    Debug.WriteLine($"File queued: {uploadQueue.Count}");
                    try
                    {
                        Debug.WriteLine($"Uploading {filepath}");
                        await UploadFile(filepath);
                        Debug.WriteLine($"Uploaded {filepath}");
                        UploadedFiles++;

                        // Unlock reward after first successful upload
                        RewardManager.Instance.UnlockReward();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error uploading {filepath}: {ex}");
                        //QueueFile(filepath);
                    }
                }
            }
        }

        public async Task UploadWorkerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (uploadQueue.Count > 0)
                {
                    if (uploadQueue.TryDequeue(out var filepath))
                    {
                        Debug.WriteLine($"File queued: {uploadQueue.Count}");
                        try
                        {
                            Debug.WriteLine($"Uploading {filepath}");
                            await UploadFile(filepath);
                            Debug.WriteLine($"Uploaded {filepath}");
                            UploadedFiles++;

                            // Unlock reward after first successful upload
                            RewardManager.Instance.UnlockReward();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error uploading {filepath}: {ex}");
                            QueueFile(filepath);
                        }
                    }
                    await Task.Delay(500, ct);
                }
                else
                {
                    await Task.Delay(5000, ct);
                }
            }
        }

        public void UploadThread(CancellationToken ct)
        {
            Debug.WriteLine("uploader thread started");
            Task.Run(() => UploadWorkerAsync(ct), ct);
        }

    }

}