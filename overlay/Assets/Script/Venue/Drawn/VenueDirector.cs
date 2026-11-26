using UnityEngine;
using UnityEngine.UI;
using YARG.Gameplay;
using YARG.Menu.Persistent;
using YARG.Settings;

namespace YARG.Venue.Drawn
{
    /// <summary>
    /// Puts a drawn venue behind the highway for one song: the stage (unless your own picture or video
    /// is used for it), the crowd if it's turned on, and the beat that keeps both moving.
    /// </summary>
    public class VenueDirector : MonoBehaviour
    {
        private GameManager _gameManager;
        private DrawnStage _stage;
        private DrawnCrowd _crowd;
        private int _beatIndex;

        // What to draw again when the screen changes shape (a Fold opened or closed, the phone
        // turned): circles and people are laid out for the shape they were drawn on
        private DrawnVenue _venue;
        private bool _drawStage;
        private bool _drawCrowd;
        private float _aspect;

        /// <param name="backgroundImage">The game's own background picture; the venue is drawn right over it.</param>
        /// <param name="drawStage">False when a picture or video of yours is the backdrop and only the crowd is wanted.</param>
        public static VenueDirector Show(RawImage backgroundImage, GameManager gameManager, DrawnVenue venue, bool drawStage)
        {
            if (backgroundImage == null || venue == null)
            {
                return null;
            }

            bool crowd = SettingsManager.Settings == null || SettingsManager.Settings.VenueCrowd.Value;
            if (!drawStage && !crowd)
            {
                return null;
            }

            var holder = new GameObject("DrawnVenue", typeof(RectTransform));
            var rect = (RectTransform) holder.transform;
            rect.SetParent(backgroundImage.transform.parent, false);
            rect.SetSiblingIndex(backgroundImage.transform.GetSiblingIndex() + 1);
            VenueShapes.Place(rect, 0f, 0f, 1f, 1f);

            var director = holder.AddComponent<VenueDirector>();
            director._gameManager = gameManager;
            director._venue = venue;
            director._drawStage = drawStage;
            director._drawCrowd = crowd;
            director.Draw();

            ToastManager.ToastInformation($"{venue.Name}\nInspired by {venue.InspiredBy}");
            return director;
        }

        private static float ScreenAspect => Screen.height > 0 ? Screen.width / (float) Screen.height : 16f / 9f;

        private void Draw()
        {
            var rect = (RectTransform) transform;
            if (_stage != null) Destroy(_stage.gameObject);
            if (_crowd != null) Destroy(_crowd.gameObject);
            _stage = null;
            _crowd = null;
            _aspect = ScreenAspect;

            if (_drawStage)
            {
                var stage = VenueShapes.Holder(rect, "Stage", 0f, 0f, 1f, 1f);
                _stage = stage.gameObject.AddComponent<DrawnStage>();
                _stage.Build(_venue);
            }

            if (_drawCrowd)
            {
                var people = VenueShapes.Holder(rect, "Crowd", 0f, 0f, 1f, 1f);
                _crowd = people.gameObject.AddComponent<DrawnCrowd>();
                _crowd.Build(_venue);
            }
        }

        private void Update()
        {
            if (Mathf.Abs(ScreenAspect - _aspect) > 0.01f)
            {
                Draw();
            }

            float beat = CurrentBeat();
            if (_stage != null) _stage.Animate(beat);
            if (_crowd != null) _crowd.Animate(beat);
        }

        // The song's position counted in beats, following its tempo map
        private float CurrentBeat()
        {
            if (_gameManager == null || _gameManager.Chart == null)
            {
                return Time.time * 2f;
            }

            double time = _gameManager.VisualTime;
            var beats = _gameManager.Chart.SyncTrack.Beatlines;
            if (beats == null || beats.Count < 2 || time < beats[0].Time)
            {
                // Before the first beat (the count-in): two beats a second
                return (float) (time * 2.0);
            }

            // The song mostly moves forward a little each frame, so start from where we were
            if (_beatIndex >= beats.Count - 1 || beats[_beatIndex].Time > time)
            {
                _beatIndex = 0;
            }

            while (_beatIndex < beats.Count - 2 && beats[_beatIndex + 1].Time <= time)
            {
                _beatIndex++;
            }

            double start = beats[_beatIndex].Time;
            double length = beats[_beatIndex + 1].Time - start;
            double fraction = length > 0.0 ? (time - start) / length : 0.0;
            if (fraction > 1.0)
            {
                // Past the last beat line: keep counting at the last tempo
                return (float) (_beatIndex + fraction);
            }

            return (float) (_beatIndex + fraction);
        }
    }
}
