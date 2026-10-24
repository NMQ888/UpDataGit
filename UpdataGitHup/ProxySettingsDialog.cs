using System;
using System.Drawing;
using System.Windows.Forms;

namespace UpdataGitHup
{
    internal sealed class ProxySettingsResult
    {
        public bool Enabled { get; set; }
        public string Address { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    internal sealed class ProxySettingsDialog : Form
    {
        private readonly CheckBox _enabled = new CheckBox();
        private readonly TextBox _address = new TextBox();
        private readonly TextBox _username = new TextBox();
        private readonly TextBox _password = new TextBox();
        private readonly Label[] _fieldLabels = new Label[3];

        private ProxySettingsDialog(PublisherSettings settings)
        {
            Text = "代理设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(520, 316);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.White;

            var title = new Label
            {
                Text = "GitHub 网络代理",
                Location = new Point(24, 18),
                Size = new Size(460, 28),
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55)
            };
            var description = new Label
            {
                Text = "启用后，GitHub API、代码推送和 Git 安装包下载都会使用此代理。",
                Location = new Point(25, 50),
                Size = new Size(470, 24),
                ForeColor = Color.FromArgb(107, 114, 128),
                AutoEllipsis = true
            };
            Controls.Add(title);
            Controls.Add(description);

            _enabled.Text = "使用代理";
            _enabled.Location = new Point(26, 85);
            _enabled.Size = new Size(160, 26);
            _enabled.Checked = settings.UseProxy;
            _enabled.CheckedChanged += (sender, args) => UpdateEnabledState();
            Controls.Add(_enabled);

            var layout = new TableLayoutPanel
            {
                Location = new Point(24, 120),
                Size = new Size(472, 116),
                ColumnCount = 2,
                RowCount = 3,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            _fieldLabels[0] = CreateLabel("代理地址");
            _address = CreateTextBox();
            _address.Text = settings.ProxyAddress ?? "";
            _fieldLabels[1] = CreateLabel("代理用户名");
            _username = CreateTextBox();
            _username.Text = settings.ProxyUsername ?? "";
            _fieldLabels[2] = CreateLabel("代理密码");
            _password = CreateTextBox();
            _password.Text = settings.ProxyPassword ?? "";
            _password.UseSystemPasswordChar = true;

            layout.Controls.Add(_fieldLabels[0], 0, 0);
            layout.Controls.Add(_address, 1, 0);
            layout.Controls.Add(_fieldLabels[1], 0, 1);
            layout.Controls.Add(_username, 1, 1);
            layout.Controls.Add(_fieldLabels[2], 0, 2);
            layout.Controls.Add(_password, 1, 2);
            Controls.Add(layout);

            var hint = new Label
            {
                Text = "代理示例：http://127.0.0.1:7890。密码会加密保存在本机。",
                Location = new Point(25, 242),
                Size = new Size(470, 20),
                ForeColor = Color.FromArgb(107, 114, 128),
                AutoEllipsis = true
            };
            Controls.Add(hint);

            var save = new Button
            {
                Text = "保存代理",
                Location = new Point(276, 272),
                Size = new Size(105, 34),
                BackColor = Color.FromArgb(35, 134, 54),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            save.FlatAppearance.BorderSize = 0;
            save.Click += SaveClicked;
            Controls.Add(save);

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(391, 272),
                Size = new Size(105, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(55, 65, 81)
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            cancel.Click += (sender, args) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);
            CancelButton = cancel;
            UpdateEnabledState();
        }

        public ProxySettingsResult Result { get; private set; }

        public static ProxySettingsResult Show(IWin32Window owner, PublisherSettings settings)
        {
            using (var dialog = new ProxySettingsDialog(settings))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return dialog.Result;
            }
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(55, 65, 81),
                Margin = new Padding(0, 2, 8, 2)
            };
        }

        private static TextBox CreateTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 0, 5),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(31, 41, 55)
            };
        }

        private void UpdateEnabledState()
        {
            var enabled = _enabled.Checked;
            _address.Enabled = enabled;
            _username.Enabled = enabled;
            _password.Enabled = enabled;
            foreach (var label in _fieldLabels) label.Enabled = enabled;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            var address = _address.Text.Trim();
            if (_enabled.Checked)
            {
                try
                {
                    address = NetworkClientFactory.ParseProxyUri(address).AbsoluteUri.TrimEnd('/');
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "代理地址无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _address.Focus();
                    return;
                }
            }

            Result = new ProxySettingsResult
            {
                Enabled = _enabled.Checked,
                Address = address,
                Username = _username.Text.Trim(),
                Password = _password.Text
            };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
