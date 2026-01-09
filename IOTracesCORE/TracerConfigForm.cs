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
        private TextBox txtOutputPath;
        private Button btnBrowseOutput;
        private CheckBox chkAnonymous;
        private CheckBox chkEnableUpload;
        private CheckBox chkAutoStart;
        private Label lblOutputPath;
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
            ClientSize = new Size(520, 200);
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

            var outputLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                AutoSize = true
            };
            outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outputLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            lblOutputPath = new Label
            {
                Text = "Output path",
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };

            txtOutputPath = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "WorkloadTrace")
            };

            btnBrowseOutput = new Button
            {
                Text = "Browse…",
                Dock = DockStyle.Fill
            };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;

            outputLayout.Controls.Add(lblOutputPath, 0, 0);
            outputLayout.Controls.Add(txtOutputPath, 1, 0);
            outputLayout.Controls.Add(btnBrowseOutput, 2, 0);
            root.Controls.Add(outputLayout);

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


        private void BtnBrowseOutput_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select output folder for trace files";
                dialog.SelectedPath = txtOutputPath.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtOutputPath.Text = dialog.SelectedPath;
                    SaveConfiguration();
                }
            }
        }

        private async void ChkEnableUpload_CheckedChanged(object sender, EventArgs e)
        {
            if (!chkEnableUpload.Checked)
            {
                isConnectionSafe = false;
                lblStatus.Text = "Uploads disabled. Traces will be stored locally.";
                lblStatus.ForeColor = Color.Orange;
                SaveConfiguration();
                return;
            }

            lblStatus.Text = "Testing connection…";
            lblStatus.ForeColor = Color.Blue;
            chkEnableUpload.Enabled = false;

            bool ok = await TestUploadConnection();

            if (ok)
            {
                isConnectionSafe = true;
                lblStatus.Text = "Connection OK. Traces will upload automatically.";
                lblStatus.ForeColor = Color.Green;
            }
            else
            {
                isConnectionSafe = false;
                chkEnableUpload.Checked = false;
                lblStatus.Text = "Upload unavailable. Traces will be stored locally.";
                lblStatus.ForeColor = Color.Red;
            }

            chkEnableUpload.Enabled = true;
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

        private void BtnRunTracer_Click(object sender, EventArgs e)
        {
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

            bool wantUpload = chkEnableUpload.Checked;
            bool finalUpload = wantUpload && isConnectionSafe;

            if (wantUpload && !isConnectionSafe)
            {
                var res = MessageBox.Show(
                    "Automatic upload could not be verified. Continue with LOCAL-ONLY logging?",
                    "Upload unavailable",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );
                if (res == DialogResult.No) return;
                finalUpload = false;
            }

            ObjectStorageHandler obj = new();
            RunTracer(txtOutputPath.Text, chkAnonymous.Checked, finalUpload, obj);
        }

        private void RunTracer(string outputPath, bool anonymous, bool upload, ObjectStorageHandler obj)
        {
            EnsureOutputDirectoryExists(outputPath);

            this.Hide();
            this.ShowInTaskbar = false;

            Task.Run(() =>
            {
                try
                {
                    Tracer trc = new Tracer(anonymous, upload, obj, outputPath);
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
                    OutputPath = txtOutputPath.Text,
                    Anonymous = chkAnonymous.Checked,
                    UploadEnabled = chkEnableUpload.Checked,
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

                txtOutputPath.Text = string.IsNullOrWhiteSpace(cfg.OutputPath)
                    ? txtOutputPath.Text
                    : cfg.OutputPath;

                chkAnonymous.Checked = cfg.Anonymous;
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
