using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IOTracesCORE
{
    public class LoadingDialog : Form
    {
        private Label lblMessage;
        private ProgressBar progressBar;

        public LoadingDialog(string message)
        {
            InitializeComponent(message);
        }

        private void InitializeComponent(string message)
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new Size(400, 150);
            this.Text = "IO Traces Core";
            this.ControlBox = false;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Message label
            lblMessage = new Label
            {
                Text = message,
                AutoSize = false,
                Size = new Size(360, 40),
                Location = new Point(20, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // Progress bar with marquee style (loading animation)
            progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Size = new Size(360, 23),
                Location = new Point(20, 70)
            };

            this.Controls.Add(lblMessage);
            this.Controls.Add(progressBar);
        }
    }

}
