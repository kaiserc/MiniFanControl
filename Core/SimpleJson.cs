using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
#if NET8_0_OR_GREATER
using System.Text.Json;
#endif

namespace MiniFanControl
{
    /// <summary>
    /// Unified zero-dependency JSON tokenizer and string escaper shared across
    /// Localization (Loc), DeviceProfile, and CLI JSON formatting.
    /// Supports both single-line and multi-line JSON objects with full escape handling.
    /// </summary>
    public static class SimpleJson
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
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
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
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

        /// <summary>
        /// Strict flat JSON object parser with explicit error reporting.
        /// On .NET 8/10+, uses System.Text.Json (RFC-8259 strict); on .NET 4.8, uses the hardened tokenizer.
        /// Rejects nested objects/arrays loudly (instead of silently skipping), rejects unclosed quotes
        /// or trailing garbage, and optionally enforces an explicit key allowlist (unknown keys rejected).
        /// </summary>
        public static bool ParseFlatObjectStrict(
            string json,
            Dictionary<string, string> target,
            HashSet<string> allowedKeys,
            out string error,
            bool stringsOnly = false)
        {
            error = null;
            if (target == null)
            {
                error = "Target dictionary is null.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON payload is empty.";
                return false;
            }

#if NET8_0_OR_GREATER
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        error = "Root JSON element must be an object.";
                        return false;
                    }
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        string key = prop.Name;
                        if (allowedKeys != null && !allowedKeys.Contains(key))
                        {
                            error = "Unknown JSON key '" + key + "'.";
                            return false;
                        }
                        JsonValueKind kind = prop.Value.ValueKind;
                        if (kind == JsonValueKind.Object || kind == JsonValueKind.Array)
                        {
                            error = "Unsupported nested object or array for key '" + key + "'.";
                            return false;
                        }
                        if (stringsOnly && kind != JsonValueKind.String)
                        {
                            error = "Expected string value for key '" + key + "'.";
                            return false;
                        }
                        if (kind == JsonValueKind.String)
                            target[key] = prop.Value.GetString() ?? "";
                        else if (kind == JsonValueKind.True)
                            target[key] = "true";
                        else if (kind == JsonValueKind.False)
                            target[key] = "false";
                        else if (kind == JsonValueKind.Number)
                            target[key] = prop.Value.GetRawText();
                        else if (kind == JsonValueKind.Null)
                            target[key] = "null";
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = "Malformed JSON: " + ex.Message;
                return false;
            }
#else
            int i = 0;
            int len = json.Length;
            SkipWhitespace(json, ref i);
            if (i >= len || json[i] != '{')
            {
                error = "Root JSON element must begin with '{'.";
                return false;
            }
            i++; // consume '{'

            bool expectCommaOrEnd = false;
            while (i < len)
            {
                SkipWhitespace(json, ref i);
                if (i >= len)
                {
                    error = "Unterminated JSON object (missing '}').";
                    return false;
                }
                if (json[i] == '}')
                {
                    i++;
                    SkipWhitespace(json, ref i);
                    if (i < len)
                    {
                        error = "Unexpected trailing characters after closing '}'.";
                        return false;
                    }
                    return true;
                }
                if (expectCommaOrEnd)
                {
                    if (json[i] != ',')
                    {
                        error = "Expected ',' or '}' between JSON properties.";
                        return false;
                    }
                    i++;
                    SkipWhitespace(json, ref i);
                    if (i >= len || json[i] == '}')
                    {
                        error = "Trailing comma in JSON object.";
                        return false;
                    }
                }
                if (json[i] != '"')
                {
                    error = "Expected quoted property name at offset " + i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                string key;
                if (!TryReadQuotedStringStrict(json, ref i, out key))
                {
                    error = "Unterminated string key at offset " + i.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }
                if (allowedKeys != null && !allowedKeys.Contains(key))
                {
                    error = "Unknown JSON key '" + key + "'.";
                    return false;
                }

                SkipWhitespace(json, ref i);
                if (i >= len || json[i] != ':')
                {
                    error = "Expected ':' after key '" + key + "'.";
                    return false;
                }
                i++; // consume ':'
                SkipWhitespace(json, ref i);
                if (i >= len)
                {
                    error = "Missing value for key '" + key + "'.";
                    return false;
                }

                if (json[i] == '"')
                {
                    string val;
                    if (!TryReadQuotedStringStrict(json, ref i, out val))
                    {
                        error = "Unterminated string value for key '" + key + "'.";
                        return false;
                    }
                    target[key] = val;
                }
                else if (json[i] == '{' || json[i] == '[')
                {
                    error = "Unsupported nested object or array for key '" + key + "'.";
                    return false;
                }
                else
                {
                    if (stringsOnly)
                    {
                        error = "Expected string value for key '" + key + "'.";
                        return false;
                    }
                    int start = i;
                    while (i < len && json[i] != ',' && json[i] != '}' && !char.IsWhiteSpace(json[i]))
                        i++;
                    string literal = json.Substring(start, i - start).Trim();
                    if (!IsValidJsonPrimitiveLiteral(literal))
                    {
                        error = "Invalid JSON literal '" + literal + "' for key '" + key + "'.";
                        return false;
                    }
                    target[key] = literal;
                }
                expectCommaOrEnd = true;
            }

            error = "Unterminated JSON object.";
            return false;
#endif
        }

#if !NET8_0_OR_GREATER
        private static bool TryReadQuotedStringStrict(string json, ref int pos, out string value)
        {
            value = "";
            if (pos >= json.Length || json[pos] != '"') return false;
            pos++;
            int start = pos;
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '\\')
                {
                    if (pos + 1 >= json.Length) return false;
                    pos += 2;
                    continue;
                }
                if (c == '"')
                {
                    string raw = json.Substring(start, pos - start);
                    pos++;
                    value = Unescape(raw);
                    return true;
                }
                pos++;
            }
            return false;
        }

        private static bool IsValidJsonPrimitiveLiteral(string literal)
        {
            if (string.IsNullOrEmpty(literal)) return false;
            if (literal == "true" || literal == "false" || literal == "null") return true;
            double d;
            return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }
#endif

        /// <summary>
        /// Parses a flat JSON object (single-line or multi-line) into key -> string-representation pairs.
        /// Supports quoted strings, booleans (true/false), numbers, and null. Returns number of parsed entries.
        /// </summary>
        public static int ParseFlatObject(string json, Dictionary<string, string> target, bool stringsOnly = false)
        {
            if (string.IsNullOrWhiteSpace(json) || target == null) return 0;

            int i = 0;
            int len = json.Length;
            SkipWhitespace(json, ref i);
            if (i >= len || json[i] != '{') return 0;
            i++; // consume '{'

            int count = 0;
            while (i < len)
            {
                SkipWhitespace(json, ref i);
                if (i >= len) break;
                if (json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                if (json[i] != '"')
                {
                    // Malformed token — abort parsing fail-closed
                    break;
                }

                string key = ReadQuotedString(json, ref i);
                SkipWhitespace(json, ref i);
                if (i >= len || json[i] != ':') break;
                i++; // consume ':'
                SkipWhitespace(json, ref i);
                if (i >= len) break;

                if (json[i] == '"')
                {
                    string val = ReadQuotedString(json, ref i);
                    if (!string.IsNullOrEmpty(key))
                    {
                        target[key] = val;
                        count++;
                    }
                }
                else if (json[i] == '{' || json[i] == '[')
                {
                    SkipCompositeValue(json, ref i);
                }
                else
                {
                    int start = i;
                    while (i < len && json[i] != ',' && json[i] != '}' && !char.IsWhiteSpace(json[i]))
                        i++;
                    string literal = json.Substring(start, i - start).Trim();
                    if (!stringsOnly && !string.IsNullOrEmpty(key) && literal.Length > 0)
                    {
                        target[key] = literal;
                        count++;
                    }
                }
            }

            return count;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            int len = s.Length;
            while (pos < len && char.IsWhiteSpace(s[pos])) pos++;
        }

        private static string ReadQuotedString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') return "";
            pos++; // skip opening quote
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
                    pos++; // skip closing quote
                    return Unescape(raw);
                }
                pos++;
            }
            return Unescape(json.Substring(start));
        }

        private static void SkipCompositeValue(string json, ref int pos)
        {
            int depth = 0;
            int len = json.Length;
            while (pos < len)
            {
                char c = json[pos];
                if (c == '"')
                {
                    ReadQuotedString(json, ref pos);
                    continue;
                }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    pos++;
                    if (depth <= 0) return;
                    continue;
                }
                pos++;
            }
        }
    }
}
