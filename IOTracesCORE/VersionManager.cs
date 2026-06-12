namespace IOTracesCORE
{
    internal class VersionManager
    {
        private static VersionManager? _instance;
        private string Version = "Release";

        private VersionManager()
        {
        }

        public static VersionManager Instance => _instance ??= new VersionManager();

        public string GetCurrentVersion()
        {
            return Version;
        }
    }
}
