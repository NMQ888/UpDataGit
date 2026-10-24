using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UpdataGitHup
{
    public partial class Form1 : Form
    {
        private readonly TextBox _folderTextBox = new TextBox();
        private readonly TextBox _repositoryTextBox = new TextBox();
        private readonly TextBox _usernameTextBox = new TextBox();
        private readonly TextBox _tokenTextBox = new TextBox();
        private readonly TextBox _authorTextBox = new TextBox();
        private readonly TextBox _emailTextBox = new TextBox();
        private readonly CheckBox _privateCheckBox = new CheckBox();
        private readonly RichTextBox _logTextBox = new RichTextBox();
        private readonly Button _publishButton = new Button();
        private readonly Button _saveButton = new Button();
        private readonly Button _proxyButton = new Button();
        private readonly Label _statusLabel = new Label();
        private readonly SettingsStore _settingsStore = new SettingsStore();
        private readonly LogStore _logStore = new LogStore();
        private List<string> _publishedTargets = new List<string>();
        private bool _useProxy;
        private string _proxyAddress = "";
        private string _proxyUsername = "";
        private string _proxyPassword = "";

        public Form1()
        {
            InitializeComponent();
            BuildInterface();
            LoadSettings();
            LoadRecentLogs();
        }

        private void BuildInterface()
        {
            Text = "GitHub 发布助手";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(960, 760);
            MinimumSize = new Size(820, 720);
            BackColor = Color.FromArgb(246, 248, 250);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 18, 24, 18),
                ColumnCount = 1,
                RowCount = 5,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 228));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var title = new Label
            {
                Text = "GitHub 发布助手",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                AutoSize = true,
                Location = new Point(0, 0)
            };
            var subtitle = new Label
            {
                Text = "粘贴项目文件夹，一键创建仓库或提交更新。",
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.FromArgb(107, 114, 128),
                AutoSize = true,
                Location = new Point(2, 42)
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            root.Controls.Add(header, 0, 0);

            var projectCard = CreateCard("项目地址", "仓库名默认使用文件夹名；已存在的仓库会拉取后提交更新。", out var projectBody);
            projectCard.Margin = new Padding(0, 0, 0, 12);
            root.Controls.Add(projectCard, 0, 1);

            var projectLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(0, 4, 0, 0)
            };
            projectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            projectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            projectLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            projectBody.Controls.Add(projectLayout);

            projectLayout.Controls.Add(CreateFieldLabel("本地项目文件夹"), 0, 0);
            StyleTextBox(_folderTextBox);
            projectLayout.Controls.Add(_folderTextBox, 1, 0);
            var browseButton = CreateButton("浏览…", false);
            browseButton.Click += BrowseFolder;
            projectLayout.Controls.Add(browseButton, 2, 0);

            projectLayout.Controls.Add(CreateFieldLabel("GitHub 仓库名"), 0, 1);
            StyleTextBox(_repositoryTextBox);
            projectLayout.Controls.Add(_repositoryTextBox, 1, 1);
            var repoHint = new Label
            {
                Text = "可选",
                ForeColor = Color.FromArgb(107, 114, 128),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            projectLayout.Controls.Add(repoHint, 2, 1);

            var accountCard = CreateCard("GitHub 账户配置", "需要有仓库创建和代码推送权限的令牌；令牌会在本机加密保存。", out var accountBody);
            accountCard.Margin = new Padding(0, 0, 0, 12);
            root.Controls.Add(accountCard, 0, 2);

            var accountLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3,
                Padding = new Padding(0, 4, 0, 0)
            };
            accountLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            accountLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            accountLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
            accountLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (var i = 0; i < 3; i++) accountLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            accountBody.Controls.Add(accountLayout);

            accountLayout.Controls.Add(CreateFieldLabel("GitHub 用户名"), 0, 0);
            StyleTextBox(_usernameTextBox);
            accountLayout.Controls.Add(_usernameTextBox, 1, 0);
            accountLayout.Controls.Add(CreateFieldLabel("访问令牌"), 2, 0);
            StyleTextBox(_tokenTextBox);
            _tokenTextBox.UseSystemPasswordChar = true;
            accountLayout.Controls.Add(_tokenTextBox, 3, 0);

            accountLayout.Controls.Add(CreateFieldLabel("提交作者名"), 0, 1);
            StyleTextBox(_authorTextBox);
            accountLayout.Controls.Add(_authorTextBox, 1, 1);
            accountLayout.Controls.Add(CreateFieldLabel("提交邮箱"), 2, 1);
            StyleTextBox(_emailTextBox);
            accountLayout.Controls.Add(_emailTextBox, 3, 1);

            _privateCheckBox.Text = "创建为私有仓库";
            _privateCheckBox.Checked = true;
            _privateCheckBox.AutoSize = true;
            _privateCheckBox.ForeColor = Color.FromArgb(55, 65, 81);
            _privateCheckBox.Anchor = AnchorStyles.Left;
            accountLayout.Controls.Add(_privateCheckBox, 1, 2);
            accountLayout.SetColumnSpan(_privateCheckBox, 2);

            var actionRow = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = new Padding(0, 4, 0, 0)
            };
            root.Controls.Add(actionRow, 0, 3);

            _publishButton.Text = "一键上传 / 更新";
            _publishButton.Size = new Size(180, 42);
            _publishButton.Location = new Point(0, 2);
            _publishButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _publishButton.BackColor = Color.FromArgb(35, 134, 54);
            _publishButton.ForeColor = Color.White;
            _publishButton.FlatStyle = FlatStyle.Flat;
            _publishButton.FlatAppearance.BorderSize = 0;
            _publishButton.Font = new Font(Font, FontStyle.Bold);
            _publishButton.Cursor = Cursors.Hand;
            _publishButton.Click += PublishClicked;
            actionRow.Controls.Add(_publishButton);

            _saveButton.Text = "保存配置";
            _saveButton.Size = new Size(100, 42);
            _saveButton.Location = new Point(192, 2);
            _saveButton.BackColor = Color.White;
            _saveButton.ForeColor = Color.FromArgb(55, 65, 81);
            _saveButton.FlatStyle = FlatStyle.Flat;
            _saveButton.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            _saveButton.Click += SaveSettingsClicked;
            actionRow.Controls.Add(_saveButton);

            _proxyButton.Text = "代理设置";
            _proxyButton.Size = new Size(110, 42);
            _proxyButton.Location = new Point(304, 2);
            _proxyButton.BackColor = Color.White;
            _proxyButton.ForeColor = Color.FromArgb(55, 65, 81);
            _proxyButton.FlatStyle = FlatStyle.Flat;
            _proxyButton.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            _proxyButton.Click += ProxySettingsClicked;
            actionRow.Controls.Add(_proxyButton);

            _statusLabel.Text = "准备就绪";
            _statusLabel.AutoEllipsis = true;
            _statusLabel.ForeColor = Color.FromArgb(107, 114, 128);
            _statusLabel.Location = new Point(426, 2);
            _statusLabel.Size = new Size(486, 42);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionRow.Controls.Add(_statusLabel);

            var logCard = CreateCard("运行日志", "日志同时保存到本机，便于查看历史上传记录。", out var logBody);
            logCard.Margin = new Padding(0, 0, 0, 0);
            root.Controls.Add(logCard, 0, 4);

            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.ReadOnly = true;
            _logTextBox.BorderStyle = BorderStyle.None;
            _logTextBox.BackColor = Color.FromArgb(249, 250, 251);
            _logTextBox.ForeColor = Color.FromArgb(55, 65, 81);
            _logTextBox.Font = new Font("Consolas", 9F);
            _logTextBox.DetectUrls = false;
            logBody.Controls.Add(_logTextBox);
        }

        private static Panel CreateCard(string heading, string description, out Panel body)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16, 10, 16, 12),
                BorderStyle = BorderStyle.FixedSingle
            };
            var contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = heading,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55)
            };
            var hint = new Label
            {
                Text = description,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 8F),
                ForeColor = Color.FromArgb(107, 114, 128)
            };
            body = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                BackColor = Color.Transparent
            };
            contentLayout.Controls.Add(title, 0, 0);
            contentLayout.Controls.Add(hint, 0, 1);
            contentLayout.Controls.Add(body, 0, 2);
            card.Controls.Add(contentLayout);
            return card;
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(55, 65, 81),
                AutoEllipsis = true
            };
        }

        private static void StyleTextBox(TextBox textBox)
        {
            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 5, 12, 5);
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = Color.White;
            textBox.ForeColor = Color.FromArgb(31, 41, 55);
        }

        private static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 4),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(35, 134, 54) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(55, 65, 81),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            return button;
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsStore.Load();
                _publishedTargets = settings.PublishedTargets ?? new List<string>();
                _folderTextBox.Text = settings.SourceFolder;
                _repositoryTextBox.Text = settings.RepositoryName;
                _usernameTextBox.Text = settings.GitHubUsername;
                _tokenTextBox.Text = settings.GitHubToken;
                _authorTextBox.Text = settings.AuthorName;
                _emailTextBox.Text = settings.AuthorEmail;
                _privateCheckBox.Checked = settings.IsPrivate;
                _useProxy = settings.UseProxy;
                _proxyAddress = settings.ProxyAddress ?? "";
                _proxyUsername = settings.ProxyUsername ?? "";
                _proxyPassword = settings.ProxyPassword ?? "";
                UpdateProxyButtonText(settings.UseProxy);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "读取本地配置失败：" + ex.Message;
            }
        }

        private void LoadRecentLogs()
        {
            try
            {
                foreach (var line in _logStore.ReadRecent(300))
                    _logTextBox.AppendText(line + Environment.NewLine);
                _logTextBox.SelectionStart = _logTextBox.TextLength;
                _logTextBox.ScrollToCaret();
            }
            catch (IOException)
            {
                // A read-only or unavailable log location should not prevent the application from opening.
            }
        }

        private void BrowseFolder(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择要发布到 GitHub 的项目文件夹";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(_folderTextBox.Text)) dialog.SelectedPath = _folderTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _folderTextBox.Text = dialog.SelectedPath;
                    _repositoryTextBox.Text = Path.GetFileNameWithoutExtension(dialog.SelectedPath);
                }
            }
        }

        private void SaveSettingsClicked(object sender, EventArgs e)
        {
            try
            {
                _settingsStore.Save(ReadSettingsFromForm());
                _statusLabel.Text = "配置已加密保存到本机。";
                AppendLog("账户配置已保存到本机。");
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "保存配置失败：" + ex.Message;
                MessageBox.Show(this, "保存配置失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ProxySettingsClicked(object sender, EventArgs e)
        {
            var current = ReadSettingsFromForm();
            var result = ProxySettingsDialog.Show(this, current);
            if (result == null) return;

            current.UseProxy = result.Enabled;
            current.ProxyAddress = result.Address;
            current.ProxyUsername = result.Username;
            current.ProxyPassword = result.Password;
            try
            {
                _settingsStore.Save(current);
                _useProxy = current.UseProxy;
                _proxyAddress = current.ProxyAddress ?? "";
                _proxyUsername = current.ProxyUsername ?? "";
                _proxyPassword = current.ProxyPassword ?? "";
                UpdateProxyButtonText(current.UseProxy);
                _statusLabel.Text = current.UseProxy ? "代理配置已保存并启用。" : "代理已关闭，配置已保存。";
                AppendLog(current.UseProxy ? "代理配置已保存并启用。" : "代理已关闭。配置已保存到本机。");
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "保存代理配置失败：" + ex.Message;
                MessageBox.Show(this, "保存代理配置失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void PublishClicked(object sender, EventArgs e)
        {
            var settings = ReadSettingsFromForm();
            try
            {
                ValidateSettings(settings);
                _settingsStore.Save(settings);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = ex.Message;
                MessageBox.Show(this, ex.Message, "请检查输入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            _statusLabel.Text = "正在准备发布…";
            AppendLog("----------------------------------------");
            AppendLog("开始发布项目：" + settings.SourceFolder);

            var progress = new Progress<string>(AppendLog);
            try
            {
                if (!GitInstaller.IsGitInstalled())
                {
                    var choice = MessageBox.Show(this,
                        "当前电脑未检测到 Git for Windows。是否从官方 GitHub 下载并自动安装？\r\n\r\n安装时 Windows 可能会请求管理员权限。",
                        "需要安装 Git for Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (choice != DialogResult.Yes)
                    {
                        _statusLabel.Text = "未安装 Git，已取消发布。";
                        AppendLog("未检测到 Git for Windows，用户选择暂不安装。");
                        return;
                    }

                    AppendLog("未检测到 Git for Windows，开始下载安装。代理配置将用于安装包下载。");
                    using (var installProgressForm = new GitInstallProgressForm())
                    {
                        installProgressForm.Show(this);
                        var installProgress = new Progress<GitInstallerProgress>(item =>
                        {
                            installProgressForm.UpdateProgress(item);
                            _statusLabel.Text = item.Message;
                        });
                        await GitInstaller.DownloadAndInstallAsync(settings, installProgress);
                    }
                    AppendLog("Git for Windows 已安装并就绪。");
                }

                var publisher = new GitHubPublisher();
                var result = await publisher.PublishAsync(settings, progress, info => FirstPushDialog.Show(this, info));
                if (result.Cancelled)
                {
                    _statusLabel.Text = "已取消，没有推送更改。";
                    AppendLog("用户取消了首次推送。");
                    return;
                }
                _repositoryTextBox.Text = result.RepositoryName;
                _publishedTargets = settings.PublishedTargets;
                _settingsStore.Save(settings);
                _statusLabel.Text = "发布完成。";
                AppendLog("发布成功。仓库：https://github.com/" + settings.GitHubUsername + "/" + result.RepositoryName);
                MessageBox.Show(this, "项目已成功上传 / 更新。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "发布失败：" + ex.Message;
                AppendLog("发布失败：" + ex.Message);
                MessageBox.Show(this, "发布失败：\r\n" + ex.Message + "\r\n\r\n请查看运行日志。", "发布失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ValidateSettings(PublisherSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.SourceFolder) || !Directory.Exists(settings.SourceFolder))
                throw new InvalidOperationException("请填写有效的本地项目文件夹地址。");
            if (string.IsNullOrWhiteSpace(settings.GitHubUsername))
                throw new InvalidOperationException("请填写 GitHub 用户名。");
            if (string.IsNullOrWhiteSpace(settings.GitHubToken))
                throw new InvalidOperationException("请填写 GitHub Personal Access Token。");
            if (string.IsNullOrWhiteSpace(settings.AuthorName) || string.IsNullOrWhiteSpace(settings.AuthorEmail))
                throw new InvalidOperationException("请填写提交作者名和提交邮箱。");
            ResolveRepositoryName(settings);
        }

        private static string ResolveRepositoryName(PublisherSettings settings)
        {
            var name = settings.RepositoryName;
            if (string.IsNullOrWhiteSpace(name)) name = new DirectoryInfo(settings.SourceFolder).Name;
            if (!GitHubPublisher.IsValidRepositoryName(name))
                throw new InvalidOperationException("仓库名只能包含英文字母、数字、点、短横线和下划线，长度为 1 到 100 个字符。");
            return name;
        }

        private PublisherSettings ReadSettingsFromForm()
        {
            return new PublisherSettings
            {
                SourceFolder = _folderTextBox.Text.Trim(),
                RepositoryName = _repositoryTextBox.Text.Trim(),
                GitHubUsername = _usernameTextBox.Text.Trim(),
                GitHubToken = _tokenTextBox.Text,
                AuthorName = _authorTextBox.Text.Trim(),
                AuthorEmail = _emailTextBox.Text.Trim(),
                IsPrivate = _privateCheckBox.Checked,
                PublishedTargets = new List<string>(_publishedTargets),
                UseProxy = _useProxy,
                ProxyAddress = _proxyAddress,
                ProxyUsername = _proxyUsername,
                ProxyPassword = _proxyPassword
            };
        }

        private void SetBusy(bool busy)
        {
            _publishButton.Enabled = !busy;
            _saveButton.Enabled = !busy;
            _proxyButton.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            if (!busy) _statusLabel.ForeColor = Color.FromArgb(107, 114, 128);
        }

        private void UpdateProxyButtonText(bool enabled)
        {
            _proxyButton.Text = enabled ? "代理：已启用" : "代理设置";
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }

            var line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message;
            _logTextBox.AppendText(line + Environment.NewLine);
            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.ScrollToCaret();
            try { _logStore.Append(line); }
            catch (IOException) { _statusLabel.Text = "日志文件写入失败，请检查本机磁盘权限。"; }
        }
    }
}
