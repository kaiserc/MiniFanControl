using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MiniFanControl
{
    public enum AppLanguage
    {
        Auto = 0,
        English = 1,
        Chinese = 2,
        German = 3,
        Spanish = 4,
        French = 5,
        Japanese = 6
    }

    /// <summary>
    /// Zero-dependency standalone localization engine for MiniFanControl.
    /// Provides built-in translations for English, Chinese (Simplified), German, Spanish, French, and Japanese.
    /// Supports drop-in community language JSON files from the 'lang/' directory.
    /// </summary>
    public static class Loc
    {
        public static AppLanguage CurrentLanguage { get; private set; }
        public static string CurrentLanguageCode { get; private set; }

        private static readonly Dictionary<string, string> _stringsEn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _stringsZh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _stringsDe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _stringsEs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _stringsFr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _stringsJa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _stringsCustom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static Loc()
        {
            CurrentLanguage = AppLanguage.Auto;
            CurrentLanguageCode = "en";
            InitBuiltInStrings();
            DetectAndSetLanguage(AppLanguage.Auto);
        }

        public static void SetLanguage(AppLanguage lang)
        {
            CurrentLanguage = lang;
            if (lang == AppLanguage.Auto)
            {
                string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                switch (iso)
                {
                    case "zh": CurrentLanguageCode = "zh"; break;
                    case "de": CurrentLanguageCode = "de"; break;
                    case "es": CurrentLanguageCode = "es"; break;
                    case "fr": CurrentLanguageCode = "fr"; break;
                    case "ja": CurrentLanguageCode = "ja"; break;
                    default: CurrentLanguageCode = "en"; break;
                }
            }
            else
            {
                switch (lang)
                {
                    case AppLanguage.Chinese: CurrentLanguageCode = "zh"; break;
                    case AppLanguage.German: CurrentLanguageCode = "de"; break;
                    case AppLanguage.Spanish: CurrentLanguageCode = "es"; break;
                    case AppLanguage.French: CurrentLanguageCode = "fr"; break;
                    case AppLanguage.Japanese: CurrentLanguageCode = "ja"; break;
                    default: CurrentLanguageCode = "en"; break;
                }
            }

            // Attempt to load external community language file if present in 'lang/'
            LoadExternalLanguage(CurrentLanguageCode);
        }

        public static void DetectAndSetLanguage(AppLanguage lang)
        {
            SetLanguage(lang);
        }

        public static string T(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key)) return "";

            // 1. Check custom loaded community strings
            if (_stringsCustom.ContainsKey(key) && !string.IsNullOrEmpty(_stringsCustom[key]))
                return _stringsCustom[key];

            // 2. Check active language dictionary
            Dictionary<string, string> dict = GetDictionaryForCode(CurrentLanguageCode);
            if (dict != null && dict.ContainsKey(key) && !string.IsNullOrEmpty(dict[key]))
                return dict[key];

            // 3. Fallback to English dictionary
            if (_stringsEn.ContainsKey(key) && !string.IsNullOrEmpty(_stringsEn[key]))
                return _stringsEn[key];

            // 4. Return fallback or key
            return fallback ?? key;
        }

        public static string Get(string key, string fallback = null)
        {
            return T(key, fallback);
        }

        public static string F(string key, params object[] args)
        {
            string fmt = T(key);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, fmt, args);
            }
            catch
            {
                return fmt;
            }
        }

        private static Dictionary<string, string> GetDictionaryForCode(string code)
        {
            switch (code)
            {
                case "zh": return _stringsZh;
                case "de": return _stringsDe;
                case "es": return _stringsEs;
                case "fr": return _stringsFr;
                case "ja": return _stringsJa;
                default: return _stringsEn;
            }
        }

        public static void LoadCustomJson(string json)
        {
            _stringsCustom.Clear();
            ParseJsonStrings(json, _stringsCustom);
        }

        public static void ClearCustomStrings()
        {
            _stringsCustom.Clear();
        }

        public static bool ValidateLanguageFiles(string langDir, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrEmpty(langDir) || !Directory.Exists(langDir))
            {
                errors.Add("Language directory does not exist: " + langDir);
                return false;
            }

            string templatePath = Path.Combine(langDir, "template.json");
            if (!File.Exists(templatePath))
            {
                errors.Add("Master template.json missing from " + langDir);
                return false;
            }

            // 1. Parse template.json to extract master required keys
            Dictionary<string, string> templateKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string templateContent = File.ReadAllText(templatePath, Encoding.UTF8);
                ParseJsonStrings(templateContent, templateKeys);
            }
            catch (Exception ex)
            {
                errors.Add("Failed to parse template.json: " + ex.Message);
                return false;
            }

            // Remove metadata comments
            templateKeys.Remove("_comment");

            if (templateKeys.Count == 0)
            {
                errors.Add("template.json contains 0 valid string keys");
                return false;
            }

            // 2. Scan all *.json files in langDir
            string[] jsonFiles = Directory.GetFiles(langDir, "*.json");
            foreach (string file in jsonFiles)
            {
                string fileName = Path.GetFileName(file);
                if (fileName.Equals("template.json", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    string content = File.ReadAllText(file, Encoding.UTF8);
                    Dictionary<string, string> fileDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    ParseJsonStrings(content, fileDict);

                    // Verify each master key
                    foreach (string masterKey in templateKeys.Keys)
                    {
                        if (!fileDict.ContainsKey(masterKey) || string.IsNullOrEmpty(fileDict[masterKey]))
                        {
                            errors.Add(string.Format("{0}: Missing required translation key '{1}'", fileName, masterKey));
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format("{0}: Failed to parse JSON: {1}", fileName, ex.Message));
                }
            }

            return errors.Count == 0;
        }

        public static bool LoadExternalLanguage(string code)
        {
            try
            {
                _stringsCustom.Clear();
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string langDir = Path.Combine(appDir, "lang");
                if (!Directory.Exists(langDir)) return false;

                string jsonPath = Path.Combine(langDir, code + ".json");
                if (File.Exists(jsonPath))
                {
                    string text = File.ReadAllText(jsonPath, Encoding.UTF8);
                    ParseJsonStrings(text, _stringsCustom);
                    return _stringsCustom.Count > 0;
                }
            }
            catch { }
            return false;
        }

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 < s.Length)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                    break;
                                }
                            }
                            sb.Append("\\u");
                            break;
                        default: sb.Append(n); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static void ParseJsonStrings(string json, Dictionary<string, string> dict)
        {
            if (string.IsNullOrEmpty(json)) return;
            int i = 0;
            int len = json.Length;
            while (i < len)
            {
                while (i < len && json[i] != '"') i++;
                if (i >= len) break;
                string k = ReadJsonTokenString(json, ref i);
                while (i < len && char.IsWhiteSpace(json[i])) i++;
                if (i < len && json[i] == ':')
                {
                    i++;
                    while (i < len && char.IsWhiteSpace(json[i])) i++;
                    if (i < len && json[i] == '"')
                    {
                        string v = ReadJsonTokenString(json, ref i);
                        if (!string.IsNullOrEmpty(k))
                        {
                            dict[k] = v;
                        }
                    }
                }
            }
        }

        private static string ReadJsonTokenString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') return "";
            pos++;
            int start = pos;
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '\\')
                {
                    pos += 2;
                    continue;
                }
                if (c == '"')
                {
                    string raw = json.Substring(start, pos - start);
                    pos++;
                    return UnescapeJsonString(raw);
                }
                pos++;
            }
            return UnescapeJsonString(json.Substring(start));
        }

        private static void InitBuiltInStrings()
        {
            // English (Default Master)
            _stringsEn["Mode_Balanced"] = "Balanced";
            _stringsEn["Mode_Performance"] = "Performance";
            _stringsEn["Mode_Silent"] = "Silent";
            _stringsEn["Mode_AiWorkload"] = "AI Workload";
            _stringsEn["Btn_Balanced"] = "⚖ Balanced";
            _stringsEn["Btn_Performance"] = "⚡ Performance";
            _stringsEn["Btn_Silent"] = "🌙 Silent";
            _stringsEn["Btn_AiWorkload"] = "🤖 AI Workload";
            _stringsEn["Section_Telemetry"] = "System Telemetry & Live Hardware Sensors";
            _stringsEn["Section_Curves"] = "Modes, Fan Curves & Live Telemetry Graph";
            _stringsEn["Section_Manual"] = "Manual Fan Control";
            _stringsEn["Section_Options"] = "Options";
            _stringsEn["Label_CpuTemp"] = "CPU TEMP";
            _stringsEn["Label_GpuTemp"] = "iGPU TEMP";
            _stringsEn["Label_NvmeTemp"] = "NVMe TEMP";
            _stringsEn["Label_ApuPower"] = "POWER •";
            _stringsEn["Label_RamPower"] = "RAM POWER";
            _stringsEn["Label_ApuFan"] = "APU FAN";
            _stringsEn["Label_SysFan"] = "SYS FAN";
            _stringsEn["Label_Custom"] = "Custom:";
            _stringsEn["Btn_UseCurve"] = "Use Curve";
            _stringsEn["Btn_AutoTune"] = "✨ Auto-Tune";
            _stringsEn["Btn_New"] = "+ New";
            _stringsEn["Btn_Del"] = "Del";
            _stringsEn["Btn_Reset"] = "Reset";
            _stringsEn["Tab_LiveTrend"] = "• Live Trend";
            _stringsEn["Tab_LivePerf"] = "• Live Perf";
            _stringsEn["Tab_OsdStudio"] = "• OSD Studio";
            _stringsEn["Osd_VrmCurrent"] = "VRM Current (TDC/EDC)";
            _stringsEn["Osd_FabricClock"] = "Infinity Fabric (FCLK)";
            _stringsEn["Osd_RyzenNpu"] = "Ryzen AI NPU Load";
            _stringsEn["Osd_GameTitle"] = "Active Game Title";

            // Chinese Simplified (简体中文)
            _stringsZh["Mode_Balanced"] = "平衡模式";
            _stringsZh["Mode_Performance"] = "高性能";
            _stringsZh["Mode_Silent"] = "静音模式";
            _stringsZh["Mode_AiWorkload"] = "AI工作负载";
            _stringsZh["Btn_Balanced"] = "⚖ 平衡";
            _stringsZh["Btn_Performance"] = "⚡ 性能";
            _stringsZh["Btn_Silent"] = "🌙 静音";
            _stringsZh["Btn_AiWorkload"] = "🤖 AI负载";
            _stringsZh["Section_Telemetry"] = "系统硬件遥测与实时传感器";
            _stringsZh["Section_Curves"] = "模式配置、风扇曲线与实时趋势图";
            _stringsZh["Section_Manual"] = "手动风扇调节";
            _stringsZh["Section_Options"] = "设置与选项";
            _stringsZh["Label_CpuTemp"] = "CPU 温度";
            _stringsZh["Label_GpuTemp"] = "iGPU 显卡温度";
            _stringsZh["Label_NvmeTemp"] = "固态硬盘温度";
            _stringsZh["Label_ApuPower"] = "APU 功耗";
            _stringsZh["Label_RamPower"] = "内存功耗";
            _stringsZh["Label_ApuFan"] = "处理器风扇";
            _stringsZh["Label_SysFan"] = "系统风扇";
            _stringsZh["Label_Custom"] = "自定义曲线:";
            _stringsZh["Btn_UseCurve"] = "启用曲线";
            _stringsZh["Btn_AutoTune"] = "✨ 智能调优";
            _stringsZh["Btn_New"] = "+ 新建";
            _stringsZh["Btn_Del"] = "删除";
            _stringsZh["Btn_Reset"] = "重置";
            _stringsZh["Tab_LiveTrend"] = "• 实时图表";
            _stringsZh["Tab_LivePerf"] = "• 游戏性能";
            _stringsZh["Tab_OsdStudio"] = "• OSD悬浮窗工作室";
            _stringsZh["Osd_VrmCurrent"] = "供电电流 (TDC/EDC)";
            _stringsZh["Osd_FabricClock"] = "总线频率 (FCLK)";
            _stringsZh["Osd_RyzenNpu"] = "Ryzen AI NPU 负载";
            _stringsZh["Osd_GameTitle"] = "当前游戏标题";

            // German (Deutsch)
            _stringsDe["Mode_Balanced"] = "Ausbalanciert";
            _stringsDe["Mode_Performance"] = "Leistung";
            _stringsDe["Mode_Silent"] = "Leise";
            _stringsDe["Mode_AiWorkload"] = "KI-Workload";
            _stringsDe["Btn_Balanced"] = "⚖ Ausbalanciert";
            _stringsDe["Btn_Performance"] = "⚡ Leistung";
            _stringsDe["Btn_Silent"] = "🌙 Leise";
            _stringsDe["Btn_AiWorkload"] = "🤖 KI-Workload";
            _stringsDe["Section_Telemetry"] = "Systemtelemetrie & Hardware-Sensoren";
            _stringsDe["Section_Curves"] = "Modi, Lüfterkurven & Live-Diagramm";
            _stringsDe["Section_Manual"] = "Manuelle Lüftersteuerung";
            _stringsDe["Section_Options"] = "Optionen";
            _stringsDe["Label_CpuTemp"] = "CPU TEMP";
            _stringsDe["Label_GpuTemp"] = "iGPU TEMP";
            _stringsDe["Label_NvmeTemp"] = "NVMe TEMP";
            _stringsDe["Label_ApuPower"] = "LEISTUNG •";
            _stringsDe["Label_RamPower"] = "RAM LEISTUNG";
            _stringsDe["Label_ApuFan"] = "APU LÜFTER";
            _stringsDe["Label_SysFan"] = "SYS LÜFTER";
            _stringsDe["Label_Custom"] = "Benutzerdefiniert:";
            _stringsDe["Btn_UseCurve"] = "Aktivieren";
            _stringsDe["Btn_AutoTune"] = "✨ Auto-Tune";
            _stringsDe["Btn_New"] = "+ Neu";
            _stringsDe["Btn_Del"] = "Löschen";
            _stringsDe["Btn_Reset"] = "Reset";
            _stringsDe["Tab_LiveTrend"] = "• Live-Trend";
            _stringsDe["Tab_LivePerf"] = "• Live-Leistung";
            _stringsDe["Tab_OsdStudio"] = "• OSD Studio";
            _stringsDe["Osd_VrmCurrent"] = "VRM Stromstärke (TDC/EDC)";
            _stringsDe["Osd_FabricClock"] = "Infinity Fabric (FCLK)";
            _stringsDe["Osd_RyzenNpu"] = "Ryzen AI NPU Auslastung";
            _stringsDe["Osd_GameTitle"] = "Aktiver Spieletitel";

            // Spanish (Español)
            _stringsEs["Mode_Balanced"] = "Equilibrado";
            _stringsEs["Mode_Performance"] = "Rendimiento";
            _stringsEs["Mode_Silent"] = "Silencioso";
            _stringsEs["Mode_AiWorkload"] = "Carga de IA";
            _stringsEs["Btn_Balanced"] = "⚖ Equilibrado";
            _stringsEs["Btn_Performance"] = "⚡ Rendimiento";
            _stringsEs["Btn_Silent"] = "🌙 Silencioso";
            _stringsEs["Btn_AiWorkload"] = "🤖 Carga IA";
            _stringsEs["Section_Telemetry"] = "Telemetría del sistema y sensores en vivo";
            _stringsEs["Section_Curves"] = "Modos, curvas de ventilador y gráfico en vivo";
            _stringsEs["Section_Manual"] = "Control manual de ventiladores";
            _stringsEs["Section_Options"] = "Opciones";
            _stringsEs["Label_CpuTemp"] = "TEMP CPU";
            _stringsEs["Label_GpuTemp"] = "TEMP iGPU";
            _stringsEs["Label_NvmeTemp"] = "TEMP NVMe";
            _stringsEs["Label_ApuPower"] = "POTENCIA •";
            _stringsEs["Label_RamPower"] = "POTENCIA RAM";
            _stringsEs["Label_ApuFan"] = "VENTILADOR APU";
            _stringsEs["Label_SysFan"] = "VENTILADOR SYS";
            _stringsEs["Label_Custom"] = "Personalizado:";
            _stringsEs["Btn_UseCurve"] = "Usar curva";
            _stringsEs["Btn_AutoTune"] = "✨ Auto-Ajuste";
            _stringsEs["Btn_New"] = "+ Nueva";
            _stringsEs["Btn_Del"] = "Borrar";
            _stringsEs["Btn_Reset"] = "Restablecer";
            _stringsEs["Tab_LiveTrend"] = "• Tendencia en vivo";
            _stringsEs["Tab_LivePerf"] = "• Rendimiento en vivo";
            _stringsEs["Tab_OsdStudio"] = "• Estudio OSD";
            _stringsEs["Osd_VrmCurrent"] = "Corriente VRM (TDC/EDC)";
            _stringsEs["Osd_FabricClock"] = "Infinity Fabric (FCLK)";
            _stringsEs["Osd_RyzenNpu"] = "Carga Ryzen AI NPU";
            _stringsEs["Osd_GameTitle"] = "Título del juego activo";

            // French (Français)
            _stringsFr["Mode_Balanced"] = "Équilibré";
            _stringsFr["Mode_Performance"] = "Performance";
            _stringsFr["Mode_Silent"] = "Silencieux";
            _stringsFr["Mode_AiWorkload"] = "Charge IA";
            _stringsFr["Btn_Balanced"] = "⚖ Équilibré";
            _stringsFr["Btn_Performance"] = "⚡ Performance";
            _stringsFr["Btn_Silent"] = "🌙 Silencieux";
            _stringsFr["Btn_AiWorkload"] = "🤖 Charge IA";
            _stringsFr["Section_Telemetry"] = "Télémétrie système et capteurs matériels";
            _stringsFr["Section_Curves"] = "Modes, courbes de ventilation et graphique en direct";
            _stringsFr["Section_Manual"] = "Contrôle manuel des ventilateurs";
            _stringsFr["Section_Options"] = "Options";
            _stringsFr["Label_CpuTemp"] = "TEMP CPU";
            _stringsFr["Label_GpuTemp"] = "TEMP iGPU";
            _stringsFr["Label_NvmeTemp"] = "TEMP NVMe";
            _stringsFr["Label_ApuPower"] = "PUISSANCE •";
            _stringsFr["Label_RamPower"] = "PUISSANCE RAM";
            _stringsFr["Label_ApuFan"] = "VENTILATEUR APU";
            _stringsFr["Label_SysFan"] = "VENTILATEUR SYS";
            _stringsFr["Label_Custom"] = "Personnalisé :";
            _stringsFr["Btn_UseCurve"] = "Appliquer";
            _stringsFr["Btn_AutoTune"] = "✨ Auto-Tune";
            _stringsFr["Btn_New"] = "+ Nouveau";
            _stringsFr["Btn_Del"] = "Suppr";
            _stringsFr["Btn_Reset"] = "Réinit";
            _stringsFr["Tab_LiveTrend"] = "• Tendance en direct";
            _stringsFr["Tab_LivePerf"] = "• Performances en direct";
            _stringsFr["Tab_OsdStudio"] = "• Studio OSD";
            _stringsFr["Osd_VrmCurrent"] = "Courant VRM (TDC/EDC)";
            _stringsFr["Osd_FabricClock"] = "Infinity Fabric (FCLK)";
            _stringsFr["Osd_RyzenNpu"] = "Charge Ryzen AI NPU";
            _stringsFr["Osd_GameTitle"] = "Titre du jeu actif";

            // Japanese (日本語)
            _stringsJa["Mode_Balanced"] = "バランス";
            _stringsJa["Mode_Performance"] = "パフォーマンス";
            _stringsJa["Mode_Silent"] = "静音";
            _stringsJa["Mode_AiWorkload"] = "AIワークロード";
            _stringsJa["Btn_Balanced"] = "⚖ バランス";
            _stringsJa["Btn_Performance"] = "⚡ 性能";
            _stringsJa["Btn_Silent"] = "🌙 静音";
            _stringsJa["Btn_AiWorkload"] = "🤖 AI負荷";
            _stringsJa["Section_Telemetry"] = "システムテレメトリとライブセンサー";
            _stringsJa["Section_Curves"] = "モード、ファンカーブ、リアルタイムグラフ";
            _stringsJa["Section_Manual"] = "手動ファン制御";
            _stringsJa["Section_Options"] = "設定・オプション";
            _stringsJa["Label_CpuTemp"] = "CPU 温度";
            _stringsJa["Label_GpuTemp"] = "iGPU 温度";
            _stringsJa["Label_NvmeTemp"] = "NVMe 温度";
            _stringsJa["Label_ApuPower"] = "APU 電力 •";
            _stringsJa["Label_RamPower"] = "RAM 電力";
            _stringsJa["Label_ApuFan"] = "APU ファン";
            _stringsJa["Label_SysFan"] = "システムファン";
            _stringsJa["Label_Custom"] = "カスタム:";
            _stringsJa["Btn_UseCurve"] = "適用";
            _stringsJa["Btn_AutoTune"] = "✨ 自動最適化";
            _stringsJa["Btn_New"] = "+ 新規";
            _stringsJa["Btn_Del"] = "削除";
            _stringsJa["Btn_Reset"] = "リセット";
            _stringsJa["Tab_LiveTrend"] = "• ライブ推移";
            _stringsJa["Tab_LivePerf"] = "• ライブパフォーマンス";
            _stringsJa["Tab_OsdStudio"] = "• OSD スタジオ";
            _stringsJa["Osd_VrmCurrent"] = "VRM 電流 (TDC/EDC)";
            _stringsJa["Osd_FabricClock"] = "バス周波数 (FCLK)";
            _stringsJa["Osd_RyzenNpu"] = "Ryzen AI NPU 負荷";
            _stringsJa["Osd_GameTitle"] = "アクティブなゲーム名";
        }
    }
}
