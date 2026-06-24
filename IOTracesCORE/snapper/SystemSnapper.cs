using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace IOTracesCORE.snapper
{
    #region JSON Schema Classes
    internal class CpuInfo
    {
        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("cores_logical")]
        public int CoresLogical { get; set; }

        [JsonPropertyName("cores_physical")]
        public int CoresPhysical { get; set; }

        [JsonPropertyName("frequency_mhz")]
        public double? FrequencyMhz { get; set; }

        [JsonPropertyName("frequency_min_mhz")]
        public double? FrequencyMinMhz { get; set; }

        [JsonPropertyName("frequency_max_mhz")]
        public double? FrequencyMaxMhz { get; set; }
    }

    internal class MemoryInfo
    {
        [JsonPropertyName("total_bytes")]
        public long TotalBytes { get; set; }

        [JsonPropertyName("available_bytes")]
        public long AvailableBytes { get; set; }

        [JsonPropertyName("used_bytes")]
        public long UsedBytes { get; set; }

        [JsonPropertyName("percent_used")]
        public double PercentUsed { get; set; }

        [JsonPropertyName("total_gb")]
        public double TotalGb { get; set; }

        [JsonPropertyName("available_gb")]
        public double AvailableGb { get; set; }

        [JsonPropertyName("swap_total_bytes")]
        public long SwapTotalBytes { get; set; }

        [JsonPropertyName("swap_used_bytes")]
        public long SwapUsedBytes { get; set; }

        [JsonPropertyName("swap_free_bytes")]
        public long SwapFreeBytes { get; set; }
    }

    internal class PartitionInfo
    {
        [JsonPropertyName("device")]
        public string Device { get; set; } = "";

        [JsonPropertyName("mountpoint")]
        public string Mountpoint { get; set; } = "";

        [JsonPropertyName("fstype")]
        public string Fstype { get; set; } = "";

        [JsonPropertyName("opts")]
        public string Opts { get; set; } = "";

        [JsonPropertyName("total_bytes")]
        public long TotalBytes { get; set; }

        [JsonPropertyName("used_bytes")]
        public long UsedBytes { get; set; }

        [JsonPropertyName("free_bytes")]
        public long FreeBytes { get; set; }

        [JsonPropertyName("percent_used")]
        public double PercentUsed { get; set; }
    }

    internal class DiskInfo
    {
        [JsonPropertyName("storage_devices")]
        public List<string> StorageDevices { get; set; } = new();

        [JsonPropertyName("partitions")]
        public List<PartitionInfo> Partitions { get; set; } = new();

        [JsonPropertyName("gpus")]
        public List<string> Gpus { get; set; } = new();
    }

    internal class NetworkAddress
    {
        [JsonPropertyName("family")]
        public string Family { get; set; } = "";

        [JsonPropertyName("address")]
        public string Address { get; set; } = "";

        [JsonPropertyName("netmask")]
        public string? Netmask { get; set; }

        [JsonPropertyName("broadcast")]
        public string? Broadcast { get; set; }
    }

    internal class NetworkInterface
    {
        [JsonPropertyName("addresses")]
        public List<NetworkAddress> Addresses { get; set; } = new();

        [JsonPropertyName("is_up")]
        public bool IsUp { get; set; }

        [JsonPropertyName("speed_mbps")]
        public long? SpeedMbps { get; set; }

        [JsonPropertyName("mtu")]
        public int Mtu { get; set; }
    }

    internal class NetworkInfo
    {
        [JsonPropertyName("interfaces")]
        public Dictionary<string, NetworkInterface> Interfaces { get; set; } = new();

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";
    }

    internal class OsInfo
    {
        [JsonPropertyName("system")]
        public string System { get; set; } = "";

        [JsonPropertyName("release")]
        public string Release { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("machine")]
        public string Machine { get; set; } = "";

        [JsonPropertyName("hostname")]
        public string Hostname { get; set; } = "";

        [JsonPropertyName("country")]
        public string Country { get; set; } = "";
    }
    #endregion

    internal class SystemSnapper
    {
        private readonly WriterManager wm;
        private readonly bool is_anonymous;
        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public SystemSnapper(WriterManager wm, bool is_anonymous = false)
        {
            this.wm = wm;
            this.is_anonymous = is_anonymous;
        }

        // Stable, non-reversible redaction of an identifying value in anonymous mode.
        // Empty/blank values are passed through unchanged.
        private string Anon(string value)
        {
            if (!is_anonymous || string.IsNullOrEmpty(value))
                return value;
            return utils.PathHasher.Hash(value, 16);
        }

        public void CaptureSpecSnapshot()
        {
            Console.WriteLine("Capturing system specification...");

            // Get country code once for all files
            string countryCode = GetCountryCodeFromIp();

            // Capture CPU info
            var cpuInfo = CaptureCpuInfo();
            string cpuJson = JsonSerializer.Serialize(cpuInfo, jsonOptions);
            wm.DirectWrite("cpu_info.json", cpuJson);

            // Capture Memory info
            var memoryInfo = CaptureMemoryInfo();
            string memoryJson = JsonSerializer.Serialize(memoryInfo, jsonOptions);
            wm.DirectWrite("memory_info.json", memoryJson);

            // Capture Disk info (includes GPUs)
            var diskInfo = CaptureDiskInfo();
            string diskJson = JsonSerializer.Serialize(diskInfo, jsonOptions);
            wm.DirectWrite("disk_info.json", diskJson);

            // Capture Network info
            var networkInfo = CaptureNetworkInfo();
            string networkJson = JsonSerializer.Serialize(networkInfo, jsonOptions);
            wm.DirectWrite("network_info.json", networkJson);

            // Capture OS info
            var osInfo = CaptureOsInfo(countryCode);
            string osJson = JsonSerializer.Serialize(osInfo, jsonOptions);
            wm.DirectWrite("os_info.json", osJson);

            Console.WriteLine("Capturing system specification complete!");
        }

        private CpuInfo CaptureCpuInfo()
        {
            var (cpuName, physicalCores, logicalCores, maxMhz) = GetCpuInfo();

            return new CpuInfo
            {
                Brand = cpuName,
                CoresLogical = logicalCores,
                CoresPhysical = physicalCores,
                FrequencyMhz = maxMhz,
                FrequencyMinMhz = null, // Windows WMI doesn't easily provide min frequency
                FrequencyMaxMhz = maxMhz
            };
        }

        private MemoryInfo CaptureMemoryInfo()
        {
            var (totalKb, freeKb) = GetMemoryInfoRaw();

            long totalBytes = totalKb * 1024L;
            long availableBytes = freeKb * 1024L;
            long usedBytes = totalBytes - availableBytes;
            double percentUsed = totalBytes > 0 ? (usedBytes * 100.0 / totalBytes) : 0;
            double totalGb = totalBytes / 1024.0 / 1024.0 / 1024.0;
            double availableGb = availableBytes / 1024.0 / 1024.0 / 1024.0;

            // Get page file (swap) info
            var (swapTotal, swapFree) = GetPageFileInfo();
            long swapUsed = swapTotal - swapFree;

            return new MemoryInfo
            {
                TotalBytes = totalBytes,
                AvailableBytes = availableBytes,
                UsedBytes = usedBytes,
                PercentUsed = Math.Round(percentUsed, 1),
                TotalGb = Math.Round(totalGb, 2),
                AvailableGb = Math.Round(availableGb, 2),
                SwapTotalBytes = swapTotal,
                SwapUsedBytes = swapUsed,
                SwapFreeBytes = swapFree
            };
        }

        private DiskInfo CaptureDiskInfo()
        {
            var diskInfo = new DiskInfo();

            // Storage devices
            var disks = GetDisks();
            foreach (var d in disks)
            {
                diskInfo.StorageDevices.Add($"{d.model}  {PrettySize(d.sizeBytes)}");
            }

            // Partitions (logical drives)
            var partitions = GetPartitions();
            diskInfo.Partitions.AddRange(partitions);

            // GPUs
            var gpus = GetGpuNames();
            diskInfo.Gpus.AddRange(gpus);

            return diskInfo;
        }

        private NetworkInfo CaptureNetworkInfo()
        {
            var networkInfo = new NetworkInfo
            {
                Hostname = Anon(Dns.GetHostName())
            };

            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();

            foreach (var iface in interfaces)
            {
                var netInterface = new NetworkInterface
                {
                    IsUp = iface.OperationalStatus == OperationalStatus.Up,
                    SpeedMbps = iface.Speed > 0 ? iface.Speed / 1_000_000 : null,
                    Mtu = GetMtu(iface)
                };

                // Get IP addresses
                var ipProps = iface.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        netInterface.Addresses.Add(new NetworkAddress
                        {
                            Family = "AF_INET",
                            Address = Anon(addr.Address.ToString()),
                            Netmask = addr.IPv4Mask?.ToString(),
                            Broadcast = null
                        });
                    }
                    else if (addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        netInterface.Addresses.Add(new NetworkAddress
                        {
                            Family = "AF_INET6",
                            Address = Anon(addr.Address.ToString()),
                            Netmask = null,
                            Broadcast = null
                        });
                    }
                }

                // Get MAC address
                var mac = iface.GetPhysicalAddress();
                if (mac != null && mac.ToString().Length > 0)
                {
                    string macStr = string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("x2")));
                    netInterface.Addresses.Add(new NetworkAddress
                    {
                        Family = "AF_PACKET",
                        Address = Anon(macStr),
                        Netmask = null,
                        Broadcast = null
                    });
                }

                networkInfo.Interfaces[iface.Name] = netInterface;
            }

            return networkInfo;
        }

        private OsInfo CaptureOsInfo(string countryCode)
        {
            var (osCaption, osVersion, osBuild) = GetOsInfo();

            return new OsInfo
            {
                System = "Windows",
                Release = osVersion,
                Version = $"{osCaption} (Build {osBuild})",
                Machine = Environment.Is64BitOperatingSystem ? "x64" : "x86",
                Hostname = Anon(Dns.GetHostName()),
                Country = countryCode
            };
        }

        private string GetCountryCodeFromIp()
        {
            try
            {
                string url = "http://ip-api.com/json/?fields=status,countryCode";

                var task = Task.Run(async () => await httpClient.GetStringAsync(url));

                if (task.Wait(TimeSpan.FromSeconds(8)))
                {
                    using var doc = JsonDocument.Parse(task.Result);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
                    {
                        if (root.TryGetProperty("countryCode", out var cc))
                        {
                            string code = cc.GetString() ?? "XX";
                            Debug.WriteLine($"Geolocation resolved: {code}");
                            return code;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IP geolocation failed (ip-api): {ex.Message}");
            }

            try
            {
                string url = "https://ipinfo.io/json";

                var task = Task.Run(async () => await httpClient.GetStringAsync(url));

                if (task.Wait(TimeSpan.FromSeconds(5)))
                {
                    using var doc = JsonDocument.Parse(task.Result);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("country", out var cc))
                    {
                        string code = cc.GetString() ?? "XX";
                        Debug.WriteLine($"Geolocation resolved (fallback): {code}");
                        return code;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fallback IP geolocation failed: {ex.Message}");
            }

            return "XX";
        }

        (string caption, string version, string build) GetOsInfo()
        {
            using var mos = new ManagementObjectSearcher("SELECT Caption,Version,BuildNumber FROM Win32_OperatingSystem");
            using var results = mos.Get();
            var mo = results.Cast<ManagementObject>().FirstOrDefault();
            return (mo?["Caption"]?.ToString() ?? "Windows",
                    mo?["Version"]?.ToString() ?? Environment.OSVersion.Version.ToString(),
                    mo?["BuildNumber"]?.ToString() ?? "");
        }

        (string name, int physicalCores, int logicalCores, int? maxMhz) GetCpuInfo()
        {
            using var mos = new ManagementObjectSearcher("SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor");
            using var results = mos.Get();

            string name = "Unknown CPU";
            int physSum = 0;
            int logSum = 0;
            int? maxMhz = null;

            foreach (ManagementObject mo in results)
            {
                name = mo["Name"]?.ToString() ?? name;
                if (int.TryParse(mo["NumberOfCores"]?.ToString(), out var cores)) physSum += cores;
                if (int.TryParse(mo["NumberOfLogicalProcessors"]?.ToString(), out var logs)) logSum += logs;
                if (int.TryParse(mo["MaxClockSpeed"]?.ToString(), out var mhz))
                    maxMhz = Math.Max(maxMhz ?? 0, mhz);
            }

            if (physSum == 0) physSum = logSum;
            if (logSum == 0) logSum = Environment.ProcessorCount;

            return (name, physSum, logSum, maxMhz);
        }

        (long totalKb, long freeKb) GetMemoryInfoRaw()
        {
            using var mos = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
            using var results = mos.Get();
            var mo = results.Cast<ManagementObject>().FirstOrDefault();

            if (mo != null &&
                ulong.TryParse(mo["TotalVisibleMemorySize"]?.ToString(), out var totalKb) &&
                ulong.TryParse(mo["FreePhysicalMemory"]?.ToString(), out var freeKb))
            {
                return ((long)totalKb, (long)freeKb);
            }
            return (0, 0);
        }

        (long totalBytes, long freeBytes) GetPageFileInfo()
        {
            try
            {
                using var mos = new ManagementObjectSearcher("SELECT AllocatedBaseSize,CurrentUsage FROM Win32_PageFileUsage");
                using var results = mos.Get();

                long totalBytes = 0;
                long usedBytes = 0;

                foreach (ManagementObject mo in results)
                {
                    if (uint.TryParse(mo["AllocatedBaseSize"]?.ToString(), out var allocated))
                        totalBytes += allocated * 1024L * 1024L; // MB to bytes
                    if (uint.TryParse(mo["CurrentUsage"]?.ToString(), out var current))
                        usedBytes += current * 1024L * 1024L; // MB to bytes
                }

                return (totalBytes, totalBytes - usedBytes);
            }
            catch
            {
                return (0, 0);
            }
        }

        List<PartitionInfo> GetPartitions()
        {
            var partitions = new List<PartitionInfo>();

            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (drive.IsReady)
                    {
                        long totalBytes = drive.TotalSize;
                        long freeBytes = drive.AvailableFreeSpace;
                        long usedBytes = totalBytes - freeBytes;
                        double percentUsed = totalBytes > 0 ? (usedBytes * 100.0 / totalBytes) : 0;

                        partitions.Add(new PartitionInfo
                        {
                            Device = drive.Name,
                            Mountpoint = drive.RootDirectory.FullName,
                            Fstype = drive.DriveFormat,
                            Opts = drive.DriveType.ToString(),
                            TotalBytes = totalBytes,
                            UsedBytes = usedBytes,
                            FreeBytes = freeBytes,
                            PercentUsed = Math.Round(percentUsed, 1)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting partitions: {ex.Message}");
            }

            return partitions;
        }

        int GetMtu(System.Net.NetworkInformation.NetworkInterface iface)
        {
            try
            {
                var ipProps = iface.GetIPProperties();
                var ipv4Props = ipProps.GetIPv4Properties();
                return ipv4Props.Mtu;
            }
            catch
            {
                return 0;
            }
        }

        string[] GetGpuNames()
        {
            using var mos = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var results = mos.Get();
            return results.Cast<ManagementObject>()
                          .Select(mo => mo["Name"]?.ToString())
                          .Where(s => !string.IsNullOrWhiteSpace(s))
                          .Distinct()
                          .ToArray()!;
        }

        (string model, long sizeBytes)[] GetDisks()
        {
            using var mos = new ManagementObjectSearcher("SELECT Model,Size FROM Win32_DiskDrive");
            using var results = mos.Get();
            return results.Cast<ManagementObject>()
                          .Select(mo => (
                              model: mo["Model"]?.ToString() ?? "Unknown Disk",
                              sizeBytes: long.TryParse(mo["Size"]?.ToString(), out var sz) ? sz : 0L
                          ))
                          .ToArray();
        }

        string PrettySize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double val = bytes;
            int i = 0;
            while (val >= 1024 && i < units.Length - 1) { val /= 1024; i++; }
            return $"{val:0.##} {units[i]}";
        }
    }
}