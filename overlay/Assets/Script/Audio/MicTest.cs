// YARG Resurrected - a microphone test. A new file on its own: nothing else is changed.
//
// On the Profiles screen a "MIC TEST" bar opens a panel that shows what the game hears from each
// player's microphone: how loud (against the level below which the game treats it as silence),
// which note, a needle that follows the voice up and down, and how many syllables it would count
// for a spoken line. "Hear myself" plays the microphone back, for headphones.
//
// The game's own microphone readings are used (the same ones a song is scored from), so what
// shows here is what counts in a song.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Audio;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Menu.Navigation;
using YARG.Menu.ProfileList;
using YARG.Player;
using YARG.Settings;
using YARG.Story;

namespace YARG.Audio
{
    public sealed class MicTest : MonoBehaviour
    {
        /// <summary>True while "Hear myself" is on in the test: the helper that silences playback without headphones stands aside.</summary>
        public static bool HearMyself { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            try
            {
                var host = new GameObject("YARG Mic Test");
                DontDestroyOnLoad(host);
                host.AddComponent<MicTest>();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The mic test could not start");
            }
        }

        private static readonly string[] _noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        // One microphone's row in the panel
        private sealed class Row
        {
            public MicDevice Mic;
            public bool WasRecording;
            public RectTransform Level;
            public Image LevelImage;
            public RectTransform Needle;
            public TextMeshProUGUI Note;
            public TextMeshProUGUI Syllables;
            public float Shown;
            public float Pitch = float.NaN;
            public float LastPitch;
            public double LastVoice = double.NegativeInfinity;
            public double LastSyllable = double.NegativeInfinity;
            public int Count;
            public float Quiet;
        }

        private Canvas _canvas;
        private GameObject _bar;
        private GameObject _panel;
        private RectTransform _rows;
        private TextMeshProUGUI _hearLabel;
        private readonly List<Row> _active = new List<Row>();
        private NavigationScheme _scheme;
        private bool _schemeOn;
        private float _nextLook;
        private bool _failed;

        private void Update()
        {
            if (_failed)
            {
                return;
            }

            try
            {
                if (_panel != null && _panel.activeSelf)
                {
                    Listen();
                    return;
                }

                // Only on the Profiles screen (looked for twice a second; it is cheap but not free)
                if (Time.unscaledTime < _nextLook)
                {
                    return;
                }

                _nextLook = Time.unscaledTime + 0.5f;
                bool profiles = SettingsManager.Settings != null && FindAnyObjectByType<ProfileListMenu>() != null;
                if (profiles && _canvas == null)
                {
                    Build();
                }

                if (_bar != null && _bar.activeSelf != profiles)
                {
                    _bar.SetActive(profiles);
                }
            }
            catch (Exception e)
            {
                _failed = true;
                YargLogger.LogException(e, "The mic test stopped after an error");
                Close();
                if (_canvas != null)
                {
                    _canvas.enabled = false;
                }
            }
        }

        private void Build()
        {
            var holder = new GameObject("Mic Test Canvas");
            holder.transform.SetParent(transform, false);
            _canvas = holder.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Over the menus and the PHONES bar, under the game's dialogs
            _canvas.sortingOrder = 96;
            var scaler = holder.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            holder.AddComponent<GraphicRaycaster>();

            // The bar: in the empty stretch of the Profiles screen's header
            var bar = StoryUi.Area(holder.transform, 0.36f, 0.895f, 0.62f, 0.965f, "Mic Test Bar");
            StoryUi.Button(bar, StoryButtonStyle.Primary, "MIC TEST", 34f, out _, out _).onClick.AddListener(Open);
            _bar = bar.gameObject;
            _bar.SetActive(false);

            var panel = StoryUi.Area(holder.transform, 0f, 0f, 1f, 1f, "Mic Test Panel");
            var scrim = panel.gameObject.AddComponent<Image>();
            scrim.color = new Color(0.02f, 0.02f, 0.03f, 0.96f);
            scrim.raycastTarget = true;
            _panel = panel.gameObject;

            StoryUi.Text(panel, 0.05f, 0.86f, 0.7f, 0.96f, "MIC TEST", 64f, true, StoryTheme.Gold, TextAlignmentOptions.Left, false);
            StoryUi.Text(panel, 0.05f, 0.77f, 0.95f, 0.86f,
                "Sing or speak. The bar is how loud the game hears you (it must pass the white line to count), the " +
                "dot follows your voice up and down, and the count is how many syllables a spoken line would get.",
                30f, false, StoryTheme.Cream, TextAlignmentOptions.TopLeft);

            _rows = StoryUi.Area(panel, 0.05f, 0.22f, 0.95f, 0.76f, "Microphones");

            var hear = StoryUi.Area(panel, 0.05f, 0.09f, 0.48f, 0.19f, "Hear");
            StoryUi.Button(hear, StoryButtonStyle.Normal, "Hear myself: off", 34f, out _, out _hearLabel).onClick.AddListener(ToggleHear);
            var close = StoryUi.Area(panel, 0.52f, 0.09f, 0.95f, 0.19f, "Close");
            StoryUi.Button(close, StoryButtonStyle.Primary, "Done", 34f, out _, out _).onClick.AddListener(Close);
            StoryUi.Text(panel, 0.05f, 0.02f, 0.95f, 0.085f,
                "Hear myself needs headphones (through the speaker it squeals) and works with \"This phone's mic\".",
                26f, false, StoryTheme.Muted, TextAlignmentOptions.Left);
            _panel.SetActive(false);
        }

        private void Open()
        {
            try
            {
                if (_panel == null || _panel.activeSelf)
                {
                    return;
                }

                foreach (Transform child in _rows)
                {
                    Destroy(child.gameObject);
                }

                _active.Clear();
                var mics = new List<KeyValuePair<string, MicDevice>>();
                foreach (var player in PlayerContainer.Players)
                {
                    var mic = player.Bindings?.Microphone;
                    if (mic != null)
                    {
                        mics.Add(new KeyValuePair<string, MicDevice>(player.Profile.Name + "  -  " + mic.DisplayName, mic));
                    }
                }

                if (mics.Count == 0)
                {
                    StoryUi.Text(_rows, 0f, 0.3f, 1f, 0.9f,
                        "No active profile has a microphone yet.\n\nClose this, pick a profile, set its Game Mode to Vocals, " +
                        "then Add New Device and choose a microphone (\"This phone's mic\" or \"Built-in mic\").",
                        36f, false, StoryTheme.Cream, TextAlignmentOptions.Center);
                }

                int shown = Mathf.Min(mics.Count, 4);
                for (int i = 0; i < shown; i++)
                {
                    float top = 1f - i / (float) shown;
                    float bottom = 1f - (i + 1) / (float) shown + 0.03f;
                    _active.Add(BuildRow(StoryUi.Area(_rows, 0f, bottom, 1f, top, "Mic"), mics[i].Key, mics[i].Value));
                }

                _panel.SetActive(true);
                _bar.SetActive(false);

                if (Navigator.Instance != null && !_schemeOn)
                {
                    _scheme = new NavigationScheme(new List<NavigationScheme.Entry>
                    {
                        new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", Close),
                        new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", ToggleHear),
                    }, null, () => _schemeOn = false);
                    Navigator.Instance.PushScheme(_scheme);
                    _schemeOn = true;
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The mic test could not open");
                Close();
            }
        }

        private Row BuildRow(RectTransform area, string title, MicDevice mic)
        {
            var row = new Row { Mic = mic, WasRecording = mic.IsRecordingOutput };
            StoryUi.Rounded(area, StoryTheme.Panel, 2.2f);
            StoryUi.Text(area, 0.02f, 0.62f, 0.6f, 0.95f, title, 32f, true, StoryTheme.Cream, TextAlignmentOptions.Left, false);
            row.Note = StoryUi.Text(area, 0.62f, 0.55f, 0.8f, 0.98f, "--", 56f, true, StoryTheme.Gold, TextAlignmentOptions.Center, false);
            row.Syllables = StoryUi.Text(area, 0.8f, 0.6f, 0.98f, 0.95f, "0 syllables", 28f, false, StoryTheme.Muted, TextAlignmentOptions.Right, false);

            // Loudness: a track, the bar, and a white line where the game starts to listen
            var track = StoryUi.Area(area, 0.02f, 0.36f, 0.98f, 0.52f, "Level Track");
            track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            row.Level = StoryUi.Area(track, 0f, 0f, 0f, 1f, "Level");
            row.LevelImage = row.Level.gameObject.AddComponent<Image>();
            row.LevelImage.color = StoryTheme.Won;
            float line = LevelToBar(SettingsManager.Settings.MicrophoneSensitivity.Value);
            StoryUi.Solid(track, line - 0.003f, -0.3f, line + 0.003f, 1.3f, Color.white);

            // Pitch: low notes on the left, high on the right
            var lane = StoryUi.Area(area, 0.02f, 0.1f, 0.98f, 0.26f, "Pitch Lane");
            lane.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            row.Needle = StoryUi.Area(lane, 0f, 0f, 0f, 1f, "Needle");
            row.Needle.gameObject.AddComponent<Image>().color = StoryTheme.Gold;
            row.Needle.gameObject.SetActive(false);

            mic.ClearOutputQueue();
            mic.IsRecordingOutput = true;
            return row;
        }

        // The game measures loudness in decibels of its own scale: about -50 (silence) to 50 (shouting)
        private static float LevelToBar(float level)
        {
            return Mathf.Clamp01((level + 50f) / 100f);
        }

        private void Listen()
        {
            double now = Time.unscaledTimeAsDouble;
            foreach (var row in _active)
            {
                float loudest = float.NaN;
                while (row.Mic.DequeueOutputFrame(out var frame))
                {
                    if (frame.IsHit)
                    {
                        // A jump in loudness: the start of a syllable, as in a song
                        if (now - row.LastSyllable >= 0.09)
                        {
                            row.Count++;
                            row.LastSyllable = now;
                        }

                        continue;
                    }

                    float pitch = frame.PitchAsMidiNote;
                    bool afterSilence = now - row.LastVoice > 0.07;
                    if ((afterSilence || Mathf.Abs(pitch - row.LastPitch) >= 1.5f) && now - row.LastSyllable >= 0.09)
                    {
                        row.Count++;
                        row.LastSyllable = now;
                        row.LastPitch = pitch;
                    }

                    row.LastVoice = now;
                    row.Pitch = pitch;
                    loudest = float.IsNaN(loudest) ? frame.Volume : Mathf.Max(loudest, frame.Volume);
                }

                bool voice = now - row.LastVoice < 0.15;
                if (!float.IsNaN(loudest))
                {
                    row.Shown = Mathf.Max(row.Shown, LevelToBar(loudest));
                }

                // Falls back smoothly when the voice stops
                row.Shown = Mathf.MoveTowards(row.Shown, voice ? row.Shown : 0f, Time.unscaledDeltaTime * 1.5f);
                row.Level.anchorMax = new Vector2(row.Shown, 1f);
                if (!float.IsNaN(loudest))
                {
                    row.Shown = Mathf.Lerp(row.Shown, LevelToBar(loudest), 0.5f);
                }

                row.LevelImage.color = row.Shown > 0.92f ? StoryTheme.Danger : StoryTheme.Won;

                row.Needle.gameObject.SetActive(voice);
                if (voice && !float.IsNaN(row.Pitch))
                {
                    // E2 (40) to C6 (84) across the lane
                    float x = Mathf.Clamp01((row.Pitch - 40f) / 44f);
                    row.Needle.anchorMin = new Vector2(Mathf.Max(0f, x - 0.008f), 0f);
                    row.Needle.anchorMax = new Vector2(Mathf.Min(1f, x + 0.008f), 1f);
                    int note = Mathf.RoundToInt(row.Pitch);
                    row.Note.text = _noteNames[((note % 12) + 12) % 12] + (note / 12 - 1);
                    row.Quiet = 0f;
                }
                else
                {
                    row.Quiet += Time.unscaledDeltaTime;
                    if (row.Quiet > 0.6f)
                    {
                        row.Note.text = "--";
                    }

                    // A new line starts after a pause, as in a song
                    if (row.Quiet > 2.5f && row.Count > 0)
                    {
                        row.Count = 0;
                    }
                }

                row.Syllables.text = row.Count == 1 ? "1 syllable" : row.Count + " syllables";
            }
        }

        private void ToggleHear()
        {
            HearMyself = !HearMyself;
            ApplyHear();
        }

        private void ApplyHear()
        {
            if (_hearLabel != null)
            {
                _hearLabel.text = HearMyself ? "Hear myself: ON" : "Hear myself: off";
            }

            float volume = HearMyself ? Mathf.Max(0.5f, SettingsManager.Settings.VocalMonitoring.Value) : 0f;
            foreach (var row in _active)
            {
                row.Mic.SetMonitoringLevel(volume);
            }
        }

        private void Close()
        {
            try
            {
                if (HearMyself)
                {
                    HearMyself = false;
                    ApplyHear();
                }

                foreach (var row in _active)
                {
                    row.Mic.IsRecordingOutput = row.WasRecording;
                    row.Mic.ClearOutputQueue();
                    // Back to what the settings say (the squeal guard takes it from there)
                    row.Mic.SetMonitoringLevel(SettingsManager.Settings.VocalMonitoring.Value);
                }

                _active.Clear();
                if (_panel != null)
                {
                    _panel.SetActive(false);
                }

                if (_schemeOn && Navigator.Instance != null)
                {
                    _schemeOn = false;
                    Navigator.Instance.PopScheme();
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "The mic test did not close cleanly");
            }
        }
    }
}
