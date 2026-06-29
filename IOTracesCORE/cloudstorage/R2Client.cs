using IOTracesCORE.utils;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace IOTracesCORE.cloudstorage
{
    internal class R2Client
    {
        // 20 s is long enough for large files on slow connections but short
        // enough to detect a dropped internet link without a noticeable lag.
        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private string EndpointUrl = "https://io-tracer-worker.1a1a11a.workers.dev";
        private string CurrentDate = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");



        public async Task PutObject(FileInfo file, Func<Task>? onUploadSuccessAsync = null)
        {
            try
            {
                string? dirName = file.DirectoryName;
                string trace_type = string.IsNullOrEmpty(dirName) ? "unknown_type" : Path.GetFileName(dirName);
                if (string.IsNullOrEmpty(trace_type))
                {
                    trace_type = "unknown_type";
                }

                string filepath = $"windows_v1/{PathHasher.deviceId}/{CurrentDate}/{trace_type}/{file.Name}";
                var endpoint = $"{EndpointUrl}/presigned-url/{filepath}";

                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);

                Debug.WriteLine(endpoint);

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

                // Fire telemetry callback in background (non-blocking)
                if (onUploadSuccessAsync != null)
                {
                    try
                    {
                        _ = Task.Run(onUploadSuccessAsync).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[R2Client] Callback error: {ex.Message}");
                    }
                }
            }
            catch (Exception)
            {
                Debug.WriteLine($"{file.FullName} failed to upload");
                throw;
            }
        }
    }
}
