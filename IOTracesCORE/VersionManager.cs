using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace IOTracesCORE
{
    internal class VersionManager
    {
        private static VersionManager? _instance;
        private readonly Velopack.UpdateManager? _updateManager;
        private bool _isCheckingForUpdates = false;

        private const string GITHUB_REPO_URL = "https://github.com/cacheMon/io-tracer-win";
        private const string UPDATE_URL = $"{GITHUB_REPO_URL}/releases/latest/download";
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
