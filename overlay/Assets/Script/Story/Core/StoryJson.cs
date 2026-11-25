// YARG Resurrected - Story Mode. Plain .NET, tested in tests/StoryTests.
//
// A small JSON reader and writer for the story's own files (the campaign script and the save
// file). It is self-contained on purpose: nothing here depends on Unity or on a JSON package, so
// the same code runs in the game (IL2CPP) and in the automated tests, and reading a file never
// relies on reflection.
//
// Values are plain .NET objects:
//   object  -> Dictionary<string, object>      array -> List<object>
//   string  -> string                          number -> double
//   true / false -> bool                       null -> null
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace YARG.Story
{
    public sealed class StoryJsonException : Exception
    {
        public StoryJsonException(string message) : base(message)
        {
        }
    }

    public static class StoryJson
    {
        // Deep enough for any story file, shallow enough that a damaged file cannot overflow the stack
        private const int MAX_DEPTH = 64;

        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new StoryJsonException("No text to read");
            }

            int pos = 0;

            // A byte order mark at the start of a file is not part of the JSON
            if (text.Length > 0 && text[0] == '﻿')
            {
                pos = 1;
            }

            object value = ReadValue(text, ref pos, 0);
            SkipSpace(text, ref pos);
            if (pos != text.Length)
            {
                throw Error(text, pos, "Unexpected text after the end");
            }

            return value;
        }

        /// <summary>Reads a JSON object, or returns null if the text is not one.</summary>
        public static Dictionary<string, object> TryParseObject(string text)
        {
            try
            {
                return Parse(text) as Dictionary<string, object>;
            }
            catch (StoryJsonException)
            {
                return null;
            }
        }

        private static StoryJsonException Error(string text, int pos, string what)
        {
            int line = 1;
            int column = 1;
            for (int i = 0; i < pos && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new StoryJsonException($"{what} (line {line}, column {column})");
        }

        private static void SkipSpace(string text, ref int pos)
        {
            while (pos < text.Length)
            {
                char c = text[pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    pos++;
                }
                else
                {
                    break;
                }
            }
        }

        private static object ReadValue(string text, ref int pos, int depth)
        {
            if (depth > MAX_DEPTH)
            {
                throw Error(text, pos, "Nested too deeply");
            }

            SkipSpace(text, ref pos);
            if (pos >= text.Length)
            {
                throw Error(text, pos, "Unexpected end");
            }

            char c = text[pos];
            switch (c)
            {
                case '{':
                    return ReadObject(text, ref pos, depth);
                case '[':
                    return ReadArray(text, ref pos, depth);
                case '"':
                    return ReadString(text, ref pos);
                case 't':
                    ReadWord(text, ref pos, "true");
                    return true;
                case 'f':
                    ReadWord(text, ref pos, "false");
                    return false;
                case 'n':
                    ReadWord(text, ref pos, "null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ReadNumber(text, ref pos);
                    }
                    throw Error(text, pos, $"Unexpected character '{c}'");
            }
        }

        private static void ReadWord(string text, ref int pos, string word)
        {
            if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
            {
                throw Error(text, pos, $"Expected {word}");
            }

            pos += word.Length;
        }

        private static Dictionary<string, object> ReadObject(string text, ref int pos, int depth)
        {
            var result = new Dictionary<string, object>();
            pos++; // {
            SkipSpace(text, ref pos);
            if (pos < text.Length && text[pos] == '}')
            {
                pos++;
                return result;
            }

            while (true)
            {
                SkipSpace(text, ref pos);
                if (pos >= text.Length || text[pos] != '"')
                {
                    throw Error(text, pos, "Expected a name in quotes");
                }

                string key = ReadString(text, ref pos);
                SkipSpace(text, ref pos);
                if (pos >= text.Length || text[pos] != ':')
                {
                    throw Error(text, pos, "Expected ':'");
                }

                pos++;
                result[key] = ReadValue(text, ref pos, depth + 1);
                SkipSpace(text, ref pos);
                if (pos >= text.Length)
                {
                    throw Error(text, pos, "Unexpected end inside an object");
                }

                if (text[pos] == ',')
                {
                    pos++;
                    continue;
                }

                if (text[pos] == '}')
                {
                    pos++;
                    return result;
                }

                throw Error(text, pos, "Expected ',' or '}'");
            }
        }

        private static List<object> ReadArray(string text, ref int pos, int depth)
        {
            var result = new List<object>();
            pos++; // [
            SkipSpace(text, ref pos);
            if (pos < text.Length && text[pos] == ']')
            {
                pos++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue(text, ref pos, depth + 1));
                SkipSpace(text, ref pos);
                if (pos >= text.Length)
                {
                    throw Error(text, pos, "Unexpected end inside a list");
                }

                if (text[pos] == ',')
                {
                    pos++;
                    continue;
                }

                if (text[pos] == ']')
                {
                    pos++;
                    return result;
                }

                throw Error(text, pos, "Expected ',' or ']'");
            }
        }

        private static string ReadString(string text, ref int pos)
        {
            pos++; // opening quote
            StringBuilder builder = null;
            int start = pos;
            while (true)
            {
                if (pos >= text.Length)
                {
                    throw Error(text, pos, "Text in quotes never ends");
                }

                char c = text[pos];
                if (c == '"')
                {
                    string plain = builder == null
                        ? text.Substring(start, pos - start)
                        : builder.Append(text, start, pos - start).ToString();
                    pos++;
                    return plain;
                }

                if (c == '\\')
                {
                    builder ??= new StringBuilder();
                    builder.Append(text, start, pos - start);
                    pos++;
                    if (pos >= text.Length)
                    {
                        throw Error(text, pos, "Text in quotes never ends");
                    }

                    char escape = text[pos];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (pos + 4 >= text.Length ||
                                !ushort.TryParse(text.Substring(pos + 1, 4), NumberStyles.AllowHexSpecifier,
                                    CultureInfo.InvariantCulture, out ushort code))
                            {
                                throw Error(text, pos, "Bad \\u escape");
                            }
                            builder.Append((char) code);
                            pos += 4;
                            break;
                        default:
                            throw Error(text, pos, $"Unknown escape '\\{escape}'");
                    }

                    pos++;
                    start = pos;
                    continue;
                }

                if (c < ' ')
                {
                    throw Error(text, pos, "Line break or control character inside quotes");
                }

                pos++;
            }
        }

        private static double ReadNumber(string text, ref int pos)
        {
            int start = pos;
            if (text[pos] == '-')
            {
                pos++;
            }

            while (pos < text.Length)
            {
                char c = text[pos];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    pos++;
                }
                else
                {
                    break;
                }
            }

            string number = text.Substring(start, pos - start);
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw Error(text, start, $"Bad number '{number}'");
            }

            return value;
        }

        // ------------------------------------------------------------------------------------
        // Writing
        // ------------------------------------------------------------------------------------

        public static string Write(object value, bool pretty = true)
        {
            var builder = new StringBuilder();
            WriteValue(builder, value, pretty, 0);
            if (pretty)
            {
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void Indent(StringBuilder builder, bool pretty, int depth)
        {
            if (!pretty)
            {
                return;
            }

            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }

        private static void WriteValue(StringBuilder builder, object value, bool pretty, int depth)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    break;
                case string text:
                    WriteString(builder, text);
                    break;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    break;
                case int number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case long number:
                    builder.Append(number.ToString(CultureInfo.InvariantCulture));
                    break;
                case float number:
                    WriteDouble(builder, number);
                    break;
                case double number:
                    WriteDouble(builder, number);
                    break;
                case IDictionary<string, object> map:
                    WriteObject(builder, map, pretty, depth);
                    break;
                // Lists and sets of text also arrive here: a List<string> is an IEnumerable<object>
                case IEnumerable<object> list:
                    WriteArray(builder, list, pretty, depth);
                    break;
                default:
                    throw new StoryJsonException($"Cannot write a value of type {value.GetType().Name}");
            }
        }

        private static void WriteDouble(StringBuilder builder, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                builder.Append('0');
                return;
            }

            // Whole numbers are written without a decimal point so files stay readable
            if (Math.Abs(number) < 1e15 && Math.Floor(number) == number)
            {
                builder.Append(((long) number).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        private static void WriteObject(StringBuilder builder, IDictionary<string, object> map, bool pretty, int depth)
        {
            if (map.Count == 0)
            {
                builder.Append("{}");
                return;
            }

            builder.Append('{');
            bool first = true;
            foreach (var pair in map)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                Indent(builder, pretty, depth + 1);
                WriteString(builder, pair.Key);
                builder.Append(pretty ? ": " : ":");
                WriteValue(builder, pair.Value, pretty, depth + 1);
            }

            Indent(builder, pretty, depth);
            builder.Append('}');
        }

        private static void WriteArray(StringBuilder builder, IEnumerable<object> list, bool pretty, int depth)
        {
            builder.Append('[');
            bool first = true;
            foreach (object item in list)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                Indent(builder, pretty, depth + 1);
                WriteValue(builder, item, pretty, depth + 1);
            }

            if (!first)
            {
                Indent(builder, pretty, depth);
            }

            builder.Append(']');
        }

        private static void WriteString(StringBuilder builder, string text)
        {
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int) c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            builder.Append('"');
        }

        // ------------------------------------------------------------------------------------
        // Reading values out of a parsed object, with a fallback when a value is missing or of
        // the wrong kind. A story file with a mistake in it should still load as far as it can.
        // ------------------------------------------------------------------------------------

        public static string Str(Dictionary<string, object> map, string key, string fallback = "")
        {
            return map != null && map.TryGetValue(key, out object value) && value is string text ? text : fallback;
        }

        public static double Num(Dictionary<string, object> map, string key, double fallback = 0)
        {
            if (map != null && map.TryGetValue(key, out object value))
            {
                if (value is double number)
                {
                    return number;
                }

                if (value is string text &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }
            }

            return fallback;
        }

        public static int Int(Dictionary<string, object> map, string key, int fallback = 0)
        {
            double number = Num(map, key, fallback);
            if (double.IsNaN(number) || number > int.MaxValue || number < int.MinValue)
            {
                return fallback;
            }

            return (int) Math.Round(number);
        }

        public static long Long(Dictionary<string, object> map, string key, long fallback = 0)
        {
            double number = Num(map, key, fallback);
            if (double.IsNaN(number) || number > long.MaxValue || number < long.MinValue)
            {
                return fallback;
            }

            return (long) Math.Round(number);
        }

        public static bool Bool(Dictionary<string, object> map, string key, bool fallback = false)
        {
            return map != null && map.TryGetValue(key, out object value) && value is bool flag ? flag : fallback;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> map, string key)
        {
            return map != null && map.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;
        }

        public static List<object> List(Dictionary<string, object> map, string key)
        {
            return map != null && map.TryGetValue(key, out object value) && value is List<object> list
                ? list
                : new List<object>();
        }

        public static List<string> Strings(Dictionary<string, object> map, string key)
        {
            var result = new List<string>();
            foreach (object item in List(map, key))
            {
                if (item is string text)
                {
                    result.Add(text);
                }
            }

            return result;
        }

        public static List<Dictionary<string, object>> Objects(Dictionary<string, object> map, string key)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (object item in List(map, key))
            {
                if (item is Dictionary<string, object> child)
                {
                    result.Add(child);
                }
            }

            return result;
        }
    }
}
