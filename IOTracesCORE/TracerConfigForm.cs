using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace IOTracesCORE
{
    public class TracerConfigForm : Form
    {
        private TextBox txtOutputPath;
        private Button btnBrowse;
        private CheckBox chkAnonymous;
        private Button btnRunDefault;
        private Button btnRunCustom;
        private Label lblOutputPath;
        private Label lblAnonymous;
        private CancellationToken cancellationToken;

        public TracerConfigForm(CancellationToken token)
        {
            cancellationToken = token;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "IO-Tracer Configuration";
            this.Width = 500;
            this.Height = 200;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            lblOutputPath = new Label
            {
                Text = "Output Path:",
                Location = new System.Drawing.Point(20, 20),
                Width = 100
            };

            txtOutputPath = new TextBox
            {
                Location = new System.Drawing.Point(120, 18),
                Width = 280,
                Text = $"{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}\\WorkloadTrace"
            };

            btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(410, 17),
                Width = 70,
                Height = 23
            };
            btnBrowse.Click += BtnBrowse_Click;

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

            btnRunCustom = new Button
            {
                Text = "Run in the background!",
                Location = new System.Drawing.Point(20, 100),
                Width = 200,
                Height = 35
            };
            btnRunCustom.Click += BtnRunCustom_Click;

            this.Controls.Add(lblOutputPath);
            this.Controls.Add(txtOutputPath);
            this.Controls.Add(btnBrowse);
            this.Controls.Add(lblAnonymous);
            this.Controls.Add(chkAnonymous);
            this.Controls.Add(btnRunDefault);
            this.Controls.Add(btnRunCustom);
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var folderBrowser = new FolderBrowserDialog())
            {
                folderBrowser.Description = "Select output folder for workload traces";
                folderBrowser.SelectedPath = txtOutputPath.Text;
                folderBrowser.ShowNewFolderButton = true;

                if (folderBrowser.ShowDialog() == DialogResult.OK)
                {
                    txtOutputPath.Text = folderBrowser.SelectedPath;
                }
            }
        }

        private void BtnRunCustom_Click(object sender, EventArgs e)
        {
            string outputPath = txtOutputPath.Text;
            bool anonymous = chkAnonymous.Checked;
            RunTracer(outputPath, anonymous);
        }

        private void RunTracer(string outputPath, bool anonymous)
        {
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            MessageBox.Show($"IO-Tracer now active!\n\nPath: {outputPath}\nAnonymous: {anonymous}",
                "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);

            this.Close();
            Task.Run(() =>
            {
                try
                {
                    Tracer trc = new Tracer(anonymous, outputPath);
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

        public static void Run(CancellationToken cancellationToken = default)
        {
            var form = new TracerConfigForm(cancellationToken);
            form.ShowDialog();
        }
    }
}