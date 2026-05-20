using IOTracesCORE.cloudstorage;
using IOTracesCORE.utils;
using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using static IOTracesCORE.utils.ConfigClasses;

namespace IOTracesCORE
{
    public class TracerConfigForm : Form
    {
        private CheckBox chkAnonymous;
        private CheckBox chkEnableUpload;
        private CheckBox chkAutoStart;
        private CheckBox chkDevMode;
        private Label lblAnonymous;
        private Label lblStatus;
        private Button btnRunTracer;
        private TextBox txtInfo;

        private bool isConnectionSafe = false;
        private readonly CancellationToken cancellationToken;

        private const string AutoStartRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "IOTracesCORE";

        public TracerConfigForm(CancellationToken token)
        {
            cancellationToken = token;
            InitializeComponent();

            LoadSavedConfiguration();
            chkEnableUpload.Checked = true;
            ChkEnableUpload_CheckedChanged(this, EventArgs.Empty);

            if (!File.Exists(AppConfigPath))
            {
                chkEnableUpload.Checked = true;
                ChkEnableUpload_CheckedChanged(this, EventArgs.Empty);
            }

            chkAutoStart.Checked = IsAutoStartEnabled();
        }


        private void InitializeComponent()
        {
            Text = "IO-Tracer Configuration";
            ClientSize = new Size(520, 220);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                RowCount = 6,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            chkAnonymous = new CheckBox
            {
                Text = "Anonymous mode",
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 0)
            };
            root.Controls.Add(chkAnonymous);

            chkAutoStart = new CheckBox
            {
                Text = "Run IO-Tracer at Windows startup",
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };
            chkAutoStart.CheckedChanged += ChkAutoStart_CheckedChanged;
            root.Controls.Add(chkAutoStart);

            chkDevMode = new CheckBox
            {
                Text = "Dev Mode (logs stored locally only)",
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };
            chkDevMode.CheckedChanged += ChkDevMode_CheckedChanged;
            root.Controls.Add(chkDevMode);

            lblStatus = new Label
            {
                Text = "Initializing upload…",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(0, 12, 0, 0)
            };
            root.Controls.Add(lblStatus);

            var bottomLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2
            };
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            txtInfo = new TextBox
            {
                Text = $"Device: {PathHasher.deviceId}",
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ForeColor = Color.Gray,
                BackColor = BackColor,
                Dock = DockStyle.Fill,
                TabStop = false
            };

            btnRunTracer = new Button
            {
                Text = "Start Tracing",
                Width = 160,
                Height = 38,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRunTracer.Click += BtnRunTracer_Click;

            bottomLayout.Controls.Add(txtInfo, 0, 0);
            bottomLayout.Controls.Add(btnRunTracer, 1, 0);
            root.Controls.Add(bottomLayout);

            chkEnableUpload = new CheckBox
            {
                Checked = true,
                Visible = false,
                Enabled = false
            };
            chkEnableUpload.CheckedChanged += ChkEnableUpload_CheckedChanged;
            Controls.Add(chkEnableUpload);
        }



        private async void ChkEnableUpload_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkEnableUpload.Checked)
            {
                isConnectionSafe = false;
                lblStatus.Text = "Uploads disabled. Traces will be stored locally.";
                lblStatus.ForeColor = Color.Orange;
                btnRunTracer.Text = "Start Tracing";
                SaveConfiguration();
                return;
            }

            lblStatus.Text = "Testing connection…";
            lblStatus.ForeColor = Color.Blue;
            chkEnableUpload.Enabled = false;
            btnRunTracer.Enabled = false;

            bool ok = await TestUploadConnection();

            if (ok)
            {
                isConnectionSafe = true;
                lblStatus.Text = "Connection OK. Traces will upload automatically.";
                lblStatus.ForeColor = Color.Green;
                btnRunTracer.Text = "Start Tracing";
            }
            else
            {
                isConnectionSafe = false;
                lblStatus.Text = "Upload unavailable. Connection error.";
                lblStatus.ForeColor = Color.Red;
                btnRunTracer.Text = "Retry Connection";
            }

            chkEnableUpload.Enabled = true;
            btnRunTracer.Enabled = true;
            SaveConfiguration();
        }

        private void ChkAutoStart_CheckedChanged(object? sender, EventArgs e)
        {
            try
            {
                SetAutoStart(chkAutoStart.Checked);
                SaveConfiguration();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Set autostart failed: " + ex.Message);
                MessageBox.Show(
                    "Failed to update auto-start setting:\n" + ex.Message,
                    "Auto-start",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                chkAutoStart.CheckedChanged -= ChkAutoStart_CheckedChanged;
                chkAutoStart.Checked = !chkAutoStart.Checked;
                chkAutoStart.CheckedChanged += ChkAutoStart_CheckedChanged;
            }
        }

        private void ChkDevMode_CheckedChanged(object? sender, EventArgs e)
        {
            if (chkDevMode.Checked)
            {
                var result = MessageBox.Show(
                    "Dev Mode disables cloud uploads and stores traces locally only.\n\n" +
                    "This is intended for development and testing purposes.\n\n" +
                    "Are you sure you want to enable Dev Mode?",
                    "Enable Dev Mode",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.No)
                {
                    chkDevMode.CheckedChanged -= ChkDevMode_CheckedChanged;
                    chkDevMode.Checked = false;
                    chkDevMode.CheckedChanged += ChkDevMode_CheckedChanged;
                    return;
                }

                chkEnableUpload.Checked = false;
                chkEnableUpload.Enabled = false;
            }
            else
            {
                chkEnableUpload.Enabled = true;
            }
            SaveConfiguration();
        }

        private async Task<bool> TestUploadConnection()
        {
            try
            {
                using (HttpClient http = new HttpClient())
                {
                    string testUrl = "https://io-tracer-worker.1a1a11a.workers.dev/connection-test.txt";
                    var response = await http.GetAsync(testUrl);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Connection test failed: " + ex.Message);
                return false;
            }
        }

        private async void BtnRunTracer_Click(object sender, EventArgs e)
        {
            if (btnRunTracer.Text == "Retry Connection")
            {
                btnRunTracer.Enabled = false;
                lblStatus.Text = "Testing connection…";
                lblStatus.ForeColor = Color.Blue;

                bool ok = await TestUploadConnection();
                btnRunTracer.Enabled = true;

                if (ok)
                {
                    isConnectionSafe = true;
                    lblStatus.Text = "Connection OK. Traces will upload automatically.";
                    lblStatus.ForeColor = Color.Green;
                    btnRunTracer.Text = "Start Tracing";
                    return;
                }
                else
                {
                    isConnectionSafe = false;
                    lblStatus.Text = "Upload unavailable. Connection error.";
                    lblStatus.ForeColor = Color.Red;
                    btnRunTracer.Text = "Retry Connection";

                    MessageBox.Show(
                        "Connection failed. Please check your internet connection and try again.",
                        "Upload unavailable",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );

                    return;
                }
            }

            SaveConfiguration();

            if (!IsAdministrator())
            {
                var result = MessageBox.Show(
                    "Starting tracing needs administrator rights.\n" +
                    "Restart IO-Tracer as administrator now?",
                    "Administrator required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.No)
                    return;

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "",
                        UseShellExecute = true,
                        Verb = "runas"
                    };

                    Process.Start(psi);
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Failed to restart as administrator:\n" + ex.Message,
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }

                return;
            }

            bool wantUpload = chkEnableUpload.Checked && !chkDevMode.Checked;
            bool finalUpload = wantUpload && isConnectionSafe;

            if (wantUpload && !isConnectionSafe)
            {
                MessageBox.Show(
                    "Automatic upload could not be verified. Please retry the connection first.",
                    "Upload unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            string outputPath = Path.Combine(Path.GetTempPath(), "IOTraces");
            ObjectStorageHandler obj = new();
            RunTracer(outputPath, chkAnonymous.Checked, finalUpload, obj, chkDevMode.Checked);
        }

        private void RunTracer(string outputPath, bool anonymous, bool upload, ObjectStorageHandler obj, bool devMode)
        {
            EnsureOutputDirectoryExists(outputPath);

            this.Hide();
            this.ShowInTaskbar = false;

            Task.Run(() =>
            {
                try
                {
                    Tracer trc = new Tracer(anonymous, upload, obj, outputPath, devMode);
                    trc.Trace(cancellationToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { Thread.Sleep(1000); }
            });
        }

        private void SaveConfiguration()
        {
            try
            {
                var cfg = new PersistedConfig
                {
                    Anonymous = chkAnonymous.Checked,
                    UploadEnabled = chkEnableUpload.Checked,
                    DevMode = chkDevMode.Checked,
                };

                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(AppConfigPath)!);
                File.WriteAllBytes(AppConfigPath, encrypted);
            }
            catch (Exception ex) { Debug.WriteLine("Save config failed: " + ex.Message); }
        }

        private void LoadSavedConfiguration()
        {
            try
            {
                Debug.Write($"Receiving config from: {AppConfigPath}");
                if (!File.Exists(AppConfigPath)) return;

                byte[] encrypted = File.ReadAllBytes(AppConfigPath);
                byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(decrypted);
                var cfg = JsonSerializer.Deserialize<PersistedConfig>(json);

                if (cfg == null) return;

                chkAnonymous.Checked = cfg.Anonymous;
                chkDevMode.Checked = cfg.DevMode;
                chkEnableUpload.Checked = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Load config failed: " + ex.Message);
                chkEnableUpload.Checked = false;
                lblStatus.Text = "Uploads disabled. Traces will be stored locally.";
                lblStatus.ForeColor = Color.Orange;
            }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string taskName = "IOTracesCORE_AutoStart";
                string exePath = Application.ExecutablePath;

                if (enable)
                {
                    string arguments = $@"/Create /TN ""{taskName}"" /TR ""\""{exePath}\"""" /SC ONLOGON /RL HIGHEST /F";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        process?.WaitForExit();
                        if (process?.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            throw new InvalidOperationException($"Failed to create scheduled task: {error}");
                        }
                    }
                }
                else
                {
                    string arguments = $@"/Delete /TN ""{taskName}"" /F";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var process = Process.Start(psi))
                    {
                        process?.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to manage auto-start: {ex.Message}", ex);
            }
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                string taskName = "IOTracesCORE_AutoStart";

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $@"/Query /TN ""{taskName}""",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static TracerConfigForm Run(CancellationToken token = default)
            => new TracerConfigForm(token);

        private static void EnsureOutputDirectoryExists(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    return;

                if (File.Exists(path))
                {
                    Debug.WriteLine($"Warning: File exists at directory path '{path}', renaming...");
                    string backupPath = path + ".backup_" + DateTime.UtcNow.Ticks;
                    File.Move(path, backupPath);
                    Debug.WriteLine($"Moved conflicting file to: {backupPath}");
                }

                Directory.CreateDirectory(path);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"IO error creating directory '{path}': {ex.Message}");
                throw new InvalidOperationException($"Cannot create directory '{path}'. A file may exist with this name.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Access denied creating directory '{path}': {ex.Message}");
                throw new InvalidOperationException($"Access denied when creating directory '{path}'. Please check permissions.", ex);
            }
        }
    }
}
