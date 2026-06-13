using IOTracesCORE.cloudstorage;
using IOTracesCORE.utils;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IOTracesCORE
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        private static CancellationTokenSource cancellationTokenSource;
        private static NotifyIcon trayIcon;
        private static bool isElevated;
        private static ToolStripMenuItem rewardMenuItem;

        [STAThread]
        static void Main(string[] args)
        {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ApplicationExit += new EventHandler(OnApplicationExit);
            SystemEvents.SessionEnding += OnSessionEnding;

            cancellationTokenSource = new CancellationTokenSource();

            var assembly = Assembly.GetExecutingAssembly();
            var iconStream = assembly.GetManifestResourceStream("IOTracesCORE.Opera_Glasses_icon-icons.com_54155.ico");
            var icon = iconStream != null ? new Icon(iconStream) : SystemIcons.Application;

            ConfigClasses.LoadTracemetaConfiguration();

            // Subscribe to reward unlock event to update UI
            RewardManager.Instance.OnRewardUnlocked += OnRewardUnlocked;

            // Subscribe to connection state changes to show balloon tips
            ObjectStorageHandler.OnConnectionStateChanged += OnConnectionStateChanged;

            string currentVersion = VersionManager.Instance.GetCurrentVersion();
            trayIcon = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = $"IO Traces Core v{currentVersion} - Running"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add($"IO Traces Core v{currentVersion}", null, null).Enabled = false;
            contextMenu.Items.Add("-");

            // Add Reward button
            rewardMenuItem = new ToolStripMenuItem(GetRewardButtonText(), null, OnRewardClicked);
            rewardMenuItem.Font = new Font(rewardMenuItem.Font, FontStyle.Regular);
            UpdateRewardButtonAppearance();
            contextMenu.Items.Add(rewardMenuItem);
            contextMenu.Items.Add("-");

            contextMenu.Items.Add("Show Status", null, (s, e) => ShowStatus());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, OnExitClicked);

            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left)
                    return;

                ShowStatus();
            };


            var form = TracerConfigForm.Run(cancellationTokenSource.Token);
            form.FormClosed += (_, __) => { cancellationTokenSource?.Cancel(); };
            Application.Run(form);
        }

        private static void ShowStatus()
        {
            TimeSpan Total_current_session = WriterManager.active_session;
            TimeSpan Total_trace_duration = WriterManager.trace_duration;
            string snapStatus = WriterManager.fs_snapshot_complete ? "Completed" : "In progress...";
            string connStatus = ObjectStorageHandler.IsConnected ? "Connected" : ObjectStorageHandler.ConnectionStatus;

            MessageBox.Show(
                $"Computer ID: {PathHasher.deviceId}\n" +
                $"Logs Created / Uploaded: {WriterManager.amount_compressed_file} / {ObjectStorageHandler.UploadedFiles}\n" +
                $"File events collected: {DisplayHelper.ToPowerOfTen(WriterManager.file_event_counter)}\n" +
                $"Filesystem snapshot: {WriterManager.fs_snapshot_file_count:N0} files ({snapStatus})\n\n" +
                $"Active session elapsed (HH:MM:SS): {(long)Total_current_session.TotalHours:00}:{Total_current_session.Minutes:00}:{Total_current_session.Seconds:00}\n" +
                $"Trace Duration: {(long)Total_trace_duration.TotalDays:00} Days {Total_trace_duration.Hours:00} Hours\n" +
                $"Internet / Upload: {connStatus}",
                "Status",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string GetRewardButtonText()
        {
            return RewardManager.Instance.IsUnlocked ? "View Reward 🎁" : "Reward 🔒";
        }

        private static void UpdateRewardButtonAppearance()
        {
            if (rewardMenuItem == null) return;

            rewardMenuItem.Text = GetRewardButtonText();

            //if (RewardManager.Instance.IsUnlocked)
            //{
            //    rewardMenuItem.BackColor = Color.LightGreen;
            //    rewardMenuItem.ForeColor = Color.DarkGreen;
            //}
            //else
            //{
            //    rewardMenuItem.BackColor = Color.LightGray;
            //    rewardMenuItem.ForeColor = Color.DarkGray;
            //}
        }

        private static void OnRewardUnlocked()
        {
            // Update UI on the main thread
            if (trayIcon?.ContextMenuStrip != null && trayIcon.ContextMenuStrip.InvokeRequired)
            {
                trayIcon.ContextMenuStrip.Invoke(new Action(UpdateRewardButtonAppearance));
            }
            else
            {
                UpdateRewardButtonAppearance();
            }

            // Show notification
            trayIcon?.ShowBalloonTip(
                3000,
                "🎉 Reward Unlocked!",
                "You've earned a reward code! Click the Reward button to view it.",
                ToolTipIcon.Info
            );
        }

        private static void OnRewardClicked(object? sender, EventArgs e)
        {
            var reward = RewardManager.Instance;

            if (reward.IsUnlocked)
            {
                var result = MessageBox.Show(
                    $"Your Prolific Submission code is:\n\n" +
                    $"    {reward.RewardCode}\n\n" +
                    $"Thank you for contributing to the research!\n\n" +
                    $"Click OK to copy the code to clipboard.",
                    "Submission Unlocked",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information
                );

                if (result == DialogResult.OK)
                {
                    try
                    {
                        Clipboard.SetText(reward.RewardCode);
                        MessageBox.Show(
                            "Code copied to clipboard!",
                            "Success",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
                    }
                }
            }
            else
            {
                MessageBox.Show(
                    "Submission Locked\n\n" +
                    $"Upload progress: {ObjectStorageHandler.UploadedFiles} / 1 files\n\n" +
                    "Upload at least 1 trace file to unlock your reward code.\n\n" +
                    "Keep the tracer running and connected to the internet.",
                    "Reward Status",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
        }

        private static void OnExitClicked(object sender, EventArgs e)
        {
            if (sender is ToolStripMenuItem menuItem)
            {
                menuItem.Enabled = false;
            }

            if (trayIcon != null)
            {
                trayIcon.Text = "IO Traces Core - Shutting down...";
            }

            cancellationTokenSource?.Cancel();

            var exitTimer = new System.Windows.Forms.Timer
            {
                Interval = 15000
            };

            exitTimer.Tick += (s, args) =>
            {
                exitTimer.Stop();
                exitTimer.Dispose();
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                }
                Application.Exit();
            };

            exitTimer.Start();
        }

        private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            Thread.Sleep(2000);
        }

        private static void OnConnectionStateChanged(string status)
        {
            if (trayIcon == null) return;

            // Marshal to UI thread if needed
            var strip = trayIcon.ContextMenuStrip;
            if (strip != null && strip.InvokeRequired)
            {
                strip.BeginInvoke(new Action(() => OnConnectionStateChanged(status)));
                return;
            }

            bool connected = ObjectStorageHandler.IsConnected;
            if (connected)
            {
                trayIcon.ShowBalloonTip(
                    4000,
                    "✅ Connection Restored",
                    "Internet connection re-established. Event collection and upload are resuming.",
                    ToolTipIcon.Info);
            }
            else
            {
                trayIcon.ShowBalloonTip(
                    4000,
                    "⚠️ Connection Lost",
                    $"Upload error detected. Tracing paused. {status}",
                    ToolTipIcon.Warning);
            }
        }

        private static void OnApplicationExit(object? sender, EventArgs e)
        {
            Debug.WriteLine("Application exiting...");
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }
    }
}