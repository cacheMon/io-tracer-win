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
        // 20 s is long enough for large files on slow connections but short
        // enough to detect a dropped internet link without a noticeable lag.
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private string EndpointUrl = "https://io-tracer-worker.1a1a11a.workers.dev";
        private string CurrentDate = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");



        public async Task PutObject(FileInfo file)
        {
            try
            {

                var currentVersion = VersionManager.Instance.GetCurrentVersion();
                if (
                    string.IsNullOrWhiteSpace(currentVersion) ||
                    currentVersion.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                )
                {
                    currentVersion = "dev";
                }
                else
                {
                    currentVersion = currentVersion.Replace('.', '_');
                }

                string dir_name = file.DirectoryName ?? "unknown_dir";
                string trace_type = Path.GetFileName(file.DirectoryName) ?? "unknown_type";

                var endpoint = $"{EndpointUrl}/windows_trace_v4_test/{PathHasher.deviceId}/{CurrentDate}/{trace_type}/{file.Name}";

                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    endpoint
                );

                Debug.WriteLine(endpoint);

                //request.Headers.Add("X-Active-Delta-Seconds", deltaSeconds.ToString());
                //request.Headers.Add("X-File-Events-Delta-Collected", deltaFileEvent.ToString());
                //request.Headers.Add("X-Computer-Id", PathHasher.deviceId);

                var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var presignedUrl = await response.Content.ReadAsStringAsync();
                presignedUrl = presignedUrl.Trim('"');


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
