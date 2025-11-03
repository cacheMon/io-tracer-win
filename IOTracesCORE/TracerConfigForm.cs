using IOTracesCORE.cloudstorage;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IOTracesCORE
{
    public class TracerConfigForm : Form
    {
        private TextBox txtOutputPath;
        private Button btnBrowseOutput;
        private CheckBox chkAnonymous;
        private CheckBox chkEnableUpload;
        private Label lblOutputPath;
        private Label lblAnonymous;
        private Label lblStatus;
        private Button btnRunTracer;

        private bool isConnectionSafe = false;
        private readonly CancellationToken cancellationToken;

        public TracerConfigForm(CancellationToken token)
        {
            cancellationToken = token;
            InitializeComponent();
            LoadSavedConfiguration();
        }

        private void InitializeComponent()
        {
            this.Text = "IO-Tracer Configuration";
            this.Width = 520;
            this.Height = 280;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

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
                Checked = true,
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

            lblStatus = new Label
            {
                Text = "Upload disabled, Traces will be stored locally.",
                Location = new Point(20, 125),
                Width = 450,
                ForeColor = Color.Orange
            };

            btnRunTracer = new Button
            {
                Text = "Start Tracing",
                Location = new Point(20, 170),
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
            this.Controls.Add(lblStatus);
            this.Controls.Add(btnRunTracer);
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

            SaveConfiguration();

            ObjectStorageHandler obj = new();
            RunTracer(txtOutputPath.Text, chkAnonymous.Checked, finalUpload, obj);
        }

        private void RunTracer(string outputPath, bool anonymous, bool upload, ObjectStorageHandler obj)
        {
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

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

        private class PersistedConfig
        {
            public string OutputPath { get; set; } = "";
            public bool Anonymous { get; set; }
            public bool UploadEnabled { get; set; }
        }

        private string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "IOTracer", "config.json.enc");

        private void SaveConfiguration()
        {
            try
            {
                var cfg = new PersistedConfig
                {
                    OutputPath = txtOutputPath.Text,
                    Anonymous = chkAnonymous.Checked,
                    UploadEnabled = chkEnableUpload.Checked
                };

                string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
                byte[] encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
                File.WriteAllBytes(ConfigPath, encrypted);
            }
            catch (Exception ex) { Debug.WriteLine("Save config failed: " + ex.Message); }
        }

        private void LoadSavedConfiguration()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;

                byte[] encrypted = File.ReadAllBytes(ConfigPath);
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

        public static TracerConfigForm Run(CancellationToken token = default)
            => new TracerConfigForm(token);
    }
}
