// YARG Resurrected - pop-outs on the right of the menus: a light blue box explaining the option
// under the cursor, and a card with an artist's picture and a short bio when choosing whose part
// to sing or rap.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using YARG.Core.Game;
using YARG.Core.Logging;

namespace YARG.Menu.Persistent
{
    public sealed class MenuInfoPanel : MonoBehaviour
    {
        private static MenuInfoPanel _instance;

        // The explanation box: light blue, white writing
        private static readonly Color HintBlue = new(0.16f, 0.55f, 0.90f, 0.96f);
        private static readonly Color CardBack = new(0.06f, 0.07f, 0.12f, 0.94f);

        private RectTransform _hintBox;
        private TextMeshProUGUI _hintTitle;
        private TextMeshProUGUI _hintText;

        private RectTransform _card;
        private RawImage _picture;
        private TextMeshProUGUI _pictureNote;
        private TextMeshProUGUI _cardName;
        private TextMeshProUGUI _cardBio;

        private string _cardFor;
        private Coroutine _lookup;

        private static MenuInfoPanel Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Menu Info Panel");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<MenuInfoPanel>();
                    _instance.Build();
                }
                return _instance;
            }
        }

        // ------------------------------------------------------------------ the explanation box

        /// <summary>Shows a light blue box on the right explaining what is under the cursor.</summary>
        public static void ShowHint(string title, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                HideHint();
                return;
            }

            var panel = Instance;
            panel._hintTitle.text = title ?? string.Empty;
            panel._hintTitle.gameObject.SetActive(!string.IsNullOrEmpty(title));
            panel._hintText.text = text;
            panel._hintBox.gameObject.SetActive(true);
        }

        public static void HideHint()
        {
            if (_instance != null) _instance._hintBox.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ the artist card

        /// <summary>Shows an artist's picture and a short bio on the right (looked up on Wikipedia, kept on the phone).</summary>
        public static void ShowArtist(string artist)
        {
            if (string.IsNullOrWhiteSpace(artist) || !Enabled)
            {
                HideArtist();
                return;
            }

            var panel = Instance;
            artist = artist.Trim();
            panel._card.gameObject.SetActive(true);
            if (artist == panel._cardFor) return;

            panel._cardFor = artist;
            panel._cardName.text = artist;
            panel._cardBio.text = "Looking up " + artist + "...";
            panel._picture.texture = null;
            panel._picture.color = new Color(1f, 1f, 1f, 0f);
            panel._pictureNote.text = string.Empty;
            if (panel._lookup != null) panel.StopCoroutine(panel._lookup);
            panel._lookup = panel.StartCoroutine(panel.Lookup(artist));
        }

        public static void HideArtist()
        {
            if (_instance == null) return;
            _instance._card.gameObject.SetActive(false);
        }

        public static void HideAll()
        {
            HideHint();
            HideArtist();
        }

        /// <summary>Settings > General > Artist Pictures and Bios.</summary>
        private static bool Enabled
        {
            get
            {
                var settings = YARG.Settings.SettingsManager.Settings;
                return settings == null || settings.ShowArtistCards.Value;
            }
        }

        /// <summary>The first name in "A feat. B", "A & B", "A, B", "A x B".</summary>
        public static string FirstArtist(string artists)
        {
            if (string.IsNullOrWhiteSpace(artists)) return null;
            string s = artists;
            foreach (string cut in new[] { " feat. ", " feat ", " ft. ", " ft ", " featuring ", " & ", ", ", " x ", " X ", " and ", " with ", " vs. ", " vs " })
            {
                int at = s.IndexOf(cut, StringComparison.OrdinalIgnoreCase);
                if (at > 0) s = s.Substring(0, at);
            }
            return s.Trim();
        }

        // ------------------------------------------------------------------ looking an artist up

        private sealed class Info
        {
            public string Title = string.Empty;
            public string Description = string.Empty;
            public string Extract = string.Empty;
            public string ImageUrl = string.Empty;
        }

        private static readonly Dictionary<string, Info> _known = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] MusicWords =
        {
            "rapper", "singer", "musician", "songwriter", "band", "group", "record producer", "producer", "dj",
            "hip hop", "hip-hop", "duo", "trio", "vocalist", "composer", "artist", "rock", "pop", "r&b",
            "rhythm and blues", "soul", "country", "metal", "punk", "jazz", "reggae", "trap", "drill",
        };

        private static string CacheFolder
        {
            get
            {
                string folder = Path.Combine(Application.persistentDataPath, "artist_cards");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        private static string SafeName(string artist)
        {
            var sb = new StringBuilder();
            foreach (char c in artist.ToLowerInvariant())
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.Length > 60 ? sb.ToString(0, 60) : sb.ToString();
        }

        private IEnumerator Lookup(string artist)
        {
            if (!_known.TryGetValue(artist, out var info))
            {
                info = ReadCache(artist);
                if (info == null)
                {
                    info = new Info();
                    yield return Find(artist, info);
                    WriteCache(artist, info);
                }
                _known[artist] = info;
            }

            if (artist != _cardFor) yield break;

            if (string.IsNullOrEmpty(info.Extract))
            {
                _cardBio.text = "No bio found for " + artist + ". (Looked up on Wikipedia; needs the internet the first time.)";
            }
            else
            {
                string bio = Shorten(info.Extract, 360);
                _cardBio.text = string.IsNullOrEmpty(info.Description)
                    ? bio
                    : "<i>" + Capital(info.Description) + "</i>\n\n" + bio;
            }

            yield return LoadPicture(artist, info);
        }

        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // Whole sentences up to about the given length
        private static string Shorten(string text, int most)
        {
            text = text.Trim();
            if (text.Length <= most) return text;
            int cut = text.LastIndexOf(". ", most, StringComparison.Ordinal);
            return cut > most / 3 ? text.Substring(0, cut + 1) : text.Substring(0, most).TrimEnd() + "...";
        }

        private static IEnumerator Find(string artist, Info into)
        {
            // The plain name first, then the usual ways Wikipedia tells musicians apart
            var titles = new List<string>
            {
                artist, artist + " (rapper)", artist + " (musician)", artist + " (singer)", artist + " (band)",
                artist + " (group)", artist + " (DJ)",
            };

            foreach (string title in titles)
            {
                var found = new Info[1];
                yield return Summary(title, found);
                if (found[0] != null && LooksLikeMusic(found[0]))
                {
                    Copy(found[0], into);
                    yield break;
                }
            }

            // Then a search
            string url = "https://en.wikipedia.org/w/rest.php/v1/search/page?limit=5&q=" +
                UnityWebRequest.EscapeURL(artist + " musician");
            using var request = Get(url);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            JArray pages = null;
            try
            {
                pages = JObject.Parse(request.downloadHandler.text)["pages"] as JArray;
            }
            catch (Exception e)
            {
                YargLogger.LogWarning("Artist search reply unreadable: " + e.Message);
            }
            if (pages == null) yield break;

            foreach (var page in pages)
            {
                string key = (string) page["key"];
                if (string.IsNullOrEmpty(key)) continue;
                var found = new Info[1];
                yield return Summary(key, found);
                if (found[0] != null && LooksLikeMusic(found[0]))
                {
                    Copy(found[0], into);
                    yield break;
                }
            }
        }

        private static void Copy(Info from, Info to)
        {
            to.Title = from.Title;
            to.Description = from.Description;
            to.Extract = from.Extract;
            to.ImageUrl = from.ImageUrl;
        }

        private static bool LooksLikeMusic(Info info)
        {
            string text = (info.Description + " " + Shorten(info.Extract, 300)).ToLowerInvariant();
            foreach (string word in MusicWords)
            {
                if (text.Contains(word)) return true;
            }
            return false;
        }

        private static UnityWebRequest Get(string url)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = 8;
            // Wikipedia asks programs to say what they are
            request.SetRequestHeader("Api-User-Agent", "YARG-Resurrected/1.0 (rhythm game; artist pictures)");
            return request;
        }

        private static IEnumerator Summary(string title, Info[] result)
        {
            string url = "https://en.wikipedia.org/api/rest_v1/page/summary/" +
                UnityWebRequest.EscapeURL(title.Replace(' ', '_')).Replace("+", "%20");
            using var request = Get(url);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            try
            {
                var json = JObject.Parse(request.downloadHandler.text);
                if ((string) json["type"] != "standard") yield break;
                result[0] = new Info
                {
                    Title = (string) json["title"] ?? title,
                    Description = (string) json["description"] ?? string.Empty,
                    Extract = (string) json["extract"] ?? string.Empty,
                    ImageUrl = (string) json["thumbnail"]?["source"] ?? string.Empty,
                };
            }
            catch (Exception e)
            {
                YargLogger.LogWarning("Artist summary unreadable: " + e.Message);
            }
        }

        private IEnumerator LoadPicture(string artist, Info info)
        {
            string path = Path.Combine(CacheFolder, SafeName(artist) + ".img");
            byte[] bytes = null;
            if (File.Exists(path))
            {
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception)
                {
                    bytes = null;
                }
            }

            if (bytes == null && !string.IsNullOrEmpty(info.ImageUrl))
            {
                using var request = Get(info.ImageUrl);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    bytes = request.downloadHandler.data;
                    try
                    {
                        File.WriteAllBytes(path, bytes);
                    }
                    catch (Exception)
                    {
                        // Not kept: looked up again next time
                    }
                }
            }

            if (artist != _cardFor) yield break;

            if (bytes == null || bytes.Length == 0)
            {
                _pictureNote.text = "No picture";
                yield break;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                _pictureNote.text = "No picture";
                yield break;
            }

            if (_picture.texture != null && _picture.texture is Texture2D old && old != texture) Destroy(old);
            _picture.texture = texture;
            _picture.color = Color.white;
            _pictureNote.text = string.Empty;
            FitPicture(texture);
        }

        // Fill the picture's box without stretching: crop the middle of the photo to the box's shape
        private void FitPicture(Texture texture)
        {
            var box = _picture.rectTransform.rect;
            if (box.width <= 0 || box.height <= 0 || texture.width <= 0 || texture.height <= 0) return;

            float boxAspect = box.width / box.height;
            float picAspect = texture.width / (float) texture.height;
            if (picAspect > boxAspect)
            {
                float w = boxAspect / picAspect;
                _picture.uvRect = new Rect((1f - w) / 2f, 0f, w, 1f);
            }
            else
            {
                float h = picAspect / boxAspect;
                // Keep the top of a portrait photo, where the face is
                _picture.uvRect = new Rect(0f, 1f - h, 1f, h);
            }
        }

        private static Info ReadCache(string artist)
        {
            try
            {
                string path = Path.Combine(CacheFolder, SafeName(artist) + ".json");
                if (!File.Exists(path)) return null;
                // Look again after a month, and after a day when nothing was found
                var age = DateTime.Now - File.GetLastWriteTime(path);
                var json = JObject.Parse(File.ReadAllText(path));
                var info = new Info
                {
                    Title = (string) json["title"] ?? string.Empty,
                    Description = (string) json["description"] ?? string.Empty,
                    Extract = (string) json["extract"] ?? string.Empty,
                    ImageUrl = (string) json["image"] ?? string.Empty,
                };
                if (age.TotalDays > 30 || (string.IsNullOrEmpty(info.Extract) && age.TotalDays > 1)) return null;
                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteCache(string artist, Info info)
        {
            try
            {
                var json = new JObject
                {
                    ["title"] = info.Title,
                    ["description"] = info.Description,
                    ["extract"] = info.Extract,
                    ["image"] = info.ImageUrl,
                };
                File.WriteAllText(Path.Combine(CacheFolder, SafeName(artist) + ".json"), json.ToString());
            }
            catch (Exception)
            {
                // Not kept
            }
        }

        // ------------------------------------------------------------------ building it

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the menus; dialogs and the help bar stay usable underneath since nothing here takes touches
            canvas.sortingOrder = 40;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // The artist card: top right, picture on the left of it, the bio beside it
            _card = Area(transform, 0.60f, 0.52f, 0.985f, 0.90f, "Artist Card");
            Fill(_card, CardBack);
            var pictureArea = Area(_card, 0.03f, 0.08f, 0.40f, 0.92f, "Picture");
            Fill(pictureArea, new Color(1f, 1f, 1f, 0.06f));
            var picture = Area(pictureArea, 0f, 0f, 1f, 1f, "Photo");
            _picture = picture.gameObject.AddComponent<RawImage>();
            _picture.raycastTarget = false;
            _picture.color = new Color(1f, 1f, 1f, 0f);
            _pictureNote = Text(pictureArea, 0f, 0f, 1f, 1f, 26, TextAlignmentOptions.Center);
            _pictureNote.color = new Color(1f, 1f, 1f, 0.6f);
            _cardName = Text(_card, 0.43f, 0.76f, 0.97f, 0.94f, 44, TextAlignmentOptions.BottomLeft);
            _cardName.fontStyle = FontStyles.Bold;
            _cardName.enableAutoSizing = true;
            _cardName.fontSizeMin = 24;
            _cardName.fontSizeMax = 48;
            _cardName.textWrappingMode = TextWrappingModes.NoWrap;
            _cardName.overflowMode = TextOverflowModes.Ellipsis;
            _cardBio = Text(_card, 0.43f, 0.06f, 0.97f, 0.74f, 26, TextAlignmentOptions.TopLeft);
            _cardBio.richText = true;
            _cardBio.enableAutoSizing = true;
            _cardBio.fontSizeMin = 18;
            _cardBio.fontSizeMax = 28;
            _cardBio.overflowMode = TextOverflowModes.Ellipsis;
            _card.gameObject.SetActive(false);

            // The explanation box: under the card, right side
            _hintBox = Area(transform, 0.60f, 0.14f, 0.985f, 0.49f, "Explanation");
            Fill(_hintBox, HintBlue);
            _hintTitle = Text(_hintBox, 0.05f, 0.74f, 0.95f, 0.95f, 38, TextAlignmentOptions.BottomLeft);
            _hintTitle.fontStyle = FontStyles.Bold;
            _hintTitle.color = Color.white;
            _hintTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _hintTitle.overflowMode = TextOverflowModes.Ellipsis;
            _hintText = Text(_hintBox, 0.05f, 0.06f, 0.95f, 0.72f, 30, TextAlignmentOptions.TopLeft);
            _hintText.color = Color.white;
            _hintText.enableAutoSizing = true;
            _hintText.fontSizeMin = 20;
            _hintText.fontSizeMax = 32;
            _hintText.overflowMode = TextOverflowModes.Ellipsis;
            _hintBox.gameObject.SetActive(false);
        }

        private static RectTransform Area(Transform parent, float x0, float y0, float x1, float y1, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void Fill(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static TextMeshProUGUI Text(Transform parent, float x0, float y0, float x1, float y1, float size,
            TextAlignmentOptions alignment)
        {
            var rect = Area(parent, x0, y0, x1, y1, "Text");
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------ what the options mean

        /// <summary>What a song modifier does, in a sentence or two.</summary>
        public static string Explain(Modifier modifier) => modifier switch
        {
            Modifier.AllStrums => "Every note has to be strummed: no hammer-ons, pull-offs or taps.",
            Modifier.AllHopos => "Every note can be played as a hammer-on or pull-off: press the fret without strumming once you're on a run.",
            Modifier.AllTaps => "Every note is a tap: just press the fret (or the chord) at the right time. No strumming at all. Great on a gamepad or for learning a song.",
            Modifier.HoposToTaps => "Hammer-ons and pull-offs become taps, so they can be played without strumming even after a miss.",
            Modifier.TapsToHopos => "Tap notes become hammer-ons and pull-offs: they need a strummed note before them.",
            Modifier.NoteShuffle => "The notes are moved to random frets. The rhythm stays the same.",
            Modifier.NoKicks => "Drums: the bass drum notes are taken out.",
            Modifier.UnpitchedOnly => "Vocals: only the timing matters, not the note you sing. Talk, rap or sing at any pitch.",
            Modifier.NoDynamics => "Drums: accent and ghost notes count as normal hits.",
            Modifier.NoVocalPercussion => "Vocals: the tambourine and cowbell parts between lines are taken out.",
            Modifier.RangeCompress => "Pro keys: the highway doesn't shift range during the song.",
            Modifier.OpensToGreens => "Open notes become green notes.",
            _ => string.Empty,
        };

        /// <summary>What an engine preset (Profile > Engine) changes.</summary>
        public static string ExplainEngine(string presetName) => presetName switch
        {
            "Casual" => "Casual: bigger timing windows, so notes count even when you're a little early or late, and strumming is more forgiving. Best for gamepads, phones and new players.",
            "Precision" => "Precision: tight timing windows for players who want every hit to be exact.",
            "Solo Taps" => "Solo Taps: guitar solos can be played without strumming.",
            "Default" => "Default: the normal timing windows.",
            _ => presetName + ": a custom engine preset (timing windows and strumming rules).",
        };
    }
}
