using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace UpdataGitHup
{
    internal sealed class GitInstallerProgress
    {
        public GitInstallerProgress(string message, int percentage, bool indeterminate)
        {
            Message = message;
            Percentage = percentage;
            Indeterminate = indeterminate;
        }

        public string Message { get; private set; }
        public int Percentage { get; private set; }
        public bool Indeterminate { get; private set; }
    }

    internal static class GitInstaller
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/git-for-windows/git/releases/latest";

        public static bool IsGitInstalled()
        {
            if (CanRunGit()) return true;
            foreach (var candidate in GetGitCandidates())
            {
                if (!File.Exists(candidate)) continue;
                AddToCurrentProcessPath(Path.GetDirectoryName(candidate));
                if (CanRunGit()) return true;
            }
            return false;
        }

        public static async Task DownloadAndInstallAsync(PublisherSettings settings, IProgress<GitInstallerProgress> progress)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "UpdataGitHup-GitInstaller-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var installerPath = Path.Combine(tempDirectory, "Git-For-Windows-Setup.exe");
            try
            {
                Report(progress, "正在查询 Git for Windows 官方最新版本…", -1, true);
                string downloadUrl;
                string expectedSha256;
                using (var client = NetworkClientFactory.Create(settings, TimeSpan.FromMinutes(10)))
                {
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                    client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
                    var releaseResponse = await client.GetAsync(LatestReleaseApi).ConfigureAwait(false);
                    var releaseJson = await releaseResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!releaseResponse.IsSuccessStatusCode)
                        throw new InvalidOperationException("查询 Git for Windows 官方版本失败：HTTP " + (int)releaseResponse.StatusCode + " " + releaseResponse.ReasonPhrase);

                    var asset = FindInstallerAsset(releaseJson, Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
                    downloadUrl = asset.Item1;
                    expectedSha256 = asset.Item2;

                    Report(progress, "正在从 GitHub 下载 Git for Windows…", 0, false);
                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("下载 Git 安装包失败：HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase);

                        var totalBytes = response.Content.Headers.ContentLength;
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(installerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            var buffer = new byte[81920];
                            long downloaded = 0;
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                            {
                                await output.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                                downloaded += read;
                                var percent = totalBytes.HasValue && totalBytes.Value > 0
                                    ? (int)Math.Min(100, downloaded * 100L / totalBytes.Value)
                                    : -1;
                                var message = totalBytes.HasValue && totalBytes.Value > 0
                                    ? "正在下载 Git for Windows… " + FormatBytes(downloaded) + " / " + FormatBytes(totalBytes.Value) + "（" + percent + "%）"
                                    : "正在下载 Git for Windows… 已下载 " + FormatBytes(downloaded);
                                Report(progress, message, percent, percent < 0);
                            }
                        }
                    }
                }

                Report(progress, "正在校验安装包…", 100, false);
                if (!string.IsNullOrWhiteSpace(expectedSha256)) VerifySha256(installerPath, expectedSha256);

                Report(progress, "正在安装 Git for Windows…（安装阶段显示活动进度）", -1, true);
                await InstallSilentlyAsync(installerPath).ConfigureAwait(false);
                if (!IsGitInstalled())
                    throw new InvalidOperationException("安装程序已结束，但未检测到 Git。请重启软件后重试，或手动安装 Git for Windows。");
                Report(progress, "Git for Windows 已安装并就绪。", 100, false);
            }
            finally
            {
                try
                {
                    if (File.Exists(installerPath)) File.Delete(installerPath);
                    if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, false);
                }
                catch (IOException)
                {
                    // Keep the setup log free of local temp paths; the installer can be removed manually if locked.
                }
                catch (UnauthorizedAccessException)
                {
                    // A cleanup failure must not turn a successful installation into a reported failure.
                }
            }
        }

        private static Tuple<string, string> FindInstallerAsset(string json, string architecture)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var release = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (release == null || !release.ContainsKey("assets"))
                throw new InvalidOperationException("GitHub 返回的版本信息格式无法识别。");

            var assets = release["assets"] as object[];
            if (assets == null) throw new InvalidOperationException("GitHub 返回的版本信息中没有安装包列表。");
            var suffix = "-" + architecture + ".exe";
            foreach (var item in assets)
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null) continue;
                var name = GetString(asset, "name");
                if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("Git-", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                var url = GetString(asset, "browser_download_url");
                if (!Uri.IsWellFormedUriString(url, UriKind.Absolute)) continue;
                var digest = GetString(asset, "digest");
                if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    digest = digest.Substring("sha256:".Length);
                else
                    digest = null;
                return Tuple.Create(url, digest);
            }

            throw new InvalidOperationException("GitHub 最新版本中没有适用于此 Windows 系统的 Git 安装包。");
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) ? Convert.ToString(value) : null;
        }

        private static void VerifySha256(string filePath, string expected)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Git 安装包 SHA-256 校验失败，已停止安装。");
            }
        }

        private static async Task InstallSilentlyAsync(string installerPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /o:PathOption=Cmd",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null) throw new InvalidOperationException("无法启动 Git 安装程序。");
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("Git 安装程序退出，代码为 " + process.ExitCode + "。");
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                    throw new InvalidOperationException("用户取消了 Windows 管理员权限确认，Git 未安装。", ex);
                throw new InvalidOperationException("启动 Git 安装程序失败：" + ex.Message, ex);
            }
        }

        private static bool CanRunGit()
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(info))
                {
                    if (process == null) return false;
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<string> GetGitCandidates()
        {
            var roots = new List<string>();
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFilesW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
            var localPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
            foreach (var root in new[] { programFiles, programFilesX86, programFilesW6432, localPrograms })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var candidate = Path.Combine(root, "Git", "cmd", "git.exe");
                if (!roots.Contains(candidate, StringComparer.OrdinalIgnoreCase)) roots.Add(candidate);
            }
            return roots;
        }

        private static void AddToCurrentProcessPath(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            var currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            if (currentPath.Split(';').Any(part => string.Equals(part.Trim(), directory, StringComparison.OrdinalIgnoreCase))) return;
            Environment.SetEnvironmentVariable("PATH", directory + ";" + currentPath, EnvironmentVariableTarget.Process);
        }

        private static string FormatBytes(long bytes)
        {
            var megabytes = bytes / (1024.0 * 1024.0);
            return megabytes >= 1 ? megabytes.ToString("0.0") + " MB" : (bytes / 1024.0).ToString("0") + " KB";
        }

        private static void Report(IProgress<GitInstallerProgress> progress, string message, int percent, bool indeterminate)
        {
            if (progress != null) progress.Report(new GitInstallerProgress(message, percent, indeterminate));
        }
    }
}
