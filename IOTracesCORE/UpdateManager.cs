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
    internal class UpdateManager
    {
        private static UpdateManager? _instance;
        private readonly Velopack.UpdateManager? _updateManager;
        private bool _isCheckingForUpdates = false;

        // Replace with your GitHub repository information
        private const string GITHUB_REPO_URL = "https://github.com/YOUR_USERNAME/YOUR_REPO_NAME";
        private const string UPDATE_URL = $"{GITHUB_REPO_URL}/releases/latest/download";

        private UpdateManager()
        {
            try
            {
                // Initialize Velopack with GitHub source
                _updateManager = new Velopack.UpdateManager(
                    new GithubSource(GITHUB_REPO_URL, null, false)
                );

                Debug.WriteLine("Update manager initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize update manager: {ex.Message}");
                _updateManager = null;
            }
        }

        public static UpdateManager Instance => _instance ??= new UpdateManager();

        public bool IsUpdateManagerAvailable => _updateManager != null;

        /// <summary>
        /// Check for updates with optional UI feedback
        /// </summary>
        /// <param name="silent">If true, only show UI when update is available</param>
        public async Task CheckForUpdatesAsync(bool silent = true)
        {
            if (_updateManager == null)
            {
                Debug.WriteLine("Update manager not available");
                if (!silent)
                {
                    MessageBox.Show(
                        "Update checking is not available in this build.",
                        "Updates Unavailable",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            if (_isCheckingForUpdates)
            {
                Debug.WriteLine("Already checking for updates");
                return;
            }

            try
            {
                _isCheckingForUpdates = true;
                Debug.WriteLine("Checking for updates...");

                var updateInfo = await _updateManager.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    Debug.WriteLine("No updates available - already on latest version");
                    if (!silent)
                    {
                        MessageBox.Show(
                            "You are running the latest version of IO Traces Core.",
                            "No Updates Available",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                var currentVersion = _updateManager.CurrentVersion?.ToString() ?? "Unknown";
                var newVersion = updateInfo.TargetFullRelease.Version.ToString();

                Debug.WriteLine($"Update available: {currentVersion} -> {newVersion}");

                var result = MessageBox.Show(
                    $"A new version of IO Traces Core is available!\n\n" +
                    $"Current Version: {currentVersion}\n" +
                    $"New Version: {newVersion}\n\n" +
                    "Would you like to download and install it now?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    await DownloadAndInstallUpdateAsync(updateInfo);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
                if (!silent)
                {
                    MessageBox.Show(
                        $"Failed to check for updates.\n\nError: {ex.Message}",
                        "Update Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                _isCheckingForUpdates = false;
            }
        }

        private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            if (_updateManager == null)
                return;

            LoadingDialog? loadingDialog = null;

            try
            {
                // Show progress dialog
                loadingDialog = new LoadingDialog("Downloading update... This may take a few moments.");
                loadingDialog.Show();
                Application.DoEvents();

                Debug.WriteLine("Downloading update...");

                // Download the update
                await _updateManager.DownloadUpdatesAsync(updateInfo);

                Debug.WriteLine("Update downloaded successfully");

                loadingDialog.Close();
                loadingDialog.Dispose();
                loadingDialog = null;

                // Ask user to restart
                var result = MessageBox.Show(
                    "Update downloaded successfully!\n\n" +
                    "The application needs to restart to complete the installation.\n\n" +
                    "Important: Any active tracing will be stopped.\n\n" +
                    "Restart now?",
                    "Update Ready",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    Debug.WriteLine("Applying update and restarting...");

                    // Apply update and restart
                    _updateManager.ApplyUpdatesAndRestart(updateInfo);

                    // The app will exit and restart here
                }
                else
                {
                    Debug.WriteLine("Update deferred by user");
                    MessageBox.Show(
                        "The update will be applied the next time you restart IO Traces Core.",
                        "Update Pending",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading/installing update: {ex.Message}");
                MessageBox.Show(
                    $"Failed to download or install update.\n\nError: {ex.Message}\n\n" +
                    "Please try again later or download the latest version manually from GitHub.",
                    "Update Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                loadingDialog?.Close();
                loadingDialog?.Dispose();
            }
        }

        /// <summary>
        /// Check for updates silently on application startup (with delay)
        /// </summary>
        public async Task CheckForUpdatesOnStartupAsync()
        {
            // Wait a bit after startup before checking (don't slow down app launch)
            await Task.Delay(10000); // 10 seconds
            await CheckForUpdatesAsync(silent: true);
        }

        /// <summary>
        /// Get current application version
        /// </summary>
        public string GetCurrentVersion()
        {
            if (_updateManager == null)
                return "Unknown";

            return _updateManager.CurrentVersion?.ToString() ?? "Unknown";
        }
    }
}
