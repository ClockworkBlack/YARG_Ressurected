// YARG Resurrected - Story Mode: the host.
//
// One object, made when the game starts and kept for as long as it runs, that holds story mode
// together: the story itself (read from Assets/Resources/YargStory), the chosen save slot, a
// snapshot of the song library, the gig being played, and the story screens. The screens only
// ever talk to this class.
//
// Story mode is a guest in the game's menus. Everything that the game calls (scene changes,
// Update, button presses) is wrapped so that if something in the story goes wrong it is written
// to the log and the story steps aside, and the game's own menus keep working.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Menu;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;

namespace YARG.Story
{
    public sealed class StoryMode : MonoBehaviour
    {
        public static StoryMode Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            try
            {
                if (Instance != null)
                {
                    return;
                }

                // Every build, not only Android: the story plays the same on a desktop
                var host = new GameObject("YARG Story Mode");
                DontDestroyOnLoad(host);
                host.AddComponent<StoryMode>();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode failed to start");
            }
        }

        private StoryGigFlow _flow;
        private StoryScreen  _screen;

        private StoryCampaign _campaign;
        private bool          _campaignTried;
        private bool          _venueRecovered;

        // Frame the menu scene last loaded: its menus set themselves up over the next frames
        private int _menuLoadedFrame;

        // After "Set up players": open the story again once the main menu is back
        private bool _reopenAfterMenu;
        private int  _mainMenuShowsAtLeave;

        // What the title screen shows for each slot, read once (index 0 unused)
        private readonly StoryProgress[] _peek = new StoryProgress[StorySaves.SLOTS + 1];
        private readonly bool[]          _peeked = new bool[StorySaves.SLOTS + 1];

        // ------------------------------------------------------------------------------------
        // What the screens see
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// The game is settled in its menus: settings loaded, no loading screen, the menu scene up
        /// and set up, nothing loading and no dialog open.
        /// </summary>
        public static bool GameReady
        {
            get
            {
                try
                {
                    var self = Instance;
                    if (self == null || SettingsManager.Settings == null)
                    {
                        return false;
                    }

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

                    if (LoadingScreen.Instance == null || LoadingScreen.IsActive)
                    {
                        return false;
                    }

                    if (!SceneManager.GetSceneByBuildIndex((int) SceneIndex.Menu).isLoaded)
                    {
                        return false;
                    }

                    var dialogs = DialogManager.Instance;
                    if (dialogs != null && dialogs.IsDialogShowing)
                    {
                        return false;
                    }

                    return Time.frameCount >= self._menuLoadedFrame + 2;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The story, read on the first <see cref="Open"/>. Null when its files are broken.</summary>
        public StoryCampaign Campaign => _campaign;

        /// <summary>The chosen save slot, 1 to 3; 0 before one is chosen.</summary>
        public int Slot { get; private set; }

        /// <summary>The story's rules for the chosen slot; null before one is chosen.</summary>
        public StoryEngine Engine { get; private set; }

        /// <summary>The songs in the library, refreshed on <see cref="Open"/> and after a rescan.</summary>
        public IReadOnlyList<StorySong> Library => StoryLibrary.Songs;

        /// <summary>The story screens are up (or about to be).</summary>
        public bool IsOpen { get; private set; }

        /// <summary>A gig is being played.</summary>
        public bool GigUnderWay => _flow != null && _flow.Busy;

        /// <summary>Raised once a gig is over and the menu is ready, after the screens were given the result.</summary>
        public event Action<StoryGigReturn> GigReturned;

        /// <summary>People playing, not counting computer players.</summary>
        public static int HumanPlayers
        {
            get
            {
                try
                {
                    int humans = 0;
                    foreach (var player in PlayerContainer.Players)
                    {
                        if (player != null && player.Profile != null && !player.Profile.IsBot)
                        {
                            humans++;
                        }
                    }

                    return humans;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        // ------------------------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------------------------

        private void Awake()
        {
            try
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(this);
                    return;
                }

                Instance = this;
                SceneManager.sceneLoaded += OnSceneLoaded;
                _flow = gameObject.AddComponent<StoryGigFlow>();
                gameObject.AddComponent<StoryMenuEntry>();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode failed to set up");
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            try
            {
                // A gig's borrowed venue setting from a run that was cut off: put it back first thing
                if (!_venueRecovered && SettingsManager.Settings != null && !GigUnderWay)
                {
                    EnsureVenueRecovered();
                }

                bool ready = GameReady;
                if (ready && !GigUnderWay)
                {
                    // Only a reference check unless the songs were rescanned
                    StoryLibrary.Refresh();
                }

                if (_reopenAfterMenu && ready && !GigUnderWay &&
                    StoryMenuEntry.MainMenuShows != _mainMenuShowsAtLeave && StoryMenuEntry.MainMenuVisible)
                {
                    _reopenAfterMenu = false;
                    Open();
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode: error in Update");
                _reopenAfterMenu = false;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (scene.buildIndex == (int) SceneIndex.Menu)
                {
                    _menuLoadedFrame = Time.frameCount;
                    return;
                }

                // Never over a song or a score screen
                _reopenAfterMenu = false;
                if (IsOpen)
                {
                    IsOpen = false;
                    HideScreen();
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story mode: error at a scene change");
            }
        }

        // Phones close apps from the background without warning: save while we still can
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Save();
            }
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        // ------------------------------------------------------------------------------------
        // Opening and closing
        // ------------------------------------------------------------------------------------

        /// <summary>Opens the story screens over the main menu (from the main menu's STORY entry).</summary>
        public void Open()
        {
            try
            {
                if (IsOpen || GigUnderWay)
                {
                    return;
                }

                if (!GameReady)
                {
                    YargLogger.LogInfo("Story: not opened, the game is still busy");
                    return;
                }

                EnsureCampaign();
                EnsureVenueRecovered();
                StoryLibrary.Refresh();
                RemoveStrayTouchControls();

                if (EnsureScreen() == null)
                {
                    ToastManager.ToastError("Story mode could not be opened.");
                    return;
                }

                IsOpen = true;
                _screen.Show();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not open");
                IsOpen = false;
                HideScreen();
            }
        }

        /// <summary>Back to the game's main menu.</summary>
        public void Close()
        {
            try
            {
                _reopenAfterMenu = false;
                if (!IsOpen)
                {
                    return;
                }

                IsOpen = false;
                HideScreen();
                Save();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not close");
            }
        }

        /// <summary>
        /// Hides the story and opens the game's own player list, so players can be added. The story
        /// opens again by itself when the player goes back to the main menu.
        /// </summary>
        public void OpenProfiles()
        {
            try
            {
                if (!GameReady || GigUnderWay)
                {
                    YargLogger.LogInfo("Story: player list not opened, the game is still busy");
                    return;
                }

                // The story's controls must be off the stack before the menu changes: the main menu
                // takes the top set of controls with it when it is hidden, whoever owns them
                IsOpen = false;
                HideScreen();
                Save();

                _reopenAfterMenu = true;
                _mainMenuShowsAtLeave = StoryMenuEntry.MainMenuShows;
                MenuManager.Instance.PushMenu(MenuManager.Menu.ProfileList);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not open the player list");
                _reopenAfterMenu = false;
            }
        }

        // ------------------------------------------------------------------------------------
        // Save slots
        // ------------------------------------------------------------------------------------

        /// <summary>The save in a slot, for the title screen. Null for an empty slot.</summary>
        public StoryProgress PeekSlot(int slot)
        {
            try
            {
                if (!StorySaves.IsSlot(slot))
                {
                    return null;
                }

                if (slot == Slot && Engine != null)
                {
                    return Engine.Progress;
                }

                if (!_peeked[slot])
                {
                    _peek[slot] = StorySaves.Load(slot);
                    _peeked[slot] = true;
                }

                return _peek[slot];
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not look at slot " + slot);
                return null;
            }
        }

        /// <summary>Starts a new story in a slot (anything saved there is replaced), chooses it and saves.</summary>
        public void StartNew(int slot, string bandName)
        {
            try
            {
                if (_campaign == null || !StorySaves.IsSlot(slot) || GigUnderWay)
                {
                    return;
                }

                // A venue setting noted in the old save must go back before the note is overwritten
                EnsureVenueRecovered();

                var progress = StoryEngine.NewProgress(_campaign, bandName, DateTime.UtcNow);
                Engine = new StoryEngine(_campaign, progress);
                Slot = slot;
                ForgetPeek(slot);
                Save();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not start a new story");
            }
        }

        /// <summary>Chooses a slot that has a story in it. False when it is empty or cannot be read.</summary>
        public bool Continue(int slot)
        {
            try
            {
                if (_campaign == null || !StorySaves.IsSlot(slot) || GigUnderWay)
                {
                    return false;
                }

                EnsureVenueRecovered();

                var progress = StorySaves.Load(slot);
                ForgetPeek(slot);
                if (progress == null)
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(progress.CampaignId) && progress.CampaignId != _campaign.Id)
                {
                    YargLogger.LogWarning("Story: slot " + slot + " was saved by another campaign (" +
                        progress.CampaignId + "); gigs it does not know are ignored");
                }

                Engine = new StoryEngine(_campaign, progress);
                Slot = slot;
                SettleUnfinishedGig();
                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not continue slot " + slot);
                return false;
            }
        }

        public void DeleteSlot(int slot)
        {
            try
            {
                if (!StorySaves.IsSlot(slot) || (GigUnderWay && slot == Slot))
                {
                    return;
                }

                // Its venue note (if any) goes back first, or deleting would lose the player's setting
                EnsureVenueRecovered();

                StorySaves.Delete(slot);
                ForgetPeek(slot);
                if (slot == Slot)
                {
                    Slot = 0;
                    Engine = null;
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not delete slot " + slot);
            }
        }

        /// <summary>Writes the chosen slot now. Cheap: call it after every change.</summary>
        public void Save()
        {
            try
            {
                if (Engine == null || !StorySaves.IsSlot(Slot))
                {
                    return;
                }

                // Also tells the two copies of a save apart (the later one wins when they are read)
                Engine.Progress.PlayedOn = StoryEngine.Stamp(DateTime.UtcNow);
                StorySaves.Write(Slot, Engine.Progress);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not save");
            }
        }

        private void ForgetPeek(int slot)
        {
            if (StorySaves.IsSlot(slot))
            {
                _peek[slot] = null;
                _peeked[slot] = false;
            }
        }

        // A save can hold a gig that was under way when the app was closed. If every song of it was
        // played, the night is recorded; otherwise it is dropped, as a set cut short would be.
        private void SettleUnfinishedGig()
        {
            var progress = Engine.Progress;
            var pending = progress.Pending;
            if (pending == null)
            {
                return;
            }

            var gig = _campaign.FindGig(pending.GigId);
            if (gig != null && pending.IsComplete && pending.Results.Count >= Math.Max(1, gig.Songs))
            {
                var outcome = Engine.Apply(Engine.Evaluate(gig, pending.Results), DateTime.UtcNow);
                YargLogger.LogInfo("Story: recorded gig " + gig.Id + " that was finished before the app closed (" +
                    (outcome.Won ? "won" : "not won") + ")");
            }
            else
            {
                YargLogger.LogInfo("Story: dropped an unfinished gig from before the app closed");
            }

            progress.Pending = null;
            Save();
        }

        private void EnsureVenueRecovered()
        {
            if (_venueRecovered || SettingsManager.Settings == null || GigUnderWay)
            {
                return;
            }

            _venueRecovered = true;
            try
            {
                StoryVenueBridge.RecoverFromSaves();
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not check the saves for a borrowed venue setting");
            }

            // The saves on disk may have changed
            for (int slot = 1; slot <= StorySaves.SLOTS; slot++)
            {
                ForgetPeek(slot);
            }
        }

        // ------------------------------------------------------------------------------------
        // The story itself
        // ------------------------------------------------------------------------------------

        private void EnsureCampaign()
        {
            if (_campaignTried)
            {
                return;
            }

            _campaignTried = true;
            try
            {
                _campaign = StoryCampaign.Load(ReadStoryFile);

                // Mistakes in the writing are logged; they do not stop the story
                var problems = StoryCheck.Problems(_campaign);
                if (problems.Count > 0)
                {
                    YargLogger.LogWarning("Story: the story files have " + problems.Count + " problem(s)");
                    for (int i = 0; i < problems.Count && i < 30; i++)
                    {
                        YargLogger.LogWarning("Story: " + problems[i]);
                    }
                }

                YargLogger.LogInfo("Story: loaded \"" + _campaign.Title + "\" with " + _campaign.Chapters.Count +
                    " chapters and " + _campaign.AllGigs.Count + " gigs");
            }
            catch (Exception e)
            {
                // The screens then say "Story could not be loaded"
                _campaign = null;
                YargLogger.LogException(e, "Story: the story files could not be read");
            }
        }

        private static string ReadStoryFile(string name)
        {
            var asset = Resources.Load<TextAsset>("YargStory/" + name);
            return asset != null ? asset.text : null;
        }

        // ------------------------------------------------------------------------------------
        // Gigs
        // ------------------------------------------------------------------------------------

        /// <summary>
        /// Starts a gig with the given set: hides the story and opens the game's instrument and
        /// difficulty screen. When it returns false, <paramref name="problem"/> says why in plain
        /// words and nothing has changed. The result arrives later through the screens'
        /// ShowGigReturn and <see cref="GigReturned"/>.
        /// </summary>
        public bool StartGig(StoryGig gig, IReadOnlyList<StorySong> set, out string problem)
        {
            problem = null;
            try
            {
                if (Engine == null || _campaign == null)
                {
                    problem = "Pick a story first.";
                    return false;
                }

                if (gig == null || _campaign.FindGig(gig.Id) != gig)
                {
                    problem = "That gig could not be found.";
                    return false;
                }

                if (GigUnderWay)
                {
                    problem = "A gig is already under way.";
                    return false;
                }

                if (!GameReady)
                {
                    problem = "The game is still busy. Try again in a moment.";
                    return false;
                }

                if (Engine.StateOf(gig) == StoryGigState.Locked)
                {
                    problem = "This gig is not open yet.";
                    return false;
                }

                int needed = Math.Max(1, gig.Songs);
                if (set == null || set.Count == 0)
                {
                    problem = "Pick the songs for the set first.";
                    return false;
                }

                if (set.Count != needed)
                {
                    problem = needed == 1 ? "This gig is a set of one song." : $"This gig needs a set of {needed} songs.";
                    return false;
                }

                if (HumanPlayers == 0)
                {
                    problem = "Nobody is set up to play yet. Use Set up players to add a player first.";
                    return false;
                }

                // Songs are looked up again now: a rescan replaces every song object in the game
                StoryLibrary.Refresh();
                var songs = new SongEntry[set.Count];
                for (int i = 0; i < set.Count; i++)
                {
                    var song = set[i];
                    var entry = song != null ? StoryLibrary.Resolve(song.Id) : null;
                    if (entry == null)
                    {
                        string name = song != null && !string.IsNullOrEmpty(song.Name) ? $"\"{song.Name}\"" : "A song";
                        problem = name + " is no longer in your song library. Pick another song.";
                        return false;
                    }

                    songs[i] = entry;
                }

                // In the save before anything else, so a crash during the gig is noticed later
                Engine.Begin(gig, set);
                Save();

                // The story's controls come off before the game's menu changes
                IsOpen = false;
                _reopenAfterMenu = false;
                HideScreen();

                try
                {
                    _flow.Launch(Engine, gig, songs);
                }
                catch (Exception e)
                {
                    YargLogger.LogException(e, "Story: the game's song screen did not open");
                    if (!_flow.Busy)
                    {
                        // It never got going: put everything back as it was
                        GlobalVariables.State = PersistentState.Default;
                        Engine.Progress.Pending = null;
                        Save();
                        if (EnsureScreen() != null)
                        {
                            IsOpen = true;
                            _screen.Show();
                        }

                        problem = "The gig could not be started.";
                        return false;
                    }

                    // Otherwise the flow notices the song screen is not there and brings the story back
                }

                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not start a gig");
                problem = "The gig could not be started.";
                return false;
            }
        }

        // Called by the gig flow the moment the menu scene loads after a gig
        internal void CoverForReturn()
        {
            try
            {
                if (EnsureScreen() != null)
                {
                    _screen.Cover();
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not cover the menu");
            }
        }

        // Called by the gig flow once the menu is ready, with the save already updated
        internal void DeliverGigReturn(StoryGigReturn result)
        {
            try
            {
                RemoveStrayTouchControls();
                StoryLibrary.Refresh();

                if (EnsureScreen() == null)
                {
                    IsOpen = false;
                    ToastManager.ToastError("Story mode could not be opened.");
                }
                else
                {
                    IsOpen = true;
                    _screen.Show();
                    _screen.ShowGigReturn(result);
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not show how the gig went");
                IsOpen = false;
                HideScreen();
            }

            try
            {
                GigReturned?.Invoke(result);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: error in a GigReturned handler");
            }
        }

        // ------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------

        private StoryScreen EnsureScreen()
        {
            if (_screen != null)
            {
                return _screen;
            }

            try
            {
                _screen = StoryScreen.Create(transform);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: the story screens could not be built");
                _screen = null;
            }

            return _screen;
        }

        private void HideScreen()
        {
            try
            {
                if (_screen != null)
                {
                    _screen.Hide();
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not hide the story screens");
            }
        }

        // On some builds the on-screen touch buttons stay over the menus after the first song and
        // take the taps meant for the story. They are put back by the game when the next song loads.
        private void RemoveStrayTouchControls()
        {
            try
            {
                if (SceneManager.GetSceneByBuildIndex((int) SceneIndex.Gameplay).isLoaded)
                {
                    return;
                }

                var touch = FindAnyObjectByType<YARG.Input.Touch.TouchControlsManager>();
                if (touch != null)
                {
                    Destroy(touch.gameObject);
                }
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Story: could not remove the touch buttons");
            }
        }
    }
}
