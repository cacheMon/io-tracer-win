using Microsoft.Win32;
using System;
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

        [STAThread]
        static void Main(string[] args)
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                if (!isElevated)
                {
                    MessageBox.Show("This application must be run as Administrator.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ApplicationExit += OnApplicationExit;
            SystemEvents.SessionEnding += OnSessionEnding;

            cancellationTokenSource = new CancellationTokenSource();

            var assembly = Assembly.GetExecutingAssembly();
            var iconStream = assembly.GetManifestResourceStream("IOTracesCORE.Opera_Glasses_icon-icons.com_54155.ico");
            var icon = iconStream != null ? new Icon(iconStream) : SystemIcons.Application;
            var trayIcon = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = "IO Traces Core - Running"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show Status", null, (s, e) =>
            {
                MessageBox.Show($"{WriterManager.amount_compressed_file} batches compressed!", "Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("Exit", null, OnExitClicked);

            trayIcon.ContextMenuStrip = contextMenu;

            trayIcon.DoubleClick += (s, e) =>
            {
                MessageBox.Show("IO Traces Core is running!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            TracerConfigForm.Run(cancellationTokenSource.Token);

            Application.Run();

            trayIcon.Dispose();
            cancellationTokenSource?.Dispose();
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
            MessageBox.Show($"System shutdown/logoff detected: {e.Reason}", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);

            cancellationTokenSource?.Cancel();

            Thread.Sleep(2000);
        }

        private static void OnApplicationExit(object sender, EventArgs e)
        {
            Console.WriteLine("Application exiting...");
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        }
    }
}