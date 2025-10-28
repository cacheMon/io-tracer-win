using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.cloudstorage
{
    internal class R2Client
    {
        private string BucketName;
        private string ServiceUrl;
        private string AccessKey;
        private string SecretKey;

        public R2Client(string bucketName, string serviceUrl, string accessKey, string secretKey)
        {
            BucketName = bucketName;
            ServiceUrl = serviceUrl;
            AccessKey = accessKey;
            SecretKey = secretKey;
        }
        public async Task PutObject(FileInfo file)
        {

            var objectKey = file.Name;
            var localPath = file.FullName;

            var config = new AmazonS3Config
            {
                ServiceURL = ServiceUrl,
                ForcePathStyle = true,
            };

            using var s3 = new AmazonS3Client(AccessKey, SecretKey, config);
            await using var fs = File.OpenRead(localPath);

            var putReq = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = objectKey,
                FilePath = localPath,
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true
            };

            try
            {
                var putRes = await s3.PutObjectAsync(putReq);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
