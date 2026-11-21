// Tupic SDK core: a small JSON reader/writer with no dependencies, so the core runs unchanged in Unity (Mono/IL2CPP,
// WebGL included), plain .NET and other engines. Objects are Dictionary<string, object>, arrays List<object>,
// numbers double (or long when integral), plus string, bool and null.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Tupic.Core
{
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWs();
            var value = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw new FormatException($"Unexpected character at {p.Pos}");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text) =>
            Parse(text) as Dictionary<string, object> ?? throw new FormatException("JSON is not an object");

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case double d: sb.Append(FormatDouble(d)); return;
                case float f: sb.Append(FormatDouble(f)); return;
                case int or long or short or byte or uint or ulong or ushort or sbyte:
                    sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    var first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                case IEnumerable list:
                    sb.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                default:
                    WriteString(sb, v.ToString()); return;
            }
        }

        static string FormatDouble(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            if (Math.Abs(d % 1) < double.Epsilon && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        // <, >, & and the JS line terminators are escaped so a value is safe inside an inline <script>.
                        if (c < 0x20 || c == '<' || c == '>' || c == '&' || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // Typed reads from parsed objects.
        public static string Str(this Dictionary<string, object> o, string key, string fallback = null) =>
            o != null && o.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;

        public static long Long(this Dictionary<string, object> o, string key, long fallback = 0) =>
            o != null && o.TryGetValue(key, out var v) && v is IConvertible ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : fallback;

        public static Dictionary<string, object> Obj(this Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(this Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as List<object> : null;

        sealed class Parser
        {
            readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; }
            public bool End => Pos >= _s.Length;

            public void SkipWs()
            {
                while (!End && char.IsWhiteSpace(_s[Pos])) Pos++;
            }

            public object ReadValue()
            {
                if (End) throw new FormatException("Unexpected end of JSON");
                var c = _s[Pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0) throw new FormatException($"Expected {word} at {Pos}");
                Pos += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var o = new Dictionary<string, object>();
                Pos++;
                SkipWs();
                if (!End && _s[Pos] == '}') { Pos++; return o; }
                while (true)
                {
                    SkipWs();
                    if (End || _s[Pos] != '"') throw new FormatException($"Expected key at {Pos}");
                    var key = ReadString();
                    SkipWs();
                    if (End || _s[Pos] != ':') throw new FormatException($"Expected ':' at {Pos}");
                    Pos++;
                    SkipWs();
                    o[key] = ReadValue();
                    SkipWs();
                    if (End) throw new FormatException("Unterminated object");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == '}') { Pos++; return o; }
                    throw new FormatException($"Expected ',' or '}}' at {Pos}");
                }
            }

            List<object> ReadArray()
            {
                var a = new List<object>();
                Pos++;
                SkipWs();
                if (!End && _s[Pos] == ']') { Pos++; return a; }
                while (true)
                {
                    SkipWs();
                    a.Add(ReadValue());
                    SkipWs();
                    if (End) throw new FormatException("Unterminated array");
                    if (_s[Pos] == ',') { Pos++; continue; }
                    if (_s[Pos] == ']') { Pos++; return a; }
                    throw new FormatException($"Expected ',' or ']' at {Pos}");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                Pos++;
                while (true)
                {
                    if (End) throw new FormatException("Unterminated string");
                    var c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw new FormatException("Unterminated escape");
                    var e = _s[Pos++];
                    switch (e)
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
                            if (Pos + 4 > _s.Length) throw new FormatException("Bad \\u escape");
                            sb.Append((char)Convert.ToInt32(_s.Substring(Pos, 4), 16));
                            Pos += 4;
                            break;
                        default: throw new FormatException($"Bad escape \\{e}");
                    }
                }
            }

            object ReadNumber()
            {
                var start = Pos;
                while (!End && "+-0123456789.eE".IndexOf(_s[Pos]) >= 0) Pos++;
                var token = _s.Substring(start, Pos - start);
                if (token.Length == 0) throw new FormatException($"Unexpected character '{_s[start]}' at {start}");
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 &&
                    long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
                return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
