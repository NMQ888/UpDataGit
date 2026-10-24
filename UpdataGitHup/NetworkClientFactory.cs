using System;
using System.Net;
using System.Net.Http;

namespace UpdataGitHup
{
    internal static class NetworkClientFactory
    {
        public static HttpClient Create(PublisherSettings settings, TimeSpan timeout)
        {
            var handler = new HttpClientHandler();
            if (settings != null && settings.UseProxy)
            {
                var proxyUri = ParseProxyUri(settings.ProxyAddress);
                var proxy = new WebProxy(proxyUri);
                if (!string.IsNullOrWhiteSpace(settings.ProxyUsername))
                    proxy.Credentials = new NetworkCredential(settings.ProxyUsername, settings.ProxyPassword ?? "");
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }

            var client = new HttpClient(handler, true);
            client.Timeout = timeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UpdataGitHup/1.0");
            return client;
        }

        public static string BuildGitProxyAddress(PublisherSettings settings)
        {
            if (settings == null || !settings.UseProxy) return null;
            var builder = new UriBuilder(ParseProxyUri(settings.ProxyAddress));
            if (!string.IsNullOrWhiteSpace(settings.ProxyUsername))
            {
                builder.UserName = settings.ProxyUsername;
                builder.Password = settings.ProxyPassword ?? "";
            }
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        public static Uri ParseProxyUri(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("已启用代理，请填写代理地址。");
            var value = address.Trim();
            if (!value.Contains("://")) value = "http://" + value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("代理地址格式无效。请输入类似 http://127.0.0.1:7890 的 HTTP 代理地址。");
            return uri;
        }
    }
}
