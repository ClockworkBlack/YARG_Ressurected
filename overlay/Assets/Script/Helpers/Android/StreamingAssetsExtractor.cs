#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace YARG.Helpers
{
    /// <summary>
    /// YARG Resurrected: unpacks StreamingAssets out of the APK on Android.
    ///
    /// On Android, Application.streamingAssetsPath points inside the APK and can't be read with
    /// System.IO, but YARG reads its languages, genres, sources, sounds and calibration music as
    /// plain files. This copies them once into &lt;persistentDataPath&gt;/StreamingAssets, and
    /// PathHelper points StreamingAssetsPath there.
    ///
    /// Why this replaces YARG-DROID's version: it read the APK through JNI with
    /// InputStream.read(byte[]). Unity passes C# arrays to Java as a copy and never copies the
    /// data back, so it wrote a zip full of zeros, extraction failed, lang/en-US.json never
    /// existed, and every menu showed raw keys like MENU.MAIN.OPTIONS.QUICKPLAY.
    ///
    /// This version reads the APK (a normal zip file that the app can open) with
    /// System.IO.Compression only:
    ///   1. assets/StreamingAssets.zip (made by StreamingAssetsZipper at build time) is unpacked;
    ///   2. if that's missing or incomplete, the raw assets/... copies Unity also puts in the APK
    ///      are used instead.
    /// The result is checked for lang/en-US.json, and it's redone whenever the APK changes.
    /// </summary>
    public static class StreamingAssetsExtractor
    {
        public const string ZipEntryName = "assets/StreamingAssets.zip";
        public const string VersionEntryName = "assets/StreamingAssets.version";
        public const string VersionFileName = "StreamingAssets.version";
        public const string ExtractedFolderName = "StreamingAssets";

        // A file every good extraction must contain (the default language).
        private const string SENTINEL = "lang/en-US.json";

        // APK folders that belong to Unity itself, not to StreamingAssets.
        private static readonly string[] _skipPrefixes =
        {
            "assets/bin/", "assets/aa/", "assets/StreamingAssets.zip", "assets/StreamingAssets.version",
        };

        /// <summary>
        /// Makes sure &lt;persistentDataPath&gt;/StreamingAssets is up to date with this APK.
        /// Never throws. Returns true when the folder is usable.
        /// </summary>
        public static bool EnsureExtracted(string persistentDataPath)
        {
            string targetDir = Path.Combine(persistentDataPath, ExtractedFolderName);
            string versionPath = Path.Combine(targetDir, VersionFileName);
            string apkPath = Application.dataPath;

            try
            {
                using (var apk = ZipFile.OpenRead(apkPath))
                {
                    string apkVersion = ReadEntryText(apk, VersionEntryName);
                    // Always tie the version to the installed APK too, so a reinstall re-extracts.
                    string version = (apkVersion ?? "noversion") + "|" + Application.version + "|" +
                        SafeTicks(apkPath);

                    if (IsUpToDate(targetDir, versionPath, version))
                    {
                        Debug.Log("[StreamingAssetsExtractor] Up to date (" + version + ").");
                        return true;
                    }

                    string tempDir = targetDir + ".tmp";
                    DeleteDirectory(tempDir);
                    Directory.CreateDirectory(tempDir);

                    int fromZip = 0;
                    var nested = apk.GetEntry(ZipEntryName);
                    if (nested != null)
                    {
                        try
                        {
                            fromZip = ExtractNestedZip(nested, tempDir, persistentDataPath);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("[StreamingAssetsExtractor] Nested zip failed, using raw APK assets: " + e);
                        }
                    }

                    int fromRaw = 0;
                    if (!File.Exists(Path.Combine(tempDir, SENTINEL)))
                    {
                        fromRaw = ExtractRawAssets(apk, tempDir);
                    }

                    File.WriteAllText(Path.Combine(tempDir, VersionFileName), version);

                    DeleteDirectory(targetDir);
                    Directory.Move(tempDir, targetDir);

                    bool ok = File.Exists(Path.Combine(targetDir, SENTINEL));
                    string message = "[StreamingAssetsExtractor] Extracted " + fromZip + " files from " +
                        ZipEntryName + " and " + fromRaw + " raw APK assets to " + targetDir;
                    if (ok)
                    {
                        Debug.Log(message);
                    }
                    else
                    {
                        Debug.LogError(message + ", but " + SENTINEL + " is missing!");
                    }
                    return ok;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[StreamingAssetsExtractor] Failed to extract StreamingAssets from " + apkPath + ": " + e);
                return File.Exists(Path.Combine(targetDir, SENTINEL));
            }
        }

        private static bool IsUpToDate(string targetDir, string versionPath, string version)
        {
            try
            {
                return File.Exists(versionPath) &&
                    File.Exists(Path.Combine(targetDir, SENTINEL)) &&
                    string.Equals(File.ReadAllText(versionPath).Trim(), version, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int ExtractNestedZip(ZipArchiveEntry nested, string tempDir, string persistentDataPath)
        {
            // ZipArchive needs a seekable stream, so copy the inner zip to a temp file first.
            string tempZip = Path.Combine(persistentDataPath, "StreamingAssets.zip.tmp");
            try
            {
                using (var input = nested.Open())
                using (var output = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
                {
                    input.CopyTo(output);
                }

                int count = 0;
                using (var inner = ZipFile.OpenRead(tempZip))
                {
                    foreach (var entry in inner.Entries)
                    {
                        if (WriteEntry(entry, entry.FullName, tempDir))
                        {
                            count++;
                        }
                    }
                }
                return count;
            }
            finally
            {
                TryDeleteFile(tempZip);
            }
        }

        private static int ExtractRawAssets(ZipArchive apk, string tempDir)
        {
            int count = 0;
            foreach (var entry in apk.Entries)
            {
                string name = entry.FullName;
                if (!name.StartsWith("assets/", StringComparison.Ordinal))
                {
                    continue;
                }

                bool skip = false;
                foreach (var prefix in _skipPrefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip)
                {
                    continue;
                }

                if (WriteEntry(entry, name.Substring("assets/".Length), tempDir))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool WriteEntry(ZipArchiveEntry entry, string relativeName, string rootDir)
        {
            // Zip entries should use '/', but older builds made on Windows used '\'.
            string relative = relativeName.Replace('\\', '/').TrimStart('/');
            if (relative.Length == 0 || relative.EndsWith("/", StringComparison.Ordinal))
            {
                return false; // directory entry
            }

            string root = Path.GetFullPath(rootDir);
            string dest = Path.GetFullPath(Path.Combine(root, relative));
            if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Debug.LogWarning("[StreamingAssetsExtractor] Skipping unsafe entry " + relativeName);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using (var input = entry.Open())
            using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                input.CopyTo(output);
            }
            return true;
        }

        private static string ReadEntryText(ZipArchive zip, string entryName)
        {
            var entry = zip.GetEntry(entryName);
            if (entry == null)
            {
                return null;
            }

            using (var input = entry.Open())
            using (var reader = new StreamReader(input, Encoding.UTF8))
            {
                return reader.ReadToEnd().Trim();
            }
        }

        private static string SafeTicks(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path).Ticks.ToString();
            }
            catch (Exception)
            {
                return "0";
            }
        }

        private static void DeleteDirectory(string dir)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        private static void TryDeleteFile(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // best effort
            }
        }
    }
}
#endif
