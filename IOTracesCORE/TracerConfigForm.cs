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
        private CheckBox chkAnonymous = null!;
        private CheckBox chkEnableUpload = null!;
        private CheckBox chkAutoStart = null!;
        private CheckBox chkDevMode = null!;
        private CheckBox chkLowOverhead = null!;
        private Label lblStatus = null!;
        private Button btnRunTracer = null!;
        private TextBox txtInfo = null!;

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

            bool firstRun = !File.Exists(AppConfigPath);
            if (firstRun)
            {
                chkEnableUpload.Checked = true;
                ChkEnableUpload_CheckedChanged(this, EventArgs.Empty);
            }

            // Start at Windows logon is ON by default for a fresh install: checking the box
            // here fires ChkAutoStart_CheckedChanged, which creates the elevated ONLOGON
            // scheduled task (schtasks /SC ONLOGON /RL HIGHEST). On later runs it just
            // reflects whether that task still exists, so a user who unticks it stays opted
            // out. Note: the task launches the exe from its current path, so a portable exe
            // moved/deleted after first run will leave a stale task.
            chkAutoStart.Checked = IsAutoStartEnabled() || firstRun;
        }


        private void InitializeComponent()
        {
            Text = "IO-Tracer Configuration";
            ClientSize = new Size(520, 250);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                RowCount = 5,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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

            chkLowOverhead = new CheckBox
            {
                Text = "Lightweight logging for low-resource machines (skips op_end + memory events)",
                AutoSize = false,
                Width = ClientSize.Width - root.Padding.Horizontal,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0, 6, 0, 0)
            };
            // Sticky auto-lightweight: if a recent run on THIS machine truncated (heavy
            // kernel-side ETW loss), default lightweight ON and say why, so a chronically
            // truncating box self-heals. Set before wiring CheckedChanged so this initial
            // default doesn't fire the handler. LoadConfiguration also ORs the marker in.
            // Unticking governs only THIS run's mode (plumbed via chkLowOverhead.Checked); it no
            // longer deletes the marker, so a one-off "run full this time" can't permanently defeat
            // the self-heal on a box that keeps truncating. The marker self-expires after 30 days
            // (TruncationState.ExpiryDays) and re-arms automatically if truncation recurs.
            if (utils.TruncationState.WasRecentlyTruncated(out _))
            {
                chkLowOverhead.Checked = true;
                chkLowOverhead.Text += "  — auto-on: a recent run lost events";
            }
            else if (utils.MachineProfile.RecommendLightweight())
            {
                // Small machine: default to lightweight (the reduced modern fs provider, ~3x fewer
                // kernel events). A saved config below still wins; the user can untick.
                chkLowOverhead.Checked = true;
                chkLowOverhead.Text += "  — auto-on: small machine (reduced fs events)";
            }
            chkLowOverhead.CheckedChanged += (s, e) =>
            {
                SaveConfiguration();
            };
            // The base text alone is long, and the auto-on suffixes above make it longer still —
            // AutoSize would just overflow past this fixed, non-resizable dialog's edge on
            // whichever machines hit one of those branches. Measure the actual final text (base
            // or with suffix) at the control's fixed width and size the control to fit, instead
            // of guessing a height that would clip on the longer variant.
            const int checkboxGlyphWidth = 20;
            Size wrappedTextSize = TextRenderer.MeasureText(
                chkLowOverhead.Text,
                chkLowOverhead.Font,
                new Size(chkLowOverhead.Width - checkboxGlyphWidth, int.MaxValue),
                TextFormatFlags.WordBreak);
            chkLowOverhead.Height = wrappedTextSize.Height + 8;
            root.Controls.Add(chkLowOverhead);

            lblStatus = new Label
            {
                Text = "Initializing upload…",
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(0, 12, 0, 0)
            };
            root.Controls.Add(lblStatus);

            // Docked to the Form itself (not nested as a root row) and given a reserved
            // height, so this row can never be squeezed out of the visible client area by
            // whatever grows above it in root (extra checkbox suffix text, a larger system
            // font/accessibility text scale, etc.) — root (Dock.Fill) yields to it instead.
            var bottomLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(16, 0, 16, 16),
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
            // Added after root: WinForms docking is Z-order sensitive (later-added controls
            // are processed first for space carve-out), so this reserves bottomLayout's slice
            // from the bottom and leaves root (Dock.Fill) whatever remains above it.
            Controls.Add(bottomLayout);

            chkEnableUpload = new CheckBox
            {
                Checked = true,
                Visible = false,
                Enabled = false
            };
            chkEnableUpload.CheckedChanged += ChkEnableUpload_CheckedChanged;
            Controls.Add(chkEnableUpload);
        }



        private async void ChkEnableUpload_CheckedChanged(object? sender, EventArgs e)
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
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    string baseUrl = "https://io-tracer-worker.1a1a11a.workers.dev";

                    // Try HEAD request first (lightweight)
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Head, baseUrl);
                        var response = await http.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                            return true;
                    }
                    catch { }

                    // Fallback: try GET to presigned-url endpoint (always exists)
                    var testUrl = $"{baseUrl}/presigned-url/connection-test";
                    var getResponse = await http.GetAsync(testUrl);
                    return getResponse.StatusCode != System.Net.HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Connection test failed: " + ex.Message);
                return false;
            }
        }

        private async void BtnRunTracer_Click(object? sender, EventArgs e)
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
            RunTracer(outputPath, chkAnonymous.Checked, finalUpload, obj, chkDevMode.Checked, chkLowOverhead.Checked);
        }

        private void RunTracer(string outputPath, bool anonymous, bool upload, ObjectStorageHandler obj, bool devMode, bool lowOverhead)
        {
            EnsureOutputDirectoryExists(outputPath);

            this.Hide();
            this.ShowInTaskbar = false;

            Task.Run(() =>
            {
                try
                {
                    Tracer trc = new Tracer(anonymous, upload, obj, outputPath, devMode, lowOverhead);
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
                    LowOverheadLogging = chkLowOverhead.Checked,
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
                // OR the truncation marker in so a machine that recently truncated defaults to
                // lightweight even if the saved config had it off (see TruncationState).
                chkLowOverhead.Checked = cfg.LowOverheadLogging
                    || utils.TruncationState.WasRecentlyTruncated(out _);
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

        // Copies the running exe into a stable per-user location (%LOCALAPPDATA%\IOTracer)
        // and returns that path, so the logon auto-start task keeps working even if the
        // original download (e.g. in Downloads) is later moved or deleted. The current
        // session keeps running from wherever it is; the stable copy is what the task
        // launches at the next logon. Falls back to the current exe path if the copy fails,
        // so auto-start still works (just less robust) rather than failing outright.
        private static string EnsureStableInstall()
        {
            string current = Application.ExecutablePath;
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IOTracer");
                string stable = Path.Combine(dir, "IOTracer.exe");
                if (string.Equals(current, stable, StringComparison.OrdinalIgnoreCase))
                    return stable; // already running from the installed location
                Directory.CreateDirectory(dir);
                File.Copy(current, stable, overwrite: true);
                return stable;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Stable-install copy failed, using current exe path: {ex.Message}");
                return current;
            }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string taskName = "IOTracesCORE_AutoStart";

                if (enable)
                {
                    // Point the logon task at a STABLE copy of the exe, not wherever it was
                    // launched from (often Downloads, which the user may later move/delete and
                    // leave the task pointing at nothing).
                    string exePath = EnsureStableInstall();
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
                        if (process == null)
                        {
                            throw new InvalidOperationException("Failed to start schtasks process.");
                        }
                        if (process.ExitCode != 0)
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
