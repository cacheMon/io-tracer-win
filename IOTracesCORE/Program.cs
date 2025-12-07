using IOTracesCORE.cloudstorage;
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
using Velopack;

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

            var currentVersion = "vRelease";
            trayIcon = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = $"IO Traces Core v{currentVersion} - Running"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add($"IO Traces Core v{currentVersion}", null, null).Enabled = false;
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Show Status", null, (s, e) =>
            {
                MessageBox.Show(
                    $"Version: {currentVersion}\n" +
                    $"Logs Created: {WriterManager.amount_compressed_file}\n" +
                    $"Logs Uploaded: {ObjectStorageHandler.UploadedFiles}",
                    "Status",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, OnExitClicked);

            trayIcon.ContextMenuStrip = contextMenu;

            trayIcon.DoubleClick += (s, e) =>
            {
                MessageBox.Show(
                    $"IO Traces Core v{currentVersion} is running!\n\n" +
                    "Right-click the tray icon for more options.",
                    "Info",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };

            var form = TracerConfigForm.Run(cancellationTokenSource.Token);
            form.FormClosed += (_, __) => { cancellationTokenSource?.Cancel(); };
            Application.Run(form);
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