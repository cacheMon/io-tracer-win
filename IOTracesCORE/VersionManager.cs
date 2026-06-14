namespace IOTracesCORE
{
    internal class VersionManager
    {
        private static readonly VersionManager _instance = new VersionManager();
        private const string Version = "Release";

        private VersionManager()
        {
        }

        public static VersionManager Instance => _instance;

        public string GetCurrentVersion()
        {
            return Version;
        }

        /// <summary>
        /// Channel + assembly version, e.g. "Release/1.2.3.0", for the trace manifest.
        /// </summary>
        public string GetVersionString()
        {
            var asmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return asmVersion != null ? $"{Version}/{asmVersion}" : Version;
        }
    }
}
