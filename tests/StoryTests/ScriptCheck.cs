using System;
using System.Collections.Generic;
using System.IO;
using YARG.Story;

namespace StoryTests
{
    /// <summary>
    /// Checks the story files while they are being written. With chapter file names, only those
    /// chapters are loaded, and complaints about things other chapters would supply are left out.
    /// </summary>
    internal static class ScriptCheck
    {
        public static int Run(string folder, List<string> onlyChapters)
        {
            string indexPath = Path.Combine(folder, "campaign.json");
            if (!File.Exists(indexPath))
            {
                Console.WriteLine("No campaign.json in " + folder);
                return 1;
            }

            bool partial = onlyChapters.Count > 0;
            StoryCampaign campaign;
            try
            {
                campaign = StoryCampaign.Load(name =>
                {
                    string path = Path.Combine(folder, name + ".json");
                    if (!File.Exists(path))
                    {
                        return null;
                    }

                    string text = File.ReadAllText(path);
                    if (name == "campaign" && partial)
                    {
                        var index = (Dictionary<string, object>) StoryJson.Parse(text);
                        var chapters = new List<object>();
                        foreach (string chapter in onlyChapters)
                        {
                            chapters.Add(Path.GetFileNameWithoutExtension(chapter));
                        }

                        index["chapters"] = chapters;
                        return StoryJson.Write(index);
                    }

                    return text;
                });
            }
            catch (StoryJsonException e)
            {
                Console.WriteLine("Cannot read the story: " + e.Message);
                return 1;
            }

            int shown = 0;
            foreach (string problem in StoryCheck.Problems(campaign))
            {
                if (partial && (problem.Contains("is never unlocked") || problem.Contains("is never earned") ||
                    problem.Contains("bonus chapter") || problem.Contains("which comes after it") ||
                    problem.Contains("requires an unknown gig")))
                {
                    continue;
                }

                Console.WriteLine("problem: " + problem);
                shown++;
            }

            // Characters outside plain ASCII may be missing from the game's font
            foreach (string path in Directory.GetFiles(folder, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (partial && name != "campaign" && !onlyChapters.Contains(name) && !onlyChapters.Contains(name + ".json"))
                {
                    continue;
                }

                string text = File.ReadAllText(path);
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c != '\n' && c != '\r' && c != '\t' && (c < ' ' || c > '~'))
                    {
                        int from = Math.Max(0, i - 25);
                        Console.WriteLine($"problem: {Path.GetFileName(path)} has the character '{c}' (U+{(int) c:X4}) near \"{text.Substring(from, Math.Min(50, text.Length - from)).Replace("\n", " ")}\"; use plain ASCII");
                        shown++;
                        break;
                    }
                }
            }

            foreach (var chapter in campaign.Chapters)
            {
                int words = 0;
                int lines = 0;
                void Add(List<StoryLine> scene)
                {
                    lines += scene.Count;
                    foreach (var line in scene)
                    {
                        words += line.Text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    }
                }

                Add(chapter.Intro);
                Add(chapter.Outro);
                foreach (var gig in chapter.Gigs)
                {
                    Add(gig.Before);
                    Add(gig.AfterWin);
                    Add(gig.AfterLose);
                    Add(gig.AfterChallenge);
                    if (gig.Before.Count < 6 || gig.AfterWin.Count < 5 || gig.AfterLose.Count < 2)
                    {
                        Console.WriteLine($"problem: gig \"{gig.Id}\" needs at least 6 lines before, 5 after a win and 2 after a loss (has {gig.Before.Count} / {gig.AfterWin.Count} / {gig.AfterLose.Count})");
                        shown++;
                    }

                    if (gig.Challenge == null)
                    {
                        Console.WriteLine($"problem: gig \"{gig.Id}\" has no challenge");
                        shown++;
                    }
                }

                Console.WriteLine($"chapter {chapter.Id} \"{chapter.Title}\": {chapter.Gigs.Count} gigs, {lines} lines, {words} words");
            }

            Console.WriteLine(shown == 0 ? "No problems found." : shown + " problem(s).");
            return shown == 0 ? 0 : 1;
        }
    }
}
