using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MiniFanControl
{
    public sealed class UpdateCheckResult
    {
        public bool Success;
        public bool UpdateAvailable;
        public string CurrentVersion;
        public string LatestVersion;
        public string ReleaseName;
        public string ReleaseUrl;
        public string PublishedAt;
        public string ReleaseNotesExcerpt;
        public string ErrorMessage;
    }

    /// <summary>
    /// Privacy-first, on-demand GitHub Releases update checker.
    /// Never phones home automatically; invoked only when the user clicks "Check for Updates".
    /// Strictly validates release URLs against the official GitHub repository prefix.
    /// </summary>
    public static class UpdateChecker
    {
        public const string OfficialRepoPrefix = "https://github.com/kaiserc/MiniFanControl/";
        public const string LatestReleasePageUrl = "https://github.com/kaiserc/MiniFanControl/releases/latest";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/kaiserc/MiniFanControl/releases/latest";
        private const int MaxResponseBytes = 256 * 1024;

        public static bool TryParseSemVer(string raw, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string s = raw.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1).Trim();

            int dashIdx = s.IndexOf('-');
            if (dashIdx >= 0) s = s.Substring(0, dashIdx);
            int plusIdx = s.IndexOf('+');
            if (plusIdx >= 0) s = s.Substring(0, plusIdx);

            string[] parts = s.Split('.');
            if (parts.Length < 2 || parts.Length > 4) return false;

            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major)) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)) return false;
            if (parts.Length >= 3)
            {
                if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            }
            return true;
        }

        public static bool IsNewerVersion(string currentVersion, string candidateVersion)
        {
            int curMaj, curMin, curPat;
            int newMaj, newMin, newPat;
            if (!TryParseSemVer(currentVersion, out curMaj, out curMin, out curPat)) return false;
            if (!TryParseSemVer(candidateVersion, out newMaj, out newMin, out newPat)) return false;

            if (newMaj != curMaj) return newMaj > curMaj;
            if (newMin != curMin) return newMin > curMin;
            return newPat > curPat;
        }

        public static string SanitizeReleaseUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return LatestReleasePageUrl;
            string trimmed = rawUrl.Trim();
            if (trimmed.StartsWith(OfficialRepoPrefix, StringComparison.OrdinalIgnoreCase))
                return trimmed;
            return LatestReleasePageUrl;
        }

        /// <summary>
        /// Extracts a top-level (depth == 1) string field from a JSON object while safely skipping
        /// nested objects/arrays (such as GitHub's "author" and "assets" fields).
        /// </summary>
        public static string ExtractTopLevelJsonString(string json, string targetKey)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(targetKey)) return null;

            int len = json.Length;
            int i = 0;
            int braceDepth = 0;
            int bracketDepth = 0;

            while (i < len)
            {
                char c = json[i];
                if (c == '"')
                {
                    int strStart = i + 1;
                    i++;
                    while (i < len)
                    {
                        if (json[i] == '\\' && i + 1 < len) { i += 2; continue; }
                        if (json[i] == '"') break;
                        i++;
                    }
                    if (i >= len) break;
                    string token = SimpleJson.Unescape(json.Substring(strStart, i - strStart));
                    i++; // skip closing quote

                    // Check if this string is a top-level property name followed by ':'
                    if (braceDepth == 1 && bracketDepth == 0)
                    {
                        int j = i;
                        while (j < len && char.IsWhiteSpace(json[j])) j++;
                        if (j < len && json[j] == ':')
                        {
                            j++;
                            while (j < len && char.IsWhiteSpace(json[j])) j++;
                            if (string.Equals(token, targetKey, StringComparison.Ordinal))
                            {
                                if (j < len && json[j] == '"')
                                {
                                    int valStart = j + 1;
                                    j++;
                                    while (j < len)
                                    {
                                        if (json[j] == '\\' && j + 1 < len) { j += 2; continue; }
                                        if (json[j] == '"') break;
                                        j++;
                                    }
                                    if (j < len)
                                        return SimpleJson.Unescape(json.Substring(valStart, j - valStart));
                                }
                                return null;
                            }
                        }
                    }
                    continue;
                }

                if (c == '{') braceDepth++;
                else if (c == '}') braceDepth--;
                else if (c == '[') bracketDepth++;
                else if (c == ']') bracketDepth--;
                i++;
            }
            return null;
        }

        public static UpdateCheckResult ParseLatestReleaseJson(string json, string currentVersion)
        {
            UpdateCheckResult res = new UpdateCheckResult();
            res.CurrentVersion = currentVersion ?? "0.0.0";
            res.ReleaseUrl = LatestReleasePageUrl;

            if (string.IsNullOrWhiteSpace(json))
            {
                res.Success = false;
                res.ErrorMessage = "Empty response from GitHub Releases API.";
                return res;
            }

            string tag = ExtractTopLevelJsonString(json, "tag_name");
            int maj, min, pat;
            if (string.IsNullOrEmpty(tag) || !TryParseSemVer(tag, out maj, out min, out pat))
            {
                res.Success = false;
                res.ErrorMessage = "Could not parse a valid release version from GitHub response.";
                return res;
            }

            res.Success = true;
            res.LatestVersion = string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", maj, min, pat);
            res.UpdateAvailable = IsNewerVersion(res.CurrentVersion, res.LatestVersion);
            res.ReleaseName = ExtractTopLevelJsonString(json, "name") ?? ("v" + res.LatestVersion);
            res.ReleaseUrl = SanitizeReleaseUrl(ExtractTopLevelJsonString(json, "html_url"));
            res.PublishedAt = ExtractTopLevelJsonString(json, "published_at") ?? "";

            string body = ExtractTopLevelJsonString(json, "body") ?? "";
            if (body.Length > 900)
                body = body.Substring(0, 900).TrimEnd() + "\n...";
            res.ReleaseNotesExcerpt = body.Trim();
            return res;
        }

        public static UpdateCheckResult CheckLatestRelease(string currentVersion)
        {
            UpdateCheckResult res = new UpdateCheckResult();
            res.CurrentVersion = currentVersion ?? "0.0.0";
            res.ReleaseUrl = LatestReleasePageUrl;

            try
            {
                // Ensure TLS 1.2 is enabled on .NET 4.8 (3072 == SecurityProtocolType.Tls12)
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(LatestReleaseApiUrl);
                req.Method = "GET";
                req.Accept = "application/vnd.github+json";
                req.UserAgent = "MiniFanControl/" + res.CurrentVersion;
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.AllowAutoRedirect = true;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream stream = resp.GetResponseStream())
                {
                    if (stream == null)
                    {
                        res.ErrorMessage = "No response body returned by GitHub.";
                        return res;
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[4096];
                        int total = 0;
                        int read;
                        while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                        {
                            total += read;
                            if (total > MaxResponseBytes) break;
                            ms.Write(buf, 0, read);
                        }
                        string json = Encoding.UTF8.GetString(ms.ToArray());
                        return ParseLatestReleaseJson(json, res.CurrentVersion);
                    }
                }
            }
            catch (WebException wex)
            {
                res.Success = false;
                res.ErrorMessage = "Could not reach GitHub Releases: " + wex.Message;
                return res;
            }
            catch (Exception ex)
            {
                res.Success = false;
                res.ErrorMessage = "Update check failed: " + ex.Message;
                return res;
            }
        }
    }
}
