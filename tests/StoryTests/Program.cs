// Tests the story mode's rules and its script files outside Unity.
//   dotnet run --project tests/StoryTests -- overlay/Assets/Resources/YargStory
using System;
using System.Collections.Generic;
using System.IO;

namespace StoryTests
{
    internal static class Program
    {
        private static int _checks;

        internal static void Check(bool condition, string what)
        {
            _checks++;
            if (!condition)
            {
                throw new Exception("FAILED: " + what);
            }

            Console.WriteLine("ok   " + what);
        }

        private static string FindStoryFolder()
        {
            string folder = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && folder != null; i++)
            {
                string candidate = Path.Combine(folder, "overlay", "Assets", "Resources", "YargStory");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                folder = Path.GetDirectoryName(folder);
            }

            throw new Exception("Could not find overlay/Assets/Resources/YargStory; pass its path as the first argument");
        }

        private static int Main(string[] args)
        {
            try
            {
                // A writer's tool: check the script, or one chapter of it, without running the tests.
                //   dotnet run --project tests/StoryTests -- --check <folder> [chapter file name ...]
                if (args.Length >= 2 && args[0] == "--check")
                {
                    return ScriptCheck.Run(args[1], new List<string>(args).GetRange(2, args.Length - 2));
                }

                JsonTests.Run();
                EngineTests.Run();
                SetlistTests.Run();

                // The real story files. Given the folder, or found from the repository layout.
                string folder = args.Length > 0 ? args[0] : FindStoryFolder();
                ContentTests.Run(folder);
                Console.WriteLine($"\nAll {_checks} checks passed.");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }
    }
}
