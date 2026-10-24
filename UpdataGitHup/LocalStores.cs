using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace UpdataGitHup
{
    internal sealed class PublisherSettings
    {
        public PublisherSettings()
        {
            PublishedTargets = new List<string>();
        }

        public string SourceFolder { get; set; }
        public string RepositoryName { get; set; }
        public string GitHubUsername { get; set; }
        public string GitHubToken { get; set; }
        public string AuthorName { get; set; }
        public string AuthorEmail { get; set; }
        public bool IsPrivate { get; set; }
        public List<string> PublishedTargets { get; set; }
        public bool UseProxy { get; set; }
        public string ProxyAddress { get; set; }
        public string ProxyUsername { get; set; }
        public string ProxyPassword { get; set; }
    }

    internal sealed class SettingsStore
    {
        private readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UpdataGitHup", "settings.xml");

        public PublisherSettings Load()
        {
            var settings = new PublisherSettings { IsPrivate = true };
            if (!File.Exists(_filePath)) return settings;

            var root = XDocument.Load(_filePath).Root;
            if (root == null) return settings;
            settings.SourceFolder = ReadValue(root, "SourceFolder");
            settings.RepositoryName = ReadValue(root, "RepositoryName");
            settings.GitHubUsername = ReadValue(root, "GitHubUsername");
            settings.AuthorName = ReadValue(root, "AuthorName");
            settings.AuthorEmail = ReadValue(root, "AuthorEmail");
            settings.ProxyAddress = ReadValue(root, "ProxyAddress");
            settings.ProxyUsername = ReadValue(root, "ProxyUsername");
            bool useProxy;
            if (bool.TryParse(ReadValue(root, "UseProxy"), out useProxy)) settings.UseProxy = useProxy;
            var targetsElement = root.Element("PublishedTargets");
            if (targetsElement != null)
                settings.PublishedTargets = targetsElement.Elements("Target").Select(element => element.Value).ToList();
            bool isPrivate;
            if (bool.TryParse(ReadValue(root, "IsPrivate"), out isPrivate)) settings.IsPrivate = isPrivate;

            var encryptedToken = ReadValue(root, "EncryptedToken");
            if (!string.IsNullOrEmpty(encryptedToken))
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encryptedToken), null, DataProtectionScope.CurrentUser);
                settings.GitHubToken = Encoding.UTF8.GetString(bytes);
            }
            var encryptedProxyPassword = ReadValue(root, "EncryptedProxyPassword");
            if (!string.IsNullOrEmpty(encryptedProxyPassword))
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encryptedProxyPassword), null, DataProtectionScope.CurrentUser);
                settings.ProxyPassword = Encoding.UTF8.GetString(bytes);
            }
            return settings;
        }

        public void Save(PublisherSettings settings)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var tokenBytes = Encoding.UTF8.GetBytes(settings.GitHubToken ?? "");
            var encryptedToken = Convert.ToBase64String(ProtectedData.Protect(tokenBytes, null, DataProtectionScope.CurrentUser));
            var proxyPasswordBytes = Encoding.UTF8.GetBytes(settings.ProxyPassword ?? "");
            var encryptedProxyPassword = Convert.ToBase64String(ProtectedData.Protect(proxyPasswordBytes, null, DataProtectionScope.CurrentUser));
            var document = new XDocument(
                new XElement("GitHubPublisherSettings",
                    new XElement("SourceFolder", settings.SourceFolder ?? ""),
                    new XElement("RepositoryName", settings.RepositoryName ?? ""),
                    new XElement("GitHubUsername", settings.GitHubUsername ?? ""),
                    new XElement("EncryptedToken", encryptedToken),
                    new XElement("AuthorName", settings.AuthorName ?? ""),
                    new XElement("AuthorEmail", settings.AuthorEmail ?? ""),
                    new XElement("IsPrivate", settings.IsPrivate),
                    new XElement("UseProxy", settings.UseProxy),
                    new XElement("ProxyAddress", settings.ProxyAddress ?? ""),
                    new XElement("ProxyUsername", settings.ProxyUsername ?? ""),
                    new XElement("EncryptedProxyPassword", encryptedProxyPassword),
                    new XElement("PublishedTargets", (settings.PublishedTargets ?? new List<string>()).Select(target => new XElement("Target", target)))));
            document.Save(_filePath);
        }

        private static string ReadValue(XElement root, string name)
        {
            var element = root.Element(name);
            return element == null ? "" : element.Value;
        }
    }

    internal sealed class LogStore
    {
        private readonly string _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UpdataGitHup", "logs");

        public void Append(string line)
        {
            if (!Directory.Exists(_directory)) Directory.CreateDirectory(_directory);
            var file = Path.Combine(_directory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
        }

        public IEnumerable<string> ReadRecent(int maximumLines)
        {
            if (!Directory.Exists(_directory)) return Enumerable.Empty<string>();
            var lines = Directory.GetFiles(_directory, "*.log")
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .SelectMany(path => File.ReadAllLines(path).Reverse())
                .Take(maximumLines)
                .Reverse()
                .ToArray();
            return lines;
        }
    }
}
