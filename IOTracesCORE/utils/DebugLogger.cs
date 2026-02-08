using System.Diagnostics;

namespace IOTracesCORE.utils
{
    internal static class DebugLogger
    {
        private static readonly string LogFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "debug.log"
        );

        /// <summary>
        /// Logs a message to debug.log. Only active in DEBUG builds.
        /// </summary>
        [Conditional("DEBUG")]
        public static void Log(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logLine);
            }
            catch
            {
                // Silently ignore logging errors to avoid disrupting app
            }
        }

        /// <summary>
        /// Logs a message without timestamp (for raw data output).
        /// </summary>
        [Conditional("DEBUG")]
        public static void LogRaw(string message)
        {
            try
            {
                File.AppendAllText(LogFilePath, message + Environment.NewLine);
            }
            catch
            {
                // Silently ignore logging errors
            }
        }

        /// <summary>
        /// Clears the debug log file.
        /// </summary>
        [Conditional("DEBUG")]
        public static void Clear()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }
            }
            catch
            {
                // Silently ignore
            }
        }
    }
}
