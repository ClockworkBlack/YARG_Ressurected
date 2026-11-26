// YARG Resurrected - Linux (x86_64) build entry point. Also the build Chromebooks run.
// Called by CI through game-ci/unity-builder:  buildMethod: YARG.Editor.Linux.YargLinuxBuild.Build
// You can also run it from the Unity editor menu: YARG > Linux > Build
//
// Copied into Assets/Editor/Linux by linux/prepare-project.sh.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YARG.Editor.Linux
{
    public static class YargLinuxBuild
    {
        private const BuildTarget TARGET = BuildTarget.StandaloneLinux64;

        // The only native libraries that belong in the Linux build
        private static readonly string[] LinuxPlugins =
        {
            "Assets/Plugins/BassNative/Linux/x86_64/",
            "Assets/Plugins/STB2CSharp/Linux/x86_64/",
            "Assets/Plugins/Sqlite3/Linux/x86_64/",
            "Assets/Plugins/LibUSB/libusb-1.0.so",
            "Assets/Plugins/DiscordGameSDK/Plugins/x86_64/discord_game_sdk.so",
        };

        [MenuItem("YARG/Linux/Build")]
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

                string output = GetArg("-customBuildPath") ?? "build/StandaloneLinux64/YARG.x86_64";
                if (Directory.Exists(output) || output.EndsWith("/", StringComparison.Ordinal))
                {
                    output = Path.Combine(output, "YARG.x86_64");
                }
                else if (!output.EndsWith(".x86_64", StringComparison.Ordinal))
                {
                    output += ".x86_64";
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                Debug.Log($"[YARG Linux] Building {scenes.Length} scenes to {output}");

                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = TARGET,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                });

                Debug.Log($"[YARG Linux] Result: {report.summary.result}, " +
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

            // Its own name, so settings, profiles and the song cache are kept apart from an
            // official YARG install (~/.config/unity3d/<company>/YARG Resurrected)
            PlayerSettings.productName = "YARG Resurrected";

            // Mono, like the Windows and Mac builds
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Disabled);

            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;

            // Chromebooks' Linux has OpenGL (virgl) but usually no Vulkan, so OpenGL comes first
            PlayerSettings.SetUseDefaultGraphicsAPIs(TARGET, false);
            PlayerSettings.SetGraphicsAPIs(TARGET, new[]
            {
                UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore,
                UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
            });

            string version = GetArg("-buildVersion");
            if (!string.IsNullOrEmpty(version) && version != "none")
            {
                PlayerSettings.bundleVersion = version;
            }
        }

        /// <summary>
        /// Only the Linux x86_64 libraries listed above go into the build.
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

                bool isLinux = path.EndsWith(".so", StringComparison.Ordinal) &&
                    LinuxPlugins.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));
                bool changed = false;

                if (isLinux)
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
                    haveBass |= path.EndsWith("/libbass.so", StringComparison.Ordinal);
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

            Debug.Log($"[YARG Linux] Native plugins: {included} Linux libs included, {excluded} others excluded");
            if (!haveBass)
            {
                throw new BuildFailedException(
                    "libbass.so is missing from Assets/Plugins/BassNative/Linux/x86_64. Run linux/prepare-project.sh first.");
            }
        }

        private static void BuildAddressables()
        {
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.Log("[YARG Linux] No Addressables settings, skipping content build");
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
