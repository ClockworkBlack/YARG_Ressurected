// YARG Resurrected - the on-screen scoreboard of a sing-off.
using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Audio;
using YARG.Core.Logging;

namespace YARG.Gameplay.VocalBattle
{
    /// <summary>
    /// Shows a sing-off as it happens, just under the vocal highway: each side's name and its
    /// average mark per line (out of 100), a tug-of-war bar that leans towards whoever is ahead,
    /// whose turn it is ("SING!", or a short countdown to the next singer), the rounds won when
    /// the lines are dealt in turns, a call after each round, and the winner at the end.
    ///
    /// Everything is drawn on its own screen overlay built here in code, so no scene or prefab of
    /// the game needs to change.
    /// </summary>
    public class SingOffHud : MonoBehaviour
    {
        private const float CALLOUT_SECONDS = 2.2f;
        // How far apart the averages have to be for the bar to lean all the way
        private const double FULL_LEAN = 40.0;

        private GameManager _gameManager;
        private RectTransform _panel;
        private readonly Image[] _nameBacks = new Image[SingOff.SIDES];
        private readonly TextMeshProUGUI[] _names = new TextMeshProUGUI[SingOff.SIDES];
        private readonly TextMeshProUGUI[] _scores = new TextMeshProUGUI[SingOff.SIDES];
        private readonly int[] _shownScores = { -1, -1 };
        private RectTransform _tugFill;
        private TextMeshProUGUI _header;
        private TextMeshProUGUI _cue;
        private TextMeshProUGUI _callout;
        private Image _calloutBack;

        private float _lean = 0.5f;
        private string _shownCue = string.Empty;
        private string _shownHeader = string.Empty;
        private float _calloutUntil;
        private bool _winnerShown;
        private float _nextLayout;

        public static void Create(GameManager gameManager)
        {
            if (!SingOff.Active || gameManager == null)
            {
                return;
            }

            try
            {
                var host = new GameObject("SingOffHud");
                host.transform.SetParent(gameManager.transform, false);
                var hud = host.AddComponent<SingOffHud>();
                hud._gameManager = gameManager;
                hud.Build();
                SingOff.RoundDecided += hud.OnRound;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't build the sing-off display");
            }
        }

        private void OnDestroy()
        {
            SingOff.RoundDecided -= OnRound;
        }

        private void Build()
        {
            var canvasObject = new GameObject("SingOffCanvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the highways and their button badges, under the touch buttons and the pause menu
            canvas.sortingOrder = 2;

            _panel = Area(canvasObject.transform, 0.22f, 0.62f, 0.78f, 0.70f, "Panel");

            for (int i = 0; i < SingOff.SIDES; i++)
            {
                var side = SingOff.Sides[i];
                bool left = i == 0;
                var box = Area(_panel, left ? 0f : 0.70f, 0.30f, left ? 0.30f : 1f, 1f, "Side" + i);
                _nameBacks[i] = box.gameObject.AddComponent<Image>();
                _nameBacks[i].color = side.Color;
                _nameBacks[i].raycastTarget = false;

                // Name towards the outside, score towards the middle
                _names[i] = Label(box, left ? 0.05f : 0.30f, 0.08f, left ? 0.70f : 0.95f, 0.92f,
                    left ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
                _names[i].text = side.Name.ToUpperInvariant();
                _names[i].color = Color.black;
                _scores[i] = Label(box, left ? 0.72f : 0.03f, 0.05f, left ? 0.97f : 0.28f, 0.95f,
                    left ? TextAlignmentOptions.Right : TextAlignmentOptions.Left);
                _scores[i].text = "0";
                _scores[i].color = Color.black;
            }

            // The tug-of-war bar: the second side's color all the way across, the first side's laid over from the left
            var bar = Area(_panel, 0.32f, 0.42f, 0.68f, 0.72f, "Bar");
            var barBack = bar.gameObject.AddComponent<Image>();
            barBack.color = SingOff.Sides[1].Color;
            barBack.raycastTarget = false;
            _tugFill = Area(bar, 0f, 0f, 0.5f, 1f, "Fill");
            var fill = _tugFill.gameObject.AddComponent<Image>();
            fill.color = SingOff.Sides[0].Color;
            fill.raycastTarget = false;
            var middle = Area(bar, 0.495f, -0.25f, 0.505f, 1.25f, "Middle");
            var middleImage = middle.gameObject.AddComponent<Image>();
            middleImage.color = Color.white;
            middleImage.raycastTarget = false;

            _header = Label(_panel, 0.32f, 0.74f, 0.68f, 1.0f, TextAlignmentOptions.Center);
            _header.color = Color.white;
            _cue = Label(_panel, 0.05f, -0.02f, 0.95f, 0.28f, TextAlignmentOptions.Center);
            _cue.color = Color.white;

            var calloutArea = Area(canvasObject.transform, 0.2f, 0.42f, 0.8f, 0.54f, "Callout");
            _calloutBack = calloutArea.gameObject.AddComponent<Image>();
            _calloutBack.color = new Color(0f, 0f, 0f, 0.75f);
            _calloutBack.raycastTarget = false;
            _callout = Label(calloutArea, 0.03f, 0.1f, 0.97f, 0.9f, TextAlignmentOptions.Center);
            _callout.color = Color.white;
            calloutArea.gameObject.SetActive(false);

            Layout();
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

        private static TextMeshProUGUI Label(Transform parent, float x0, float y0, float x1, float y1,
            TextAlignmentOptions alignment)
        {
            var rect = Area(parent, x0, y0, x1, y1, "Text");
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            // Sized to its box, so it looks the same on any screen
            text.enableAutoSizing = true;
            text.fontSizeMin = 8;
            text.fontSizeMax = 120;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Puts the scoreboard right under the vocal highway, wherever that is on this screen.</summary>
        private void Layout()
        {
            _nextLayout = Time.unscaledTime + 1f;
            float top = 0.70f;
            try
            {
                var vocalTrack = _gameManager != null ? _gameManager.VocalTrack : null;
                if (vocalTrack != null && vocalTrack.isActiveAndEnabled)
                {
                    float under = vocalTrack.GetVocalLayoutRect().y - 0.012f;
                    if (under > 0.3f && under < 0.95f)
                    {
                        top = under;
                    }
                }
            }
            catch (Exception)
            {
                // Keep the default place
            }

            _panel.anchorMin = new Vector2(0.22f, top - 0.085f);
            _panel.anchorMax = new Vector2(0.78f, top);
        }

        private void Update()
        {
            if (_gameManager == null || !SingOff.Active || _panel == null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextLayout)
            {
                Layout();
            }

            double time = _gameManager.SongTime;
            SingOff.Update(time);

            var first = SingOff.Sides[0];
            var second = SingOff.Sides[1];

            // Scores
            for (int i = 0; i < SingOff.SIDES; i++)
            {
                int score = (int) Math.Round(SingOff.Sides[i].Average);
                if (score != _shownScores[i])
                {
                    _shownScores[i] = score;
                    _scores[i].text = score.ToString(CultureInfo.InvariantCulture);
                }
            }

            // The bar leans towards whoever is ahead
            float target = 0.5f;
            if (first.Lines > 0 && second.Lines > 0)
            {
                double difference = (first.Average - second.Average) / FULL_LEAN;
                target = 0.5f + 0.42f * Mathf.Clamp((float) difference, -1f, 1f);
            }
            _lean = Mathf.Lerp(_lean, target, Mathf.Clamp01(Time.unscaledDeltaTime * 4f));
            _tugFill.anchorMax = new Vector2(_lean, 1f);

            // Whose turn
            bool firstSings = SingOff.IsSinging(0, time, out double firstWait);
            bool secondSings = SingOff.IsSinging(1, time, out double secondWait);
            string cue;
            if (firstSings && secondSings)
            {
                cue = "EVERYBODY SING!";
            }
            else if (firstSings)
            {
                cue = first.Name.ToUpperInvariant() + ": SING!";
            }
            else if (secondSings)
            {
                cue = second.Name.ToUpperInvariant() + ": SING!";
            }
            else
            {
                bool firstNext = firstWait <= secondWait;
                double wait = firstNext ? firstWait : secondWait;
                cue = wait < 5.5
                    ? "NEXT: " + (firstNext ? first : second).Name.ToUpperInvariant() + " IN " +
                        Math.Max(1, (int) Math.Ceiling(wait)).ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }
            if (cue != _shownCue)
            {
                _shownCue = cue;
                _cue.text = cue;
            }
            _cue.color = firstSings == secondSings ? Color.white : firstSings ? first.Color : second.Color;

            // The side that isn't singing steps back
            SetBright(0, firstSings || !secondSings);
            SetBright(1, secondSings || !firstSings);

            string header = SingOff.SplitLines
                ? "ROUNDS  " + first.RoundsWon.ToString(CultureInfo.InvariantCulture) + " - " +
                    second.RoundsWon.ToString(CultureInfo.InvariantCulture)
                : "SING-OFF";
            if (header != _shownHeader)
            {
                _shownHeader = header;
                _header.text = header;
            }

            // The winner, once the last line has been marked
            if (!_winnerShown && SingOff.LastLineEnd > 0 && time > SingOff.LastLineEnd + 1.2)
            {
                _winnerShown = true;
                int leader = SingOff.Leader();
                ShowCallout(leader < 0
                    ? "IT'S A TIE!"
                    : SingOff.Sides[leader].Name.ToUpperInvariant() + " WINS THE SING-OFF!",
                    leader < 0 ? Color.white : SingOff.Sides[leader].Color, float.MaxValue);
                Play(SfxSample.StarGold);
            }
            else if (!_winnerShown && _callout.transform.parent.gameObject.activeSelf && Time.unscaledTime > _calloutUntil)
            {
                _callout.transform.parent.gameObject.SetActive(false);
            }
        }

        private void SetBright(int index, bool bright)
        {
            var color = SingOff.Sides[index].Color;
            color.a = bright ? 1f : 0.4f;
            if (_nameBacks[index].color != color)
            {
                _nameBacks[index].color = color;
            }
        }

        private void OnRound(int round, int winner)
        {
            if (_winnerShown)
            {
                return;
            }

            string number = round.ToString(CultureInfo.InvariantCulture);
            if (winner < 0)
            {
                ShowCallout("ROUND " + number + ": EVEN", Color.white, CALLOUT_SECONDS);
            }
            else
            {
                ShowCallout("ROUND " + number + " TO " + SingOff.Sides[winner].Name.ToUpperInvariant() + "!",
                    SingOff.Sides[winner].Color, CALLOUT_SECONDS);
                Play(SfxSample.StarGain);
            }
        }

        private void ShowCallout(string text, Color color, float seconds)
        {
            if (_callout == null)
            {
                return;
            }

            _callout.text = text;
            _callout.color = color;
            _callout.transform.parent.gameObject.SetActive(true);
            _calloutUntil = seconds >= float.MaxValue ? float.MaxValue : Time.unscaledTime + seconds;
        }

        private static void Play(SfxSample sample)
        {
            try
            {
                GlobalAudioHandler.PlaySoundEffect(sample);
            }
            catch (Exception)
            {
                // A missing sound must never get in the way of the song
            }
        }
    }
}
