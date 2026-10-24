using System;
using System.Drawing;
using System.Windows.Forms;

namespace UpdataGitHup
{
    internal sealed class GitInstallProgressForm : Form
    {
        private readonly Label _message = new Label();
        private readonly ProgressBar _progressBar = new ProgressBar();

        public GitInstallProgressForm()
        {
            Text = "Git for Windows 安装进度";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(510, 150);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;

            var title = new Label
            {
                Text = "正在准备 Git for Windows",
                Location = new Point(24, 20),
                Size = new Size(460, 27),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55)
            };
            _message.Text = "准备下载…";
            _message.Location = new Point(25, 57);
            _message.Size = new Size(460, 24);
            _message.ForeColor = Color.FromArgb(75, 85, 99);
            _message.AutoEllipsis = true;
            _progressBar.Location = new Point(25, 94);
            _progressBar.Size = new Size(460, 20);
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 28;
            Controls.Add(title);
            Controls.Add(_message);
            Controls.Add(_progressBar);
        }

        public void UpdateProgress(GitInstallerProgress progress)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<GitInstallerProgress>(UpdateProgress), progress);
                return;
            }

            _message.Text = progress.Message;
            if (progress.Indeterminate)
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
                _progressBar.MarqueeAnimationSpeed = 28;
                return;
            }

            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.MarqueeAnimationSpeed = 0;
            _progressBar.Value = Math.Max(0, Math.Min(100, progress.Percentage));
        }
    }
}
