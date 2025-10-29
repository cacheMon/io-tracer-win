using Amazon.S3;
using Amazon.S3.Model;
using IOTracesCORE.utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace IOTracesCORE.cloudstorage
{
    internal class R2Client
    {
        static readonly HttpClient http = new HttpClient();

        private string EndpointUrl = "https://io-tracer-worker.1a1a11a.workers.dev";

 

        public async Task PutObject(FileInfo file)
        {
            try
            {
                var response = await http.GetAsync($"{EndpointUrl}/{PathHasher.deviceId}/{file.Name}");
                response.EnsureSuccessStatusCode();

                var presignedUrl = await response.Content.ReadAsStringAsync();
                presignedUrl = presignedUrl.Trim('"');

                //Debug.WriteLine("presigned URL: " + presignedUrl);

                using var fileStream = System.IO.File.OpenRead(file.FullName);
                var uploadRequest = new HttpRequestMessage(HttpMethod.Put, presignedUrl)
                {
                    Content = new StreamContent(fileStream)
                };


                var uploadResponse = await http.SendAsync(uploadRequest);
                uploadResponse.EnsureSuccessStatusCode();

                Debug.WriteLine($"{file.FullName} successfully uploaded");

            }
            catch (Exception)
            {
                Debug.WriteLine($"{file.FullName} failed to upload");
                throw;
            }
        }
    }
}
