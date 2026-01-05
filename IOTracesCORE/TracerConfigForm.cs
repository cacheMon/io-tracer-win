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

            if (!File.Exists(AppConfigPath))
            {
                chkEnableUpload.Checked = true;
                ChkEnableUpload_CheckedChanged(this, EventArgs.Empty);
            }

            chkAutoStart.Checked = IsAutoStartEnabled();
        }


        private void InitializeComponent()
        {
            this.Text = "IO-Tracer Configuration";
            this.Width = 520;
            this.Height = 280;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            txtInfo = new TextBox
            {
                Text = $"Device: {PathHasher.deviceId}",
                Location = new Point(345, 65),
                Width = 450,
                ForeColor = Color.Gray,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = this.BackColor,
                TabStop = false
            };

            lblOutputPath = new Label
            {
                Text = "Output Path:",
                Location = new Point(20, 25),
                Width = 100
            };

            txtOutputPath = new TextBox
            {
                Location = new Point(130, 22),
                Width = 280,
                Text = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "WorkloadTrace"
                )
            };

            btnBrowseOutput = new Button
            {
                Text = "Browse…",
                Location = new Point(420, 21),
                Width = 70,
                Height = 25
            };
            btnBrowseOutput.Click += BtnBrowseOutput_Click;

            lblAnonymous = new Label
            {
                Text = "Anonymous:",
                Location = new Point(20, 65),
                Width = 100
            };

            chkAnonymous = new CheckBox
            {
                Location = new Point(130, 63),
                Checked = false,
                Width = 20
            };

            chkEnableUpload = new CheckBox
            {
                Text = "Enable auto upload",
                Location = new Point(20, 95),
                Width = 250,
                Checked = false
            };
            chkEnableUpload.CheckedChanged += ChkEnableUpload_CheckedChanged;

            chkAutoStart = new CheckBox
            {
                Text = "Run IO-Tracer at Windows startup",
                Location = new Point(20, 125),
                Width = 300,
                Checked = false
            };
            chkAutoStart.CheckedChanged += ChkAutoStart_CheckedChanged;


            lblStatus = new Label
            {
                Text = "Upload disabled, Traces will be stored locally.",
                Location = new Point(20, 155),
                Width = 450,
                ForeColor = Color.Orange
            };

            btnRunTracer = new Button
            {
                Text = "Start Tracing",
                Location = new Point(20, 180),
                Width = 200,
                Height = 40,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRunTracer.Click += BtnRunTracer_Click;

            this.Controls.Add(lblOutputPath);
            this.Controls.Add(txtOutputPath);
            this.Controls.Add(btnBrowseOutput);
            this.Controls.Add(lblAnonymous);
            this.Controls.Add(chkAnonymous);
            this.Controls.Add(chkEnableUpload);
            this.Controls.Add(chkAutoStart);
            this.Controls.Add(lblStatus);
            this.Controls.Add(btnRunTracer);
            this.Controls.Add(txtInfo);
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
                chkEnableUpload.Checked = cfg.UploadEnabled;
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
