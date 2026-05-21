using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// Service Control Manager wrapper for managing the IOTracerMinifilter kernel driver.
    /// Handles installation, loading, and unloading of the minifilter driver service.
    ///
    /// Operations are idempotent — calling Install/Load multiple times is safe.
    /// </summary>
    internal static class DriverService
    {
        private const string ServiceName = "IOTracerMinifilter";
        private const string DisplayName = "IO Tracer Minifilter";

        /// <summary>
        /// Ensures the driver service is installed. If already installed, returns silently.
        /// Requires admin privileges.
        /// </summary>
        public static bool EnsureInstalled(string sysPath)
        {
            if (!File.Exists(sysPath))
            {
                Debug.WriteLine($"[IOTracer] Driver file not found: {sysPath}");
                return false;
            }

            IntPtr scmHandle = IntPtr.Zero;
            IntPtr serviceHandle = IntPtr.Zero;

            try
            {
                // Open Service Control Manager
                scmHandle = OpenSCManager(null, null, ScmAccess.AllAccess);
                if (scmHandle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[IOTracer] OpenSCManager failed: 0x{err:X8}");
                    return false;
                }

                // Check if service already exists
                serviceHandle = OpenService(scmHandle, ServiceName, ServiceAccess.QueryStatus);
                if (serviceHandle != IntPtr.Zero)
                {
                    Debug.WriteLine($"[IOTracer] Driver service '{ServiceName}' already installed");
                    CloseServiceHandle(serviceHandle);
                    return true;
                }

                // Create new service
                serviceHandle = CreateService(
                    scmHandle,
                    ServiceName,
                    DisplayName,
                    ServiceAccess.AllAccess,
                    ServiceType.FileSystemDriver,
                    ServiceStart.Demand,
                    ServiceErrorControl.Normal,
                    sysPath,
                    null,   // lpLoadOrderGroup
                    IntPtr.Zero,  // lpdwTagId
                    null,   // lpDependencies
                    null,   // lpServiceStartName (LocalSystem)
                    null);  // lpPassword

                if (serviceHandle == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1073)  // ERROR_SERVICE_EXISTS
                    {
                        Debug.WriteLine($"[IOTracer] Driver service already exists");
                        return true;
                    }
                    Debug.WriteLine($"[IOTracer] CreateService failed: 0x{err:X8}");
                    return false;
                }

                Debug.WriteLine($"[IOTracer] Driver service installed: {sysPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] EnsureInstalled exception: {ex.Message}");
                return false;
            }
            finally
            {
                if (serviceHandle != IntPtr.Zero) CloseServiceHandle(serviceHandle);
                if (scmHandle != IntPtr.Zero) CloseServiceHandle(scmHandle);
            }
        }

        /// <summary>
        /// Ensures the driver service is loaded (running). If already loaded, returns silently.
        /// Falls back to fltmc.exe if SCM operations fail.
        /// </summary>
        public static bool EnsureLoaded()
        {
            IntPtr scmHandle = IntPtr.Zero;
            IntPtr serviceHandle = IntPtr.Zero;

            try
            {
                scmHandle = OpenSCManager(null, null, ScmAccess.AllAccess);
                if (scmHandle == IntPtr.Zero) return FallbackFltmcLoad();

                serviceHandle = OpenService(scmHandle, ServiceName, ServiceAccess.Start | ServiceAccess.QueryStatus);
                if (serviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[IOTracer] Service not found: {ServiceName}");
                    return false;
                }

                // Check current status
                if (GetServiceStatus(serviceHandle) == ServiceState.Running)
                {
                    Debug.WriteLine($"[IOTracer] Driver service already running");
                    return true;
                }

                // Start the service
                if (!StartService(serviceHandle, 0, null))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1056)  // ERROR_SERVICE_ALREADY_RUNNING
                    {
                        Debug.WriteLine($"[IOTracer] Service already running");
                        return true;
                    }
                    Debug.WriteLine($"[IOTracer] StartService failed: 0x{err:X8}, trying fltmc");
                    return FallbackFltmcLoad();
                }

                Debug.WriteLine($"[IOTracer] Driver service started");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] EnsureLoaded exception: {ex.Message}, falling back to fltmc");
                return FallbackFltmcLoad();
            }
            finally
            {
                if (serviceHandle != IntPtr.Zero) CloseServiceHandle(serviceHandle);
                if (scmHandle != IntPtr.Zero) CloseServiceHandle(scmHandle);
            }
        }

        /// <summary>
        /// Unloads (stops) the driver service.
        /// </summary>
        public static bool Unload()
        {
            IntPtr scmHandle = IntPtr.Zero;
            IntPtr serviceHandle = IntPtr.Zero;

            try
            {
                scmHandle = OpenSCManager(null, null, ScmAccess.AllAccess);
                if (scmHandle == IntPtr.Zero) return FallbackFltmcUnload();

                serviceHandle = OpenService(scmHandle, ServiceName, ServiceAccess.Stop | ServiceAccess.QueryStatus);
                if (serviceHandle == IntPtr.Zero)
                {
                    Debug.WriteLine($"[IOTracer] Service not found for unload");
                    return true;  // Already doesn't exist
                }

                // Check current status
                if (GetServiceStatus(serviceHandle) == ServiceState.Stopped)
                {
                    Debug.WriteLine($"[IOTracer] Driver service already stopped");
                    return true;
                }

                // Stop the service
                SERVICE_STATUS status = new SERVICE_STATUS();
                if (!ControlService(serviceHandle, ServiceControl.Stop, ref status))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 1062)  // ERROR_SERVICE_NOT_ACTIVE
                    {
                        return true;  // Already stopped
                    }
                    Debug.WriteLine($"[IOTracer] ControlService(STOP) failed: 0x{err:X8}, trying fltmc");
                    return FallbackFltmcUnload();
                }

                Debug.WriteLine($"[IOTracer] Driver service stopped");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] Unload exception: {ex.Message}, falling back to fltmc");
                return FallbackFltmcUnload();
            }
            finally
            {
                if (serviceHandle != IntPtr.Zero) CloseServiceHandle(serviceHandle);
                if (scmHandle != IntPtr.Zero) CloseServiceHandle(scmHandle);
            }
        }

        /// <summary>
        /// Returns true if the driver service is currently loaded/running.
        /// </summary>
        public static bool IsLoaded()
        {
            IntPtr scmHandle = IntPtr.Zero;
            IntPtr serviceHandle = IntPtr.Zero;

            try
            {
                scmHandle = OpenSCManager(null, null, ScmAccess.AllAccess);
                if (scmHandle == IntPtr.Zero) return false;

                serviceHandle = OpenService(scmHandle, ServiceName, ServiceAccess.QueryStatus);
                if (serviceHandle == IntPtr.Zero) return false;

                ServiceState state = GetServiceStatus(serviceHandle);
                return state == ServiceState.Running;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (serviceHandle != IntPtr.Zero) CloseServiceHandle(serviceHandle);
                if (scmHandle != IntPtr.Zero) CloseServiceHandle(scmHandle);
            }
        }

        private static ServiceState GetServiceStatus(IntPtr serviceHandle)
        {
            SERVICE_STATUS status = new SERVICE_STATUS();
            if (QueryServiceStatus(serviceHandle, ref status))
                return (ServiceState)status.dwCurrentState;
            return ServiceState.Unknown;
        }

        private static bool FallbackFltmcLoad()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "fltmc.exe",
                    Arguments = $"load {ServiceName}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(5000);
                    if (proc?.ExitCode == 0)
                    {
                        Debug.WriteLine($"[IOTracer] fltmc load succeeded");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] fltmc load failed: {ex.Message}");
            }
            return false;
        }

        private static bool FallbackFltmcUnload()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "fltmc.exe",
                    Arguments = $"unload {ServiceName}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(5000);
                    if (proc?.ExitCode == 0)
                    {
                        Debug.WriteLine($"[IOTracer] fltmc unload succeeded");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[IOTracer] fltmc unload failed: {ex.Message}");
            }
            return false;
        }

        // P/Invoke declarations for advapi32.dll

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, ScmAccess desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateService(
            IntPtr hSCManager,
            string serviceName,
            string displayName,
            ServiceAccess desiredAccess,
            ServiceType serviceType,
            ServiceStart startType,
            ServiceErrorControl errorControl,
            string binaryPathName,
            string? loadOrderGroup,
            IntPtr lpdwTagId,
            string? dependencies,
            string? serviceUserName,
            string? servicePassword);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, ServiceAccess desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(IntPtr hService, int dwNumServiceArgs, string?[] lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ControlService(IntPtr hService, ServiceControl dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        private enum ScmAccess : uint
        {
            Connect = 0x0001,
            CreateService = 0x0002,
            EnumerateService = 0x0004,
            Lock = 0x0008,
            QueryLockStatus = 0x0010,
            ModifyBootConfig = 0x0020,
            AllAccess = 0xF003F
        }

        private enum ServiceAccess : uint
        {
            QueryConfig = 0x0001,
            ChangeConfig = 0x0002,
            QueryStatus = 0x0004,
            EnumerateDependents = 0x0008,
            Start = 0x0010,
            Stop = 0x0020,
            PauseContinue = 0x0040,
            Interrogate = 0x0080,
            UserDefinedControl = 0x0100,
            AllAccess = 0xF01FF
        }

        private enum ServiceType : uint
        {
            KernelDriver = 0x00000001,
            FileSystemDriver = 0x00000002,
            Adapter = 0x00000004,
            RecognizerDriver = 0x00000008,
            Win32OwnProcess = 0x00000010,
            Win32ShareProcess = 0x00000020,
            InteractiveProcess = 0x00000100
        }

        private enum ServiceStart : uint
        {
            Boot = 0x00000000,
            System = 0x00000001,
            Auto = 0x00000002,
            Demand = 0x00000003,
            Disabled = 0x00000004
        }

        private enum ServiceErrorControl : uint
        {
            Ignore = 0x00000000,
            Normal = 0x00000001,
            Severe = 0x00000002,
            Critical = 0x00000003
        }

        private enum ServiceControl : uint
        {
            Stop = 0x00000001,
            Pause = 0x00000002,
            Continue = 0x00000003,
            Interrogate = 0x00000004,
            ShutDown = 0x00000005,
            ParamChange = 0x00000006,
            NetBindAdd = 0x00000007,
            NetBindRemove = 0x00000008,
            NetBindEnable = 0x00000009,
            NetBindDisable = 0x0000000A
        }

        private enum ServiceState : uint
        {
            Stopped = 0x00000001,
            StartPending = 0x00000002,
            StopPending = 0x00000003,
            Running = 0x00000004,
            ContinuePending = 0x00000005,
            PausePending = 0x00000006,
            Paused = 0x00000007,
            Unknown = 0xFFFFFFFF
        }
    }
}
