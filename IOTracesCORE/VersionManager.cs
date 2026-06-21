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

        /// <summary>
        /// The git commit the tracer was built from (short SHA), stamped into the
        /// assembly's informational version at build time (see IOTracesCORE.csproj).
        /// Lets a trace be tied back to the exact source revision. Returns "unknown"
        /// for builds made without git (the SHA is then simply absent).
        /// </summary>
        public string GetCommitId()
        {
            var attrs = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            string? info = attrs.Length > 0
                ? ((System.Reflection.AssemblyInformationalVersionAttribute)attrs[0]).InformationalVersion
                : null;
            return ParseCommitId(info);
        }

        /// <summary>
        /// Extracts the commit SHA the SDK appends after '+' in an informational
        /// version string (e.g. "1.2.3+ab12cd34" -> "ab12cd34"). Returns "unknown"
        /// when no SHA is present. Pure helper, separated for unit testing.
        /// </summary>
        internal static string ParseCommitId(string? informationalVersion)
        {
            if (string.IsNullOrEmpty(informationalVersion)) return "unknown";
            int plus = informationalVersion.IndexOf('+');
            if (plus < 0 || plus == informationalVersion.Length - 1) return "unknown";
            return informationalVersion.Substring(plus + 1);
        }
    }
}
