using System;
using System.Globalization;
using System.Text;

namespace MiniFanControl
{
    /// <summary>
    /// Builds privacy-sanitized JSON payloads and pre-filled GitHub New Issue URLs
    /// for one-click community hardware profile submissions.
    /// </summary>
    public static class HardwareProfileSubmission
    {
        public const string ISSUES_BASE_URL = "https://github.com/kaiserc/MiniFanControl/issues/new";
        public const int MAX_URL_LENGTH = 1800;

        /// <summary>
        /// Removes local filesystem paths (e.g. C:\Users\name\...) so submissions never leak Windows usernames.
        /// </summary>
        public static string SanitizeField(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string s = value.Trim();
            int usersIdx = s.IndexOf("\\Users\\", StringComparison.OrdinalIgnoreCase);
            if (usersIdx >= 0)
            {
                s = s.Substring(0, usersIdx) + "\\Users\\<redacted>";
            }
            return s.Replace("\r", " ").Replace("\n", " ");
        }

        public static string BuildSanitizedJson(SystemHardwareInfo sysInfo, HardwareBackend hw, string appVersion)
        {
            string mfr = SanitizeField(sysInfo != null ? sysInfo.Manufacturer : "");
            string prod = SanitizeField(sysInfo != null ? sysInfo.ProductName : "");
            string board = SanitizeField(sysInfo != null ? sysInfo.BaseBoard : "");
            string cpu = SanitizeField(sysInfo != null ? sysInfo.CpuName : "");
            string bios = SanitizeField(sysInfo != null ? sysInfo.BiosVersion : "");
            string matchedId = (sysInfo != null && sysInfo.MatchedProfile != null) ? sysInfo.MatchedProfile.Id : "unknown-safe";
            bool isVerified = (sysInfo != null && sysInfo.MatchedProfile != null && sysInfo.MatchedProfile.IsVerified);
            bool isReadOnly = (hw != null && (hw.Profile == null || hw.Profile.IsReadOnly));
            string accessMethod = (hw != null) ? hw.AccessMethod.ToString() : "None";
            string sioChip = (hw != null && hw.SuperIoChipId != 0)
                ? string.Format(CultureInfo.InvariantCulture, "0x{0:X4}", hw.SuperIoChipId)
                : "none";
            string sioBase = (hw != null && hw.SuperIoBaseAddr != 0)
                ? string.Format(CultureInfo.InvariantCulture, "0x{0:X4}", hw.SuperIoBaseAddr)
                : "none";
            string driver = NativeInpOut.ActiveDriverName;
            bool hvci = NativeInpOut.IsHvciEnabled();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"appVersion\": \"{0}\",\n", SimpleJson.Escape(appVersion ?? ""));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"manufacturer\": \"{0}\",\n", SimpleJson.Escape(mfr));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"productName\": \"{0}\",\n", SimpleJson.Escape(prod));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"baseBoard\": \"{0}\",\n", SimpleJson.Escape(board));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"cpu\": \"{0}\",\n", SimpleJson.Escape(cpu));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"biosVersion\": \"{0}\",\n", SimpleJson.Escape(bios));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"matchedProfileId\": \"{0}\",\n", SimpleJson.Escape(matchedId));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isVerified\": {0},\n", isVerified ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"isReadOnly\": {0},\n", isReadOnly ? "true" : "false");
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"accessMethod\": \"{0}\",\n", SimpleJson.Escape(accessMethod));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"superIoChipId\": \"{0}\",\n", SimpleJson.Escape(sioChip));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"superIoBaseAddr\": \"{0}\",\n", SimpleJson.Escape(sioBase));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"activeDriver\": \"{0}\",\n", SimpleJson.Escape(driver));
            sb.AppendFormat(CultureInfo.InvariantCulture, "  \"hvciEnabled\": {0}\n", hvci ? "true" : "false");
            sb.Append("}");
            return sb.ToString();
        }

        public static string BuildGitHubIssueUrl(SystemHardwareInfo sysInfo, HardwareBackend hw, string appVersion)
        {
            string mfr = SanitizeField(sysInfo != null ? sysInfo.Manufacturer : "");
            string prod = SanitizeField(sysInfo != null ? sysInfo.ProductName : "");
            string model = string.IsNullOrEmpty(prod) ? (string.IsNullOrEmpty(mfr) ? "Mini PC" : mfr) : (string.IsNullOrEmpty(mfr) ? prod : (mfr + " " + prod));
            if (model.Length > 70) model = model.Substring(0, 70);

            string title = string.Format("[Hardware Profile] {0}", model);
            string json = BuildSanitizedJson(sysInfo, hw, appVersion);

            string body = "### Hardware Profile Submission (Generated by MiniFanControl v" + (appVersion ?? "") + ")\n\n"
                + "```json\n" + json + "\n```\n\n"
                + "*(Full `ec-probe-report.txt` has been copied to your clipboard — paste below if needed.)*\n";

            string url = ISSUES_BASE_URL
                + "?template=unsupported_hardware.md"
                + "&title=" + Uri.EscapeDataString(title)
                + "&body=" + Uri.EscapeDataString(body);

            if (url.Length > MAX_URL_LENGTH)
            {
                string shortBody = "Generated by MiniFanControl v" + (appVersion ?? "") + ". Paste full probe report from clipboard (Ctrl+V).";
                url = ISSUES_BASE_URL
                    + "?template=unsupported_hardware.md"
                    + "&title=" + Uri.EscapeDataString(title)
                    + "&body=" + Uri.EscapeDataString(shortBody);
            }
            return url;
        }
    }
}
