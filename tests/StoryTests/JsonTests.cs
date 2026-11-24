using System;
using System.Collections.Generic;
using YARG.Story;

namespace StoryTests
{
    internal static class JsonTests
    {
        public static void Run()
        {
            Console.WriteLine("\n== The story's JSON reader and writer ==");

            var root = (Dictionary<string, object>) StoryJson.Parse(
                "﻿{ \"a\": 1, \"b\": [true, false, null, -2.5e1, \"x\\n\\u00e9\\\"\"], \"c\": {\"d\": \"R&B\"}, \"e\": \"\" }");
            Program.Check(StoryJson.Int(root, "a") == 1, "reads a number");
            var list = StoryJson.List(root, "b");
            Program.Check(list.Count == 5 && (bool) list[0] && !(bool) list[1] && list[2] == null, "reads true, false and null");
            Program.Check((double) list[3] == -25.0, "reads a negative number with an exponent");
            Program.Check((string) list[4] == "x\né\"", "reads escapes in text");
            Program.Check(StoryJson.Str(StoryJson.Obj(root, "c"), "d") == "R&B", "reads a nested object");
            Program.Check(StoryJson.Str(root, "missing", "fallback") == "fallback", "a missing value gives the fallback");
            Program.Check(StoryJson.Int(root, "c", 7) == 7, "a value of the wrong kind gives the fallback");

            foreach (string bad in new[] { "", "{", "{\"a\":}", "[1,]", "{\"a\":1,}", "\"open", "{\"a\":1} x", "nul", "{'a':1}", "[1 2]", "\"line\nbreak\"" })
            {
                bool threw = false;
                try
                {
                    StoryJson.Parse(bad);
                }
                catch (StoryJsonException)
                {
                    threw = true;
                }

                Program.Check(threw, "rejects broken JSON: " + bad.Replace("\n", "\\n"));
            }

            Program.Check(StoryJson.TryParseObject("[1]") == null && StoryJson.TryParseObject("{bad") == null,
                "TryParseObject gives null for a list or broken text");

            // A deeply nested file is refused rather than crashing
            bool deepThrew = false;
            try
            {
                StoryJson.Parse(new string('[', 5000));
            }
            catch (StoryJsonException)
            {
                deepThrew = true;
            }

            Program.Check(deepThrew, "refuses text nested thousands deep");

            var written = new Dictionary<string, object>
            {
                ["text"] = "He said \"go\"\n\ttab \\ slash \u0001",
                ["whole"] = 42,
                ["big"] = 9007199254740993L,
                ["fraction"] = 0.125,
                ["yes"] = true,
                ["nothing"] = null,
                ["list"] = new List<object> { 1, "two", new Dictionary<string, object>() },
                ["names"] = new List<string> { "a", "b" },
                ["set"] = new HashSet<string> { "only" },
                ["empty"] = new List<object>(),
            };

            foreach (bool pretty in new[] { true, false })
            {
                string text = StoryJson.Write(written, pretty);
                var back = (Dictionary<string, object>) StoryJson.Parse(text);
                Program.Check(StoryJson.Str(back, "text") == (string) written["text"],
                    $"text survives writing and reading (pretty: {pretty})");
                Program.Check(StoryJson.Int(back, "whole") == 42 && StoryJson.Num(back, "fraction") == 0.125 &&
                    StoryJson.Bool(back, "yes") && back["nothing"] == null,
                    $"numbers, true and null survive (pretty: {pretty})");
                Program.Check(StoryJson.Strings(back, "names").Count == 2 && StoryJson.Strings(back, "set")[0] == "only" &&
                    StoryJson.List(back, "empty").Count == 0 && StoryJson.List(back, "list").Count == 3,
                    $"lists and sets survive (pretty: {pretty})");
                Program.Check(text.Contains("42") && !text.Contains("42.0"), "whole numbers are written without a decimal point");
            }
        }
    }
}
