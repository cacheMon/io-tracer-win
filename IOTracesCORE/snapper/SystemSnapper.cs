using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.snapper
{
    internal class SystemSnapper
    {
        private readonly WriterManager wm;
        public SystemSnapper(WriterManager wm) { 
            this.wm = wm;
        }
        public void CaptureSpecSnapshot()
        {
            StringBuilder sb = new();
            Console.WriteLine("Capturing system specification...");
            // --- OS / Machine ---
            var (osCaption, osVersion, osBuild) = GetOsInfo();
            sb.AppendLine($"System: {osCaption}");
            sb.AppendLine($"Version: {osVersion} (Build {osBuild})");
            sb.AppendLine($"Machine: {Environment.Is64BitOperatingSystem switch { true => "x64", false => "x86" }}");
            sb.AppendLine();

            // --- CPU ---
            var (cpuName, physicalCores, logicalCores, maxMhz) = GetCpuInfo();
            sb.AppendLine($"CPU Brand: {cpuName}");
            sb.AppendLine($"CPU Cores (logical): {logicalCores}");
            sb.AppendLine($"CPU Cores (physical): {physicalCores}");
            sb.AppendLine($"CPU Max Frequency: {(maxMhz.HasValue ? $"{maxMhz} MHz" : "N/A")}");
            sb.AppendLine();

            // --- Memory ---
            var (totalGb, freeGb) = GetMemoryInfo();
            sb.AppendLine($"Total Memory: {totalGb:0.##} GB");
            sb.AppendLine($"Available Memory: {freeGb:0.##} GB");
            sb.AppendLine();

            // --- GPU(s) ---
            var gpus = GetGpuNames();
            sb.AppendLine($"GPUs: {(gpus.Length == 0 ? "None detected" : string.Join(", ", gpus))}");

            // --- Storage ---
            var disks = GetDisks();
            sb.AppendLine("Storages:");
            if (disks.Length == 0)
            {
                sb.AppendLine("Could not detect");
            }
            else
            {
                foreach (var d in disks)
                    sb.AppendLine($"{d.model}  {PrettySize(d.sizeBytes)}");
            }
            wm.DirectWrite("spec.txt",sb.ToString());
            Console.WriteLine("Capturing system specification complete!");
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
                    maxMhz = Math.Max(maxMhz ?? 0, mhz); // take the highest across sockets
            }

            if (physSum == 0) physSum = logSum; // fallback
            if (logSum == 0) logSum = Environment.ProcessorCount;

            return (name, physSum, logSum, maxMhz);
        }

         (double totalGb, double freeGb) GetMemoryInfo()
        {
            using var mos = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem");
            using var results = mos.Get();
            var mo = results.Cast<ManagementObject>().FirstOrDefault();

            static double KBtoGB(ulong kb) => kb / 1024.0 / 1024.0;

            if (mo != null &&
                ulong.TryParse(mo["TotalVisibleMemorySize"]?.ToString(), out var totalKb) &&
                ulong.TryParse(mo["FreePhysicalMemory"]?.ToString(), out var freeKb))
            {
                return (KBtoGB(totalKb), KBtoGB(freeKb));
            }
            return (0, 0);
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
