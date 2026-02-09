using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IOTracesCORE.utils
{
    public static class PrivilegeManager
    {
        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool LookupPrivilegeValue(string? host, string name, ref long pluid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct TokPriv1Luid
        {
            public int Count;
            public long Luid;
            public int Attr;
        }

        internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const string SE_BACKUP_NAME = "SeBackupPrivilege";
        internal const string SE_RESTORE_NAME = "SeRestorePrivilege";
        internal const int TOKEN_QUERY = 0x00000008;
        internal const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;

        public static bool EnableBackupPrivileges()
        {
            try
            {
                bool ret1 = EnablePrivilege(SE_BACKUP_NAME);
                bool ret2 = EnablePrivilege(SE_RESTORE_NAME);
                return ret1 && ret2;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to enable backup privileges: {ex.Message}");
                return false;
            }
        }

        private static bool EnablePrivilege(string privilege)
        {
            IntPtr htok = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref htok))
                {
                    return false;
                }

                TokPriv1Luid tp;
                tp.Count = 1;
                tp.Luid = 0;
                tp.Attr = SE_PRIVILEGE_ENABLED;

                if (!LookupPrivilegeValue(null, privilege, ref tp.Luid))
                {
                    return false;
                }

                if (!AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (htok != IntPtr.Zero)
                {
                    CloseHandle(htok);
                }
            }
        }
    }
}
