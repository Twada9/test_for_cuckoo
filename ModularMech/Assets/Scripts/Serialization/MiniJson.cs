using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ModularMech.Serialization
{
    /// <summary>
    /// 依存ゼロの最小 JSON 実装。<see cref="global::UnityEngine.JsonUtility"/> は
    /// Dictionary/List のような動的な形を扱えないため、Loadout 保存データのような
    /// 「スロット→パーツID」の連想配列を読み書きするためだけに自前で持つ。
    ///
    /// <para>
    /// 対応する値の形: object(Dictionary&lt;string,object&gt;) / array(List&lt;object&gt;) /
    /// string / number(常に double として読む) / true / false / null。
    /// </para>
    /// <para>
    /// 非対応・注意点(呼び出し側は前提にしないこと):
    /// <list type="bullet">
    /// <item>コメント (// や /* */) は書けない。</item>
    /// <item>末尾カンマ、単一引用符文字列、裸の NaN/Infinity リテラルは読めない。</item>
    /// <item>16進数値リテラル (0x..) は読めない。</item>
    /// <item>数値の指数表記 (1e10 等) は読み込みには対応するが、書き出しは常に
    /// 通常の整数/小数表記になる(指数表記では書き出さない)。</item>
    /// <item>数値は内部的に常に double で保持される。int64 の全域や 10進小数の
    /// 完全な精度は保証しない(Loadout の version/index 程度の用途を想定)。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class MiniJson
    {
        // 悪意のある/壊れた入力(極端に深いネスト)で StackOverflow しないための上限。
        // 実運用の Loadout JSON は深さ3〜4程度なので、64 あれば十分すぎる余裕がある。
        private const int MaxDepth = 64;

        /// <summary>
        /// JSON 文字列を Dictionary/List/string/double/bool/null の木にして返す。
        /// 壊れた JSON では例外を投げず null を返す。呼び出し側はこれを警告として扱えばよい。
        /// </summary>
        public static object Deserialize(string json)
        {
            if (json == null)
            {
                return null;
            }

            try
            {
                var parser = new Parser(json);
                object result = parser.ParseRoot();
                return result;
            }
            catch (JsonParseException)
            {
                return null;
            }
            catch (Exception)
            {
                // パーサ内部の想定外の例外も「壊れたJSON」として握りつぶす。
                // ここで落とすと呼び出し側の「例外ではなく警告」という契約が崩れるため。
                return null;
            }
        }

        /// <summary>
        /// Dictionary&lt;string,object&gt; / IEnumerable&lt;object&gt; / string / bool / 数値 / null
        /// の木を JSON 文字列へ書き出す。上記以外の型が混ざっている場合はプログラムのバグとみなし
        /// ArgumentException を投げる(こちらは外部入力ではなく自前で組み立てたオブジェクトの想定のため)。
        /// </summary>
        public static string Serialize(object value, bool pretty = false)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private sealed class JsonParseException : Exception
        {
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _pos;

            public Parser(string s)
            {
                _s = s;
                _pos = 0;
            }

            public object ParseRoot()
            {
                object result = ParseValue(0);
                SkipWhitespace();
                if (!IsAtEnd)
                {
                    // 先頭の値の後ろに余剰トークンがある = 壊れたJSON。
                    throw new JsonParseException();
                }
                return result;
            }

            private bool IsAtEnd => _pos >= _s.Length;

            private void SkipWhitespace()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        _pos++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private char Peek()
            {
                if (IsAtEnd)
                {
                    throw new JsonParseException();
                }
                return _s[_pos];
            }

            private char Next()
            {
                if (IsAtEnd)
                {
                    throw new JsonParseException();
                }
                return _s[_pos++];
            }

            private object ParseValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw new JsonParseException();
                }

                SkipWhitespace();
                char c = Peek();
                switch (c)
                {
                    case '{':
                        return ParseObject(depth);
                    case '[':
                        return ParseArray(depth);
                    case '"':
                        return ParseString();
                    case 't':
                        ExpectLiteral("true");
                        return true;
                    case 'f':
                        ExpectLiteral("false");
                        return false;
                    case 'n':
                        ExpectLiteral("null");
                        return null;
                    default:
                        if (c == '-' || IsDigit(c))
                        {
                            return ParseNumber();
                        }
                        throw new JsonParseException();
                }
            }

            private Dictionary<string, object> ParseObject(int depth)
            {
                var dict = new Dictionary<string, object>();
                if (Next() != '{')
                {
                    throw new JsonParseException();
                }

                SkipWhitespace();
                if (!IsAtEnd && Peek() == '}')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (IsAtEnd || Peek() != '"')
                    {
                        throw new JsonParseException();
                    }
                    string key = ParseString();

                    SkipWhitespace();
                    if (Next() != ':')
                    {
                        throw new JsonParseException();
                    }

                    object val = ParseValue(depth + 1);
                    dict[key] = val;

                    SkipWhitespace();
                    char c = Next();
                    if (c == ',')
                    {
                        continue;
                    }
                    if (c == '}')
                    {
                        break;
                    }
                    throw new JsonParseException();
                }

                return dict;
            }

            private List<object> ParseArray(int depth)
            {
                var list = new List<object>();
                if (Next() != '[')
                {
                    throw new JsonParseException();
                }

                SkipWhitespace();
                if (!IsAtEnd && Peek() == ']')
                {
                    _pos++;
                    return list;
                }

                while (true)
                {
                    object val = ParseValue(depth + 1);
                    list.Add(val);

                    SkipWhitespace();
                    char c = Next();
                    if (c == ',')
                    {
                        continue;
                    }
                    if (c == ']')
                    {
                        break;
                    }
                    throw new JsonParseException();
                }

                return list;
            }

            private string ParseString()
            {
                if (Next() != '"')
                {
                    throw new JsonParseException();
                }

                var sb = new StringBuilder();
                while (true)
                {
                    if (IsAtEnd)
                    {
                        throw new JsonParseException();
                    }
                    char c = _s[_pos++];
                    if (c == '"')
                    {
                        break;
                    }

                    if (c == '\\')
                    {
                        if (IsAtEnd)
                        {
                            throw new JsonParseException();
                        }
                        char esc = _s[_pos++];
                        switch (esc)
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
                                if (_pos + 4 > _s.Length)
                                {
                                    throw new JsonParseException();
                                }
                                string hex = _s.Substring(_pos, 4);
                                if (!ushort.TryParse(
                                        hex,
                                        NumberStyles.AllowHexSpecifier,
                                        CultureInfo.InvariantCulture,
                                        out ushort code))
                                {
                                    throw new JsonParseException();
                                }
                                sb.Append((char)code);
                                _pos += 4;
                                break;
                            default:
                                throw new JsonParseException();
                        }
                    }
                    else if (c < 0x20)
                    {
                        // 生の制御文字はエスケープなしでは許容しない(JSON仕様準拠)。
                        throw new JsonParseException();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                return sb.ToString();
            }

            private object ParseNumber()
            {
                int start = _pos;
                if (!IsAtEnd && _s[_pos] == '-')
                {
                    _pos++;
                }

                if (IsAtEnd || !IsDigit(_s[_pos]))
                {
                    throw new JsonParseException();
                }

                if (_s[_pos] == '0')
                {
                    _pos++;
                }
                else
                {
                    while (!IsAtEnd && IsDigit(_s[_pos]))
                    {
                        _pos++;
                    }
                }

                if (!IsAtEnd && _s[_pos] == '.')
                {
                    _pos++;
                    if (IsAtEnd || !IsDigit(_s[_pos]))
                    {
                        throw new JsonParseException();
                    }
                    while (!IsAtEnd && IsDigit(_s[_pos]))
                    {
                        _pos++;
                    }
                }

                if (!IsAtEnd && (_s[_pos] == 'e' || _s[_pos] == 'E'))
                {
                    _pos++;
                    if (!IsAtEnd && (_s[_pos] == '+' || _s[_pos] == '-'))
                    {
                        _pos++;
                    }
                    if (IsAtEnd || !IsDigit(_s[_pos]))
                    {
                        throw new JsonParseException();
                    }
                    while (!IsAtEnd && IsDigit(_s[_pos]))
                    {
                        _pos++;
                    }
                }

                string token = _s.Substring(start, _pos - start);
                if (!double.TryParse(
                        token,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double result))
                {
                    throw new JsonParseException();
                }

                return result;
            }

            private void ExpectLiteral(string literal)
            {
                if (_pos + literal.Length > _s.Length ||
                    string.CompareOrdinal(_s, _pos, literal, 0, literal.Length) != 0)
                {
                    throw new JsonParseException();
                }
                _pos += literal.Length;
            }

            private static bool IsDigit(char c)
            {
                return c >= '0' && c <= '9';
            }
        }

        private static void WriteValue(StringBuilder sb, object value, bool pretty, int indent)
        {
            if (indent > MaxDepth)
            {
                // 呼び出し側が組み立てたオブジェクトが異常に深い(循環参照の疑いを含む)。
                // 外部入力ではなく自前のバグなので例外で気づけるようにする。
                throw new InvalidOperationException(
                    "MiniJson.Serialize: 構造の深さが上限を超えました。循環参照の可能性があります。");
            }

            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case IDictionary<string, object> dict:
                    WriteObject(sb, dict, pretty, indent);
                    return;
                case IEnumerable<object> list:
                    WriteArray(sb, list, pretty, indent);
                    return;
            }

            if (IsNumeric(value))
            {
                WriteNumber(sb, Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            }

            throw new ArgumentException(
                $"MiniJson.Serialize: シリアライズできない型です ({value.GetType()})。" +
                "Dictionary<string,object> / IEnumerable<object> / string / bool / 数値 / null のみ対応。");
        }

        private static void WriteObject(
            StringBuilder sb, IDictionary<string, object> dict, bool pretty, int indent)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;

                if (pretty)
                {
                    sb.Append('\n');
                    AppendIndent(sb, indent + 1);
                }

                WriteString(sb, kvp.Key);
                sb.Append(':');
                if (pretty)
                {
                    sb.Append(' ');
                }
                WriteValue(sb, kvp.Value, pretty, indent + 1);
            }

            if (pretty && !first)
            {
                sb.Append('\n');
                AppendIndent(sb, indent);
            }

            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable<object> list, bool pretty, int indent)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;

                if (pretty)
                {
                    sb.Append('\n');
                    AppendIndent(sb, indent + 1);
                }

                WriteValue(sb, item, pretty, indent + 1);
            }

            if (pretty && !first)
            {
                sb.Append('\n');
                AppendIndent(sb, indent);
            }

            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                // JSON は NaN/Infinity を表現できない。壊れた保存データを作らないよう 0 に丸める。
                sb.Append('0');
                return;
            }

            // 整数値は "1.0" のような冗長表記を避けてそのまま整数として書く。
            if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
            {
                sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private static void AppendIndent(StringBuilder sb, int indent)
        {
            for (int i = 0; i < indent; i++)
            {
                sb.Append("  ");
            }
        }

        private static bool IsNumeric(object value)
        {
            return value is double || value is float || value is int || value is long ||
                   value is short || value is byte || value is sbyte || value is uint ||
                   value is ulong || value is ushort || value is decimal;
        }
    }
}
