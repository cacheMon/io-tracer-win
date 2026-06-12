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
    }
}
