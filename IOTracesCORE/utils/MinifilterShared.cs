using System.Runtime.InteropServices;

namespace IOTracesCORE.utils
{
    /// <summary>
    /// Shared message structures between minifilter driver and userspace client.
    /// Must match exactly with IOTracerMinifilter/shared/iotracer_shared.h
    /// </summary>
    internal static class MinifilterShared
    {
        public const string PortName = @"\IOTracerMinifilterPort";
        public const int MaxPath = 512;
        public const int FilterMessageHeaderSize = 16;  // ReplyLength (4) + MessageId (8) + padding (4)

        /// <summary>
        /// Standard FILTER_MESSAGE_HEADER from fltlib.h.
        /// All filter messages must begin with this structure.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FilterMessageHeader
        {
            public uint ReplyLength;
            public ulong MessageId;
        }

        /// <summary>
        /// Message sent from minifilter driver to userspace on file create.
        /// Delivers normalized absolute path for cache pre-population.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct IotracerMessage
        {
            public FilterMessageHeader Header;  // Required first field for FilterGetMessage
            public uint ProcessId;
            public ulong FileObjectPtr;         // Maps to FilesystemHandlers.nameByObj key
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
            public string FileName;             // Normalized absolute path, null-terminated
        }
    }
}
