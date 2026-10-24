# GitHub 发布助手

一个 Windows 桌面小工具：选择本地项目文件夹后，会在 GitHub 上创建同名仓库（或更新现有仓库），并显示、保存每次操作的日志。

## 运行要求

- Windows 与 .NET Framework 4.8
- Git for Windows（未安装时，首次发布会询问是否自动下载并安装）
- GitHub 个人访问令牌（Personal Access Token）

## 使用

1. 启动 `UpdataGitHup.exe`，填写 GitHub 用户名、访问令牌、提交作者名和邮箱。
2. 粘贴本地项目文件夹地址。仓库名默认使用文件夹名，也可以填写其他仓库名。
3. 选择新仓库要设为私有还是公开，然后点“保存配置”。
4. 点“一键上传 / 更新”。工具会检查当前账户下的同名仓库：不存在时创建，存在时拉取默认分支并提交更新。
5. 首次把某个本地项目推送到已有且含项目文件的仓库时，会让你选择覆盖，或改用其他仓库名新建仓库。选择覆盖会删除远端独有文件；选择改名会保留原仓库。后续推送会直接更新已记录的仓库。
6. 需要代理时，点“代理设置”，填写 HTTP/HTTPS 代理地址及可选的认证信息。代理会用于 GitHub API、Git HTTPS 操作和 Git 安装包下载；代理密码会加密保存在本机。

若未安装 Git for Windows，工具会询问是否从 [Git for Windows 官方 GitHub 发布页](https://github.com/git-for-windows/git/releases/latest) 下载并安装。下载进度会显示百分比；安装阶段会显示活动进度，并可能触发 Windows 管理员权限确认。

令牌权限：经典令牌创建私有仓库需要 `repo` 权限，创建公开仓库需要 `public_repo` 或 `repo` 权限；细粒度令牌创建仓库需要 `Administration: Read and write`，向仓库推送需要 `Contents: Read and write`。组织策略可能还需要批准令牌。可查看 [GitHub 创建仓库 API 的权限说明](https://docs.github.com/en/rest/repos/repos#create-a-repository-for-the-authenticated-user)。

## 同步行为

- 上传前会在临时目录克隆远端默认分支，再把本地文件复制到工作区并提交，因此远端已有的提交历史会保留。
- 本地 `.git` 目录不会上传；远端存在而本地没有的文件会保留，避免误删 GitHub 上已有的 README、许可证等文件。
- 有 `.gitignore` 时按它过滤文件；项目和远端都没有 `.gitignore` 时，会使用基础 Visual Studio 忽略规则排除 `.vs`、`bin`、`obj`、用户设置文件。
- 新仓库名称默认来自文件夹名，支持字母、数字、点、短横线和下划线。新仓库创建后将使用 `main` 分支；已有仓库使用其默认分支。
- 首次推送记录随本机配置保存，以本地文件夹、GitHub 用户名和仓库名作为识别条件；换电脑或清除配置后，已有仓库会再次触发首次推送确认。
- 访问令牌和代理密码使用 Windows DPAPI 按当前 Windows 用户加密，配置保存在 `%LOCALAPPDATA%\UpdataGitHup\settings.xml`。日志保存在 `%LOCALAPPDATA%\UpdataGitHup\logs`。

## 构建

使用 Visual Studio 打开 `UpdataGitHup.slnx`，选择 .NET Framework 4.8 后构建即可。
