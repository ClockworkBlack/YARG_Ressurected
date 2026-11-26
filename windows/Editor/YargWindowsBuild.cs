// YARG Resurrected - Windows (x64) build entry point.
// Called by CI through game-ci/unity-builder:  buildMethod: YARG.Editor.Windows.YargWindowsBuild.Build
// You can also run it from the Unity editor menu: YARG > Windows > Build
//
// Copied into Assets/Editor/Windows by windows/prepare-project.sh.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YARG.Editor.Windows
{
    public static class YargWindowsBuild
    {
        private const BuildTarget TARGET = BuildTarget.StandaloneWindows64;

        // The only native libraries that belong in a Windows x64 build
        private static readonly string[] WindowsPluginFolders =
        {
            "/Windows/x86_64/",
            "/DiscordGameSDK/Plugins/x86_64/",
        };

        [MenuItem("YARG/Windows/Build")]
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

                string output = GetArg("-customBuildPath") ?? "build/StandaloneWindows64/YARG.exe";
                if (!output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    output = Path.Combine(output, "YARG.exe");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                Debug.Log($"[YARG Windows] Building {scenes.Length} scenes to {output}");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = TARGET,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });

                Debug.Log($"[YARG Windows] Result: {report.summary.result}, " +
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

            // A different product name than official YARG, so settings, profiles and the song cache
            // live in their own folder (AppData\LocalLow\<company>\YARG Resurrected) and the two
            // can be installed side by side.
            PlayerSettings.productName = "YARG Resurrected";

            // Mono: a Windows IL2CPP build has to be made on a Windows machine, and Mono runs
            // YARG just as well on a PC. (The Android build needs IL2CPP; Windows doesn't.)
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Disabled);

            // Keep running (menu music, phones connected as controllers) when the window loses focus
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;

            string version = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(version) && version != "none")
            {
                PlayerSettings.bundleVersion = version;
            }
        }

        /// <summary>
        /// Only the Windows x64 native libraries go into the build. Linux .so, macOS .dylib/.bundle,
        /// 32-bit Windows and the Android arm64 libraries are all left out.
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

                bool isWindows64 = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                    WindowsPluginFolders.Any(folder => path.Contains(folder));
                bool changed = false;

                if (isWindows64)
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
                    if (importer.GetPlatformData(TARGET, "CPU") != "x86_64")
                    {
                        importer.SetPlatformData(TARGET, "CPU", "x86_64");
                        changed = true;
                    }
                    haveBass |= path.EndsWith("/bass.dll", StringComparison.OrdinalIgnoreCase);
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

            Debug.Log($"[YARG Windows] Native plugins: {included} Windows x64 libs included, {excluded} others excluded");
            if (!haveBass)
            {
                throw new BuildFailedException(
                    "bass.dll for Windows x64 is missing from Assets/Plugins/BassNative/Windows/x86_64. " +
                    "Run windows/prepare-project.sh first.");
            }
        }

        private static void BuildAddressables()
        {
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.Log("[YARG Windows] No Addressables settings, skipping content build");
                return;
            }

            AddressableAssetSettings.CleanPlayerContent();
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error))
            {
                throw new BuildFailedException("Addressables build failed: " + result.Error);
            }
        }

        private static string GetArg(string name)
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
}
