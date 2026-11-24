using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Input;
using YARG.Gameplay.Player;
using YARG.Gameplay.Visuals;
using YARG.Input;
using YARG.Player;
using YARG.Settings;

namespace YARG.Gameplay.HUD
{
    /// <summary>
    /// Shows, just under each fret of a guitar highway, the controller button that plays it: L2, L1,
    /// R1, R2 and Cross with the Guitar Hero 2 gamepad layout, or whatever the profile has bound.
    /// The badges are the same drawn icons as the menu prompts (see ControllerGlyphs), in the frets'
    /// colors.
    ///
    /// They sit on their own screen overlay and follow the highway: the highways are drawn by one
    /// shared camera with its own per-highway matrices, so the positions are asked from
    /// HighwayCameraRendering each frame rather than placed in the 3D scene.
    /// Only shown for a player with a gamepad, and only with Settings > General > Show Controller
    /// Buttons on.
    /// </summary>
    public class FretButtonHints : MonoBehaviour
    {
        // In lane widths: how big a badge is, and how far under the middle of the frets it sits
        private const float BADGE_SIZE = 0.40f;
        private const float BADGE_DROP = 0.50f;

        private static readonly Color[] FretColors =
        {
            new(0.20f, 0.85f, 0.35f, 1f), // Green
            new(0.95f, 0.25f, 0.25f, 1f), // Red
            new(0.98f, 0.85f, 0.20f, 1f), // Yellow
            new(0.25f, 0.55f, 0.98f, 1f), // Blue
            new(0.98f, 0.55f, 0.15f, 1f), // Orange
        };

        private TrackPlayer _trackPlayer;
        private YargPlayer _player;
        private int[] _lanes;
        private int _laneCount;

        private HighwayCameraRendering _highways;
        private Canvas _canvas;
        private readonly RectTransform[] _badges = new RectTransform[5];
        private readonly Image[] _images = new Image[5];

        /// <param name="lanes">For green, red, yellow, blue, orange: the lane (0 = leftmost) the fret is in.</param>
        public static void Create(TrackPlayer trackPlayer, int[] lanes, int laneCount)
        {
            if (trackPlayer == null || trackPlayer.Player == null || lanes == null || lanes.Length < 5)
            {
                return;
            }

            var player = trackPlayer.Player;
            if (player.IsReplay || player.Profile.IsBot || player.Bindings == null)
            {
                return;
            }

            var host = new GameObject("FretButtonHints");
            host.transform.SetParent(trackPlayer.transform, false);

            var hints = host.AddComponent<FretButtonHints>();
            hints._trackPlayer = trackPlayer;
            hints._player = player;
            hints._lanes = lanes;
            hints._laneCount = laneCount;
            hints.Build();
        }

        private void Build()
        {
            var canvasObject = new GameObject("FretButtonHintsCanvas");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the highway picture, under the touch buttons (100)
            _canvas.sortingOrder = 1;

            for (int i = 0; i < _badges.Length; i++)
            {
                var badge = new GameObject("Fret" + i);
                badge.transform.SetParent(canvasObject.transform, false);

                var image = badge.AddComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.color = FretColors[i];
                image.enabled = false;

                _images[i] = image;
                _badges[i] = image.rectTransform;
            }

            RefreshIcons();
        }

        // The button bound to each fret can change (layout setting, binding menu), so look again now and then
        private float _nextRefresh;

        private void RefreshIcons()
        {
            _nextRefresh = Time.unscaledTime + 2f;

            bool show = ControllerLayout.ShowButtonNames;
            var bindings = _player.Bindings != null ? _player.Bindings[GameMode.FiveFretGuitar] : null;

            for (int i = 0; i < _images.Length; i++)
            {
                Sprite sprite = null;
                if (show && bindings != null &&
                    bindings.TryGetBindingByAction((GuitarAction) ((int) GuitarAction.GreenFret + i)) is ButtonBinding fret)
                {
                    foreach (var bound in fret.Bindings)
                    {
                        if (bound.Control?.device is Gamepad pad &&
                            !YARG.Remote.RemotePlayManager.IsRemoteDevice(pad))
                        {
                            sprite = ControllerGlyphs.GetSpriteForControl(bound.Control);
                            if (sprite != null) break;
                        }
                    }
                }

                _images[i].sprite = sprite;
                _images[i].enabled = sprite != null;
            }
        }

        private void LateUpdate()
        {
            if (_trackPlayer == null || _canvas == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                RefreshIcons();
            }

            if (_highways == null)
            {
                _highways = FindAnyObjectByType<HighwayCameraRendering>();
                if (_highways == null)
                {
                    _canvas.enabled = false;
                    return;
                }
            }

            int highway = _trackPlayer.HighwayIndex;
            if (highway < 0 || highway >= _highways.HighwayCount())
            {
                _canvas.enabled = false;
                return;
            }

            // Lane width on screen, from the middles of the first two lanes at the strikeline
            var first = _highways.GetTrackPositionScreenSpace(highway, 0.5f / _laneCount, 0f);
            var second = _highways.GetTrackPositionScreenSpace(highway, 1.5f / _laneCount, 0f);
            if (first == null || second == null)
            {
                _canvas.enabled = false;
                return;
            }

            float laneWidth = Mathf.Abs(second.Value.x - first.Value.x);
            if (laneWidth < 1f)
            {
                _canvas.enabled = false;
                return;
            }

            _canvas.enabled = true;

            float size = laneWidth * BADGE_SIZE;
            for (int i = 0; i < _badges.Length; i++)
            {
                if (!_images[i].enabled) continue;

                var at = _highways.GetTrackPositionScreenSpace(highway, (_lanes[i] + 0.5f) / _laneCount, 0f);
                if (at == null)
                {
                    continue;
                }

                // Screen position with y measured up from the bottom, as the HUD uses it
                _badges[i].position = new Vector3(at.Value.x, at.Value.y - laneWidth * BADGE_DROP, 0f);
                _badges[i].sizeDelta = new Vector2(size, size);
            }
        }
    }
}
