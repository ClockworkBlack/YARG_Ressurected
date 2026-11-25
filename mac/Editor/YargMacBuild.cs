// YARG Resurrected - macOS build entry point.
// Called by CI through game-ci/unity-builder:  buildMethod: YARG.Editor.Mac.YargMacBuild.Build
// You can also run it from the Unity editor menu: YARG > Mac > Build
//
// Copied into Assets/Editor/Mac by mac/prepare-project.sh.
//
// The app is built for Intel (x64). Apple Silicon Macs run it through Rosetta 2. Two reasons:
// the sqlite library in the project is Intel-only, and native Apple Silicon code must be
// code-signed to run at all, which a build made on Linux (CI) is not.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YARG.Editor.Mac
{
    public static class YargMacBuild
    {
        private const BuildTarget TARGET = BuildTarget.StandaloneOSX;
        public const string BUNDLE_ID = "in.yarg.resurrected";

        // The only native libraries that belong in the Mac build
        private static readonly string[] MacPlugins =
        {
            "Assets/Plugins/BassNative/Mac/",
            "Assets/Plugins/STB2CSharp/MacOS/",
            "Assets/Plugins/Sqlite3/Mac/",
            "Assets/Plugins/DiscordGameSDK/Plugins/x86_64/discord_game_sdk.bundle",
        };

        [MenuItem("YARG/Mac/Build")]
        public static void BuildFromMenu() => RunBuild(exitWhenDone: false);

        // Entry point for CI
        public static void Build() => RunBuild(exitWhenDone: true);

        private static void RunBuild(bool exitWhenDone)
        {
            int exitCode = 1;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != TARGET)
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, TARGET);
                }

                ConfigurePlayerSettings();
                ConfigureNativePlugins();
                BuildAddressables();

                string output = GetArg("-customBuildPath") ?? "build/StandaloneOSX/YARG.app";
                if (!output.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    output = Path.Combine(output, "YARG.app");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                Debug.Log($"[YARG Mac] Building {scenes.Length} scenes to {output}");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = TARGET,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });

                Debug.Log($"[YARG Mac] Result: {report.summary.result}, " +
                    $"{report.summary.totalErrors} errors, size {report.summary.totalSize / (1024 * 1024)} MB");
                exitCode = report.summary.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }

            if (exitWhenDone)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void ConfigurePlayerSettings()
        {
            var standalone = NamedBuildTarget.Standalone;

            // Its own name and bundle id, so settings, profiles and the song cache are kept apart
            // from an official YARG install
            PlayerSettings.productName = "YARG Resurrected";
            PlayerSettings.SetApplicationIdentifier(standalone, BUNDLE_ID);

            // Mono: IL2CPP for macOS can only be built on a Mac
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Disabled);

            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;

            SetIntelArchitecture();

            string version = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(version) && version != "none")
            {
                PlayerSettings.bundleVersion = version;
            }
        }

        private static void SetIntelArchitecture()
        {
            // UnityEditor.OSXStandalone.UserBuildSettings only exists when the Mac module is
            // installed, so set it by reflection to keep this file compiling in any editor.
            var type = Type.GetType("UnityEditor.OSXStandalone.UserBuildSettings, UnityEditor.OSXStandalone.Extensions");
            var property = type?.GetProperty("architecture");
            if (property != null)
            {
                property.SetValue(null, Enum.Parse(property.PropertyType, "x64"));
                Debug.Log("[YARG Mac] Architecture: x64 (Intel; Rosetta 2 on Apple Silicon)");
            }
            else
            {
                EditorUserBuildSettings.SetPlatformSettings(
                    BuildPipeline.GetBuildTargetName(TARGET), "Architecture", "x64");
                Debug.Log("[YARG Mac] Architecture set to x64 through platform settings");
            }
        }

        /// <summary>
        /// Only the Mac libraries listed above go into the app. Windows, Linux, Android and the
        /// arm64-only libusb are left out.
        /// </summary>
        private static void ConfigureNativePlugins()
        {
            int included = 0, excluded = 0;
            bool haveBass = false;
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                string path = importer.assetPath;
                if (!importer.isNativePlugin || !path.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                bool isMac = (path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) &&
                    MacPlugins.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));
                bool changed = false;

                if (isMac)
                {
                    if (!importer.GetCompatibleWithPlatform(TARGET))
                    {
                        importer.SetCompatibleWithPlatform(TARGET, true);
                        changed = true;
                    }
                    if (importer.GetCompatibleWithAnyPlatform() && importer.GetExcludeFromAnyPlatform(TARGET))
                    {
                        importer.SetExcludeFromAnyPlatform(TARGET, false);
                        changed = true;
                    }
                    if (importer.GetPlatformData(TARGET, "CPU") != "AnyCPU")
                    {
                        importer.SetPlatformData(TARGET, "CPU", "AnyCPU");
                        changed = true;
                    }
                    haveBass |= path.EndsWith("/libbass.dylib", StringComparison.OrdinalIgnoreCase);
                    included++;
                }
                else
                {
                    if (importer.GetCompatibleWithAnyPlatform() && !importer.GetExcludeFromAnyPlatform(TARGET))
                    {
                        importer.SetExcludeFromAnyPlatform(TARGET, true);
                        changed = true;
                    }
                    if (importer.GetCompatibleWithPlatform(TARGET))
                    {
                        importer.SetCompatibleWithPlatform(TARGET, false);
                        changed = true;
                    }
                    excluded++;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            Debug.Log($"[YARG Mac] Native plugins: {included} Mac libs included, {excluded} others excluded");
            if (!haveBass)
            {
                throw new BuildFailedException(
                    "libbass.dylib is missing from Assets/Plugins/BassNative/Mac. Run mac/prepare-project.sh first.");
            }
        }

        private static void BuildAddressables()
        {
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.Log("[YARG Mac] No Addressables settings, skipping content build");
                return;
            }

            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error))
            {
                throw new BuildFailedException("Addressables build failed: " + result.Error);
            }
        }

        internal static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// macOS only lets an app use the microphone (vocals) when its Info.plist says why.
    /// Without this the mic stays silent. The phone remote's network use needs no entry.
    /// </summary>
    public class YargMacInfoPlistPatcher : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        private const string MIC_KEY = "NSMicrophoneUsageDescription";
        private const string MIC_TEXT = "YARG Resurrected uses the microphone for vocals.";

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX)
            {
                return;
            }

            string plist = Path.Combine(report.summary.outputPath, "Contents", "Info.plist");
            if (!File.Exists(plist))
            {
                Debug.LogWarning("[YARG Mac] Info.plist not found at " + plist);
                return;
            }

            string xml = File.ReadAllText(plist);
            if (xml.Contains(MIC_KEY))
            {
                return;
            }

            int end = xml.LastIndexOf("</dict>", StringComparison.Ordinal);
            if (end < 0)
            {
                Debug.LogWarning("[YARG Mac] Info.plist has no <dict>; microphone entry not added");
                return;
            }

            xml = xml.Insert(end, $"\t<key>{MIC_KEY}</key>\n\t<string>{MIC_TEXT}</string>\n");
            File.WriteAllText(plist, xml);
            Debug.Log("[YARG Mac] Added the microphone entry to Info.plist");
        }
    }
}
