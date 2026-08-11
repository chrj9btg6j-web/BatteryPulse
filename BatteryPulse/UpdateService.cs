using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BatteryPulse
{
    public sealed class UpdateInfo
    {
        public bool IsConfigured;
        public bool IsUpdateAvailable;
        public string CurrentVersion;
        public string LatestVersion;
        public string ReleaseName;
        public string ReleaseUrl;
        public DateTime CheckedAt = DateTime.Now;
        public string Error;
    }

    /// <summary>
    /// Read-only GitHub Release checker. It never downloads or executes an installer.
    /// Configure the two URLs in AppSettings before publishing the first release.
    /// </summary>
    public static class UpdateService
    {
        // Public GitHub Release source used by installed copies to find updates.
        public const string DefaultApiUrl = "https://api.github.com/repos/chrj9btg6j-web/BatteryPulse/releases/latest";
        public const string DefaultPageUrl = "https://github.com/chrj9btg6j-web/BatteryPulse/releases/latest";

        public static string CurrentVersionText
        {
            get
            {
                Version version = typeof(UpdateService).Assembly.GetName().Version;
                return version == null ? "0.0.0" : version.ToString(3);
            }
        }

        public static void CheckAsync(string apiUrl, string pageUrl, Action<UpdateInfo> completed)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo result = Check(apiUrl, pageUrl);
                if (completed != null) completed(result);
            });
        }

        public static bool OpenUrl(string url)
        {
            Uri uri;
            if (!TryHttpsUrl(url, out uri)) return false;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static UpdateInfo Check(string apiUrl, string pageUrl)
        {
            var result = new UpdateInfo
            {
                IsConfigured = !string.IsNullOrWhiteSpace(apiUrl),
                CurrentVersion = CurrentVersionText
            };
            if (!result.IsConfigured) return result;

            Uri api;
            if (!TryHttpsUrl(apiUrl, out api))
            {
                result.Error = "更新來源網址不是安全的 HTTPS 網址。";
                return result;
            }

            try
            {
                string json = Download(api);
                string tag = JsonString(json, "tag_name");
                string releaseName = JsonString(json, "name");
                string htmlUrl = JsonString(json, "html_url");
                Version latest = ParseVersion(tag);
                Version current = ParseVersion(result.CurrentVersion);
                if (latest == null || current == null)
                {
                    result.Error = "GitHub Release 沒有可辨識的版本號。";
                    return result;
                }

                result.LatestVersion = latest.ToString(3);
                result.ReleaseName = string.IsNullOrWhiteSpace(releaseName) ? tag : releaseName;
                result.ReleaseUrl = ValidPageUrl(pageUrl) ? pageUrl : htmlUrl;
                result.IsUpdateAvailable = latest > current && !string.IsNullOrWhiteSpace(result.ReleaseUrl);
            }
            catch (WebException ex)
            {
                result.Error = ex.Message;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            return result;
        }

        private static string Download(Uri uri)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "BatteryPulse/" + CurrentVersionText;
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static string JsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            string pattern = "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"\\\\])*)\\\"";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!match.Success) return string.Empty;
            try { return Regex.Unescape(match.Groups["value"].Value); }
            catch { return match.Groups["value"].Value; }
        }

        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            Match match = Regex.Match(value, @"\d+(?:\.\d+){0,3}", RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            try { return new Version(match.Value); }
            catch { return null; }
        }

        private static bool ValidPageUrl(string url)
        {
            Uri ignored;
            return TryHttpsUrl(url, out ignored);
        }

        private static bool TryHttpsUrl(string value, out Uri uri)
        {
            uri = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri)) return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                uri = null;
                return false;
            }
            return true;
        }
    }
}
