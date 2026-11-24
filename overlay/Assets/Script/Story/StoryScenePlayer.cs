// YARG Resurrected - Story Mode screens: the scene player.
//
// Plays the lines of one or more scenes in a row: the room behind (it changes when a line names a
// new backdrop), who is speaking (a portrait, the band's silhouette, or for the narrator - a drum
// break telling its own story - a waveform instead of a face), and the line itself, typed out on
// unscaled time. A tap or Green finishes the line, then moves on; the Skip button or holding
// Start ends the scene. Title lines are full-screen cards.

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace YARG.Story
{
    public sealed partial class StoryScreen
    {
        private const float LETTERS_PER_SECOND = 48f;

        /// <summary>One scene to play: its lines, the name it is remembered by once seen, and the room it starts in.</summary>
        private sealed class Step
        {
            public readonly List<StoryLine> Lines;
            public readonly string Key;
            public readonly string Backdrop;

            public Step(List<StoryLine> lines, string key, string backdrop)
            {
                Lines = lines ?? new List<StoryLine>();
                Key = key;
                Backdrop = backdrop;
            }
        }

        private List<Step> _steps = new List<Step>();
        private int _step;
        private int _line;
        private Action _afterScenes;
        private readonly HashSet<string> _met = new HashSet<string>();
        private TextMeshProUGUI _lineText;
        private int _letters;
        private float _typed;
        private bool _typing;

        /// <summary>Plays the scenes that have lines, one after another, then runs <paramref name="after"/>.</summary>
        private void PlayScenes(List<Step> steps, Action after)
        {
            _steps = new List<Step>();
            foreach (var step in steps)
            {
                if (step != null && step.Lines.Count > 0) _steps.Add(step);
            }

            _afterScenes = after;
            if (_steps.Count == 0)
            {
                FinishScenes();
                return;
            }

            StartStep(0);
        }

        private void StartStep(int index)
        {
            _step = index;
            _line = 0;
            _met.Clear();

            // Remembered as seen as soon as it starts, so skipping it or closing the app midway
            // does not play it again and again
            var step = _steps[index];
            var engine = Engine;
            if (!string.IsNullOrEmpty(step.Key) && engine != null && !engine.Progress.HasSeen(step.Key))
            {
                engine.Progress.MarkSeen(step.Key);
                Save();
            }

            ShowLine(false);
        }

        private void ShowLine(bool finished)
        {
            _typing = !finished;
            Open(View.Scene, BuildScene);
        }

        private void FinishScenes()
        {
            var after = _afterScenes;
            _afterScenes = null;
            _steps = new List<Step>();
            if (after != null) after();
            else ShowTour(null, null);
        }

        private void Advance()
        {
            StoryUi.KeepAwake();
            if (_typing)
            {
                _typing = false;
                if (_lineText != null) _lineText.maxVisibleCharacters = 99999;
                _anim.Energy = 0.3f;
                return;
            }

            _line++;
            if (_line < _steps[_step].Lines.Count)
            {
                ShowLine(false);
            }
            else if (_step + 1 < _steps.Count)
            {
                StartStep(_step + 1);
            }
            else
            {
                FinishScenes();
            }
        }

        private void SkipScene()
        {
            StoryUi.KeepAwake();
            if (_step + 1 < _steps.Count) StartStep(_step + 1);
            else FinishScenes();
        }

        private void UpdateScene(float delta)
        {
            if (!_typing || _lineText == null) return;

            _typed += delta;
            int shown = Mathf.FloorToInt(_typed * LETTERS_PER_SECOND);
            if (shown >= _letters)
            {
                _typing = false;
                _lineText.maxVisibleCharacters = 99999;
                _anim.Energy = 0.3f;
                return;
            }

            if (_lineText.maxVisibleCharacters != shown) _lineText.maxVisibleCharacters = shown;
        }

        private void BuildScene()
        {
            var lines = _steps[_step].Lines;
            var line = lines[Mathf.Clamp(_line, 0, lines.Count - 1)];
            var campaign = Campaign;
            var layer = Top;
            layer.Confirm = Advance;
            layer.Skip = SkipScene;
            _lineText = null;

            // Anywhere on the screen: next
            var catcher = StoryUi.Area(_viewRoot, 0f, 0f, 1f, 1f, "Tap");
            var catcherImage = catcher.gameObject.AddComponent<Image>();
            catcherImage.color = new Color(0f, 0f, 0f, 0.001f);
            var catcherButton = catcher.gameObject.AddComponent<Button>();
            catcherButton.transition = Selectable.Transition.None;
            catcherButton.onClick.AddListener(() => Safely(Advance));

            if (line.Speaker == StoryLine.TITLE)
            {
                StoryArt.Box(_artRoot, 0f, 0f, 1f, 1f, StoryTheme.Ink);
                StoryUi.Text(_viewRoot, 0.06f, 0.46f, 0.94f, 0.72f, Fill(line.Text), Wide ? 110 : 84, true, StoryTheme.Gold,
                    TextAlignmentOptions.Center);
                StoryUi.Solid(_viewRoot, 0.44f, 0.445f, 0.56f, 0.45f, StoryTheme.Gold);
                StoryUi.Text(_viewRoot, 0.1f, 0.28f, 0.9f, 0.43f, Fill(line.Sub), 40, false, StoryTheme.Cream, TextAlignmentOptions.Center);
                _typing = false;
                SkipButton();
                return;
            }

            // The room: the last backdrop named up to this line, or the scene's own
            string room = _steps[_step].Backdrop;
            for (int i = 0; i <= _line && i < lines.Count; i++)
            {
                if (!string.IsNullOrEmpty(lines[i].Backdrop)) room = lines[i].Backdrop;
            }

            Picture(room);

            bool narrator = line.Speaker == StoryLine.NARRATOR || string.IsNullOrEmpty(line.Speaker);
            bool you = line.Speaker == StoryLine.YOU;
            var character = !narrator && !you && campaign != null ? campaign.FindCharacter(line.Speaker) : null;
            string name = you ? (Engine != null ? Engine.Progress.BandName : "") : character != null ? character.Name : "";
            if (narrator && campaign != null)
            {
                var voice = campaign.FindCharacter(StoryLine.NARRATOR);
                if (voice != null) name = voice.Name;
            }

            string role = "";
            if (character != null && _met.Add(character.Id)) role = character.Role;

            // The box the words go in
            var box = Wide ? StoryUi.Area(_viewRoot, 0f, 0f, 1f, 0.36f, "Words") : StoryUi.Area(_viewRoot, 0f, 0f, 1f, 0.4f, "Words");
            StoryUi.Inset(box, M, M * 0.5f, M, 0f);
            StoryUi.Rounded(box, StoryTheme.Scrim, 1.4f);
            StoryArt.Frame(box, 0f, 0f, 1f, 1f, StoryUi.Alpha(StoryTheme.Gold, 0.3f));

            float textLeft = 40f;
            bool picture = character != null || you;
            if (narrator)
            {
                var panel = Wide ? StoryUi.Area(_viewRoot, 0.1f, 0.4f, 0.9f, 0.78f, "Narrator") : StoryUi.Area(_viewRoot, 0.04f, 0.44f, 0.96f, 0.72f, "Narrator");
                StoryUi.Rounded(panel, new Color(0f, 0f, 0f, 0.88f), 1.2f);
                var wave = StoryUi.Inset(StoryUi.Area(panel, 0f, 0f, 1f, 1f, "Wave"), 30f, 20f, 30f, 20f);
                var size = Wide ? new Vector2(_size.x * 0.8f - 60f, _size.y * 0.38f - 40f) : new Vector2(_size.x * 0.92f - 60f, _size.y * 0.28f - 40f);
                StoryArt.Waveform(wave, size, NarratorLayers(), _anim, 1f);
            }
            else if (picture)
            {
                float side = Wide ? Mathf.Min(_size.y * 0.5f, _size.x * 0.26f) : Mathf.Min(_size.x * 0.62f, _size.y * 0.3f);
                var holder = StoryUi.Area(_viewRoot, Wide ? 0f : 0.5f, Wide ? 0.36f : 0.4f, Wide ? 0f : 0.5f, Wide ? 0.36f : 0.4f, "Portrait");
                holder.pivot = Wide ? new Vector2(0f, 0f) : new Vector2(0.5f, 0f);
                holder.sizeDelta = new Vector2(side, side);
                holder.anchoredPosition = Wide ? new Vector2(M + 24f, -side * 0.3f) : new Vector2(0f, -side * 0.06f);
                if (you) StoryArt.Silhouette(holder, _anim);
                else StoryArt.Portrait(holder, character.Portrait, line.Mood, _anim);
                // The words sit beside the portrait, which leans into the box
                if (Wide) textLeft = side + 64f;
            }

            float nameHeight = string.IsNullOrEmpty(name) ? 0f : 64f;
            if (nameHeight > 0f)
            {
                var plate = StoryUi.Area(box, 0f, 1f, 1f, 1f, "Name");
                plate.offsetMin = new Vector2(textLeft, -82f);
                plate.offsetMax = new Vector2(-40f, -18f);
                StoryUi.Text(StoryUi.Area(plate, 0f, 0f, 0.55f, 1f, "Speaker"), name, 44, true,
                    narrator ? StoryTheme.Gold : you ? StoryTheme.Gold : StoryTheme.Cream, TextAlignmentOptions.Left, false);
                if (!string.IsNullOrEmpty(role))
                {
                    StoryUi.Text(StoryUi.Area(plate, 0.55f, 0f, 1f, 1f, "Role"), Fill(role), 28, false, StoryTheme.Muted,
                        TextAlignmentOptions.Right);
                }
            }

            var words = StoryUi.Area(box, 0f, 0f, 1f, 1f, "Line");
            StoryUi.Inset(words, textLeft, 24f, 40f, 26f + nameHeight);
            _lineText = StoryUi.Text(words, Fill(line.Text), 44, false, StoryTheme.Cream, TextAlignmentOptions.TopLeft);
            _letters = Mathf.Max(1, _lineText.text.Length);
            _typed = 0f;
            _lineText.maxVisibleCharacters = _typing ? 0 : 99999;
            _anim.Energy = _typing ? 1f : 0.3f;

            // Position in the scene: a row of small dots under the box
            int count = lines.Count;
            if (count > 1 && count <= 40)
            {
                var dots = StoryUi.Area(box, 0.3f, 0f, 0.7f, 0f, "Dots");
                dots.offsetMin = new Vector2(0f, 6f);
                dots.offsetMax = new Vector2(0f, 16f);
                for (int i = 0; i < count; i++)
                {
                    StoryArt.Pill(dots, i / (float) count + 0.15f / count, 0f, (i + 1) / (float) count - 0.15f / count, 1f,
                        i <= _line ? StoryTheme.Gold : StoryUi.Alpha(StoryTheme.Faint, 0.6f));
                }
            }

            SkipButton();
        }

        private void SkipButton()
        {
            var skip = StoryUi.Area(_viewRoot, 1f, 1f, 1f, 1f, "Skip");
            skip.offsetMin = new Vector2(-M - 220f, -M - 96f);
            skip.offsetMax = new Vector2(-M, -M);
            AddButton(skip, StoryButtonStyle.Normal, "Skip", 36, SkipScene);
        }

        /// <summary>The parts of the band found again so far: each one adds a line to the narrator's waveform.</summary>
        private static List<string> NarratorLayers()
        {
            var layers = new List<string>();
            var engine = Engine;
            if (engine == null) return layers;

            foreach (string layer in StoryKeepsake.Layers)
            {
                foreach (string id in engine.Progress.Keepsakes)
                {
                    var keepsake = engine.Campaign.FindKeepsake(id);
                    if (keepsake != null && keepsake.Layer == layer)
                    {
                        layers.Add(layer);
                        break;
                    }
                }
            }

            return layers;
        }
    }
}
