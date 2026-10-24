using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UpdataGitHup
{
    internal sealed class GitHubPublisher
    {
        private const string GitHubApiRoot = "https://api.github.com";
        private static readonly Regex RepositoryNamePattern = new Regex("^[A-Za-z0-9._-]{1,100}$", RegexOptions.Compiled);

        public static bool IsValidRepositoryName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && name != "."
                && name != ".."
                && RepositoryNamePattern.IsMatch(name);
        }

        public async Task<PublishResult> PublishAsync(PublisherSettings settings, IProgress<string> progress,
            Func<FirstPushInfo, FirstPushDecision> chooseFirstPushAction)
        {
            var source = Path.GetFullPath(settings.SourceFolder);
            var repositoryName = string.IsNullOrWhiteSpace(settings.RepositoryName)
                ? new DirectoryInfo(source).Name
                : settings.RepositoryName.Trim();
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("找不到项目文件夹：" + source);
            if (!IsValidRepositoryName(repositoryName)) throw new InvalidOperationException("仓库名无效。");
            var hasGitProjectMarkers = HasGitProjectMarkers(source);

            var gitVersion = await Task.Run(() => RunGit(null, "--version", null, null, null, false));
            if (gitVersion.ExitCode != 0)
                throw new InvalidOperationException("未能运行 Git。请先安装 Git for Windows，并确认 git 命令已加入 PATH。");
            Report(progress, "Git 已就绪：" + gitVersion.Output.Trim());

            if (settings.PublishedTargets == null) settings.PublishedTargets = new List<string>();
            var remoteUrl = "https://github.com/" + settings.GitHubUsername + "/" + repositoryName + ".git";
            bool exists;
            using (var client = CreateApiClient(settings))
            {
                Report(progress, "正在验证 GitHub 账户…");
                var account = await GetAuthenticatedLoginAsync(client);
                if (!string.Equals(account, settings.GitHubUsername, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("令牌对应的 GitHub 账户是 “" + account + "”，与填写的用户名不一致。");

                Report(progress, "正在检查仓库 " + settings.GitHubUsername + "/" + repositoryName + "…");
                exists = await RepositoryExistsAsync(client, settings.GitHubUsername, repositoryName);
                if (!exists)
                {
                    Report(progress, "仓库不存在，正在创建" + (settings.IsPrivate ? "私有" : "公开") + "仓库…");
                    await CreateRepositoryAsync(client, repositoryName, settings.IsPrivate);
                    Report(progress, "GitHub 仓库创建成功。");
                }
                else
                {
                    Report(progress, hasGitProjectMarkers
                        ? "仓库已存在，将直接提交本地项目更新。"
                        : "仓库已存在；本地项目缺少完整 Git 标识，稍后请确认覆盖或改名。");
                }
            }

            var tempRoot = Path.Combine(Path.GetTempPath(), "UpdataGitHup-" + Guid.NewGuid().ToString("N"));
            var askPassDirectory = Path.Combine(tempRoot, "auth");
            var checkoutDirectory = Path.Combine(tempRoot, "checkout");
            Directory.CreateDirectory(askPassDirectory);
            var askPassCommand = CreateAskPassHelper(askPassDirectory);
            try
            {
                var auth = new GitAuthentication(askPassCommand, settings.GitHubUsername, settings.GitHubToken,
                    NetworkClientFactory.BuildGitProxyAddress(settings), settings.ProxyPassword);
                if (exists)
                {
                    Report(progress, "正在克隆现有仓库…");
                    var cloneResult = await Task.Run(() => RunGit(null,
                        "clone " + QuoteArgument(remoteUrl) + " " + QuoteArgument(checkoutDirectory),
                        auth, progress, "clone", true));
                    if (cloneResult.ExitCode != 0)
                        throw GitFailure("克隆仓库失败", cloneResult);
                }
                else
                {
                    Directory.CreateDirectory(checkoutDirectory);
                    await RunGitRequiredAsync(checkoutDirectory, "init", auth, progress, "init");
                    await RunGitRequiredAsync(checkoutDirectory, "checkout -b main", auth, progress, "checkout");
                    await RunGitRequiredAsync(checkoutDirectory, "remote add origin " + QuoteArgument(remoteUrl), auth, progress, "remote");
                }

                await RunGitRequiredAsync(checkoutDirectory, "config user.name " + QuoteArgument(settings.AuthorName), auth, progress, "config");
                await RunGitRequiredAsync(checkoutDirectory, "config user.email " + QuoteArgument(settings.AuthorEmail), auth, progress, "config");

                var branchResult = await Task.Run(() => RunGit(checkoutDirectory, "branch --show-current", auth, progress, "branch", false));
                if (branchResult.ExitCode != 0 || string.IsNullOrWhiteSpace(branchResult.Output))
                {
                    await RunGitRequiredAsync(checkoutDirectory, "checkout -b main", auth, progress, "checkout");
                }

                var overwriteExisting = false;
                if (exists && !hasGitProjectMarkers)
                {
                    var existingFileCount = await Task.Run(() => CountExistingProjectFiles(checkoutDirectory, auth, progress));
                    Report(progress, "本地项目缺少 .git 文件夹、.gitattributes 或 .gitignore；等待选择覆盖或改名。");
                    var decision = chooseFirstPushAction == null
                        ? new FirstPushDecision(FirstPushAction.Cancel, null)
                        : chooseFirstPushAction(new FirstPushInfo
                        {
                            Owner = settings.GitHubUsername,
                            RepositoryName = repositoryName,
                            ExistingFileCount = existingFileCount,
                            ValidationMessage = "本地项目缺少完整 Git 标识。请选择覆盖同名仓库或改用其他仓库名。"
                        });

                    if (decision.Action == FirstPushAction.Cancel)
                    {
                        Report(progress, "已取消上传，没有修改远端仓库。");
                        return new PublishResult(true, repositoryName);
                    }

                    if (decision.Action == FirstPushAction.Overwrite)
                    {
                        overwriteExisting = true;
                        Report(progress, "已选择覆盖当前仓库内容。");
                    }
                    else if (decision.Action == FirstPushAction.Rename)
                    {
                        var renamedRepository = decision.NewRepositoryName == null ? "" : decision.NewRepositoryName.Trim();
                        if (!IsValidRepositoryName(renamedRepository)
                            || string.Equals(renamedRepository, repositoryName, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("新仓库名无效或与当前仓库名相同。");

                        while (true)
                        {
                            bool renamedTargetExists;
                            using (var client = CreateApiClient(settings))
                            {
                                renamedTargetExists = await RepositoryExistsAsync(client, settings.GitHubUsername, renamedRepository);
                                if (!renamedTargetExists)
                                {
                                    Report(progress, "正在创建新仓库 " + settings.GitHubUsername + "/" + renamedRepository + "…");
                                    await CreateRepositoryAsync(client, renamedRepository, settings.IsPrivate);
                                }
                            }

                            if (!renamedTargetExists) break;
                            Report(progress, "新填写的仓库名已被占用，请重新选择。");
                            decision = chooseFirstPushAction == null
                                ? new FirstPushDecision(FirstPushAction.Cancel, null)
                                : chooseFirstPushAction(new FirstPushInfo
                                {
                                    Owner = settings.GitHubUsername,
                                    RepositoryName = repositoryName,
                                    ExistingFileCount = existingFileCount,
                                    SuggestedName = renamedRepository,
                                    ValidationMessage = "仓库名 “" + renamedRepository + "” 已存在。请换一个名称，或选择覆盖原仓库。"
                                });

                            if (decision.Action == FirstPushAction.Cancel)
                            {
                                Report(progress, "已取消上传，没有修改远端仓库。");
                                return new PublishResult(true, repositoryName);
                            }
                            if (decision.Action == FirstPushAction.Overwrite)
                            {
                                overwriteExisting = true;
                                break;
                            }

                            renamedRepository = decision.NewRepositoryName == null ? "" : decision.NewRepositoryName.Trim();
                            if (!IsValidRepositoryName(renamedRepository)
                                || string.Equals(renamedRepository, repositoryName, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("新仓库名无效或与当前仓库名相同。");
                        }

                        if (!overwriteExisting)
                        {
                            repositoryName = renamedRepository;
                            remoteUrl = "https://github.com/" + settings.GitHubUsername + "/" + repositoryName + ".git";
                            Report(progress, "已选择新仓库名；原仓库将保持不变。");
                            await Task.Run(() => DeleteDirectoryWithoutFollowingLinks(checkoutDirectory));
                            Directory.CreateDirectory(checkoutDirectory);
                            await InitializeNewCheckoutAsync(checkoutDirectory, remoteUrl, auth, progress);
                        }
                    }
                }

                Report(progress, "正在复制项目文件到临时工作区…");
                if (overwriteExisting) ClearRepositoryContent(checkoutDirectory);
                CopyProjectFiles(source, checkoutDirectory);
                EnsureDefaultGitIgnore(source, checkoutDirectory);

                await RunGitRequiredAsync(checkoutDirectory, "add -A", auth, progress, "add");
                var staged = await Task.Run(() => RunGit(checkoutDirectory, "diff --cached --quiet", auth, progress, "diff", false));
                if (staged.ExitCode == 0)
                {
                    Report(progress, "文件内容没有变化，仓库已是最新状态。");
                }
                else if (staged.ExitCode == 1)
                {
                    var commitMessage = "Update " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    Report(progress, "正在创建提交…");
                    await RunGitRequiredAsync(checkoutDirectory, "commit -m " + QuoteArgument(commitMessage), auth, progress, "commit");
                }
                else
                {
                    throw GitFailure("检查暂存文件失败", staged);
                }

                Report(progress, "正在上传到 GitHub…");
                var branch = await Task.Run(() => RunGit(checkoutDirectory, "branch --show-current", auth, progress, "branch", false));
                var branchName = branch.Output.Trim();
                if (branch.ExitCode != 0 || string.IsNullOrWhiteSpace(branchName)) branchName = "main";
                var pushArgs = "push -u origin " + QuoteArgument(branchName);
                var pushResult = await Task.Run(() => RunGit(checkoutDirectory, pushArgs, auth, progress, "push", true));
                if (pushResult.ExitCode != 0) throw GitFailure("推送到 GitHub 失败", pushResult);
                Report(progress, "推送完成。");
                settings.RepositoryName = repositoryName;
                var targetKey = GetTargetKey(source, settings.GitHubUsername, repositoryName);
                if (!ContainsTarget(settings.PublishedTargets, targetKey)) settings.PublishedTargets.Add(targetKey);
                return new PublishResult(false, repositoryName);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot)) DeleteDirectoryWithoutFollowingLinks(tempRoot);
                }
                catch (Exception ex)
                {
                    Report(progress, "临时工作区清理失败：" + ex.Message + "；稍后可手动删除：" + tempRoot);
                }
            }
        }

        private static async Task InitializeNewCheckoutAsync(string checkoutDirectory, string remoteUrl,
            GitAuthentication auth, IProgress<string> progress)
        {
            await RunGitRequiredAsync(checkoutDirectory, "init", auth, progress, "init");
            await RunGitRequiredAsync(checkoutDirectory, "checkout -b main", auth, progress, "checkout");
            await RunGitRequiredAsync(checkoutDirectory, "remote add origin " + QuoteArgument(remoteUrl), auth, progress, "remote");
        }

        private static int CountExistingProjectFiles(string checkoutDirectory, GitAuthentication auth, IProgress<string> progress)
        {
            var result = RunGit(checkoutDirectory, "ls-files", auth, progress, "ls-files", false);
            if (result.ExitCode != 0) throw GitFailure("检查仓库内容失败", result);
            return result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(path => !IsRepositoryMetadata(path));
        }

        private static bool IsRepositoryMetadata(string path)
        {
            var name = Path.GetFileName(path);
            return string.Equals(name, ".gitignore", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ".gitattributes", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("README", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("CONTRIBUTING", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(".github/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasGitProjectMarkers(string source)
        {
            return Directory.Exists(Path.Combine(source, ".git"))
                && File.Exists(Path.Combine(source, ".gitattributes"))
                && File.Exists(Path.Combine(source, ".gitignore"));
        }

        private static void ClearRepositoryContent(string checkoutDirectory)
        {
            var gitDirectory = Path.Combine(checkoutDirectory, ".git");
            if (!Directory.Exists(gitDirectory)) throw new InvalidOperationException("无法安全覆盖：临时仓库的 Git 元数据不存在。");

            foreach (var file in Directory.GetFiles(checkoutDirectory)) DeleteFileWithoutFollowingLinks(file);
            foreach (var directory in Directory.GetDirectories(checkoutDirectory))
            {
                if (string.Equals(Path.GetFileName(directory), ".git", StringComparison.OrdinalIgnoreCase)) continue;
                DeleteDirectoryWithoutFollowingLinks(directory);
            }
        }

        private static void DeleteDirectoryWithoutFollowingLinks(string directory)
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory, false);
                return;
            }
            foreach (var file in Directory.GetFiles(directory)) DeleteFileWithoutFollowingLinks(file);
            foreach (var childDirectory in Directory.GetDirectories(directory)) DeleteDirectoryWithoutFollowingLinks(childDirectory);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(directory, attributes & ~FileAttributes.ReadOnly);
            Directory.Delete(directory, false);
        }

        private static void DeleteFileWithoutFollowingLinks(string file)
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReparsePoint) == 0
                && (attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            File.Delete(file);
        }

        private static string GetTargetKey(string sourceFolder, string owner, string repository)
        {
            return Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant()
                + "|" + (owner ?? "").ToUpperInvariant() + "|" + (repository ?? "").ToUpperInvariant();
        }

        private static bool ContainsTarget(IEnumerable<string> targets, string targetKey)
        {
            return targets != null && targets.Any(target => string.Equals(target, targetKey, StringComparison.OrdinalIgnoreCase));
        }

        private static HttpClient CreateApiClient(PublisherSettings settings)
        {
            var client = NetworkClientFactory.Create(settings, TimeSpan.FromSeconds(45));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.GitHubToken);
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
            return client;
        }

        private static async Task<string> GetAuthenticatedLoginAsync(HttpClient client)
        {
            var response = await client.GetAsync(GitHubApiRoot + "/user");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw ApiFailure("验证 GitHub 账户失败", response.StatusCode, body);

            var match = Regex.Match(body, "\"login\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!match.Success) throw new InvalidOperationException("GitHub 返回的账户信息无法识别。");
            return match.Groups[1].Value;
        }

        private static async Task<bool> RepositoryExistsAsync(HttpClient client, string owner, string repository)
        {
            var url = GitHubApiRoot + "/repos/" + Uri.EscapeDataString(owner) + "/" + Uri.EscapeDataString(repository);
            var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.NotFound) return false;
            if (!response.IsSuccessStatusCode)
                throw ApiFailure("检查仓库失败", response.StatusCode, body);
            return true;
        }

        private static async Task CreateRepositoryAsync(HttpClient client, string repository, bool isPrivate)
        {
            var json = "{\"name\":\"" + repository + "\",\"private\":" + (isPrivate ? "true" : "false") + "}";
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(GitHubApiRoot + "/user/repos", content);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw ApiFailure("创建仓库失败", response.StatusCode, body);
        }

        private static InvalidOperationException ApiFailure(string action, HttpStatusCode status, string body)
        {
            var details = ExtractApiMessage(body);
            var help = "HTTP " + (int)status + " " + status + (string.IsNullOrWhiteSpace(details) ? "" : "：" + details);
            if (status == HttpStatusCode.Unauthorized)
                help += "。请检查访问令牌是否有效。";
            else if (status == HttpStatusCode.Forbidden)
                help += "。请检查令牌权限、组织策略或 API 限流状态。";
            return new InvalidOperationException(action + "：" + help);
        }

        private static string ExtractApiMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "";
            var match = Regex.Match(body, "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
            if (match.Success) return Regex.Unescape(match.Groups[1].Value);
            return body.Length > 240 ? body.Substring(0, 240) : body;
        }

        private static async Task RunGitRequiredAsync(string directory, string arguments, GitAuthentication auth,
            IProgress<string> progress, string operation)
        {
            var result = await Task.Run(() => RunGit(directory, arguments, auth, progress, operation, true));
            if (result.ExitCode != 0) throw GitFailure("Git 操作失败", result);
        }

        private static GitResult RunGit(string directory, string arguments, GitAuthentication auth,
            IProgress<string> progress, string operation, bool authenticate)
        {
            var info = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = directory == null ? arguments : "-C " + QuoteArgument(directory) + " " + arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (authenticate && auth != null)
            {
                info.EnvironmentVariables["GIT_ASKPASS"] = auth.AskPassCommand;
                info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                info.EnvironmentVariables["UPDATAGITHUB_GIT_USERNAME"] = auth.Username;
                info.EnvironmentVariables["UPDATAGITHUB_GIT_TOKEN"] = auth.Token;
                if (!string.IsNullOrWhiteSpace(auth.ProxyAddress))
                {
                    info.EnvironmentVariables["http_proxy"] = auth.ProxyAddress;
                    info.EnvironmentVariables["https_proxy"] = auth.ProxyAddress;
                    int configCount;
                    if (!int.TryParse(info.EnvironmentVariables["GIT_CONFIG_COUNT"], out configCount) || configCount < 0)
                        configCount = 0;
                    info.EnvironmentVariables["GIT_CONFIG_KEY_" + configCount] = "http.proxy";
                    info.EnvironmentVariables["GIT_CONFIG_VALUE_" + configCount] = auth.ProxyAddress;
                    info.EnvironmentVariables["GIT_CONFIG_COUNT"] = (configCount + 1).ToString();
                }
            }

            var output = new StringBuilder();
            var errors = new StringBuilder();
            using (var process = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                process.OutputDataReceived += (sender, argsLine) =>
                {
                    if (argsLine.Data != null) output.AppendLine(argsLine.Data);
                };
                process.ErrorDataReceived += (sender, argsLine) =>
                {
                    if (argsLine.Data == null) return;
                    var safeLine = SanitizeGitOutput(argsLine.Data, auth);
                    errors.AppendLine(safeLine);
                    if (operation == "clone" || operation == "push") Report(progress, safeLine.Trim());
                };
                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    process.WaitForExit();
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new InvalidOperationException("找不到 Git。请安装 Git for Windows，并确认 git 命令已加入 PATH。", ex);
                }
                return new GitResult(process.ExitCode, SanitizeGitOutput(output.ToString(), auth), SanitizeGitOutput(errors.ToString(), auth));
            }
        }

        private static string SanitizeGitOutput(string value, GitAuthentication auth)
        {
            if (string.IsNullOrEmpty(value) || auth == null) return value;
            var sanitized = value;
            foreach (var secret in new[] { auth.Token, auth.ProxyPassword, auth.ProxyAddress })
            {
                if (!string.IsNullOrEmpty(secret)) sanitized = sanitized.Replace(secret, "[REDACTED]");
            }
            return sanitized;
        }

        private static InvalidOperationException GitFailure(string action, GitResult result)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            detail = string.IsNullOrWhiteSpace(detail) ? "Git 返回代码 " + result.ExitCode : detail.Trim();
            if (detail.Length > 1200) detail = detail.Substring(0, 1200);
            if (detail.IndexOf("non-fast-forward", StringComparison.OrdinalIgnoreCase) >= 0
                || detail.IndexOf("fetch first", StringComparison.OrdinalIgnoreCase) >= 0)
                detail += "\r\n远端有新的提交，请稍后再次上传；如果反复失败，请先从 GitHub 同步该仓库。";
            return new InvalidOperationException(action + "：\r\n" + detail);
        }

        private static void CopyProjectFiles(string source, string destination)
        {
            var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("项目文件夹不能是发布工具的临时工作区。");

            CopyDirectoryContents(sourceRoot, destinationRoot);
        }

        private static void CopyDirectoryContents(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source))
            {
                var info = new DirectoryInfo(directory);
                if (string.Equals(info.Name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                CopyDirectoryContents(directory, Path.Combine(destination, info.Name));
            }

            foreach (var file in Directory.GetFiles(source))
            {
                var info = new FileInfo(file);
                if (string.Equals(info.Name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(destination, info.Name);
                File.Copy(file, target, true);
            }
        }

        private static void EnsureDefaultGitIgnore(string source, string checkout)
        {
            var sourceIgnore = Path.Combine(source, ".gitignore");
            var checkoutIgnore = Path.Combine(checkout, ".gitignore");
            if (File.Exists(sourceIgnore) || File.Exists(checkoutIgnore)) return;
            var defaults = "# Visual Studio generated files\r\n.vs/\r\n[Bb]in/\r\n[Oo]bj/\r\n*.user\r\n*.suo\r\n";
            File.WriteAllText(checkoutIgnore, defaults, new UTF8Encoding(false));
        }

        private static string CreateAskPassHelper(string directory)
        {
            var scriptPath = Path.Combine(directory, "askpass.vbs");
            var content = "Set shell = CreateObject(\"WScript.Shell\")\r\n"
                + "Set env = shell.Environment(\"PROCESS\")\r\n"
                + "prompt = \"\"\r\n"
                + "If WScript.Arguments.Count > 0 Then prompt = LCase(WScript.Arguments(0))\r\n"
                + "If InStr(prompt, \"username\") > 0 Then\r\n"
                + "  WScript.StdOut.WriteLine env(\"UPDATAGITHUB_GIT_USERNAME\")\r\n"
                + "Else\r\n"
                + "  WScript.StdOut.WriteLine env(\"UPDATAGITHUB_GIT_TOKEN\")\r\n"
                + "End If\r\n";
            File.WriteAllText(scriptPath, content, Encoding.ASCII);
            var cscript = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cscript.exe");
            return QuoteShellArgument(cscript) + " //nologo " + QuoteShellArgument(scriptPath);
        }

        private static string QuoteArgument(string value)
        {
            if (value == null) return "\"\"";
            var builder = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '\"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('\"');
                    backslashes = 0;
                    continue;
                }
                builder.Append('\\', backslashes);
                builder.Append(character);
                backslashes = 0;
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('\"');
            return builder.ToString();
        }

        private static string QuoteShellArgument(string value)
        {
            return "'" + (value ?? "").Replace("'", "'\"'\"'") + "'";
        }

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null && !string.IsNullOrWhiteSpace(message)) progress.Report(message);
        }

        private sealed class GitAuthentication
        {
            public GitAuthentication(string askPassCommand, string username, string token, string proxyAddress, string proxyPassword)
            {
                AskPassCommand = askPassCommand;
                Username = username;
                Token = token;
                ProxyAddress = proxyAddress;
                ProxyPassword = proxyPassword;
            }
            public string AskPassCommand { get; private set; }
            public string Username { get; private set; }
            public string Token { get; private set; }
            public string ProxyAddress { get; private set; }
            public string ProxyPassword { get; private set; }
        }

        private sealed class GitResult
        {
            public GitResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }
            public int ExitCode { get; private set; }
            public string Output { get; private set; }
            public string Error { get; private set; }
        }
    }
}
