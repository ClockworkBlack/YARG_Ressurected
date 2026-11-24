// YARG Resurrected - Story Mode: playing a gig with the game's own screens.
//
// A gig is played as a "show": the game's setlist mode, even for a single song. That way the
// game's own instrument and difficulty screen checks every player against the songs, the score
// screen appears after each song and moves on to the next one by itself, and a section picked on
// the Song Setup screen cannot quietly turn the gig into a practice run.
//
// The story then only watches the scenes go by:
//   - the score screen loading  -> how that song went (copied at once: the game wipes it soon after);
//   - the menu loading again    -> the gig is over: finished, cut short, or a song failed to load;
//   - the song screen closing without a song starting -> the player backed out.
// Back at the menu it waits until the menus are up and taking input, works out what the night
// earned, saves, and hands the result to the story screens.
//
// The game does not tidy up its "what is being played" state after a back-out or a failed load,
// so this does it, or the next song from the library would start in a strange state.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Gameplay;
using YARG.Menu;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Menu.ScoreScreen;
using YARG.Menu.SongSetup;

namespace YARG.Story
{
    public enum StoryGigReturnKind
    {
        /// <summary>Every song of the set was played to the end. Won or lost is in the outcome.</summary>
        Finished,

        /// <summary>Some songs, not all (quit, skipped, ended early). Nothing is recorded.</summary>
        CutShort,

        /// <summary>Left the instrument and difficulty screen without playing.</summary>
        BackedOut,

        /// <summary>The game could not load a song.</summary>
        LoadFailed,
    }

    /// <summary>How a gig went, handed to the story screens when the game is back in the menu.</summary>
    public sealed class StoryGigReturn
    {
        public StoryGigReturnKind Kind;
        public StoryGig Gig;

        /// <summary>Finished only: already applied to the save and saved.</summary>
        public StoryGigOutcome Outcome;

        /// <summary>Results of the songs that were finished, in set order.</summary>
        public List<StorySongResult> Played = new List<StorySongResult>();

        /// <summary>Plain English, may be empty: why something did not count.</summary>
        public string Note = "";
    }

    public sealed class StoryGigFlow : MonoBehaviour
    {
        private enum Phase
        {
            Idle,

            /// <summary>The instrument and difficulty screen is up.</summary>
            Choosing,

            /// <summary>In a song or on a score screen.</summary>
            Playing,

            /// <summary>Back in the menu, waiting for it to be ready.</summary>
            Returning,
        }

        private Phase               _phase;
        private StoryEngine         _engine;
        private StoryGig            _gig;
        private SongEntry[]         _songs;
        private StorySongResult[]   _results;
        private readonly List<string> _notes = new List<string>();
        private int                 _pushFrame;
        private int                 _releaseFrame = -1;

        /// <summary>A gig is under way (from the song screen until the result is handed over).</summary>
        public bool Busy => _phase != Phase.Idle;

        private static StoryMode Owner => StoryMode.Instance;

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        // ------------------------------------------------------------------------------------
        // Starting
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Sets the game up to play <paramref name="songs"/> as a show and opens its instrument and
        /// difficulty screen. The story screen must already be hidden (its controls popped), the
        /// save must already hold the gig as pending. Throws if the game's menu refuses.
        /// </summary>
        internal void Launch(StoryEngine engine, StoryGig gig, SongEntry[] songs)
        {
            // A section picked on the Song Setup screen would make the song a practice run
            foreach (var song in songs)
            {
                if (SongSetupState.TryGetSpan(song, out _, out _))
                {
                    SongSetupState.ClearSpan();
                }
            }

            _engine = engine;
            _gig = gig;
            _songs = songs;
            _results = new StorySongResult[songs.Length];
            _notes.Clear();
            _releaseFrame = -1;

            // PersistentState is a struct held in a static field: set its fields in place
            GlobalVariables.State = PersistentState.Default;
            GlobalVariables.State.PlayingAShow = true;
            GlobalVariables.State.ShowSongs.AddRange(songs);
            GlobalVariables.State.ShowIndex = 0;
            GlobalVariables.State.CurrentSong = songs[0];

            _phase = Phase.Choosing;
            _pushFrame = Time.frameCount;

            MenuManager.Instance.PushMenu(MenuManager.Menu.DifficultySelect);

            // The speed box remembers the last speed used; a gig is played at full speed
            var menu = DifficultySelectMenu.Current;
            if (menu != null)
            {
                menu.ShownSongSpeed = 1f;
            }
        }

        // ------------------------------------------------------------------------------------
        // Watching
        // ------------------------------------------------------------------------------------

        private void Update()
        {
            try
            {
                if (_releaseFrame >= 0 && Time.frameCount >= _releaseFrame)
                {
                    _releaseFrame = -1;
                    ReleaseVenue();
                }

                // Back from the song screen without a song: it is gone, the menu is still here and
                // no scene is loading. (Pressing Ready blocks menu input at once, so that is not this.)
                if (_phase == Phase.Choosing && Time.frameCount > _pushFrame + 1 &&
                    DifficultySelectMenu.Current == null && MenuIsIdle())
                {
                    if (StoryMode.HumanPlayers == 0)
                    {
                        AddNote("Nobody is set up to play yet.");
                    }

                    GlobalVariables.State = PersistentState.Default;
                    Finish(StoryGigReturnKind.BackedOut);
                }
            }
            catch (Exception e)
            {
                Fail(e, "Story: the gig went wrong while waiting");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                // The venue setting is only borrowed for the start of a song
                if (scene.buildIndex != (int) SceneIndex.Gameplay)
                {
                    _releaseFrame = -1;
                    ReleaseVenue();
                }

                if (_phase == Phase.Idle || _phase == Phase.Returning)
                {
                    return;
                }

                switch (scene.buildIndex)
                {
                    case (int) SceneIndex.Gameplay:
                        OnGameplayLoaded();
                        break;

                    case (int) SceneIndex.Score:
                        CaptureResult();
                        break;

                    case (int) SceneIndex.Menu:
                        OnMenuLoaded();
                        break;
                }
            }
            catch (Exception e)
            {
                Fail(e, "Story: the gig went wrong at a scene change");
            }
        }

        private void OnGameplayLoaded()
        {
            _phase = Phase.Playing;

            if (GlobalVariables.State.IsPractice)
            {
                AddNote("Practice runs do not count toward a gig.");
            }

            if (_gig != null)
            {
                StoryVenueBridge.Hold(_gig.VenueId, _engine?.Progress, SaveNow);
            }

            // The venue is picked as the song starts; give the setting back just after that
            var game = FindAnyObjectByType<GameManager>();
            if (game != null)
            {
                game.SongStarted += OnSongStarted;
            }
        }

        private void OnSongStarted()
        {
            _releaseFrame = Time.frameCount + 3;
        }

        // The score screen is up: copy the result now, the game clears it as the screen closes
        private void CaptureResult()
        {
            var state = GlobalVariables.State;
            int index = state.ShowIndex;
            if (!state.ScoreScreenStats.HasValue || _songs == null || index < 0 || index >= _songs.Length ||
                !ReferenceEquals(state.CurrentSong, _songs[index]))
            {
                YargLogger.LogWarning("Story: a score screen came up that does not belong to the gig");
                return;
            }

            if (state.PlayingWithReplay)
            {
                return;
            }

            var result = BuildResult(state.ScoreScreenStats.Value, state.SongSpeed, _songs[index], out string why);
            if (result == null)
            {
                AddNote(why);
                return;
            }

            // The score screen's "Restart" plays the same song again: keep the better night
            if (_results[index] == null || IsBetter(result, _results[index]))
            {
                _results[index] = result;
            }

            // So the story knows how far the set got if the app is closed before it ends
            var pending = _engine?.Progress.Pending;
            if (pending != null && _gig != null && pending.GigId == _gig.Id)
            {
                pending.Results.Clear();
                foreach (var done in _results)
                {
                    if (done != null)
                    {
                        pending.Results.Add(done);
                    }
                }

                SaveNow();
            }
        }

        private static StorySongResult BuildResult(ScoreScreenStats stats, float speed, SongEntry song, out string why)
        {
            string name = StoryLibrary.Plain(song.Name.Original);
            why = "";

            int humans = 0;
            int humansWithNotes = 0;
            int starSum = 0;
            bool allGold = true;
            bool allFullCombo = true;
            double percent = 0;
            int streak = 0;
            long score = 0;
            int lowest = int.MaxValue;

            var cards = stats.PlayerScores;
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    var player = card.Player;
                    var played = card.Stats;
                    if (player == null || played == null || player.Profile == null)
                    {
                        continue;
                    }

                    // Computer players and replay ghosts are not the band
                    if (player.Profile.IsBot || player.IsReplay)
                    {
                        continue;
                    }

                    humans++;

                    // A part with no notes reports 100% and a full combo: it says nothing
                    if (played.TotalNotes <= 0)
                    {
                        continue;
                    }

                    humansWithNotes++;
                    int stars = Clamp((int) played.Stars, 0, 6);
                    starSum += stars;
                    if (stars < 6)
                    {
                        allGold = false;
                    }

                    percent += played.Percent * 100.0;
                    streak = Math.Max(streak, played.MaxCombo);
                    if (!played.IsFullCombo)
                    {
                        allFullCombo = false;
                    }

                    score += played.TotalScore;
                    lowest = Math.Min(lowest, Clamp((int) player.Profile.CurrentDifficulty, 0, 4));
                }
            }

            if (humans == 0)
            {
                why = $"\"{name}\" was played by computer players only, so it did not count.";
                return null;
            }

            if (humansWithNotes == 0)
            {
                why = $"Nobody had notes to play on \"{name}\", so it did not count.";
                return null;
            }

            if (speed < 0.999f)
            {
                why = $"\"{name}\" was played slower than full speed, so it did not count.";
                return null;
            }

            return new StorySongResult
            {
                SongId = song.Hash.ToString(),
                SongName = name,
                SongArtist = StoryLibrary.Plain(song.Artist.Original),
                Stars = Math.Min(5, starSum / humansWithNotes),
                Gold = allGold,
                Percent = Math.Max(0, Math.Min(100, percent / humansWithNotes)),
                BestStreak = streak,
                FullCombo = allFullCombo,
                Score = score,
                Players = humansWithNotes,
                LowestDifficulty = lowest == int.MaxValue ? 0 : lowest,
            };
        }

        private static bool IsBetter(StorySongResult a, StorySongResult b)
        {
            if (a.Stars != b.Stars) return a.Stars > b.Stars;
            if (a.Gold != b.Gold) return a.Gold;
            return a.Score > b.Score;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private void OnMenuLoaded()
        {
            // Quitting and the score screen both clear the song; a song that could not be loaded
            // leaves it set, and nothing else in the game will clear it
            var failedSong = GlobalVariables.State.CurrentSong;
            bool loadFailed = failedSong != null;
            if (loadFailed)
            {
                AddNote($"\"{StoryLibrary.Plain(failedSong.Name.Original)}\" could not be loaded. Try a different song.");
                GlobalVariables.State = PersistentState.Default;
            }

            _phase = Phase.Returning;

            // The main menu is about to appear underneath: cover it so it does not flash. Not after a
            // failed load: the game then shows its own dialog over the main menu, and with a controller
            // that dialog is driven through the main menu's controls, which the cover would hide.
            if (!loadFailed)
            {
                Owner?.CoverForReturn();
            }

            StartCoroutine(ReturnWhenReady(loadFailed));
        }

        private IEnumerator ReturnWhenReady(bool loadFailed)
        {
            // The menus set themselves up (and push their controls) over the next frames
            yield return null;
            yield return null;

            while (!MenuIsIdle() || !LoadingScreenGone())
            {
                yield return null;
            }

            try
            {
                Finish(loadFailed ? StoryGigReturnKind.LoadFailed : StoryGigReturnKind.CutShort);
            }
            catch (Exception e)
            {
                Fail(e, "Story: could not finish the gig");
            }
        }

        // ------------------------------------------------------------------------------------
        // Ending
        // ------------------------------------------------------------------------------------

        // kind is what happened if the set is not complete; a complete set is always Finished
        private void Finish(StoryGigReturnKind kind)
        {
            ReleaseVenue();

            var result = new StoryGigReturn { Kind = kind, Gig = _gig };
            bool complete = _results != null && _results.Length > 0;
            if (_results != null)
            {
                foreach (var done in _results)
                {
                    if (done != null)
                    {
                        result.Played.Add(done);
                    }
                    else
                    {
                        complete = false;
                    }
                }
            }

            if (kind == StoryGigReturnKind.BackedOut)
            {
                complete = false;
            }

            var engine = _engine;
            if (engine != null && _gig != null)
            {
                if (complete)
                {
                    result.Kind = StoryGigReturnKind.Finished;
                    result.Outcome = engine.Apply(engine.Evaluate(_gig, result.Played), DateTime.UtcNow);
                }

                // Apply clears it for a finished set; anything else is not recorded at all
                engine.Progress.Pending = null;
            }

            result.Note = string.Join(" ", _notes);
            Clear();
            SaveNow();

            YargLogger.LogInfo("Story: gig over (" + result.Kind + ", " + result.Played.Count + " songs counted)");
            Owner?.DeliverGigReturn(result);
        }

        private void Clear()
        {
            _phase = Phase.Idle;
            _engine = null;
            _gig = null;
            _songs = null;
            _results = null;
            _notes.Clear();
            _releaseFrame = -1;
        }

        // Something broke: tidy up, keep the game's menus working, and give the story screens a
        // plain "cut short" so the player is not left on a blank cover
        private void Fail(Exception e, string message)
        {
            YargLogger.LogException(e, message);
            var gig = _gig;
            try
            {
                ReleaseVenue();
                if (_engine != null)
                {
                    _engine.Progress.Pending = null;
                }

                SaveNow();
            }
            catch (Exception inner)
            {
                YargLogger.LogException(inner, "Story: could not tidy up after a gig went wrong");
            }

            // Only once back in the menu: earlier, the game's own song screens are still in charge
            // and the player simply carries on there (the story stays closed)
            bool inMenu = _phase == Phase.Returning;
            Clear();
            StopAllCoroutines();

            if (inMenu)
            {
                StartCoroutine(DeliverWhenIdle(new StoryGigReturn
                {
                    Kind = StoryGigReturnKind.CutShort,
                    Gig = gig,
                    Note = "Something went wrong, so this night was not counted.",
                }));
            }
        }

        private IEnumerator DeliverWhenIdle(StoryGigReturn result)
        {
            yield return null;
            while (!MenuIsIdle() || !LoadingScreenGone())
            {
                yield return null;
            }

            Owner?.DeliverGigReturn(result);
        }

        private void ReleaseVenue()
        {
            if (StoryVenueBridge.IsHeld)
            {
                StoryVenueBridge.Release(_engine?.Progress ?? Owner?.Engine?.Progress, SaveNow);
            }
        }

        private void AddNote(string note)
        {
            if (!string.IsNullOrEmpty(note) && !_notes.Contains(note))
            {
                _notes.Add(note);
            }
        }

        private static void SaveNow()
        {
            Owner?.Save();
        }

        // The menu scene is up, no scene is loading and nothing is in a dialog
        private static bool MenuIsIdle()
        {
            var global = GlobalVariables.Instance;
            var navigator = Navigator.Instance;
            if (global == null || navigator == null || MenuManager.Instance == null)
            {
                return false;
            }

            if (global.CurrentScene != SceneIndex.Menu || navigator.DisableMenuInputs)
            {
                return false;
            }

            var dialogs = DialogManager.Instance;
            return dialogs == null || !dialogs.IsDialogShowing;
        }

        private static bool LoadingScreenGone()
        {
            return LoadingScreen.Instance == null || !LoadingScreen.IsActive;
        }
    }
}
