using Amazon.S3;
using Amazon.S3.Model;
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


        public ObjectStorageHandler(string bucketName, string serviceUrl, string accessKey, string secretKey)
        {
            r2Client = new R2Client(bucketName: bucketName, serviceUrl: serviceUrl, accessKey: accessKey, secretKey: secretKey);
        }

        public async Task UploadFile(string filepath)
        {
            FileInfo fi = new FileInfo(filepath);
            await r2Client.PutObject(fi);
        }

        public void QueueFile(string filepath)
        {
            uploadQueue.Enqueue(filepath);
        }



        public async Task UploadWorkerAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (uploadQueue.Count > 0)
                {
                    if(uploadQueue.TryDequeue(out var filepath))
                    {
                        Debug.WriteLine($"File queued: {uploadQueue.Count}");
                        try
                        {
                            Debug.WriteLine($"Uploading {filepath}");
                            await UploadFile(filepath);
                            Debug.WriteLine($"Uploaded {filepath}");
                            UploadedFiles++;
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
                    await Task.Delay(5000,ct);
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
