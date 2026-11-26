// The Song Setup screen.
//
// On the instrument / difficulty screen (after picking a song) a "SONG SETUP" bar appears at the
// bottom. Tapping it (clicking it on a computer), or the controller's Song Setup button (shown in
// the help bar), opens this screen, where you can, while listening to the song:
//
//   - change its speed (50% to 200%) and its key (12 semitones down to 12 up);
//   - pick a stretch of it on a bar of the whole song, by dragging the two yellow ends, and tap
//     anywhere on the bar to hear the song from there;
//   - turn freestyle guitar on or off.
//
// Everything you set is heard straight away in the preview, and is used when the song starts:
// the speed goes into the game's own speed box, the key change is applied to the whole song,
// and a picked stretch is played on repeat in practice mode.
//
// The screen is built in code (no scene or prefab changes) on its own canvas, and laid out in
// fractions of the screen so it fits both the folded and the unfolded display.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YARG.Audio.BASS;
using YARG.Core.Audio;
using YARG.Core.Input;
using YARG.Core.Song;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Navigation;
using YARG.Settings;

namespace YARG.Menu.SongSetup
{
    /// <summary>A bar that reports where along its width it is being touched (0 to 1).</summary>
    public sealed class TouchBar : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<float> Pressed;
        public Action<float> Dragged;
        public Action        Released;

        private float Position(PointerEventData data)
        {
            var rect = (RectTransform) transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera,
                out var local))
            {
                return 0f;
            }
            return Mathf.Clamp01((local.x - rect.rect.xMin) / Mathf.Max(1f, rect.rect.width));
        }

        public void OnPointerDown(PointerEventData data) => Pressed?.Invoke(Position(data));
        public void OnDrag(PointerEventData data) => Dragged?.Invoke(Position(data));
        public void OnPointerUp(PointerEventData data) => Released?.Invoke();
    }

    public sealed class SongSetupPanel : MonoBehaviour
    {
        // (The same range as Left / Right on the instrument screen)
        private const float MIN_SPEED = 0.25f;
        private const float MAX_SPEED = 2.0f;
        private const float SPEED_STEP = 0.05f;
        private const int   MAX_KEY = 12;
        private const double MIN_SPAN = 2.0;

        private static readonly Color Yellow = new(1f, 0.85f, 0.3f);
        private static readonly Color Dark = new(0.15f, 0.2f, 0.3f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // The phone, and the Windows, Mac and Linux builds (not inside the Unity editor)
#if (UNITY_ANDROID || UNITY_STANDALONE) && !UNITY_EDITOR
            var host = new GameObject("YARG Song Setup");
            DontDestroyOnLoad(host);
            host.AddComponent<SongSetupPanel>();
#endif
        }

        private Canvas          _canvas;
        private GameObject      _openBar;
        private TextMeshProUGUI _openBarText;
        private GameObject      _panel;

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _speedLabel;
        private TextMeshProUGUI _keyLabel;
        private TextMeshProUGUI _sectionLabel;
        private TextMeshProUGUI _previewButton;
        private TextMeshProUGUI _freestyleButton;
        private RectTransform   _speedFill;
        private RectTransform   _keyFill;
        private RectTransform   _selection;
        private RectTransform   _startHandle;
        private RectTransform   _endHandle;
        private RectTransform   _playhead;

        // What is being set up
        private SongEntry _song;
        private float     _speed = 1f;
        private int       _key;
        private double    _length = 1;
        private double    _start;
        private double    _end = 1;

        // Preview
        private StemMixer       _mixer;
        private Task<StemMixer> _loading;
        private SongEntry       _loadingSong;
        private bool            _playing;
        private bool            _playWhenLoaded;
        private double          _position;      // where the preview is (or will start), in seconds of song
        private double          _positionClock; // when _position was true
        private int             _grabbed;       // 0 = nothing, 1 = start end, 2 = finish end

        private string _lastBarText;

        private static SongSetupPanel _instance;
        private bool _schemeOn;

        /// <summary>True while the instrument screen is up and the setup screen can be opened.</summary>
        public static bool CanOpen => _instance != null && _instance._canvas != null &&
            DifficultySelectMenu.Current != null && !_instance._panel.activeSelf;

        /// <summary>Opens the setup screen (the controller's Song Setup button).</summary>
        public static void OpenFromMenu()
        {
            if (CanOpen)
            {
                Safely(_instance.Open);
            }
        }

        /// <summary>True while the setup screen is showing.</summary>
        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

        /// <summary>Closes the setup screen if it is showing.</summary>
        public static void CloseFromMenu()
        {
            if (IsOpen)
            {
                Safely(_instance.Close);
            }
        }

        private void Awake()
        {
            _instance = this;
            try
            {
                Build();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void OnDestroy()
        {
            DropPreview();
        }

        private void Update()
        {
            if (_canvas == null)
            {
                return;
            }

            var menu = DifficultySelectMenu.Current;
            bool open = _panel.activeSelf;

            if (menu == null)
            {
                // Left the screen (back to the library, or the song is starting)
                if (open)
                {
                    Close();
                }
                if (_openBar.activeSelf)
                {
                    _openBar.SetActive(false);
                }
                return;
            }

            if (!open)
            {
                // Out of the way of the venue list, which it would otherwise cover
                bool showBar = !YARG.Venue.Drawn.VenuePicker.PanelOpen;
                if (_openBar.activeSelf != showBar)
                {
                    _openBar.SetActive(showBar);
                }
                if (showBar)
                {
                    UpdateOpenBar(menu);
                }
                return;
            }

            UpdatePreview();
            UpdateSongBar();
        }

        #region Opening and closing

        private void UpdateOpenBar(DifficultySelectMenu menu)
        {
            var song = GlobalVariables.State.CurrentSong;
            float key = SongSetupState.KeyShiftFor(song);
            string section = SongSetupState.TryGetSpan(song, out double a, out double b)
                ? $"{Clock(a)} to {Clock(b)}"
                : "whole song";
            string text = $"<b>SONG SETUP</b>  Speed, key, section and freestyle  |  "
                + $"{Mathf.RoundToInt(menu.ShownSongSpeed * 100f)}%  |  key {Signed(Mathf.RoundToInt(key))}  |  {section}"
                + (SongSetupState.Freestyle ? "  |  <color=#FFD84D>freestyle on</color>" : "");
            if (text != _lastBarText)
            {
                _lastBarText = text;
                _openBarText.text = text;
            }
        }

        private void Open()
        {
            var menu = DifficultySelectMenu.Current;
            var song = GlobalVariables.State.CurrentSong;
            if (menu == null || song == null)
            {
                return;
            }

            SongSetupState.Use(song);
            _song = song;
            _speed = Mathf.Clamp(menu.ShownSongSpeed, MIN_SPEED, MAX_SPEED);
            // (What is shown here is what the song will use)
            menu.ShownSongSpeed = _speed;
            _key = Mathf.RoundToInt(SongSetupState.KeyShift);
            _length = Math.Max(1.0, song.SongLengthSeconds);
            if (SongSetupState.HasSpan)
            {
                _start = SongSetupState.SpanStart;
                _end = SongSetupState.SpanEnd;
            }
            else
            {
                _start = 0;
                _end = _length;
            }
            _position = _start;
            _playing = false;
            _playWhenLoaded = false;
            _grabbed = 0;

            string name = song.Name;
            string artist = song.Artist;
            _title.text = $"<b>SONG SETUP</b>   {name}  <color=#B8C4DC>{artist}</color>";

            _openBar.SetActive(false);
            _panel.SetActive(true);

            // The controller works this screen while it is open (and the screen under it waits)
            if (Navigator.Instance != null && !_schemeOn)
            {
                Navigator.Instance.PushScheme(new NavigationScheme(new List<NavigationScheme.Entry>
                {
                    new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Close", () => Safely(Close)),
                    new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.SongSetupPreview", () => Safely(TogglePreview)),
                    new NavigationScheme.Entry(MenuAction.Yellow, "Menu.Common.SongSetupFreestyle", () => Safely(ToggleFreestyle)),
                    new NavigationScheme.Entry(MenuAction.Blue, "Menu.Common.SongSetupStart", () => Safely(() => SetStart(PreviewPosition, false))),
                    new NavigationScheme.Entry(MenuAction.Orange, "Menu.Common.SongSetupEnd", () => Safely(() => SetEnd(PreviewPosition, false))),
                    new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.SongSetupSlower", () => Safely(() => SetSpeed(_speed - SPEED_STEP))),
                    new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.SongSetupFaster", () => Safely(() => SetSpeed(_speed + SPEED_STEP))),
                    new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.SongSetupKeyUp", () => Safely(() => SetKey(_key + 1))),
                    new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.SongSetupKeyDown", () => Safely(() => SetKey(_key - 1))),
                }, false, () => _schemeOn = false));
                _schemeOn = true;
            }

            StartLoading(song);
            RefreshLabels();
        }

        private void Close()
        {
            DropPreview();
            _panel.SetActive(false);
            _lastBarText = null;
            if (_schemeOn && Navigator.Instance != null)
            {
                _schemeOn = false;
                Navigator.Instance.PopScheme();
            }
        }

        #endregion

        #region Preview

        private void StartLoading(SongEntry song)
        {
            DropPreview();
            _loadingSong = song;
            _previewMissing = false;
            float speed = _speed;
            double volume = Math.Max(0.5, SettingsManager.Settings.PreviewVolume.Value);
            _loading = Task.Run(() => song.LoadAudio(speed, volume, SongStem.Crowd));
        }

        private void DropPreview()
        {
            _playing = false;
            _playWhenLoaded = false;
            if (_mixer != null)
            {
                try
                {
                    _mixer.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                _mixer = null;
            }

            // A load still under way is thrown away when it finishes
            var loading = _loading;
            _loading = null;
            _loadingSong = null;
            loading?.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    t.Result?.Dispose();
                }
            });
        }

        private double Now => Time.realtimeSinceStartupAsDouble;

        private bool _previewMissing;

        private double PreviewPosition => _playing ? _position + (Now - _positionClock) * _speed : _position;

        private void UpdatePreview()
        {
            if (_loading != null && _loading.IsCompleted)
            {
                var task = _loading;
                _loading = null;
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                {
                    _mixer = task.Result;
                    if (_mixer.Length > 1)
                    {
                        bool whole = _end >= _length - 0.05;
                        _length = _mixer.Length;
                        _end = whole ? _length : Math.Min(_end, _length);
                        _start = Math.Min(_start, Math.Max(0, _end - MIN_SPAN));
                    }
                    ApplySound();
                    if (_playWhenLoaded)
                    {
                        _playWhenLoaded = false;
                        PlayFrom(_position);
                    }
                    RefreshLabels();
                }
                else
                {
                    // The song's audio would not load: say so instead of "Loading..." for ever
                    _playWhenLoaded = false;
                    _previewMissing = true;
                    RefreshLabels();
                }
            }

            if (_playing && _grabbed == 0)
            {
                // Round and round the chosen stretch
                double position = PreviewPosition;
                if (position >= _end - 0.02 || position >= _length - 0.05)
                {
                    PlayFrom(_start);
                }
            }
        }

        private void ApplySound()
        {
            if (_mixer == null)
            {
                return;
            }
            _mixer.SetSpeed(_speed, true);
            (_mixer as BassStemMixer)?.SetKeyShift(_key);
        }

        private void PlayFrom(double position)
        {
            position = Math.Min(Math.Max(0, position), Math.Max(0, _length - 0.5));
            _position = position;
            _positionClock = Now;

            if (_mixer == null)
            {
                if (_previewMissing)
                {
                    return;
                }

                // Still loading: start as soon as it is ready
                _playWhenLoaded = true;
                RefreshLabels();
                return;
            }

            _mixer.SetPosition(position);
            _mixer.Play();
            _playing = true;
            RefreshLabels();
        }

        private void StopPreview()
        {
            _position = PreviewPosition;
            _playing = false;
            _playWhenLoaded = false;
            _mixer?.Pause();
            RefreshLabels();
        }

        private void TogglePreview()
        {
            if (_playing || _playWhenLoaded)
            {
                StopPreview();
            }
            else
            {
                double from = _position >= _start && _position < _end - 0.5 ? _position : _start;
                PlayFrom(from);
            }
        }

        #endregion

        #region Changing things

        private void SetSpeed(float speed)
        {
            speed = Mathf.Round(Mathf.Clamp(speed, MIN_SPEED, MAX_SPEED) / SPEED_STEP) * SPEED_STEP;
            if (Mathf.Approximately(speed, _speed))
            {
                return;
            }

            // Keep the preview's place across the change
            _position = PreviewPosition;
            _positionClock = Now;
            _speed = speed;
            ApplySound();

            var menu = DifficultySelectMenu.Current;
            if (menu != null)
            {
                menu.ShownSongSpeed = speed;
            }
            RefreshLabels();
        }

        private void SetKey(int key)
        {
            key = Mathf.Clamp(key, -MAX_KEY, MAX_KEY);
            if (key == _key)
            {
                return;
            }
            _key = key;
            SongSetupState.SetKeyShift(key);
            ApplySound();
            RefreshLabels();
        }

        private void StoreSpan()
        {
            if (_start <= 0.25 && _end >= _length - 0.25)
            {
                SongSetupState.ClearSpan();
            }
            else
            {
                SongSetupState.SetSpan(_start, _end);
            }
        }

        private void SetStart(double start, bool listen)
        {
            _start = Math.Min(Math.Max(0, start), _end - MIN_SPAN);
            if (_start < 0)
            {
                _start = 0;
            }
            StoreSpan();
            if (listen && (_playing || _playWhenLoaded))
            {
                PlayFrom(_start);
            }
            RefreshLabels();
        }

        private void SetEnd(double end, bool listen)
        {
            _end = Math.Max(Math.Min(_length, end), _start + MIN_SPAN);
            if (_end > _length)
            {
                _end = _length;
            }
            StoreSpan();
            if (listen && (_playing || _playWhenLoaded))
            {
                // Hear the last few seconds before the end
                PlayFrom(Math.Max(_start, _end - 3.0));
            }
            RefreshLabels();
        }

        private void WholeSong()
        {
            _start = 0;
            _end = _length;
            SongSetupState.ClearSpan();
            RefreshLabels();
        }

        private void ResetAll()
        {
            SetSpeed(1f);
            SetKey(0);
            WholeSong();
            SongSetupState.Freestyle = false;
            RefreshLabels();
        }

        private void ToggleFreestyle()
        {
            SongSetupState.Freestyle = !SongSetupState.Freestyle;
            RefreshLabels();
        }

        // The bar of the whole song
        private void SongBarPressed(float at)
        {
            float startAt = (float) (_start / _length);
            float endAt = (float) (_end / _length);
            float toStart = Mathf.Abs(at - startAt);
            float toEnd = Mathf.Abs(at - endAt);

            const float REACH = 0.045f;
            if (toStart <= REACH && toStart <= toEnd)
            {
                _grabbed = 1;
            }
            else if (toEnd <= REACH)
            {
                _grabbed = 2;
            }
            else
            {
                // A tap anywhere else: listen from there
                _grabbed = 0;
                PlayFrom(at * _length);
            }
        }

        private void SongBarDragged(float at)
        {
            if (_grabbed == 1)
            {
                SetStart(at * _length, false);
            }
            else if (_grabbed == 2)
            {
                SetEnd(at * _length, false);
            }
        }

        private void SongBarReleased()
        {
            int grabbed = _grabbed;
            _grabbed = 0;
            if (grabbed == 1)
            {
                SetStart(_start, true);
                if (!_playing && !_playWhenLoaded)
                {
                    PlayFrom(_start);
                }
            }
            else if (grabbed == 2)
            {
                SetEnd(_end, true);
                if (!_playing && !_playWhenLoaded)
                {
                    PlayFrom(Math.Max(_start, _end - 3.0));
                }
            }
        }

        #endregion

        #region Showing things

        private static string Clock(double seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt((float) seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        private static string Signed(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }

        private void RefreshLabels()
        {
            _speedLabel.text = $"Speed  <b>{Mathf.RoundToInt(_speed * 100f)}%</b>";
            _keyLabel.text = _key == 0
                ? "Key  <b>as recorded</b>"
                : $"Key  <b>{Signed(_key)}</b> semitone{(Math.Abs(_key) == 1 ? "" : "s")}";

            bool whole = _start <= 0.25 && _end >= _length - 0.25;
            _sectionLabel.text = whole
                ? $"Section  <b>whole song</b> ({Clock(_length)})"
                : $"Section  <b>{Clock(_start)} to {Clock(_end)}</b> ({Clock(_end - _start)} long, played on repeat as practice)";

            _previewButton.text = _playing ? "Stop preview"
                : _previewMissing ? "No preview"
                : _playWhenLoaded ? "Loading..."
                : "Play preview";
            _freestyleButton.text = SongSetupState.Freestyle
                ? "Freestyle guitar: ON"
                : "Freestyle guitar: off";

            SetFill(_speedFill, 0f, (_speed - MIN_SPEED) / (MAX_SPEED - MIN_SPEED));
            float middle = 0.5f;
            float keyAt = (_key + MAX_KEY) / (2f * MAX_KEY);
            SetFill(_keyFill, Mathf.Min(middle, keyAt), Mathf.Max(middle, keyAt));
        }

        private void UpdateSongBar()
        {
            float startAt = (float) (_start / _length);
            float endAt = (float) (_end / _length);
            SetFill(_selection, startAt, endAt);
            SetMarker(_startHandle, startAt);
            SetMarker(_endHandle, endAt);
            SetMarker(_playhead, Mathf.Clamp01((float) (PreviewPosition / _length)));
        }

        private static void SetFill(RectTransform rect, float from, float to)
        {
            rect.anchorMin = new Vector2(Mathf.Clamp01(from), 0f);
            rect.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Max(from, to)), 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetMarker(RectTransform rect, float at)
        {
            rect.anchorMin = new Vector2(at, 0f);
            rect.anchorMax = new Vector2(at, 1f);
        }

        #endregion

        #region Building the screen

        private void Build()
        {
            var canvasObject = new GameObject("Canvas");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 95;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();
            var root = (RectTransform) canvasObject.transform;

            // The bar that opens the screen, above the PHONES bar
            {
                _openBar = new GameObject("Open bar");
                _openBar.transform.SetParent(root, false);
                var rect = _openBar.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.04f, 0f);
                rect.anchorMax = new Vector2(0.96f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 168f);
                rect.sizeDelta = new Vector2(0f, 62f);
                _openBar.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
                var button = _openBar.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Safely(Open));
                _openBarText = Label(rect, 0.01f, 0.05f, 0.99f, 0.95f, 30, TextAlignmentOptions.Center);
                _openBar.SetActive(false);
            }

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(root, false);
            var panel = _panel.AddComponent<RectTransform>();
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            _panel.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.08f, 0.97f); // also blocks touches behind it

            _title = Label(panel, 0.04f, 0.895f, 0.96f, 0.985f, 46, TextAlignmentOptions.Left);

            // Speed
            _speedLabel = Label(panel, 0.04f, 0.76f, 0.33f, 0.86f, 38, TextAlignmentOptions.Left);
            {
                var bar = Bar(panel, 0.35f, 0.775f, 0.78f, 0.845f, out _speedFill);
                bar.Pressed = at => Safely(() => SetSpeed(MIN_SPEED + at * (MAX_SPEED - MIN_SPEED)));
                bar.Dragged = bar.Pressed;
                // 100% sits a third of the way along
                Tick((RectTransform) bar.transform, (1f - MIN_SPEED) / (MAX_SPEED - MIN_SPEED));
            }
            TextButton(panel, 0.80f, 0.765f, 0.87f, 0.855f, "-", Dark, Color.white, () => SetSpeed(_speed - SPEED_STEP));
            TextButton(panel, 0.89f, 0.765f, 0.96f, 0.855f, "+", Dark, Color.white, () => SetSpeed(_speed + SPEED_STEP));

            // Key
            _keyLabel = Label(panel, 0.04f, 0.62f, 0.33f, 0.72f, 38, TextAlignmentOptions.Left);
            {
                var bar = Bar(panel, 0.35f, 0.635f, 0.78f, 0.705f, out _keyFill);
                bar.Pressed = at => Safely(() => SetKey(Mathf.RoundToInt(at * 2f * MAX_KEY) - MAX_KEY));
                bar.Dragged = bar.Pressed;
                Tick((RectTransform) bar.transform, 0.5f);
            }
            TextButton(panel, 0.80f, 0.625f, 0.87f, 0.715f, "-", Dark, Color.white, () => SetKey(_key - 1));
            TextButton(panel, 0.89f, 0.625f, 0.96f, 0.715f, "+", Dark, Color.white, () => SetKey(_key + 1));

            // Section
            _sectionLabel = Label(panel, 0.04f, 0.505f, 0.96f, 0.585f, 36, TextAlignmentOptions.Left);
            {
                var bar = Bar(panel, 0.05f, 0.365f, 0.95f, 0.485f, out _selection);
                _selection.GetComponent<Image>().color = new Color(1f, 0.85f, 0.3f, 0.45f);
                bar.Pressed = at => Safely(() => SongBarPressed(at));
                bar.Dragged = at => Safely(() => SongBarDragged(at));
                bar.Released = () => Safely(SongBarReleased);

                var barRect = (RectTransform) bar.transform;
                _playhead = Marker(barRect, 6f, Color.white, 1f);
                _startHandle = Marker(barRect, 22f, Yellow, 1.25f);
                _endHandle = Marker(barRect, 22f, Yellow, 1.25f);
            }
            var hint = Label(panel, 0.04f, 0.305f, 0.96f, 0.355f, 26, TextAlignmentOptions.Center);
            hint.color = new Color(0.72f, 0.77f, 0.86f);
            hint.text = Application.isMobilePlatform
                ? "Drag the yellow ends to choose a section. Tap anywhere on the bar to listen from there."
                : "Drag the yellow ends to choose a section. Click anywhere on the bar to listen from there.";

            _previewButton = TextButton(panel, 0.04f, 0.185f, 0.34f, 0.285f, "Play preview", Yellow, Color.black,
                TogglePreview);
            TextButton(panel, 0.36f, 0.185f, 0.545f, 0.285f, "Start here", Dark, Color.white,
                () => SetStart(PreviewPosition, false));
            TextButton(panel, 0.565f, 0.185f, 0.75f, 0.285f, "End here", Dark, Color.white,
                () => SetEnd(PreviewPosition, false));
            TextButton(panel, 0.77f, 0.185f, 0.96f, 0.285f, "Whole song", Dark, Color.white, WholeSong);

            _freestyleButton = TextButton(panel, 0.04f, 0.045f, 0.44f, 0.145f, "Freestyle guitar: off", Dark,
                Color.white, ToggleFreestyle);
            TextButton(panel, 0.46f, 0.045f, 0.66f, 0.145f, "Reset", Dark, Color.white, ResetAll);
            TextButton(panel, 0.68f, 0.045f, 0.96f, 0.145f, "Done", Yellow, Color.black, Close);

            _panel.SetActive(false);
        }

        private static void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static RectTransform Area(RectTransform parent, float x0, float y0, float x1, float y1, string name)
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

        private static TextMeshProUGUI Label(RectTransform parent, float x0, float y0, float x1, float y1,
            float size, TextAlignmentOptions alignment)
        {
            var rect = Area(parent, x0, y0, x1, y1, "Text");
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.enableAutoSizing = true;
            text.fontSizeMin = 14;
            text.fontSizeMax = size;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.richText = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI TextButton(RectTransform parent, float x0, float y0, float x1, float y1,
            string label, Color background, Color textColor, Action onClick)
        {
            var rect = Area(parent, x0, y0, x1, y1, "Button");
            rect.gameObject.AddComponent<Image>().color = background;
            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => Safely(onClick));

            var text = Label(rect, 0.04f, 0.08f, 0.96f, 0.92f, 36, TextAlignmentOptions.Center);
            text.text = label;
            text.color = textColor;
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        // A touchable bar with a coloured part inside it
        private static TouchBar Bar(RectTransform parent, float x0, float y0, float x1, float y1,
            out RectTransform fill)
        {
            var rect = Area(parent, x0, y0, x1, y1, "Bar");
            rect.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f);
            var bar = rect.gameObject.AddComponent<TouchBar>();

            fill = Area(rect, 0f, 0f, 0f, 1f, "Fill");
            var image = fill.gameObject.AddComponent<Image>();
            image.color = Yellow;
            image.raycastTarget = false;
            return bar;
        }

        // A thin fixed line on a bar (where "normal" is)
        private static void Tick(RectTransform bar, float at)
        {
            var rect = Marker(bar, 4f, new Color(1f, 1f, 1f, 0.6f), 1.3f);
            SetMarker(rect, at);
        }

        // A vertical line on a bar that can be moved along it
        private static RectTransform Marker(RectTransform bar, float width, Color color, float tallness)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(bar, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.localScale = new Vector3(1f, tallness, 1f);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        #endregion
    }
}
