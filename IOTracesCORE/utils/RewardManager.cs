using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE.utils
{
    internal class RewardManager
    {
        private static RewardManager? _instance;
        private static readonly object _lock = new object();

        private const string REWARD_CODE = "CKXDRTBX";
        private const string MARKER_FILENAME = "reward_unlocked.marker";

        private bool _isUnlocked;

        private RewardManager()
        {
            _isUnlocked = CheckMarkerFileExists();
        }

        public static RewardManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new RewardManager();
                    }
                }
                return _instance;
            }
        }

        private static string MarkerFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IOTracer",
                MARKER_FILENAME
            );

        public bool IsUnlocked => _isUnlocked;

        public string RewardCode => REWARD_CODE;

        private bool CheckMarkerFileExists()
        {
            try
            {
                return File.Exists(MarkerFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking reward marker: {ex.Message}");
                return false;
            }
        }

        public void UnlockReward()
        {
            if (_isUnlocked)
                return;

            try
            {
                string? directory = Path.GetDirectoryName(MarkerFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(MarkerFilePath, $"Unlocked: {DateTime.UtcNow:O}");
                _isUnlocked = true;
                Debug.WriteLine("Reward unlocked and marker file created.");

                OnRewardUnlocked?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating reward marker: {ex.Message}");
            }
        }

        public event Action? OnRewardUnlocked;

        public string GetStatusMessage()
        {
            if (_isUnlocked)
            {
                return $"Submission Unlocked!\n\nYour code: {REWARD_CODE}\n\nThank you for contributing to the research!";
            }
            else
            {
                return "Submission Locked\n\nUpload at least 1 trace file to unlock your reward code.\n\nKeep the tracer running and connected to the internet.";
            }
        }
    }
}
