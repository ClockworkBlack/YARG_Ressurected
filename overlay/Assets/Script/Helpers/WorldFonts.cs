// YARG Resurrected - lyrics, song titles and artist names in every language. A new file on its own:
// nothing else is changed.
//
// The game carries fonts for Latin letters and for Japanese. A character none of them has (Korean
// above all, but also Chinese, Thai, Arabic, Hebrew, Hindi and more) is drawn as an empty box.
// The phone itself has fonts for all of these, so at startup the ones it has are added to the
// list the game falls back on when its own fonts lack a character. Nothing is added to the app's
// size, and text the game could already draw looks exactly as before.
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using YARG.Core.Logging;

namespace YARG.Helpers
{
    public static class WorldFonts
    {
        // One row per script: what it is for, then the beginnings of font file names to look for,
        // best first. All compared in lower case.
        private static readonly string[][] _wanted =
        {
            // One font with Korean, Chinese and Japanese together
            new[] { "Korean, Chinese, Japanese", "notosanscjk", "seccjk", "sourcehansans", "droidsansfallback" },
            new[] { "Cyrillic, Greek, Vietnamese", "roboto-regular", "notosans-regular", "notosans-vf", "segoeui", "arial" },
            new[] { "Thai", "notosansthai", "droidsansthai", "leelawui", "tahoma" },
            new[] { "Arabic", "notonaskharabic", "notosansarabic", "droidnaskh" },
            new[] { "Hebrew", "notosanshebrew", "droidsanshebrew" },
            new[] { "Devanagari", "notosansdevanagari", "nirmala" },
            new[] { "Bengali", "notosansbengali" },
            new[] { "Tamil", "notosanstamil" },
            new[] { "Telugu", "notosanstelugu" },
            new[] { "Gujarati", "notosansgujarati" },
            new[] { "Gurmukhi", "notosansgurmukhi" },
            new[] { "Kannada", "notosanskannada" },
            new[] { "Malayalam", "notosansmalayalam" },
            new[] { "Armenian", "notosansarmenian" },
            new[] { "Georgian", "notosansgeorgian" },
            new[] { "Ethiopic", "notosansethiopic" },
            new[] { "Khmer", "notosanskhmer" },
            new[] { "Lao", "notosanslao" },
            new[] { "Myanmar", "notosansmyanmar" },
            new[] { "Sinhala", "notosanssinhala" },
        };

        // Used only when the device has no font with all three together
        private static readonly string[][] _wantedApart =
        {
            new[] { "Korean", "notosanskr", "nanumgothic", "samsungkorean", "malgun", "applesdgothicneo", "applegothic" },
            new[] { "Chinese", "notosanssc", "notosanshans", "notosanstc", "notosanshant", "msyh", "pingfang" },
            new[] { "Japanese", "notosansjp", "mtlmr3m", "yugothm", "meiryo", "hiragino" },
        };

        // Not the plain upright face of a family
        private static readonly string[] _styles =
        {
            "bold", "italic", "black", "light", "thin", "medium", "condensed", "semi", "extra", "serif", "mono",
        };

        private static bool _done;

        /// <summary>The fonts that were added, as "Script: file name" lines (for the log and for support).</summary>
        public static readonly List<string> Added = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            try
            {
                Register();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The device's fonts for other languages could not be added");
            }
        }

        private static void Register()
        {
            if (_done) return;
            _done = true;

            var files = DeviceFontFiles();
            if (files.Count == 0)
            {
                YargLogger.LogWarning("No device fonts found: characters the game's own fonts lack will show as boxes");
                return;
            }

            var fallbacks = TMP_Settings.fallbackFontAssets;
            if (fallbacks == null)
            {
                fallbacks = new List<TMP_FontAsset>();
                TMP_Settings.fallbackFontAssets = fallbacks;
            }

            // The game's fonts are drawn with this shader, so it is certainly in the build
            Shader shader = null;
            var reference = TMP_Settings.defaultFontAsset;
            if (reference != null && reference.material != null)
            {
                shader = reference.material.shader;
            }

            FontEngine.InitializeFontEngine();

            bool together = false;
            for (int i = 0; i < _wanted.Length; i++)
            {
                bool found = Add(_wanted[i], files, fallbacks, shader);
                if (i == 0) together = found;
            }

            if (!together)
            {
                foreach (var row in _wantedApart)
                {
                    Add(row, files, fallbacks, shader);
                }
            }

            YargLogger.LogFormatInfo("Device fonts added for other languages: {0}", Added.Count == 0 ? "none" : string.Join("; ", Added));
        }

        private static bool Add(string[] row, List<string> files, List<TMP_FontAsset> fallbacks, Shader shader)
        {
            string path = Pick(row, files);
            if (path == null)
            {
                return false;
            }

            try
            {
                // Glyphs are drawn into the font's texture the first time a text needs them. 48 points
                // in a 1024 texture holds about 250 at a time; more textures are made as they fill.
                var asset = TMP_FontAsset.CreateFontAsset(path, 0, 48, 6, GlyphRenderMode.SDFAA, 1024, 1024);
                if (asset == null)
                {
                    return false;
                }

                asset.name = "Device font " + Path.GetFileNameWithoutExtension(path);
                asset.hideFlags = HideFlags.DontSave;
                if (asset.material != null)
                {
                    asset.material.hideFlags = HideFlags.DontSave;
                    if (shader != null)
                    {
                        asset.material.shader = shader;
                    }
                }

                fallbacks.Add(asset);
                Added.Add(row[0] + ": " + Path.GetFileName(path));
                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogWarning("Device font " + path + " could not be used: " + e.Message);
                return false;
            }
        }

        /// <summary>The best file for a script: the earliest name in the row, and of those the plain upright face.</summary>
        private static string Pick(string[] row, List<string> files)
        {
            string best = null;
            int bestScore = int.MaxValue;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file).ToLowerInvariant();
                for (int k = 1; k < row.Length; k++)
                {
                    if (!name.StartsWith(row[k], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    int score = k * 100;
                    foreach (string style in _styles)
                    {
                        if (name.Contains(style)) score += 10;
                    }

                    // "NotoSansThaiUI" is a squeezed version for menus; the full one reads better
                    if (name.Contains("ui-") || name.Contains("ui.")) score += 5;
                    if (!name.Contains("regular") && !name.Contains("-vf")) score += 1;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = file;
                    }

                    break;
                }
            }

            return best;
        }

        private static List<string> DeviceFontFiles()
        {
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Take(string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                string extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension != ".ttf" && extension != ".otf" && extension != ".ttc" && extension != ".otc") return;
                if (seen.Add(path)) files.Add(path);
            }

            try
            {
                foreach (string path in Font.GetPathsToOSFonts())
                {
                    Take(path);
                }
            }
            catch (Exception e)
            {
                YargLogger.LogFormatWarning("Could not ask the device for its fonts: {0}", e.Message);
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // Where Android and the phone makers keep their fonts, in case the list above is short
            foreach (string folder in new[] { "/system/fonts", "/product/fonts", "/system_ext/fonts", "/system/font", "/data/fonts/files" })
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        Take(path);
                    }
                }
                catch (Exception)
                {
                    // Not readable on this phone: the others will do
                }
            }
#endif

            return files;
        }
    }
}
