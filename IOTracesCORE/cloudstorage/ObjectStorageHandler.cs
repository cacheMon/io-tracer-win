using Amazon.S3;
using Amazon.S3.Model;
using System;
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
        public ObjectStorageHandler()
        {

        }

        public static async Task RunAsync()
        {
            var serviceUrl = "http://localhost:9000";   
            var accessKey = "admin";
            var secretKey = "password123";
            var bucketName = "bucket2";             
            var objectKey = "C:\\Users\\ASUS\\Documents\\todo.txt";               
            var localPath = "C:\\Users\\ASUS\\Documents\\todo.txt";               

            var config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                ForcePathStyle = true,
            };

            using var s3 = new AmazonS3Client(accessKey, secretKey, config);

            await using var fileStream = File.OpenRead(localPath);

            var putReq = new PutObjectRequest
            {
                BucketName = bucketName,
                Key = objectKey,
                InputStream = fileStream,
                ContentType = "text/plain"
            };

            try
            {
                var putRes = await s3.PutObjectAsync(putReq);
                Debug.WriteLine("Upload success. ETag: " + putRes.ETag);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Upload failed: " + ex.Message);
            }
        }
    }
        
}
