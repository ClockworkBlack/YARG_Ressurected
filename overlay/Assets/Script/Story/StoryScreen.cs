// YARG Resurrected - Story Mode screens.
//
// The one canvas that shows the story: title, tour, gig cards, the song picker, scenes, results
// and the liner notes. StoryMode (the game side) creates it, shows and hides it, and hands it the
// result of every gig; this class switches between the views and owns all input.
//
// Input: touch works through ordinary UGUI buttons. Pads and keyboards never press UGUI buttons in
// this game, so the screen pushes one navigation scheme of its own while it is shown and moves its
// own focus: Up/Down/Left/Right go to the nearest control in that direction (whatever the layout),
// Green presses it, Red goes back, Yellow and Blue are the two extra actions of the view, and
// holding Start skips a scene. Handlers only queue the action; it runs in Update, so nothing that
// loads a scene or pushes a menu ever runs inside the Navigator's own loops.
//
// Layout: the canvas is matched to 1920x1080 in landscape and 1080x1920 in portrait, so text keeps
// its size on every screen shape. The views are built inside the device's safe area, above the
// game's help bar, and rebuilt from scratch when the phone is turned or unfolded.

using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;

namespace YARG.Story
{
    public sealed partial class StoryScreen : MonoBehaviour
    {
        private enum View
        {
            None,
            Cover,
            Title,
            BandName,
            Tour,
            Gig,
            Picker,
            Scene,
            Results,
            Message,
            Notes,
        }

        /// <summary>A control the pad can reach.</summary>
        private sealed class Focusable
        {
            public RectTransform Rect;
            public Action Press;
            public Image Ring;

            // Set when the control sits in a scroll list, so moving onto it scrolls it into view
            public ScrollRect Scroll;
            public RectTransform Row;
            public float ViewHeight;
        }

        /// <summary>A view, or a box shown over it: its controls and what the buttons do there.</summary>
        private sealed class Layer
        {
            public RectTransform Root;
            public readonly List<Focusable> Focus = new List<Focusable>();
            public int Index = -1;
            public Action Back;
            public Action Yellow, Blue;
            public string YellowLabel, BlueLabel;

            /// <summary>Takes a direction first (dx, dy with y up); true when it used it.</summary>
            public Func<int, int, bool> Move;

            /// <summary>Replaces "press the focused control" for Green (scenes).</summary>
            public Action Confirm;

            /// <summary>Holding Start: only scenes have it.</summary>
            public Action Skip;
        }

        private struct Pressed
        {
            public MenuAction Action;
            public bool Repeat;
        }

        private const float HEADER = 132f;

        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _root, _art, _safe;

        // Size of the safe area and of the whole canvas, in canvas units
        private Vector2 _size = new Vector2(1920f, 1000f);
        private Vector2 _full = new Vector2(1920f, 1080f);
        private int _screenWidth, _screenHeight;
        private Rect _safeArea;
        private float _barPixels;

        private bool _shown;
        private View _view = View.None;
        private Action _build;
        private RectTransform _viewRoot, _artRoot;
        private StoryAnim _anim = new StoryAnim();
        private readonly List<Layer> _layers = new List<Layer>();
        private int _keepFocus = -1;

        private NavigationScheme _scheme;
        private bool _onStack;
        private string _pushedLabels;
        private readonly List<Pressed> _pressed = new List<Pressed>();

        private int _dragBefore = -1;
        private float _lastFailure = -100f;

        private static FieldInfo _stackField;
        private static bool _stackLooked;

        private static StoryMode Mode => StoryMode.Instance;
        private static StoryCampaign Campaign => Mode != null ? Mode.Campaign : null;
        private static StoryEngine Engine => Mode != null ? Mode.Engine : null;

        private float M => _size.x < 1300f ? 32f : 48f;

        /// <summary>Two columns side by side, or one above the other on a tall screen.</summary>
        private bool Wide => _size.x >= _size.y * 0.8f;

        // ------------------------------------------------------------------------------------
        // What StoryMode calls
        // ------------------------------------------------------------------------------------

        /// <summary>Builds the (hidden) story canvas as a child of the story host.</summary>
        public static StoryScreen Create(Transform host)
        {
            var go = new GameObject("Story Screen");
            go.transform.SetParent(host, false);
            var screen = go.AddComponent<StoryScreen>();
            screen.Build();
            return screen;
        }

        public bool Visible => _shown;

        /// <summary>Shows the view the player was last on (the title the first time) and takes input.</summary>
        public void Show()
        {
            Safely(() =>
            {
                Reveal();
                bool needsStory = _view != View.Title && _view != View.None && _view != View.Cover;
                if (_build == null || _view == View.Cover || _view == View.Scene || _view == View.Results ||
                    _view == View.Message || (needsStory && Engine == null))
                {
                    ShowTitle();
                }
                else
                {
                    Rebuild(false);
                }
            });
        }

        /// <summary>Hides everything and gives the input back. The last view is remembered for Show.</summary>
        public void Hide()
        {
            try
            {
                _shown = false;
                if (_canvas != null) _canvas.enabled = false;
                Clear();
                _pressed.Clear();
                PopScheme();
                if (_dragBefore >= 0 && EventSystem.current != null)
                {
                    EventSystem.current.pixelDragThreshold = _dragBefore;
                }

                _dragBefore = -1;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode could not hide its screen");
            }
        }

        /// <summary>A plain full-screen card at once, with no input: hides the main menu while a gig is wrapped up.</summary>
        public void Cover()
        {
            Safely(() =>
            {
                Reveal();
                if (_canvas != null) _canvas.enabled = true;
                Open(View.Cover, BuildCover);
            });
        }

        /// <summary>The results of a gig (or a short message when it did not finish), then the scenes it earned.</summary>
        public void ShowGigReturn(StoryGigReturn result)
        {
            Safely(() =>
            {
                Reveal();
                if (result == null || Engine == null)
                {
                    ShowTitle();
                    return;
                }

                _return = result;
                if (result.Gig != null) _gig = result.Gig;

                if (result.Kind == StoryGigReturnKind.Finished && result.Outcome != null)
                {
                    // The set just played went into the save: suggest a new one next time
                    _setFor = null;
                    Open(View.Results, BuildResults);
                }
                else
                {
                    Open(View.Message, BuildMessage);
                }
            });
        }

        // ------------------------------------------------------------------------------------
        // Canvas
        // ------------------------------------------------------------------------------------

        private void Build()
        {
            var canvasObject = new GameObject("Story Canvas");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the menus (0) and the PHONES bar (90); below the help bar (100), dialogs and toasts
            _canvas.sortingOrder = 92;
            _scaler = canvasObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasObject.AddComponent<GraphicRaycaster>();
            _root = (RectTransform) canvasObject.transform;

            // Full-bleed and opaque: it also swallows every touch meant for the menus underneath
            var blocker = StoryUi.Area(_root, 0f, 0f, 1f, 1f, "Backdrop").gameObject.AddComponent<Image>();
            blocker.color = StoryTheme.Ink;

            _art = StoryUi.Area(_root, 0f, 0f, 1f, 1f, "Art");
            _safe = StoryUi.Area(_root, 0f, 0f, 1f, 1f, "Safe");
            _canvas.enabled = false;
            Measure(true);
        }

        private void Reveal()
        {
            _shown = true;
            if (_dragBefore < 0 && EventSystem.current != null)
            {
                // Taps on list rows are cancelled once the finger moves more than this many pixels;
                // the game's 10 is under a millimetre on a phone
                _dragBefore = EventSystem.current.pixelDragThreshold;
                EventSystem.current.pixelDragThreshold = Mathf.Max(_dragBefore, Mathf.RoundToInt(Screen.dpi * 0.08f));
            }

            Measure(false);
        }

        /// <summary>Works out the safe area in canvas units. True when anything changed.</summary>
        private bool Measure(bool force)
        {
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            var safe = Screen.safeArea;

            // The help bar (order 100) is drawn over the bottom of everything below it. It is
            // reserved even while hidden, so the layout does not jump when it appears.
            float bar = 0f;
            var help = HelpBar.Instance;
            if (help != null)
            {
                var rect = (RectTransform) help.transform;
                bar = rect.rect.height * rect.lossyScale.y;
            }

            if (bar < 1f || bar > height * 0.3f)
            {
                bar = 75f * Mathf.Min(width / 1920f, height / 1080f);
            }

            if (!force && width == _screenWidth && height == _screenHeight && safe == _safeArea &&
                Mathf.Abs(bar - _barPixels) < 0.5f)
            {
                return false;
            }

            _screenWidth = width;
            _screenHeight = height;
            _safeArea = safe;
            _barPixels = bar;

            var reference = width >= height ? new Vector2(1920f, 1080f) : new Vector2(1080f, 1920f);
            _scaler.referenceResolution = reference;
            float scale = Mathf.Max(0.01f, Mathf.Min(width / reference.x, height / reference.y));

            float bottom = Mathf.Max(safe.yMin, bar);
            float top = Mathf.Max(bottom + 1f, safe.yMax);
            _safe.anchorMin = new Vector2(safe.xMin / width, bottom / height);
            _safe.anchorMax = new Vector2(safe.xMax / width, top / height);
            _safe.offsetMin = Vector2.zero;
            _safe.offsetMax = Vector2.zero;
            _size = new Vector2((safe.xMax - safe.xMin) / scale, (top - bottom) / scale);
            _full = new Vector2(width / scale, height / scale);
            return true;
        }

        // ------------------------------------------------------------------------------------
        // Frame loop
        // ------------------------------------------------------------------------------------

        private void Update()
        {
            try
            {
                if (!_shown)
                {
                    // A scheme of ours left under someone else's: take it away once it is on top
                    PopScheme();
                    return;
                }

                UpdateScheme();
                UpdateTypedName();

                for (int i = 0; i < _pressed.Count && _shown; i++)
                {
                    Handle(_pressed[i]);
                }

                _pressed.Clear();
                if (_view == View.Scene)
                {
                    UpdateScene(Time.unscaledDeltaTime);
                }

                _anim.Tick(Time.unscaledTime, Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                _pressed.Clear();
                Fail(e);
            }
        }

        private void LateUpdate()
        {
            try
            {
                if (_canvas == null) return;

                // Only over the menus, never over a song or the score screen
                bool inMenus = SceneManager.GetSceneByName("MenuScene").isLoaded &&
                    !SceneManager.GetSceneByName("Gameplay").isLoaded &&
                    !SceneManager.GetSceneByName("ScoreScene").isLoaded &&
                    !SceneManager.GetSceneByName("CalibrationScene").isLoaded;
                bool show = _shown && inMenus;
                if (_canvas.enabled != show)
                {
                    _canvas.enabled = show;
                }

                if (show && Measure(false) && _build != null)
                {
                    Rebuild(true);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                PopScheme();
            }
            catch (Exception)
            {
                // The game is shutting down
            }
        }

        // ------------------------------------------------------------------------------------
        // Views
        // ------------------------------------------------------------------------------------

        private Layer Top => _layers.Count > 0 ? _layers[_layers.Count - 1] : null;

        /// <summary>Switches to a view: forgets the old one and builds the new one.</summary>
        private void Open(View view, Action build)
        {
            _view = view;
            _build = build;
            _keepFocus = -1;
            Rebuild(false);
        }

        /// <summary>Remembers a view for the next Show without building it (used right before hiding).</summary>
        private void Remember(View view, Action build)
        {
            _view = view;
            _build = build;
        }

        private void Rebuild(bool keepFocus)
        {
            int focus = keepFocus && _layers.Count > 0 ? _layers[0].Index : _keepFocus;
            Clear();
            _viewRoot = StoryUi.Area(_safe, 0f, 0f, 1f, 1f, "View");
            _artRoot = StoryUi.Area(_art, 0f, 0f, 1f, 1f, "Picture");
            var layer = new Layer { Root = _viewRoot };
            _layers.Add(layer);
            _build?.Invoke();

            if (focus >= 0 && focus < layer.Focus.Count)
            {
                layer.Index = focus;
            }

            if (layer.Index < 0 && layer.Focus.Count > 0)
            {
                layer.Index = 0;
            }

            _keepFocus = -1;
            Canvas.ForceUpdateCanvases();
            ShowFocus(Top);
        }

        private void Clear()
        {
            if (_viewRoot != null) Destroy(_viewRoot.gameObject);
            if (_artRoot != null) Destroy(_artRoot.gameObject);
            _viewRoot = null;
            _artRoot = null;
            _layers.Clear();
            _anim = new StoryAnim();
        }

        /// <summary>The picture behind the view, over the whole screen (safe area or not).</summary>
        private void Picture(string key)
        {
            StoryArt.Backdrop(_artRoot, Campaign, key, _full, _anim);
        }

        /// <summary>
        /// The bar at the top of most views: a Back button, a big title and a line under it.
        /// Returns the area below it.
        /// </summary>
        private RectTransform Header(string title, string line, Action back)
        {
            var header = StoryUi.Row(_viewRoot, 8f, HEADER, M, M, "Header");
            float left = 0f;
            if (back != null)
            {
                var box = StoryUi.Area(header, 0f, 0f, 0f, 1f, "Back");
                box.offsetMin = new Vector2(0f, 16f);
                box.offsetMax = new Vector2(200f, -16f);
                AddButton(box, StoryButtonStyle.Normal, "Back", 36, back);
                left = 228f;
                Top.Back = back;
            }

            var titleArea = StoryUi.Area(header, 0f, 0.42f, 1f, 1f, "Title");
            titleArea.offsetMin = new Vector2(left, 0f);
            StoryUi.Text(titleArea, title, 60, true, StoryTheme.Gold, TextAlignmentOptions.BottomLeft, false);
            if (!string.IsNullOrEmpty(line))
            {
                var lineArea = StoryUi.Area(header, 0f, 0.05f, 1f, 0.42f, "Line");
                lineArea.offsetMin = new Vector2(left, 0f);
                StoryUi.Text(lineArea, line, 32, false, StoryTheme.Muted, TextAlignmentOptions.TopLeft, false);
            }

            var body = StoryUi.Area(_viewRoot, 0f, 0f, 1f, 1f, "Body");
            body.offsetMax = new Vector2(0f, -(HEADER + 16f));
            return body;
        }

        /// <summary>The size of the area under the header, in units.</summary>
        private Vector2 BodySize => new Vector2(_size.x, _size.y - HEADER - 16f);

        /// <summary>A button the pad can reach too. For cards, fill the rect with texts afterwards.</summary>
        private Focusable AddButton(RectTransform rect, StoryButtonStyle style, string text, float size, Action press)
        {
            var layer = Top;
            var button = StoryUi.Button(rect, style, text, size, out _, out _);
            var focus = new Focusable { Rect = rect, Press = press, Ring = StoryUi.Ring(rect, StoryTheme.Gold) };
            _anim.AddPulse(focus.Ring, 0.55f, 1f, 1.1f, 0f);
            button.onClick.AddListener(() => Tap(layer, focus));
            layer.Focus.Add(focus);
            return focus;
        }

        private void InList(Focusable focus, ScrollRect scroll, RectTransform row, float viewHeight)
        {
            focus.Scroll = scroll;
            focus.Row = row;
            focus.ViewHeight = viewHeight;
        }

        private void Prefer(Focusable focus)
        {
            var layer = Top;
            int index = layer.Focus.IndexOf(focus);
            if (index >= 0) layer.Index = index;
        }

        private void Tap(Layer layer, Focusable focus)
        {
            StoryUi.KeepAwake();
            if (!_shown || layer != Top) return;

            Safely(() =>
            {
                int index = layer.Focus.IndexOf(focus);
                if (index >= 0)
                {
                    layer.Index = index;
                    ShowFocus(layer);
                }

                focus.Press?.Invoke();
            });
        }

        private void ShowFocus(Layer layer)
        {
            if (layer == null) return;

            for (int i = 0; i < layer.Focus.Count; i++)
            {
                var ring = layer.Focus[i].Ring;
                if (ring != null) ring.gameObject.SetActive(i == layer.Index);
            }

            if (layer.Index >= 0 && layer.Index < layer.Focus.Count)
            {
                var focus = layer.Focus[layer.Index];
                if (focus.Scroll != null)
                {
                    StoryUi.Reveal(focus.Scroll, focus.Row, focus.ViewHeight);
                }
            }
        }

        /// <summary>Moves the focus to the nearest control in a direction, as the eye would.</summary>
        private void MoveFocus(Layer layer, int dx, int dy)
        {
            if (layer.Focus.Count == 0) return;

            if (layer.Index < 0 || layer.Index >= layer.Focus.Count)
            {
                layer.Index = 0;
                ShowFocus(layer);
                return;
            }

            var from = Center(layer.Focus[layer.Index].Rect);
            var direction = new Vector2(dx, dy);
            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < layer.Focus.Count; i++)
            {
                var rect = layer.Focus[i].Rect;
                if (i == layer.Index || rect == null || !rect.gameObject.activeInHierarchy) continue;

                var offset = Center(rect) - from;
                float along = Vector2.Dot(offset, direction);
                if (along <= 1f) continue;

                float across = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
                float cost = along + across * 2.5f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }

            if (best >= 0)
            {
                layer.Index = best;
                ShowFocus(layer);
            }
        }

        private static Vector2 Center(RectTransform rect)
        {
            return rect.TransformPoint(rect.rect.center);
        }

        // ------------------------------------------------------------------------------------
        // Boxes over a view: notices and questions
        // ------------------------------------------------------------------------------------

        private void Notice(string title, string body, Action then = null)
        {
            Ask(title, body, "OK", then, null, null);
        }

        private void Ask(string title, string body, string yes, Action onYes, string no, Action onNo)
        {
            var root = StoryUi.Area(_viewRoot, 0f, 0f, 1f, 1f, "Box");
            var scrim = root.gameObject.AddComponent<Image>();
            scrim.color = StoryTheme.Scrim;

            var layer = new Layer { Root = root };
            _layers.Add(layer);

            var panel = Wide ? StoryUi.Area(root, 0.2f, 0.2f, 0.8f, 0.8f, "Panel") : StoryUi.Area(root, 0.04f, 0.28f, 0.96f, 0.72f, "Panel");
            StoryUi.Rounded(panel, StoryTheme.PanelLight, 1.2f);
            StoryArt.Frame(panel, 0f, 0f, 1f, 1f, StoryUi.Alpha(StoryTheme.Gold, 0.5f));
            var title_ = StoryUi.Area(panel, 0f, 0.78f, 1f, 0.95f, "Title");
            StoryUi.Inset(title_, 40f, 0f, 40f, 0f);
            StoryUi.Text(title_, title, 52, true, StoryTheme.Gold, TextAlignmentOptions.Center);
            var text = StoryUi.Area(panel, 0f, 0.3f, 1f, 0.78f, "Message");
            StoryUi.Inset(text, 48f, 0f, 48f, 0f);
            StoryUi.Text(text, body, 36, false, StoryTheme.Cream, TextAlignmentOptions.Center);

            Action close = () =>
            {
                Destroy(root.gameObject);
                _layers.Remove(layer);
                ShowFocus(Top);
            };

            var yesBox = no != null ? StoryUi.Area(panel, 0.52f, 0.06f, 0.94f, 0.24f, "Yes") : StoryUi.Area(panel, 0.3f, 0.06f, 0.7f, 0.24f, "Yes");
            var yesFocus = AddButton(yesBox, StoryButtonStyle.Primary, yes, 38, () =>
            {
                close();
                onYes?.Invoke();
            });

            layer.Back = () =>
            {
                close();
                (no != null ? onNo : onYes)?.Invoke();
            };

            if (no != null)
            {
                var noBox = StoryUi.Area(panel, 0.06f, 0.06f, 0.48f, 0.24f, "No");
                var noFocus = AddButton(noBox, StoryButtonStyle.Normal, no, 38, layer.Back);
                Prefer(noFocus);
            }
            else
            {
                Prefer(yesFocus);
            }

            ShowFocus(layer);
        }

        // ------------------------------------------------------------------------------------
        // Input
        // ------------------------------------------------------------------------------------

        private void Queue(MenuAction action, bool repeat)
        {
            if (_shown)
            {
                _pressed.Add(new Pressed { Action = action, Repeat = repeat });
            }
        }

        private void Handle(Pressed pressed)
        {
            var layer = Top;
            if (layer == null || _view == View.Cover) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogShowing) return;

            switch (pressed.Action)
            {
                case MenuAction.Up: Direction(layer, 0, 1); break;
                case MenuAction.Down: Direction(layer, 0, -1); break;
                case MenuAction.Left: Direction(layer, -1, 0); break;
                case MenuAction.Right: Direction(layer, 1, 0); break;
                case MenuAction.Green:
                    if (pressed.Repeat) break;
                    if (layer.Confirm != null)
                    {
                        layer.Confirm();
                    }
                    else if (layer.Index >= 0 && layer.Index < layer.Focus.Count)
                    {
                        layer.Focus[layer.Index].Press?.Invoke();
                    }

                    break;
                case MenuAction.Red: layer.Back?.Invoke(); break;
                case MenuAction.Yellow: layer.Yellow?.Invoke(); break;
                case MenuAction.Blue: layer.Blue?.Invoke(); break;
                case MenuAction.Start: layer.Skip?.Invoke(); break;
            }
        }

        private void Direction(Layer layer, int dx, int dy)
        {
            if (layer.Move != null && layer.Move(dx, dy)) return;
            MoveFocus(layer, dx, dy);
        }

        private string Labels()
        {
            var layer = Top;
            if (layer == null) return "";
            return (layer.Yellow != null ? layer.YellowLabel : "") + "|" + (layer.Blue != null ? layer.BlueLabel : "") + "|" +
                (layer.Skip != null ? "skip" : "") + "|" + (layer.Back != null ? "back" : "");
        }

        /// <summary>Keeps exactly one scheme of ours on the Navigator, with the labels of the current view.</summary>
        private void UpdateScheme()
        {
            var navigator = Navigator.Instance;
            if (navigator == null) return;

            if (_view == View.Cover || _view == View.None)
            {
                PopScheme();
                return;
            }

            // Nothing may be pushed or popped while a game dialog is open: it pops the top one when it closes
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogShowing) return;

            string labels = Labels();
            if (_onStack && labels == _pushedLabels) return;
            if (_onStack)
            {
                if (!TopIsOurs()) return;
                navigator.PopScheme();
            }

            var layer = Top;
            var entries = new List<NavigationScheme.Entry>
            {
                new NavigationScheme.Entry(MenuAction.Green, "Menu.Common.Confirm", ctx => Queue(MenuAction.Green, ctx.IsRepeat)),
            };

            if (layer != null && layer.Back != null)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Red, "Menu.Common.Back", ctx => Queue(MenuAction.Red, false)));
            }

            if (layer != null && layer.Yellow != null)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Yellow, StoryUi.Key(layer.YellowLabel ?? "More"), ctx => Queue(MenuAction.Yellow, false)));
            }

            if (layer != null && layer.Blue != null)
            {
                entries.Add(new NavigationScheme.Entry(MenuAction.Blue, StoryUi.Key(layer.BlueLabel ?? "More"), ctx => Queue(MenuAction.Blue, false)));
            }

            if (layer != null && layer.Skip != null)
            {
                // A short press does nothing; holding Start for a second skips the scene
                entries.Add(new NavigationScheme.Entry(MenuAction.Start, StoryUi.Key("Hold to skip"), ctx => { },
                    onHoldHandler: ctx => Queue(MenuAction.Start, false), holdSeconds: 1f));
            }

            entries.Add(new NavigationScheme.Entry(MenuAction.Up, "Menu.Common.Up", ctx => Queue(MenuAction.Up, ctx.IsRepeat), hide: true));
            entries.Add(new NavigationScheme.Entry(MenuAction.Down, "Menu.Common.Down", ctx => Queue(MenuAction.Down, ctx.IsRepeat), hide: true));
            entries.Add(new NavigationScheme.Entry(MenuAction.Left, "Menu.Common.Left", ctx => Queue(MenuAction.Left, ctx.IsRepeat), hide: true));
            entries.Add(new NavigationScheme.Entry(MenuAction.Right, "Menu.Common.Right", ctx => Queue(MenuAction.Right, ctx.IsRepeat), hide: true));

            NavigationScheme scheme = null;
            scheme = new NavigationScheme(entries, null, () =>
            {
                if (ReferenceEquals(_scheme, scheme)) _onStack = false;
            });
            _scheme = scheme;
            navigator.PushScheme(scheme);
            _onStack = true;
            _pushedLabels = labels;
        }

        /// <summary>Pops our scheme, but only if it is the one on top (Navigator pops blindly).</summary>
        private void PopScheme()
        {
            if (!_onStack || Navigator.Instance == null) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogShowing) return;
            if (!TopIsOurs()) return;

            Navigator.Instance.PopScheme();
            _onStack = false;
        }

        private bool TopIsOurs()
        {
            try
            {
                if (!_stackLooked)
                {
                    _stackLooked = true;
                    _stackField = typeof(Navigator).GetField("_schemeStack", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (_stackField != null && _stackField.GetValue(Navigator.Instance) is Stack<NavigationScheme> stack)
                {
                    return stack.Count > 0 && ReferenceEquals(stack.Peek(), _scheme);
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode could not look at the navigation stack");
                _stackField = null;
            }

            // Without a way to look, trust our own bookkeeping
            return _onStack;
        }

        // ------------------------------------------------------------------------------------
        // Safety
        // ------------------------------------------------------------------------------------

        private void Safely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>
        /// Something went wrong in a view: log it and fall back to the tour (or the title). If
        /// that fails too, or it keeps happening, story mode steps aside and the menus come back.
        /// </summary>
        private void Fail(Exception e)
        {
            YargLogger.LogException(e, "Story mode screen failed");
            bool again = Time.unscaledTime - _lastFailure < 2f;
            _lastFailure = Time.unscaledTime;
            try
            {
                if (!again && _shown && Mode != null)
                {
                    if (Engine != null) ShowTour(null, null);
                    else ShowTitle();
                    return;
                }
            }
            catch (Exception second)
            {
                YargLogger.LogException(second, "Story mode could not recover its screen");
            }

            _view = View.None;
            _build = null;
            Hide();
            try
            {
                Mode?.Close();
            }
            catch (Exception third)
            {
                YargLogger.LogException(third, "Story mode could not close");
            }
        }

        // ------------------------------------------------------------------------------------
        // Shared bits
        // ------------------------------------------------------------------------------------

        private static string Fill(string text)
        {
            var engine = Engine;
            return engine != null ? engine.Fill(text) : (text ?? "").Replace("{band}", "the band");
        }

        private static void Save()
        {
            try
            {
                Mode?.Save();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode could not save");
            }
        }

        private void BuildCover()
        {
            var campaign = Campaign;
            StoryUi.Text(_viewRoot, 0.1f, 0.42f, 0.9f, 0.58f, campaign != null ? campaign.Title : "", 56, true,
                StoryUi.Alpha(StoryTheme.Gold, 0.8f), TextAlignmentOptions.Center, false);
            var wave = StoryUi.Area(_viewRoot, 0.3f, 0.3f, 0.7f, 0.4f, "Wave");
            StoryArt.Waveform(wave, new Vector2(_size.x * 0.4f, _size.y * 0.1f), null, _anim, 0.2f);
        }
    }
}
