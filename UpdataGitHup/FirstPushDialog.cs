using System;
using System.Drawing;
using System.Windows.Forms;

namespace UpdataGitHup
{
    internal enum FirstPushAction
    {
        Cancel,
        Overwrite,
        Rename
    }

    internal sealed class FirstPushDecision
    {
        public FirstPushDecision(FirstPushAction action, string newRepositoryName)
        {
            Action = action;
            NewRepositoryName = newRepositoryName;
        }

        public FirstPushAction Action { get; private set; }
        public string NewRepositoryName { get; private set; }
    }

    internal sealed class FirstPushInfo
    {
        public string Owner { get; set; }
        public string RepositoryName { get; set; }
        public int ExistingFileCount { get; set; }
        public string SuggestedName { get; set; }
        public string ValidationMessage { get; set; }
    }

    internal sealed class PublishResult
    {
        public PublishResult(bool cancelled, string repositoryName)
        {
            Cancelled = cancelled;
            RepositoryName = repositoryName;
        }

        public bool Cancelled { get; private set; }
        public string RepositoryName { get; private set; }
    }

    internal sealed class FirstPushDialog : Form
    {
        private readonly TextBox _newName = new TextBox();
        private readonly Label _message = new Label();
        private readonly FirstPushInfo _info;

        private FirstPushDialog(FirstPushInfo info)
        {
            _info = info;
            Text = "已有仓库确认";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 300);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;

            var title = new Label
            {
                Text = "同名远程仓库已存在",
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                Location = new Point(24, 20),
                AutoSize = true
            };
            _message.Text = info.ValidationMessage ??
                ("仓库 " + info.Owner + "/" + info.RepositoryName + " 中检测到 " + info.ExistingFileCount + " 个项目文件。请选择覆盖或改用其他名称：");
            _message.Location = new Point(25, 56);
            _message.Size = new Size(470, 42);
            _message.ForeColor = Color.FromArgb(75, 85, 99);
            _message.AutoEllipsis = true;
            Controls.Add(title);
            Controls.Add(_message);

            var overwriteHint = new Label
            {
                Text = "用本地项目替换当前仓库内容；远端独有文件会被删除。",
                Location = new Point(26, 110),
                Size = new Size(460, 24),
                ForeColor = Color.FromArgb(107, 114, 128)
            };
            Controls.Add(overwriteHint);

            var renameLabel = new Label
            {
                Text = "改用其他名称新建仓库（保留当前仓库不变）：",
                Location = new Point(26, 148),
                Size = new Size(460, 24),
                ForeColor = Color.FromArgb(55, 65, 81)
            };
            Controls.Add(renameLabel);

            _newName.Location = new Point(26, 176);
            _newName.Size = new Size(460, 27);
            _newName.BorderStyle = BorderStyle.FixedSingle;
            _newName.Text = string.IsNullOrWhiteSpace(info.SuggestedName)
                ? GetSuggestedName(info.RepositoryName)
                : info.SuggestedName;
            Controls.Add(_newName);

            var overwriteButton = new Button
            {
                Text = "覆盖当前仓库",
                Location = new Point(26, 230),
                Size = new Size(145, 40),
                BackColor = Color.FromArgb(180, 35, 24),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            overwriteButton.FlatAppearance.BorderSize = 0;
            overwriteButton.Click += (sender, args) =>
            {
                Decision = new FirstPushDecision(FirstPushAction.Overwrite, null);
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(overwriteButton);

            var renameButton = new Button
            {
                Text = "改名并新建仓库",
                Location = new Point(182, 230),
                Size = new Size(170, 40),
                BackColor = Color.FromArgb(35, 134, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            renameButton.FlatAppearance.BorderSize = 0;
            renameButton.Click += RenameClicked;
            Controls.Add(renameButton);

            var cancelButton = new Button
            {
                Text = "取消",
                Location = new Point(363, 230),
                Size = new Size(123, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(55, 65, 81)
            };
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            cancelButton.Click += (sender, args) =>
            {
                Decision = new FirstPushDecision(FirstPushAction.Cancel, null);
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancelButton);
            CancelButton = cancelButton;
        }

        public FirstPushDecision Decision { get; private set; }

        public static FirstPushDecision Show(IWin32Window owner, FirstPushInfo info)
        {
            using (var dialog = new FirstPushDialog(info))
            {
                dialog.ShowDialog(owner);
                return dialog.Decision ?? new FirstPushDecision(FirstPushAction.Cancel, null);
            }
        }

        private void RenameClicked(object sender, EventArgs e)
        {
            var name = _newName.Text.Trim();
            if (!GitHubPublisher.IsValidRepositoryName(name))
            {
                MessageBox.Show(this, "仓库名只能包含英文字母、数字、点、短横线和下划线，长度为 1 到 100 个字符。", "仓库名无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _newName.Focus();
                return;
            }
            if (string.Equals(name, _info.RepositoryName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "新仓库名需要与当前仓库名不同。", "仓库名未改变", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _newName.Focus();
                return;
            }
            Decision = new FirstPushDecision(FirstPushAction.Rename, name);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string GetSuggestedName(string currentName)
        {
            const string suffix = "-new";
            var prefixLength = Math.Min(currentName.Length, 100 - suffix.Length);
            return currentName.Substring(0, prefixLength) + suffix;
        }
    }
}
