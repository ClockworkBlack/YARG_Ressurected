// YARG Resurrected - Android build entry point.
// Called by CI through game-ci/unity-builder:  buildMethod: YARG.Editor.Android.YargAndroidBuild.Build
// You can also run it from the Unity editor menu: YARG > Android > Build APK
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace YARG.Editor.Android
{
    public static class YargAndroidBuild
    {
        public const string PACKAGE_ID = "in.yarg.resurrected";
        private const string ANDROID_PLUGIN_ROOT = "Assets/Plugins/Android/";

        [MenuItem("YARG/Android/Build APK")]
        public static void BuildFromMenu() => RunBuild(exitWhenDone: false);

        // Entry point for CI
        public static void Build() => RunBuild(exitWhenDone: true);

        private static void RunBuild(bool exitWhenDone)
        {
            int exitCode = 1;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                }

                ConfigurePlayerSettings();
                ConfigureNativePlugins();
                BuildAddressables();

                string output = GetArg("-customBuildPath") ?? "build/Android/YARG.apk";
                if (!output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    output = Path.Combine(output, "YARG.apk");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                Debug.Log($"[YARG Android] Building {scenes.Length} scenes to {output}");

                EditorUserBuildSettings.buildAppBundle = false;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                });

                Debug.Log($"[YARG Android] Result: {report.summary.result}, " +
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
            var android = NamedBuildTarget.Android;

            PlayerSettings.SetApplicationIdentifier(android, PACKAGE_ID);
            PlayerSettings.productName = "YARG Resurrected";
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Minimal);
            PlayerSettings.stripEngineCode = false;
            // Keep the project's own .NET Framework API level (YARG uses System.Drawing etc.).

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Minis (MIDI) needs the classic Activity entry point, not Unity 6's default GameActivity.
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.forceSDCardPermission = true;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

            // Landscape, either side: follows the phone's rotation (and its rotation lock).
            // Works on the Fold's inner and cover screens. OrientationUnlock re-applies this at runtime.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            // Landscape only (both sides): the game's screens are laid out for a wide screen
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Vulkan first (works with Turnip / Adreno 830), GLES3 fallback.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan, UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

            // Signing: use a real keystore if CI provides one (lets new builds install over old ones),
            // otherwise Unity signs with a debug key.
            string keystore = GetArg("-androidKeystoreName");
            if (!string.IsNullOrEmpty(keystore) && File.Exists(keystore))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystore;
                PlayerSettings.Android.keystorePass = GetArg("-androidKeystorePass") ?? string.Empty;
                PlayerSettings.Android.keyaliasName = GetArg("-androidKeyaliasName") ?? string.Empty;
                PlayerSettings.Android.keyaliasPass = GetArg("-androidKeyaliasPass") ?? string.Empty;
                Debug.Log("[YARG Android] Signing with provided keystore");
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("[YARG Android] No keystore provided, using Unity's debug signing");
            }

            string versionCode = GetArg("-androidVersionCode");
            if (int.TryParse(versionCode, out int code))
            {
                PlayerSettings.Android.bundleVersionCode = code;
            }
        }

        /// <summary>
        /// YARG ships desktop .so/.dll/.dylib files marked "Any Platform". Keep all of them out of the APK
        /// and include only our arm64 builds under Assets/Plugins/Android/libs/arm64-v8a.
        /// </summary>
        private static void ConfigureNativePlugins()
        {
            int included = 0, excluded = 0;
            foreach (var importer in PluginImporter.GetAllImporters())
            {
                if (!importer.isNativePlugin || !importer.assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                bool isOurs = importer.assetPath.StartsWith(ANDROID_PLUGIN_ROOT, StringComparison.Ordinal);
                bool changed = false;

                if (isOurs)
                {
                    if (!importer.GetCompatibleWithPlatform(BuildTarget.Android))
                    {
                        importer.SetCompatibleWithAnyPlatform(false);
                        importer.SetCompatibleWithEditor(false);
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                        changed = true;
                    }
                    if (importer.assetPath.Contains("/arm64-v8a/"))
                    {
                        importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                        changed = true;
                    }
                    included++;
                }
                else
                {
                    if (importer.GetCompatibleWithAnyPlatform())
                    {
                        importer.SetExcludeFromAnyPlatform(BuildTarget.Android, true);
                        changed = true;
                    }
                    if (importer.GetCompatibleWithPlatform(BuildTarget.Android))
                    {
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
                        changed = true;
                    }
                    excluded++;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            Debug.Log($"[YARG Android] Native plugins: {included} Android libs included, {excluded} desktop libs excluded");
            if (included == 0)
            {
                throw new BuildFailedException(
                    "No Android native libraries found in Assets/Plugins/Android/libs/arm64-v8a. " +
                    "Run android/native/build-android-native.sh first.");
            }
        }

        private static void BuildAddressables()
        {
            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.Log("[YARG Android] No Addressables settings, skipping content build");
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

    /// <summary>
    /// Runs after every other pre-build hook and makes sure the APK is arm64 only
    /// (64-bit phones like the Snapdragon 8 Elite can't run 32-bit code anyway, and a second
    /// architecture would double the IL2CPP build time).
    /// </summary>
    public class YargAndroidArchitectureGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                Debug.Log($"[YARG Android] Target architectures were {PlayerSettings.Android.targetArchitectures}; forcing ARM64 only.");
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            }
        }
    }

    /// <summary>
    /// Adds the storage permissions YARG needs to read song folders on shared storage.
    /// </summary>
    public class YargAndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogWarning("[YARG Android] Manifest not found at " + manifest);
                return;
            }

            string xml = File.ReadAllText(manifest);
            string[] permissions =
            {
                "android.permission.MANAGE_EXTERNAL_STORAGE",
                "android.permission.READ_EXTERNAL_STORAGE",
                "android.permission.WRITE_EXTERNAL_STORAGE",
                "android.permission.READ_MEDIA_AUDIO",
                "android.permission.RECORD_AUDIO",
                // Phone remote (other phones join over Wi-Fi)
                "android.permission.INTERNET",
                "android.permission.ACCESS_NETWORK_STATE",
                "android.permission.ACCESS_WIFI_STATE",
                "android.permission.WAKE_LOCK",
                // "Start game network" (local-only hotspot)
                "android.permission.CHANGE_WIFI_STATE",
                // Fast Bluetooth for controllers (YargBluetoothBoost.java): to see paired controllers
                "android.permission.BLUETOOTH_CONNECT",
                "android.permission.BLUETOOTH",
            };

            foreach (var permission in permissions)
            {
                if (!xml.Contains($"\"{permission}\""))
                {
                    xml = Regex.Replace(xml, @"(<manifest[^>]*>)",
                        $"$1\n  <uses-permission android:name=\"{permission}\" />", RegexOptions.Singleline);
                }
            }

            // The local-only hotspot needs "Nearby devices" on Android 13+, and Location before that.
            if (!xml.Contains("android.permission.NEARBY_WIFI_DEVICES"))
            {
                xml = Regex.Replace(xml, @"(<manifest[^>]*>)",
                    "$1\n  <uses-permission android:name=\"android.permission.NEARBY_WIFI_DEVICES\" android:usesPermissionFlags=\"neverForLocation\" />",
                    RegexOptions.Singleline);
            }
            if (!xml.Contains("android.permission.ACCESS_FINE_LOCATION"))
            {
                xml = Regex.Replace(xml, @"(<manifest[^>]*>)",
                    "$1\n  <uses-permission android:name=\"android.permission.ACCESS_FINE_LOCATION\" android:maxSdkVersion=\"32\" />" +
                    "\n  <uses-permission android:name=\"android.permission.ACCESS_COARSE_LOCATION\" android:maxSdkVersion=\"32\" />",
                    RegexOptions.Singleline);
            }

            if (!xml.Contains("android.hardware.gamepad"))
            {
                xml = Regex.Replace(xml, @"(<manifest[^>]*>)",
                    "$1\n  <uses-feature android:name=\"android.hardware.gamepad\" android:required=\"false\" />" +
                    "\n  <uses-feature android:name=\"android.hardware.touchscreen\" android:required=\"false\" />",
                    RegexOptions.Singleline);
            }

            if (!xml.Contains("requestLegacyExternalStorage"))
            {
                xml = Regex.Replace(xml, @"<application", "<application android:requestLegacyExternalStorage=\"true\"");
            }

            // Foldables (Galaxy Z Fold): the game's window changes size when the phone is folded
            // or unfolded, and the game redraws itself for the new size (the activity handles
            // screenSize/smallestScreenSize/screenLayout/density changes, so it is not
            // restarted). A non-resizeable activity is letterboxed instead, or Android offers to
            // restart it. Either landscape side (userLandscape follows the rotation lock); no
            // aspect-ratio cap, so the long cover screen is filled too.
            xml = Regex.Replace(xml, @"(<activity\b[^>]*android:name=""(?:com\.unity3d\.player\.UnityPlayer[^""]*|in\.yarg\.resurrected\.YargActivity)""[^>]*>)", match =>
            {
                string tag = match.Value;
                tag = SetAttribute(tag, "android:resizeableActivity", "true");
                tag = SetAttribute(tag, "android:screenOrientation", "userLandscape");
                tag = Regex.Replace(tag, @"\s+android:maxAspectRatio=""[^""]*""", string.Empty);
                return tag;
            }, RegexOptions.Singleline);
            xml = Regex.Replace(xml, @"\s*<meta-data\s+android:name=""android\.max_aspect""[^>]*/>", string.Empty);

            File.WriteAllText(manifest, xml);
            Debug.Log("[YARG Android] Patched AndroidManifest.xml with storage/gamepad entries and a resizeable activity");
        }

        private static string SetAttribute(string tag, string name, string value)
        {
            string pattern = Regex.Escape(name) + @"=""[^""]*""";
            if (Regex.IsMatch(tag, pattern))
            {
                return Regex.Replace(tag, pattern, name + "=\"" + value + "\"");
            }

            int end = tag.EndsWith("/>") ? tag.Length - 2 : tag.Length - 1;
            return tag.Substring(0, end) + " " + name + "=\"" + value + "\"" + tag.Substring(end);
        }
    }
}
