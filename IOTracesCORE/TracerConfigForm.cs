using Amazon.S3;
using Amazon.S3.Model;
using CredentialManagement;
using IOTracesCORE.cloudstorage;
using IOTracesCORE.snapper;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace IOTracesCORE
{
    public class TracerConfigForm : Form
    {
        private TextBox txtOutputPath;
        private CheckBox chkAnonymous;
        private CheckBox chkEnableR2;
        private GroupBox grpR2Settings;
        private TextBox txtAccountId;
        private TextBox txtAccessKey;
        private TextBox txtSecretKey;
        private TextBox txtBucketName;
        private Button btnTestConnection;
        private Button btnSaveConfig;
        private Button btnRunTracer;
        private Label lblStatus;
        private Label lblOutputPath;
        private Label lblAnonymous;

        private bool isConnectionSafe = false;

        private CancellationToken cancellationToken;

        public TracerConfigForm(CancellationToken token)
        {
            cancellationToken = token;
            InitializeComponent();
            LoadSavedConfiguration();
        }

        private void InitializeComponent()
        {
            this.Text = "IO-Tracer Configuration";
            this.Width = 550;
            this.Height = 500;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            lblOutputPath = new Label
            {
                Text = "Output Path:",
                Location = new Point(20, 20),
                Width = 100
            };

            lblAnonymous = new Label
            {
                Text = "Anonymous:",
                Location = new System.Drawing.Point(20, 55),
                Width = 100
            };

            chkAnonymous = new CheckBox
            {
                Location = new System.Drawing.Point(120, 53),
                Checked = true,
                Width = 20
            };

            txtOutputPath = new TextBox
            {
                Location = new Point(120, 18),
                Width = 280,
                Text = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\WorkloadTrace"
            };

            chkEnableR2 = new CheckBox
            {
                Text = "Enable R2 Upload",
                Location = new Point(20, 80),
                Width = 150,
                Checked = false
            };
            chkEnableR2.CheckedChanged += ChkEnableR2_CheckedChanged;

            grpR2Settings = new GroupBox
            {
                Text = "R2 Configuration",
                Location = new Point(20, 110),
                Size = new Size(490, 250),
                Enabled = false
            };

            var lblAccountId = new Label
            {
                Text = "Account ID:",
                Location = new Point(10, 30),
                Width = 100
            };

            txtAccountId = new TextBox
            {
                Location = new Point(110, 28),
                Width = 350,
                PlaceholderText = "Your R2 Account ID"
            };

            var lblAccessKey = new Label
            {
                Text = "Access Key:",
                Location = new Point(10, 60),
                Width = 100
            };

            txtAccessKey = new TextBox
            {
                Location = new Point(110, 58),
                Width = 350,
                PlaceholderText = "R2 Access Key ID"
            };

            var lblSecretKey = new Label
            {
                Text = "Secret Key:",
                Location = new Point(10, 90),
                Width = 100
            };

            txtSecretKey = new TextBox
            {
                Location = new Point(110, 88),
                Width = 300,
                UseSystemPasswordChar = true,
                PlaceholderText = "R2 Secret Access Key"
            };

            var btnShowHide = new Button
            {
                Text = "👁",
                Location = new Point(415, 87),
                Size = new Size(45, 23)
            };
            btnShowHide.Click += (s, e) =>
            {
                txtSecretKey.UseSystemPasswordChar = !txtSecretKey.UseSystemPasswordChar;
                btnShowHide.Text = txtSecretKey.UseSystemPasswordChar ? "👁" : "🔒";
            };

            var lblBucketName = new Label
            {
                Text = "Bucket Name:",
                Location = new Point(10, 120),
                Width = 100
            };

            txtBucketName = new TextBox
            {
                Location = new Point(110, 118),
                Width = 350,
                PlaceholderText = "io-traces"
            };

            btnTestConnection = new Button
            {
                Text = "Test Connection",
                Location = new Point(110, 160),
                Width = 120,
                Height = 30
            };
            btnTestConnection.Click += BtnTestConnection_Click;

            btnSaveConfig = new Button
            {
                Text = "Save Config",
                Location = new Point(240, 160),
                Width = 120,
                Height = 30,
                Enabled = false
            };
            btnSaveConfig.Click += BtnSaveConfig_Click;

            lblStatus = new Label
            {
                Text = "",
                Location = new Point(110, 200),
                Width = 350,
                Height = 40,
                ForeColor = Color.Blue
            };

            grpR2Settings.Controls.Add(lblAccountId);
            grpR2Settings.Controls.Add(txtAccountId);
            grpR2Settings.Controls.Add(lblAccessKey);
            grpR2Settings.Controls.Add(txtAccessKey);
            grpR2Settings.Controls.Add(lblSecretKey);
            grpR2Settings.Controls.Add(txtSecretKey);
            grpR2Settings.Controls.Add(btnShowHide);
            grpR2Settings.Controls.Add(lblBucketName);
            grpR2Settings.Controls.Add(txtBucketName);
            grpR2Settings.Controls.Add(btnTestConnection);
            grpR2Settings.Controls.Add(btnSaveConfig);
            grpR2Settings.Controls.Add(lblStatus);

            btnRunTracer = new Button
            {
                Text = "Start Tracing",
                Location = new Point(20, 380),
                Width = 200,
                Height = 40,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRunTracer.Click += BtnRunTracer_Click;

            this.Controls.Add(lblOutputPath);
            this.Controls.Add(lblAnonymous);
            this.Controls.Add(txtOutputPath);
            this.Controls.Add(chkAnonymous);
            this.Controls.Add(chkEnableR2);
            this.Controls.Add(grpR2Settings);
            this.Controls.Add(btnRunTracer);
        }

        private void ChkEnableR2_CheckedChanged(object sender, EventArgs e)
        {
            grpR2Settings.Enabled = chkEnableR2.Checked;
            if (!chkEnableR2.Checked)
            {
                lblStatus.Text = "R2 upload disabled, traces will be stored locally only";
                lblStatus.ForeColor = Color.Orange;
            }
            else
            {
                lblStatus.Text = "";
            }
        }

        private async void BtnTestConnection_Click(object sender, EventArgs e)
        {
            if (!ValidateR2Fields())
            {
                MessageBox.Show("Please fill in all R2 fields", "Validation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnTestConnection.Enabled = false;
            lblStatus.Text = "testing connection...";
            lblStatus.ForeColor = Color.Blue;

            try
            {
                var testResult = await TestR2Connection();
                if (testResult)
                {
                    lblStatus.Text = "connection successful!";
                    lblStatus.ForeColor = Color.Green;
                    btnSaveConfig.Enabled = true;
                    isConnectionSafe = true;
                }
                else
                {
                    lblStatus.Text = "connection failed. Check credentials.";
                    lblStatus.ForeColor = Color.Red;
                    btnSaveConfig.Enabled = false;
                    isConnectionSafe = false;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"connection failed: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
                btnSaveConfig.Enabled = false;
                isConnectionSafe = false;
            }
            finally
            {
                btnTestConnection.Enabled = true;
            }
        }

        private async Task<bool> TestR2Connection()
        {
            try
            {
                var config = new AmazonS3Config
                {
                    ServiceURL = $"https://{txtAccountId.Text.Trim()}.r2.cloudflarestorage.com",
                    ForcePathStyle = true,
                    Timeout = TimeSpan.FromSeconds(5)
                };

                using (var client = new AmazonS3Client(
                    txtAccessKey.Text.Trim(),
                    txtSecretKey.Text.Trim(),
                    config))
                {
                    var response = await client.ListObjectsV2Async(new ListObjectsV2Request
                    {
                        BucketName = txtBucketName.Text.Trim(),
                        MaxKeys = 1
                    });
                    return true;
                }
            }
            catch (AmazonS3Exception)
            {
                
                return false;
            }
        }

        private void BtnSaveConfig_Click(object sender, EventArgs e)
        {
            try
            {
                using (var cred = new Credential())
                {
                    cred.Target = "IOTracer_R2";
                    cred.Type = CredentialType.Generic;
                    cred.Username = txtAccessKey.Text.Trim();
                    cred.Password = txtSecretKey.Text.Trim();
                    cred.PersistanceType = PersistanceType.LocalComputer;
                    cred.Save();
                }

                var config = new IOTracerConfig
                {
                    OutputPath = txtOutputPath.Text,
                    Anonymous = chkAnonymous.Checked,
                    R2UploadEnabled = chkEnableR2.Checked,
                    R2AccountId = txtAccountId.Text.Trim(),
                    R2BucketName = txtBucketName.Text.Trim()
                };

                SaveConfiguration(config);

                lblStatus.Text = "configuration saved!";
                lblStatus.ForeColor = Color.Green;

                MessageBox.Show("configuration saved!", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"failed to save configuration: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveConfiguration(IOTracerConfig config)
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IOTracer",
                "config.json"
            );

            Directory.CreateDirectory(Path.GetDirectoryName(configPath));

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json),
                null,
                DataProtectionScope.CurrentUser
            );

            File.WriteAllBytes(configPath + ".enc", encrypted);
        }

        private void LoadSavedConfiguration()
        {
            try
            {
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IOTracer",
                    "config.json.enc"
                );

                if (File.Exists(configPath))
                {
                    var encrypted = File.ReadAllBytes(configPath);
                    var decrypted = ProtectedData.Unprotect(
                        encrypted,
                        null,
                        DataProtectionScope.CurrentUser
                    );

                    var json = Encoding.UTF8.GetString(decrypted);
                    var config = JsonSerializer.Deserialize<IOTracerConfig>(json);

                    txtOutputPath.Text = config.OutputPath;
                    chkAnonymous.Checked = config.Anonymous;
                    chkEnableR2.Checked = config.R2UploadEnabled;
                    txtAccountId.Text = config.R2AccountId;
                    txtBucketName.Text = config.R2BucketName;

                    using (var cred = new Credential())
                    {
                        cred.Target = "IOTracer_R2";
                        if (cred.Load())
                        {
                            txtAccessKey.Text = cred.Username;
                            txtSecretKey.Text = cred.Password;
                        }
                    }

                    grpR2Settings.Enabled = config.R2UploadEnabled;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load config: {ex.Message}");
            }
        }

        private bool ValidateR2Fields()
        {
            return !string.IsNullOrWhiteSpace(txtAccountId.Text) &&
                   !string.IsNullOrWhiteSpace(txtAccessKey.Text) &&
                   !string.IsNullOrWhiteSpace(txtSecretKey.Text) &&
                   !string.IsNullOrWhiteSpace(txtBucketName.Text);
        }

        private void BtnRunTracer_Click(object sender, EventArgs e)
        {
            bool upload = chkEnableR2.Checked;
            if (chkEnableR2.Checked && !isConnectionSafe)
            {
                var result = MessageBox.Show(
                    "R2 configuration is not safe or hasn't been tested. Continue with local storage only?",
                    "Configuration Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == System.Windows.Forms.DialogResult.No)
                {
                    return;
                } else
                {
                    upload = false;
                }
            }
            ObjectStorageHandler objHand = new(
                bucketName: txtBucketName.Text.Trim(),
                serviceUrl: $"https://{txtAccountId.Text.Trim()}.r2.cloudflarestorage.com",
                accessKey: txtAccessKey.Text.Trim(),
                secretKey: txtSecretKey.Text.Trim()
            );
            RunTracer(txtOutputPath.Text, chkAnonymous.Checked, upload, objHand);
        }

        private void RunTracer(string outputPath, bool anonymous, bool upload, ObjectStorageHandler obj)
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }


            this.Hide();
            this.ShowInTaskbar = false;
            Task.Run(() =>
            {
                try
                {
                    Tracer trc = new Tracer(anonymous, upload, obj, outputPath);
                    trc.Trace(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Thread.Sleep(1000);
                }
            });
        }

        public static TracerConfigForm Run(CancellationToken cancellationToken = default)
        {
            return new TracerConfigForm(cancellationToken);
        }
    }
}
