// YARG Resurrected - automated play test for the desktop builds (Windows, Mac, Linux).
//
// Does nothing unless the game is started with  -yarg-smoke-test . CI starts each build that way
// on a real machine of its own system, before the build is published:
//
//   YARG.x86_64 -yarg-smoke-test -yarg-smoke-songs <folder> -yarg-smoke-out <folder>
//
// It then plays the game by itself, like a person would:
//   1. waits for the main menu (so the game started, its native libraries loaded and the
//      menus work) and closes the first-run notice, as "I understand" does,
//   2. joins a phone over the network, the way a friend's browser does: opens the phone page,
//      connects as a guitarist, checks the game made a player for it and that its whammy bar
//      reaches the game, looks at PLAY WITH PHONES, and leaves again,
//   3. scans <folder> for songs (song cache, chart reading),
//   4. adds a bot guitarist on Expert, opens the instrument screen and the Song Setup screen
//      on it (speed, key, section, freestyle),
//   5. starts the first song found with Guitar Sound On Notes switched on, so the guitar track
//      is generated on this system (audio: the song only moves forward when it is playing),
//   6. checks the song time moves on and the bot hits notes, with the computer's microphone
//      open and listening the way a singer's is (on a build machine it hears the song itself),
//   7. leaves the song for the menu, writes result.txt (PASS or FAIL and why) plus screenshots
//      (menu.png, phones.png, song-setup.png, gameplay.png) to <folder>, and quits with exit
//      code 0 (pass) or 1 (fail).
// If any step takes too long it fails with the step it was stuck on. Errors the game logged
// along the way are listed in result.txt.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using YARG.Audio.GuitarVoice;
using YARG.Audio.Vocals;
using YARG.Core;
using YARG.Core.Audio;
using YARG.Core.Game;
using YARG.Gameplay;
using YARG.Menu;
using YARG.Menu.DifficultySelect;
using YARG.Menu.Persistent;
using YARG.Menu.SongSetup;
using YARG.Player;
using YARG.Remote;
using YARG.Settings;
using YARG.Song;

namespace YARG.Testing
{
    public class SmokeTest : MonoBehaviour
    {
        private const string ARG = "-yarg-smoke-test";
        private const float TIMEOUT_SECONDS = 480f;
        private const double SONG_SECONDS_TO_PLAY = 20.0;
        private const int MIN_NOTES_HIT = 10;
        private const string PHONE_NAME = "Smoke Phone";

        private readonly StringBuilder _report = new();
        private readonly object _errorLock = new();
        private readonly List<string> _errors = new();
        private int _errorCount;
        private string _outDir;
        private string _step = "starting";
        private bool _finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Contains(ARG))
            {
                return;
            }

            var go = new GameObject("YARG Smoke Test");
            DontDestroyOnLoad(go);
            go.AddComponent<SmokeTest>();
        }

        private void Start()
        {
            _outDir = GetArg("-yarg-smoke-out") ?? Path.Combine(Application.persistentDataPath, "smoke-test");
            Directory.CreateDirectory(_outDir);
            Application.logMessageReceivedThreaded += OnLog;
            Note($"Platform: {Application.platform}, {SystemInfo.operatingSystem}, Unity {Application.unityVersion}");
            Note($"Graphics: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
            Run().Forget();
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }

        // Errors and exceptions the game logs while the test runs (any thread)
        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }

            lock (_errorLock)
            {
                _errorCount++;
                if (_errors.Count < 25)
                {
                    // (The game's log lines carry a link to the source file: keep only its text)
                    string line = System.Text.RegularExpressions.Regex.Replace((condition ?? "").Trim(),
                        "<a href=\"[^\"]*\"[^>]*>([^<]*)</a>", "$1");
                    int end = line.IndexOf('\n');
                    if (end > 0)
                    {
                        line = line.Substring(0, end);
                    }
                    if (line.Length > 300)
                    {
                        line = line.Substring(0, 300) + "...";
                    }
                    line = type + ": " + line;
                    if (!_errors.Contains(line))
                    {
                        _errors.Add(line);
                    }
                }
            }
        }

        private void Update()
        {
            if (!_finished && Time.realtimeSinceStartup > TIMEOUT_SECONDS)
            {
                Finish(false, $"timed out after {TIMEOUT_SECONDS:0} s while {_step}");
            }
        }

        private async UniTaskVoid Run()
        {
            try
            {
                // 1. Main menu
                _step = "waiting for the main menu";
                await UniTask.WaitUntil(() => SceneManager.GetSceneByName("MenuScene").isLoaded);
                await UniTask.Delay(TimeSpan.FromSeconds(5), ignoreTimeScale: true);
                Note($"Main menu reached after {Time.realtimeSinceStartup:0.0} s");

                // The first-run notice covers the menu until "I understand" is pressed
                _step = "closing the first-run notice";
                var dialogs = DialogManager.Instance;
                if (dialogs != null && dialogs.IsDialogShowing)
                {
                    dialogs.ClearDialog();
                    Note("Closed the first-run notice");
                    await UniTask.Delay(TimeSpan.FromSeconds(1), ignoreTimeScale: true);
                }
                await Screenshot("menu.png");

                // 2. A phone joins
                if (!await CheckPhones())
                {
                    return;
                }

                // 3. Songs
                _step = "scanning songs";
                string songs = GetArg("-yarg-smoke-songs");
                if (string.IsNullOrEmpty(songs) || !Directory.Exists(songs))
                {
                    Finish(false, $"song folder not found: '{songs}'");
                    return;
                }
                songs = Path.GetFullPath(songs);
                if (!SettingsManager.Settings.SongFolders.Contains(songs))
                {
                    SettingsManager.Settings.SongFolders.Add(songs);
                }
                using (var context = new LoadingContext())
                {
                    // Same call as Settings > Songs > Scan Songs
                    await SongContainer.RunRefresh(false, context);
                }
                Note($"Songs found: {SongContainer.Count}");
                var song = SongContainer.Songs.FirstOrDefault();
                if (song == null)
                {
                    Finish(false, "the song scan found no songs");
                    return;
                }
                Note($"Playing: {song.Name.Original} by {song.Artist.Original}");

                // 4. A bot guitarist
                _step = "adding a bot player";
                // (-yarg-smoke-vocals: a singer instead, for looking at the lyrics; see below)
                bool vocalsLook = Environment.GetCommandLineArgs().Contains("-yarg-smoke-vocals");
                // (-yarg-smoke-late N: a person instead of the bot, whose controller is N ms late;
                // see PlayLate)
                int lateMs = int.TryParse(GetArg("-yarg-smoke-late"), out int lateArg) ? lateArg : int.MinValue;
                bool latePlayer = lateMs != int.MinValue && !vocalsLook;
                if (latePlayer)
                {
                    // (The first notes are all missed: that must not end the song)
                    SettingsManager.Settings.NoFail.SetValueWithoutNotify(YARG.Gameplay.HUD.NoFailMode.On);
                    SettingsManager.Settings.AutoTiming.SetValueWithoutNotify(true);
                }
                if (Environment.GetCommandLineArgs().Contains("-yarg-smoke-static"))
                {
                    SettingsManager.Settings.StaticVocalsMode.SetValueWithoutNotify(true);
                }
                var profile = new YargProfile
                {
                    Name = latePlayer ? "Late Player" : "Smoke Test Bot",
                    IsBot = !latePlayer,
                    GameMode = vocalsLook ? GameMode.Vocals : GameMode.FiveFretGuitar,
                    CurrentInstrument = vocalsLook ? Instrument.Vocals : Instrument.FiveFretGuitar,
                    CurrentDifficulty = Difficulty.Expert,
                };
                PlayerContainer.AddProfile(profile);
                if (PlayerContainer.CreatePlayerFromProfile(profile, false) == null)
                {
                    Finish(false, "couldn't create a player from the bot profile");
                    return;
                }

                GlobalVariables.State = PersistentState.Default;
                GlobalVariables.State.CurrentSong = song;
                GlobalVariables.State.SongSpeed = 1f;

                // Tidy names, and a song's verses dealt out between its artists
                if (!CheckNamesAndVerses())
                {
                    return;
                }

                // (-yarg-smoke-artist N: the singer is the song's Nth credited artist)
                int artistLook = int.TryParse(GetArg("-yarg-smoke-artist"), out int artistArg) ? artistArg : -1;
                if (vocalsLook && artistLook >= 0)
                {
                    YARG.Gameplay.VocalBattle.VocalParts.SetGuessedArtist(profile, song, artistLook);
                }
                _vocalsLook = vocalsLook;

                // (-yarg-smoke-battle: a second singer, head to head. With -yarg-smoke-artist they
                // are the song's first artist; without, the two trade lines.)
                bool battleLook = vocalsLook && Environment.GetCommandLineArgs().Contains("-yarg-smoke-battle");
                SettingsManager.Settings.SingOff.SetValueWithoutNotify(battleLook);
                if (battleLook)
                {
                    var second = new YargProfile
                    {
                        Name = "Second Singer",
                        IsBot = true,
                        GameMode = GameMode.Vocals,
                        CurrentInstrument = Instrument.Vocals,
                        CurrentDifficulty = Difficulty.Expert,
                    };
                    PlayerContainer.AddProfile(second);
                    if (PlayerContainer.CreatePlayerFromProfile(second, false) == null)
                    {
                        Finish(false, "couldn't create the second singer");
                        return;
                    }
                    if (artistLook >= 0)
                    {
                        YARG.Gameplay.VocalBattle.VocalParts.SetGuessedArtist(second, song, 0);
                    }
                }

                // Guitar controllers: the forgiving engine, the sound preview, and the profile
                // menus worked the way a controller works them (up, down and confirm)
                if (!await CheckControllerMenus())
                {
                    return;
                }

                // The instrument screen, and Song Setup on it (once the messages about the
                // phone have gone from the corner of the screen)
                await UniTask.Delay(TimeSpan.FromSeconds(6), ignoreTimeScale: true);
                if (!await CheckSongSetup())
                {
                    return;
                }

                // 5. Start the song, with the generated guitar track switched on (the test song
                // is a single recording, so the game has to make the guitar's track itself)
                _step = "loading the song";
                // (-yarg-smoke-no-voice, -yarg-smoke-no-venue: with those left out, to see what each costs)
                bool noVoice = Environment.GetCommandLineArgs().Contains("-yarg-smoke-no-voice");
                if (Environment.GetCommandLineArgs().Contains("-yarg-smoke-no-venue"))
                {
                    SettingsManager.Settings.DisableGlobalBackgrounds.SetValueWithoutNotify(true);
                    SettingsManager.Settings.VenueCrowd.SetValueWithoutNotify(false);
                }
                SettingsManager.Settings.GuitarVoice.SetValueWithoutNotify(noVoice ? GuitarVoiceMode.Off : GuitarVoiceMode.Rock);
                GuitarVoiceLoader.LastTracksAdded = -1;
                GlobalVariables.State.CurrentSong = song;
                GlobalVariables.State.SongSpeed = 1f;
                GlobalVariables.Instance.LoadScene(SceneIndex.Gameplay);

                GameManager game = null;
                await UniTask.WaitUntil(() =>
                {
                    game ??= FindFirstObjectByType<GameManager>();
                    return game != null && game.IsSongStarted;
                });
                Note($"Song started after {Time.realtimeSinceStartup:0.0} s");

                if (vocalsLook && artistLook >= 0)
                {
                    var battle = typeof(YARG.Gameplay.VocalBattle.SingOff);
                    if (!YARG.Gameplay.VocalBattle.SingOff.GuessedVerses)
                    {
                        Finish(false, "the singer chose an artist, but the song's verses were not dealt out");
                        return;
                    }
                    var dealt = YARG.Gameplay.VocalBattle.SingOff.TrackOr(null);
                    var shares = dealt.Parts.Select(p => p.NotePhrases.Count(x => x.PhraseParentNote.ChildNotes.Any(n => !n.IsPercussion)));
                    Note($"Vocal Challenge: verses dealt out between {string.Join(", ", YARG.Gameplay.VocalBattle.SingOff.GuessedNames)}" +
                        $" (lines each: {string.Join(", ", shares)}); the singer has part {YARG.Gameplay.VocalBattle.SingOff.PartIndex(PlayerContainer.Players[0]) + 1}");
                    if (shares.Any(x => x == 0))
                    {
                        Finish(false, "an artist was dealt no lines");
                        return;
                    }
                }

                if (battleLook)
                {
                    if (!YARG.Gameplay.VocalBattle.SingOff.Active)
                    {
                        Finish(false, "two singers head to head, but no battle was set up");
                        return;
                    }
                    var sides = YARG.Gameplay.VocalBattle.SingOff.Sides;
                    Note($"Vocal Challenge battle: {sides[0].Name} against {sides[1].Name}, " +
                        (YARG.Gameplay.VocalBattle.SingOff.SplitLines ? "trading lines" : "artist against artist"));
                }

                if (vocalsLook)
                {
                    // Not a test with a verdict: pictures of the vocals at a few moments, to look at
                    double from = game.SongTime;
                    float began = Time.realtimeSinceStartup;
                    int shots = 0;
                    while (game != null && shots < 6 && Time.realtimeSinceStartup - began < 240f)
                    {
                        if (game.SongTime - from > 8 + shots * 3.5)
                        {
                            shots++;
                            await Screenshot($"vocals-{shots}.png");
                            Note($"Vocals picture {shots} at song time {game.SongTime:0.00} s");
                        }
                        await UniTask.NextFrame();
                    }
                    if (battleLook)
                    {
                        var sides = YARG.Gameplay.VocalBattle.SingOff.Sides;
                        Note($"Battle so far: {sides[0].Name} {sides[0].Lines} lines marked, {sides[1].Name} {sides[1].Lines} lines marked");
                        if (sides[0].Lines + sides[1].Lines == 0)
                        {
                            Finish(false, "no line of the battle was marked");
                            return;
                        }
                    }
                    Finish(true, "pictures of the vocals taken");
                    return;
                }

                if (latePlayer)
                {
                    await PlayLate(game, profile, lateMs);
                    return;
                }

                int tracks = GuitarVoiceLoader.LastTracksAdded;
                if (tracks < 1 && !noVoice)
                {
                    Finish(false, tracks < 0
                        ? "Guitar Sound On Notes never ran for the song"
                        : "Guitar Sound On Notes made no guitar track for the song");
                    return;
                }
                Note($"Guitar Sound On Notes: {tracks} generated track");

                // 6. Play, with the computer's microphone listening (on a build machine it hears
                // the song itself, through the virtual sound card)
                _step = "playing the song";
                var mic = OpenMicrophone(out string micName);
                int micReadings = 0, micNotes = 0;
                double startTime = game.SongTime;
                int startFrame = Time.frameCount;
                float startReal = Time.realtimeSinceStartup;
                bool shot = false;
                // Smoothness: memory the game makes while playing (what the garbage collector
                // then has to clear away, which is a hitch on a phone) and the longest frames
                long madeBytes = 0, lastUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                int collectionsBefore = GC.CollectionCount(0);
                var frameTimes = new List<float>();
                while (game != null && game.SongTime - startTime < SONG_SECONDS_TO_PLAY)
                {
                    long used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                    if (used > lastUsed) madeBytes += used - lastUsed;
                    lastUsed = used;
                    frameTimes.Add(Time.unscaledDeltaTime);
                    while (mic != null && mic.DequeueOutputFrame(out var frame))
                    {
                        micReadings++;
                        if (!frame.IsHit && frame.Pitch > 0f)
                        {
                            micNotes++;
                        }
                    }
                    if (SceneManager.GetSceneByName("MenuScene").isLoaded)
                    {
                        Finish(false, "the song stopped and the game went back to the menu");
                        return;
                    }
                    if (!shot && game.SongTime - startTime > SONG_SECONDS_TO_PLAY / 2)
                    {
                        shot = true;
                        await Screenshot("gameplay.png");
                    }
                    if (Time.realtimeSinceStartup - startReal > 120f)
                    {
                        Finish(false, $"song time only reached {game.SongTime:0.0} s in 120 s (audio not playing?)");
                        return;
                    }
                    await UniTask.NextFrame();
                }

                if (mic != null)
                {
                    mic.IsRecordingOutput = false;
                    mic.Dispose();
                    Note($"Microphone: '{micName}' listened to the song: {micReadings} readings, {micNotes} of them a note");
                }

                if (game == null)
                {
                    Finish(false, "the gameplay screen closed during the song");
                    return;
                }

                float seconds = Time.realtimeSinceStartup - startReal;
                Note($"Played {game.SongTime - startTime:0.0} s of the song in {seconds:0.0} s, " +
                    $"{(Time.frameCount - startFrame) / Math.Max(seconds, 0.001f):0} fps");
                // The volume normalizer reads the song once and stops (it used to run for the whole song)
                if (SettingsManager.Settings.EnableNormalization.Value)
                {
                    bool analysing = YARG.Audio.BASS.BassNormalizer.Analysing;
                    Note($"Volume normalizer: read {YARG.Audio.BASS.BassNormalizer.LastWindowsRead / 10.0:0} s of the song, " +
                        (analysing ? "still running" : "finished"));
                    if (analysing)
                    {
                        Finish(false, "the volume normalizer was still running 20 seconds into the song");
                        return;
                    }
                }

                if (frameTimes.Count > 10)
                {
                    var ordered = frameTimes.OrderBy(x => x).ToList();
                    float middle = ordered[ordered.Count / 2];
                    Note($"Smoothness: {madeBytes / 1024.0 / Math.Max(seconds, 0.001f):0} KB of memory made per second " +
                        $"({madeBytes / 1024.0 / frameTimes.Count:0.0} KB a frame), {GC.CollectionCount(0) - collectionsBefore} garbage collections; " +
                        $"frames: middle {middle * 1000:0} ms, longest {ordered[ordered.Count - 1] * 1000:0} ms, " +
                        $"{ordered.Count(x => x > middle * 2.5f)} of {ordered.Count} more than 2.5 times the middle");
                }

                int hit = game.Players.Sum(p => p.BaseStats.NotesHit);
                int total = game.Players.Sum(p => p.BaseStats.TotalNotes);
                Note($"Bot hit {hit} notes (chart has {total}), score {game.Players.Sum(p => p.Score)}");
                if (hit < MIN_NOTES_HIT)
                {
                    Finish(false, $"the bot only hit {hit} notes");
                    return;
                }

                // 7. Leave the song the way Quit in the pause menu does, and close the game from
                // the menu (closing it in the middle of a song is a different, rougher path)
                _step = "leaving the song";
                game.ForceQuitSong();
                float leaveBy = Time.realtimeSinceStartup + 45f;
                while (!SceneManager.GetSceneByName("MenuScene").isLoaded && Time.realtimeSinceStartup < leaveBy)
                {
                    await UniTask.NextFrame();
                }
                if (!SceneManager.GetSceneByName("MenuScene").isLoaded)
                {
                    Finish(false, "the game did not get back to the menu after leaving the song");
                    return;
                }
                await UniTask.Delay(TimeSpan.FromSeconds(2), ignoreTimeScale: true);
                Note("Back at the menu after leaving the song");

                Finish(true, "menu, phone join, song scan, Song Setup, guitar sound, song start, audio and gameplay all work");
            }
            catch (Exception e)
            {
                Finish(false, $"exception while {_step}: {e}");
            }
        }

        #region A phone joins over the network

        // What a friend's phone does, from this same machine: open the page, join as a
        // guitarist, move the whammy bar, leave.
        private async UniTask<bool> CheckPhones()
        {
            _step = "waiting for the phone server";
            float deadline = Time.realtimeSinceStartup + 20f;
            while (RemotePlayManager.ServerPort == 0 && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.NextFrame();
            }
            int port = RemotePlayManager.ServerPort;
            if (port == 0)
            {
                Finish(false, "the phone server (phones as guitars and mics) did not start");
                return false;
            }

            _step = "opening the phone page";
            string health = null, page = null;
            await UniTask.RunOnThreadPool(() =>
            {
                health = TestPhone.HttpGet(port, "/health");
                page = TestPhone.HttpGet(port, "/");
            });
            if (health != "ok" || page == null || !page.Contains("<title>YARG Remote</title>"))
            {
                Finish(false, $"the phone page was not served on port {port} (health '{health}', " +
                    $"page {(page == null ? "missing" : page.Length + " characters")})");
                return false;
            }
            Note($"Phone page served on port {port} ({page.Length} characters)");

            // The secure link (https) is what lets a phone's browser use its microphone
            _step = "testing the phone page's secure link";
            deadline = Time.realtimeSinceStartup + 20f;
            while (RemotePlayManager.SecureLinkState == 0 && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.NextFrame();
            }
            if (RemotePlayManager.SecureLinkState != 1)
            {
                Finish(false, "the phone page's secure link (https, needed for phone mics) does not work on this system");
                return false;
            }
            Note("Phone page's secure link (https) works");

            _step = "joining a phone";
            TestPhone phone = null;
            string welcome = null;
            await UniTask.RunOnThreadPool(() =>
            {
                phone = new TestPhone(port);
                phone.Upgrade();
                phone.SendText("hello\nsmoke-test-phone\n" + PHONE_NAME);
                welcome = phone.ReceiveText();
            });

            try
            {
                if (welcome == null || !welcome.StartsWith("welcome\n"))
                {
                    Finish(false, "the phone was not welcomed: '" + welcome + "'");
                    return false;
                }

                // The phone picks guitar on Expert: the game makes a player for it
                await UniTask.RunOnThreadPool(() => phone.SendText("role\nguitar\nexpert"));

                YargPlayer phonePlayer = null;
                Gamepad pad = null;
                deadline = Time.realtimeSinceStartup + 15f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    pad ??= InputSystem.devices.OfType<Gamepad>().FirstOrDefault(g => RemotePlayManager.IsRemoteDevice(g));
                    phonePlayer = PlayerContainer.Players.FirstOrDefault(p => p.Profile.Name == PHONE_NAME);
                    if (pad != null && phonePlayer != null && phonePlayer.Bindings.ContainsDevice(pad))
                    {
                        break;
                    }
                    await UniTask.NextFrame();
                }
                if (pad == null || phonePlayer == null || !phonePlayer.Bindings.ContainsDevice(pad))
                {
                    Finish(false, $"the phone joined but got no player (controller {(pad == null ? "missing" : "there")}, " +
                        $"player {(phonePlayer == null ? "missing" : "there")})");
                    return false;
                }
                Note($"Phone joined: player '{phonePlayer.Profile.Name}' on {phonePlayer.Profile.CurrentInstrument} " +
                    $"{phonePlayer.Profile.CurrentDifficulty}, controller '{pad.displayName}'");

                // Whammy bar fully pressed, no buttons (a button would work the menu):
                // [1][buttons u32][whammy u8][touch time f64, 0 = not synced]
                // The game takes no controller input while its window is in the background (the
                // same for every controller), so this is only checked with the window in front.
                deadline = Time.realtimeSinceStartup + 5f;
                while (!Application.isFocused && Time.realtimeSinceStartup < deadline)
                {
                    await UniTask.NextFrame();
                }
                if (Application.isFocused)
                {
                    var packet = new byte[14];
                    packet[0] = 1;
                    packet[5] = 255;
                    await UniTask.RunOnThreadPool(() => phone.SendBinary(packet));

                    bool moved = false;
                    deadline = Time.realtimeSinceStartup + 5f;
                    while (Time.realtimeSinceStartup < deadline)
                    {
                        if (pad.added && pad.rightStick.ReadValue().x > 0.5f)
                        {
                            moved = true;
                            break;
                        }
                        await UniTask.NextFrame();
                    }
                    if (!moved)
                    {
                        Finish(false, "the phone's whammy bar did not reach the game");
                        return false;
                    }
                    Note("Phone's whammy bar reaches the game");
                }
                else
                {
                    Note("Phone's whammy bar: not checked (the game's window is in the background, where it takes no controller input)");
                }

                // PLAY WITH PHONES (the join code screen)
                _step = "opening PLAY WITH PHONES";
                RemotePlayManager.OpenPhones();
                await UniTask.Delay(TimeSpan.FromSeconds(1.5), ignoreTimeScale: true);
                if (!RemotePlayManager.PhonesOpen)
                {
                    Finish(false, "PLAY WITH PHONES did not open");
                    return false;
                }
                await Screenshot("phones.png");
                RemotePlayManager.ClosePhones();
            }
            finally
            {
                phone?.Dispose();
            }

            // The phone leaves, and takes its player with it
            _step = "waiting for the phone to leave";
            deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline &&
                (RemotePlayManager.PhoneCount > 0 || PlayerContainer.Players.Any(p => p.Profile.Name == PHONE_NAME)))
            {
                await UniTask.NextFrame();
            }
            if (RemotePlayManager.PhoneCount > 0 || PlayerContainer.Players.Any(p => p.Profile.Name == PHONE_NAME))
            {
                Finish(false, "the phone left but its player stayed");
                return false;
            }
            Note("Phone left, and its player with it");
            return true;
        }

        /// <summary>
        /// The least a phone's browser does: plain http and a WebSocket, over a socket.
        /// (Blocking calls with timeouts: only used from worker threads.)
        /// </summary>
        private sealed class TestPhone : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly NetworkStream _stream;

            public TestPhone(int port)
            {
                _tcp = Connect(port);
                _stream = _tcp.GetStream();
            }

            private static TcpClient Connect(int port)
            {
                var tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 8000, SendTimeout = 8000 };
                tcp.Connect("127.0.0.1", port);
                return tcp;
            }

            /// <summary>The body of a page, or null if it is not there.</summary>
            public static string HttpGet(int port, string path)
            {
                try
                {
                    using (var tcp = Connect(port))
                    using (var stream = tcp.GetStream())
                    {
                        var request = Encoding.ASCII.GetBytes(
                            "GET " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
                        stream.Write(request, 0, request.Length);

                        var all = new MemoryStream();
                        var buffer = new byte[16384];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            all.Write(buffer, 0, read);
                        }

                        string text = Encoding.UTF8.GetString(all.ToArray());
                        int body = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                        if (body < 0 || !text.StartsWith("HTTP/1.1 200", StringComparison.Ordinal))
                        {
                            return null;
                        }
                        return text.Substring(body + 4);
                    }
                }
                catch (Exception e)
                {
                    Debug.Log("[YARG Smoke Test] " + path + " could not be read: " + e.Message);
                    return null;
                }
            }

            /// <summary>Turns the connection into a WebSocket.</summary>
            public void Upgrade()
            {
                var request = Encoding.ASCII.GetBytes(
                    "GET /ws HTTP/1.1\r\nHost: 127.0.0.1\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                    "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n\r\n");
                _stream.Write(request, 0, request.Length);

                // Read the reply's headers, one byte at a time, up to the empty line
                var head = new StringBuilder();
                var one = new byte[1];
                while (head.Length < 4096)
                {
                    if (_stream.Read(one, 0, 1) <= 0)
                    {
                        break;
                    }
                    head.Append((char) one[0]);
                    int n = head.Length;
                    if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n')
                    {
                        break;
                    }
                }

                if (!head.ToString().StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
                {
                    throw new IOException("the phone server refused the connection: " + head.ToString().Trim());
                }
            }

            public void SendText(string text)
            {
                SendFrame(0x1, Encoding.UTF8.GetBytes(text));
            }

            public void SendBinary(byte[] data)
            {
                SendFrame(0x2, data);
            }

            // One whole frame, masked as a browser's are (the lengths used here are under 126)
            private void SendFrame(int opcode, byte[] payload)
            {
                if (payload.Length >= 126)
                {
                    throw new ArgumentException("message too long for the test phone");
                }

                var mask = new byte[] { 0x3A, 0x91, 0x5C, 0xE7 };
                var frame = new byte[6 + payload.Length];
                frame[0] = (byte) (0x80 | opcode);
                frame[1] = (byte) (0x80 | payload.Length);
                Array.Copy(mask, 0, frame, 2, 4);
                for (int i = 0; i < payload.Length; i++)
                {
                    frame[6 + i] = (byte) (payload[i] ^ mask[i & 3]);
                }
                _stream.Write(frame, 0, frame.Length);
            }

            /// <summary>The next text message from the game.</summary>
            public string ReceiveText()
            {
                var header = new byte[8];
                for (int tries = 0; tries < 50; tries++)
                {
                    ReadExactly(header, 2);
                    int opcode = header[0] & 0x0F;
                    long length = header[1] & 0x7F;
                    if (length == 126)
                    {
                        ReadExactly(header, 2);
                        length = (header[0] << 8) | header[1];
                    }
                    else if (length == 127)
                    {
                        ReadExactly(header, 8);
                        length = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            length = (length << 8) | header[i];
                        }
                    }
                    if (length < 0 || length > 1 << 20)
                    {
                        throw new IOException("message too long from the phone server");
                    }

                    var payload = new byte[(int) length];
                    ReadExactly(payload, payload.Length);
                    if (opcode == 0x1)
                    {
                        return Encoding.UTF8.GetString(payload);
                    }
                    if (opcode == 0x8)
                    {
                        return null;
                    }
                }
                return null;
            }

            private void ReadExactly(byte[] buffer, int count)
            {
                int done = 0;
                while (done < count)
                {
                    int read = _stream.Read(buffer, done, count - done);
                    if (read <= 0)
                    {
                        throw new IOException("the phone server closed the connection");
                    }
                    done += read;
                }
            }

            public void Dispose()
            {
                try
                {
                    _tcp.Close();
                }
                catch (Exception)
                {
                    // already closed
                }
            }
        }

        #endregion

        #region The microphone

        // Opens a microphone the way a singer's profile does, listening the way a song (and the
        // mic test) does. What it hears is noted, not judged: a build machine may have no
        // microphone, or a silent one. Null if there is none to open.
        private MicDevice OpenMicrophone(out string name)
        {
            name = null;
            List<(int id, string name)> devices;
            try
            {
                devices = GlobalAudioHandler.GetAllInputDevices();
            }
            catch (Exception e)
            {
                Note("Microphone: the list of microphones could not be read: " + e.Message);
                return null;
            }

            if (devices == null || devices.Count == 0)
            {
                Note("Microphone: none on this machine (not checked)");
                return null;
            }

            // A build machine's virtual sound card first (it plays the game's own sound back as a
            // microphone, so there is something to hear), then the system's default microphone.
            // Some of what a system lists cannot record (on Linux, sound plug-ins are listed
            // next to the real devices), so the next one is tried when one will not open.
            static int Rank(string device)
            {
                string[] wanted = { "blackhole", "background music", "cable output", "monitor", "loopback", "default", "pulse" };
                for (int i = 0; i < wanted.Length; i++)
                {
                    if (device != null && device.IndexOf(wanted[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
                return wanted.Length;
            }

            var candidates = devices.OrderBy(d => Rank(d.name)).Take(6).ToList();
            Note($"Microphones listed: {devices.Count} (" + string.Join(", ", candidates.Select(d => "'" + d.name + "'")) + ")");

            foreach (var candidate in candidates)
            {
                MicDevice mic = null;
                try
                {
                    mic = GlobalAudioHandler.CreateInputDevice(candidate.id, candidate.name);
                }
                catch (Exception e)
                {
                    Note($"Microphone: '{candidate.name}' could not be opened: {e.Message}");
                }

                if (mic == null)
                {
                    continue;
                }

                try
                {
                    (mic as IVoiceMic)?.BeginSong(false);
                    mic.ClearOutputQueue();
                    mic.IsRecordingOutput = true;
                }
                catch (Exception e)
                {
                    Note($"Microphone: '{candidate.name}' opened but could not listen: {e.Message}");
                    mic.Dispose();
                    return null;
                }

                name = candidate.name;
                return mic;
            }

            Note("Microphone: none of those listed could be opened (not checked)");
            return null;
        }

        #endregion

        #region Song Setup

        // The instrument / difficulty screen that follows picking a song, and Song Setup on it
        private async UniTask<bool> CheckSongSetup()
        {
            _step = "opening the instrument screen";
            MenuManager.Instance.PushMenu(MenuManager.Menu.DifficultySelect);

            float deadline = Time.realtimeSinceStartup + 15f;
            while (!SongSetupPanel.CanOpen && Time.realtimeSinceStartup < deadline)
            {
                await UniTask.NextFrame();
            }
            if (DifficultySelectMenu.Current == null)
            {
                Finish(false, "the instrument screen did not open");
                return false;
            }
            if (!SongSetupPanel.CanOpen)
            {
                Finish(false, "Song Setup is not available on the instrument screen");
                return false;
            }

            _step = "opening Song Setup";
            SongSetupPanel.OpenFromMenu();
            // (Long enough for the song's preview to load)
            await UniTask.Delay(TimeSpan.FromSeconds(2.5), ignoreTimeScale: true);
            if (!SongSetupPanel.IsOpen)
            {
                Finish(false, "Song Setup did not open");
                return false;
            }
            await Screenshot("song-setup.png");
            Note("Song Setup opened on the instrument screen");

            SongSetupPanel.CloseFromMenu();
            await UniTask.NextFrame();

            // A singer: the Vocal Challenge list
            if (_vocalsLook && DifficultySelectMenu.Current != null)
            {
                _step = "opening Vocal Challenge";
                string shown = DifficultySelectMenu.Current.OpenVocalChallenge();
                await UniTask.Delay(TimeSpan.FromSeconds(1.0), ignoreTimeScale: true);
                await Screenshot("vocal-challenge.png");
                Note($"Vocal Challenge row shows: {shown.Replace("\n", " / ")}");
            }

            if (DifficultySelectMenu.Current != null)
            {
                MenuManager.Instance.PopMenu();
            }
            await UniTask.Delay(TimeSpan.FromSeconds(0.5), ignoreTimeScale: true);
            return true;
        }

        #endregion

        private async UniTask Press(YARG.Core.Input.MenuAction action, double wait = 0.35)
        {
            // The same call a controller's menu button ends in
            YARG.Input.InputManager.OnMenuAction(action, true);
            await UniTask.NextFrame();
            YARG.Input.InputManager.OnMenuAction(action, false);
            await UniTask.Delay(TimeSpan.FromSeconds(wait), ignoreTimeScale: true);
            await UniTask.NextFrame();
        }

        private bool _vocalsLook;

        /// <summary>
        /// Plays the song as a person would whose controller is <paramref name="lateMs"/> late:
        /// every note fretted and strummed at the right moment, reaching the game that much after.
        /// Automatic timing should find the delay within a few dozen notes, after which the notes
        /// are hit.
        /// </summary>
        private async UniTask PlayLate(GameManager game, YargProfile profile, int lateMs)
        {
            _step = "playing with a late controller";
            var guitar = game.Players.OfType<YARG.Gameplay.Player.FiveFretGuitarPlayer>().FirstOrDefault();
            if (guitar == null)
            {
                Finish(false, "no guitar player in the song");
                return;
            }

            var notes = guitar.NoteTrack.Notes;
            double late = lateMs / 1000.0;
            int next = 0;
            await UniTask.Yield(PlayerLoopTiming.EarlyUpdate);
            while (next < notes.Count && notes[next].Time + late < game.InputTime + 0.5) next++;
            int first = next;

            var frets = new[]
            {
                YARG.Core.Input.GuitarAction.GreenFret, YARG.Core.Input.GuitarAction.RedFret,
                YARG.Core.Input.GuitarAction.YellowFret, YARG.Core.Input.GuitarAction.BlueFret,
                YARG.Core.Input.GuitarAction.OrangeFret,
            };
            int held = 0;
            double strumUntil = double.MaxValue;
            var hitAt = new List<bool>();
            var calibrationAt = new List<long>();
            float began = Time.realtimeSinceStartup;

            // The song's time right now, on the clock controllers are stamped with. Inputs are sent
            // at the very start of a frame, before the game moves on to this time, the way a real
            // controller's inputs arrive.
            double now = 0;
            void Send(double songTime, YARG.Core.Input.GuitarAction action, bool pressed)
            {
                // The moment on the controller's clock that is this moment of the song
                double stamp = YARG.Input.InputManager.CurrentInputTime - (now - songTime);
                guitar.InjectInput(new YARG.Core.Input.GameInput(stamp, (int) action, pressed));
            }

            while (game != null && next < notes.Count && Time.realtimeSinceStartup - began < 200f)
            {
                if (SceneManager.GetSceneByName("MenuScene").isLoaded)
                {
                    break;
                }

                now = game.GetRelativeInputTime(YARG.Input.InputManager.CurrentInputTime);
                if (now >= strumUntil)
                {
                    Send(strumUntil, YARG.Core.Input.GuitarAction.StrumDown, false);
                    strumUntil = double.MaxValue;
                }

                while (next < notes.Count && notes[next].Time + late <= now)
                {
                    var note = notes[next];
                    double at = note.Time + late;
                    if (strumUntil != double.MaxValue)
                    {
                        Send(at - 0.012, YARG.Core.Input.GuitarAction.StrumDown, false);
                    }

                    int want = note.NoteMask & 31;
                    for (int f = 0; f < 5; f++)
                    {
                        bool isHeld = (held & (1 << f)) != 0, wanted = (want & (1 << f)) != 0;
                        if (isHeld != wanted)
                        {
                            Send(at - 0.008, frets[f], wanted);
                        }
                    }
                    held = want;
                    Send(at, YARG.Core.Input.GuitarAction.StrumDown, true);
                    strumUntil = at + 0.04;
                    next++;
                    calibrationAt.Add(profile.InputCalibrationMilliseconds);
                }

                await UniTask.Yield(PlayerLoopTiming.EarlyUpdate);
            }

            await UniTask.Delay(TimeSpan.FromSeconds(1.0), ignoreTimeScale: true);
            if (game == null || SceneManager.GetSceneByName("MenuScene").isLoaded)
            {
                Finish(false, "the song stopped while the late controller was playing");
                return;
            }

            int played = next - first;
            int lastCount = Math.Min(20, played);
            int lastHit = 0;
            for (int i = next - lastCount; i < next; i++)
            {
                if (notes[i].WasHit) lastHit++;
            }
            int firstHit = 0, firstCount = Math.Min(8, played);
            for (int i = first; i < first + firstCount; i++)
            {
                if (notes[i].WasHit) firstHit++;
            }

            long found = profile.InputCalibrationMilliseconds;
            Note($"Late controller ({lateMs} ms): {played} notes played; of the first {firstCount}, {firstHit} hit; of the last {lastCount}, {lastHit} hit");
            Note($"Automatic timing set the profile's input calibration to {found} ms (it was 0); as the notes went by: " +
                string.Join(" ", calibrationAt.Where((_, i) => i % 8 == 0)));
            await Screenshot("late-player.png");

            if (played < 40)
            {
                Finish(false, $"only {played} notes could be played");
                return;
            }
            if (Math.Abs(found - lateMs) > 25)
            {
                Finish(false, $"automatic timing found {found} ms for a controller {lateMs} ms late");
                return;
            }
            if (lastHit < lastCount - 2)
            {
                Finish(false, $"with the timing found, only {lastHit} of the last {lastCount} notes were hit");
                return;
            }
            Finish(true, $"a controller {lateMs} ms late was found ({found} ms) and its notes hit");
        }

        private bool CheckNamesAndVerses()
        {
            _step = "tidy song names";
            var names = new (string Name, string Artist, string WantName, string WantArtist)[]
            {
                ("Rio_Da_Yung_Og__Veeze_-_Superstar_Official_Visualizer", "Unknown Artist", "Superstar", "Rio Da Yung Og & Veeze"),
                ("Gee (Official Video) [HD]", "Girls' Generation", "Gee", "Girls' Generation"),
                ("Get Throwed (Lyrics) Ft. Pimp C, Jeezy & Z-Ro", "Bun B", "Get Throwed feat. Pimp C, Jeezy & Z-Ro", "Bun B"),
                ("Veeze - guillermoandrade [Official Music Video]", "Veeze", "guillermoandrade", "Veeze"),
                // What tells two versions apart stays, and so does a name that only looks like junk
                ("No Scrubs (Live)", "TLC", "No Scrubs (Live)", "TLC"),
                ("Video Killed the Radio Star", "The Buggles", "Video Killed the Radio Star", "The Buggles"),
            };
            foreach (var (name, artist, wantName, wantArtist) in names)
            {
                YARG.Core.Song.SongNames.Clean(name, artist, out string gotName, out string gotArtist);
                if (gotName != wantName || gotArtist != wantArtist)
                {
                    Finish(false, $"\"{name}\" by \"{artist}\" was tidied to \"{gotName}\" by \"{gotArtist}\"");
                    return false;
                }
            }

            string credits = string.Join(" | ", YARG.Core.Song.SongNames.Credits("Bun B", "Get Throwed feat. Pimp C, Jeezy & Z-Ro"));
            string band = string.Join(" | ", YARG.Core.Song.SongNames.Credits("Florence and the Machine", "Dog Days Are Over"));
            if (credits != "Bun B | Pimp C | Jeezy" || band != "Florence and the Machine")
            {
                Finish(false, $"credited artists read wrong: \"{credits}\", \"{band}\"");
                return false;
            }
            Note($"Names tidied ({names.Length} checked); credited artists read: {credits}");

            // 40 lines of 2 s, 2 s bars: a pause before line 10, then 30 lines (60 s = 30 bars) with none
            _step = "dealing verses";
            var starts = new List<double>();
            var ends = new List<double>();
            var sung = new List<bool>();
            var bars = new List<double>();
            for (int i = 0; i < 60; i++) bars.Add(i * 2.0);
            for (int i = 0; i < 40; i++)
            {
                double at = i * 2.0 + (i >= 10 ? 6.0 : 0.0);
                starts.Add(at);
                ends.Add(at + 1.8);
                sung.Add(true);
            }
            var verses = YARG.Gameplay.VocalBattle.VerseDealer.Verses(starts, ends, sung, bars);
            if (verses[0] != 0 || verses[9] != 0 || verses[10] != 1 || verses[25] != 1 || verses[26] != 2 || verses[39] != 2)
            {
                Finish(false, "verses cut wrong: " + string.Join(",", verses));
                return false;
            }
            Note("Verses cut at a pause and after 16 bars");
            return true;
        }

        private async UniTask<bool> CheckControllerMenus()
        {
            var green = YARG.Core.Input.MenuAction.Green;
            var up = YARG.Core.Input.MenuAction.Up;
            var down = YARG.Core.Input.MenuAction.Down;

            // The engine guitar controllers are given
            _step = "the Relaxed engine";
            var relaxed = YARG.Settings.Customization.CustomContentManager.EnginePresets.GetPresetById(EnginePreset.Relaxed.Id);
            if (relaxed == null || relaxed.FiveFretGuitar.HitWindow.MaxWindow < 0.19 || relaxed.FiveFretGuitar.AntiGhosting)
            {
                Finish(false, "the Relaxed engine is not among the engines, or is not the forgiving one");
                return false;
            }
            Note($"Relaxed engine: notes count {relaxed.FiveFretGuitar.HitWindow.MaxWindow * 500:0} ms either side, strum to fret {relaxed.FiveFretGuitar.StrumLeniency * 1000:0} ms");

            // Co-op guitar on every song with guitar (the test song has no co-op part of its own)
            _step = "co-op guitar on a song without a co-op part";
            var anySong = YARG.Song.SongContainer.Songs.FirstOrDefault(x => x.HasInstrument(Instrument.FiveFretGuitar));
            if (anySong != null && !anySong.HasInstrument(Instrument.FiveFretCoopGuitar))
            {
                Finish(false, "a song with guitar does not offer co-op guitar");
                return false;
            }
            Note("Co-op guitar is offered on a song with only a guitar part");

            // Deep Music Mode on a song without the converter's whole-song parts: built from the
            // song's keys and drums (guitar every two beats, keys and drums on the beats between)
            _step = "Deep Music Mode on any song";
            {
                var text = new StringBuilder("[Song]\n{\n  Resolution = 192\n}\n[SyncTrack]\n{\n  0 = TS 4\n  0 = B 120000\n}\n[Events]\n{\n}\n");
                void Part(string name, int from, int step, int fret)
                {
                    text.Append("[" + name + "]\n{\n");
                    for (int t = from; t < 192 * 64; t += step)
                    {
                        text.Append($"  {t} = N {fret} 0\n");
                    }
                    text.Append("}\n");
                }
                Part("ExpertSingle", 0, 384, 1);
                Part("ExpertDoubleBass", 0, 384, 2);
                Part("ExpertKeyboard", 192, 384, 3);
                Part("ExpertDrums", 192, 384, 1);
                var chart = YARG.Core.Chart.SongChart.FromDotChart(YARG.Core.Chart.ParseSettings.Default, text.ToString());
                int guitarNotes = chart.GetFiveFretTrack(Instrument.FiveFretGuitar).GetDifficulty(Difficulty.Expert).Notes.Count;
                int deepGuitar = chart.GetFiveFretTrack(Instrument.FiveFretGuitar, Modifier.DeepMusicMode, Difficulty.Expert)
                    .GetDifficulty(Difficulty.Expert).Notes.Count;
                int bassNotes = chart.GetFiveFretTrack(Instrument.FiveFretBass).GetDifficulty(Difficulty.Expert).Notes.Count;
                var deepBassTrack = chart.GetFiveFretTrack(Instrument.FiveFretBass, Modifier.DeepMusicMode, Difficulty.Expert)
                    .GetDifficulty(Difficulty.Expert);
                bool linked = true;
                for (int i = 1; i < deepBassTrack.Notes.Count; i++)
                {
                    linked &= deepBassTrack.Notes[i].PreviousNote == deepBassTrack.Notes[i - 1]
                        && deepBassTrack.Notes[i - 1].NextNote == deepBassTrack.Notes[i]
                        && deepBassTrack.Notes[i].Tick > deepBassTrack.Notes[i - 1].Tick;
                }
                int plain = chart.GetFiveFretTrack(Instrument.FiveFretGuitar, Modifier.None, Difficulty.Expert)
                    .GetDifficulty(Difficulty.Expert).Notes.Count;
                if (deepGuitar != guitarNotes * 2 || deepBassTrack.Notes.Count != bassNotes * 2 || !linked || plain != guitarNotes)
                {
                    Finish(false, $"Deep Music Mode on a song without its parts: guitar {guitarNotes} -> {deepGuitar}, bass {bassNotes} -> {deepBassTrack.Notes.Count}, in order {linked}, without the modifier {plain}");
                    return false;
                }
                // ...and on co-op guitar, where it is offered too
                var (coopModifiers, _) = GameMode.FiveFretGuitar.PossibleModifiers(Instrument.FiveFretCoopGuitar);
                int deepCoop = chart.GetFiveFretTrack(Instrument.FiveFretCoopGuitar, Modifier.DeepMusicMode, Difficulty.Expert)
                    .GetDifficulty(Difficulty.Expert).Notes.Count;
                if ((coopModifiers & Modifier.DeepMusicMode) == 0 || deepCoop != guitarNotes * 2)
                {
                    Finish(false, $"Deep Music Mode on co-op guitar: offered {(coopModifiers & Modifier.DeepMusicMode) != 0}, notes {deepCoop}");
                    return false;
                }
                Note($"Deep Music Mode on any song: guitar {guitarNotes} notes + keys = {deepGuitar}, bass {bassNotes} + drums = {deepBassTrack.Notes.Count}, co-op guitar {deepCoop}");
            }

            // Real instrument tracks: the file that says which tracks a part plays
            _step = "reading yarg-tracks.txt";
            var parts = GameManager.ParseTrackParts(new[]
            {
                "# a comment", "guitar=guitar,vocals,keys", "bass=bass", "rhythm = bass, drums", "nonsense", "coop=",
            });
            if (parts == null || parts.Count != 3 || parts["guitar"].Length != 3 || parts["guitar"][1] != SongStem.Vocals
                || parts["rhythm"].Length != 2 || parts["rhythm"][1] != SongStem.Drums)
            {
                Finish(false, "yarg-tracks.txt is not read as written");
                return false;
            }
            Note("Real instrument tracks: the parts file is read (guitar plays guitar, vocals and keys; Deep Music Mode bass plays bass and drums)");

            // Hearing a guitar sound before choosing it
            _step = "guitar sound preview";
            short[] riff = await UniTask.RunOnThreadPool(() => GuitarVoicePreview.Render(GuitarVoiceTone.Rock));
            int loudest = 0;
            foreach (short v in riff)
            {
                loudest = Math.Max(loudest, Math.Abs((int) v));
            }
            if (riff.Length < GuitarVoiceSynth.SAMPLE_RATE * 2 * 3 || loudest < 3000)
            {
                Finish(false, $"the guitar sound preview is silent or too short ({riff.Length} samples, loudest {loudest})");
                return false;
            }
            GuitarVoicePreview.Play(GuitarVoiceMode.Rock);
            await UniTask.Delay(TimeSpan.FromSeconds(2.0), ignoreTimeScale: true);
            GuitarVoicePreview.Stop();
            Note($"Guitar sound preview: {riff.Length / 2.0 / GuitarVoiceSynth.SAMPLE_RATE:0.0} s riff made and played");

            // Renaming with up / down / confirm: "A", then "b", then Save
            _step = "renaming a profile with a controller";
            var profile = new YargProfile
            {
                Name = "Smoke Rename",
                GameMode = GameMode.FiveFretGuitar,
                CurrentInstrument = Instrument.FiveFretGuitar,
                CurrentDifficulty = Difficulty.Easy,
            };
            PlayerContainer.AddProfile(profile);
            bool renameLeft = false;
            YARG.Menu.ProfileList.ProfileControllerMenus.ShowRename(profile, () => renameLeft = true);
            await UniTask.Delay(TimeSpan.FromSeconds(0.8), ignoreTimeScale: true);
            // Rows: Save, Delete, Space, A, B, ...
            await Press(down);
            await Press(down);
            await Press(down);
            await Press(green, 1.2);     // "A"; the list comes back on the same row
            await Press(down);
            await Press(green, 1.2);     // "b"
            await Screenshot("rename.png");
            for (int i = 0; i < 4; i++)
            {
                await Press(up);
            }
            await Press(green, 1.2);     // Save
            if (!renameLeft || profile.Name != "Ab")
            {
                DialogManager.Instance.ClearDialog();
                Finish(false, $"renaming with a controller gave '{profile.Name}' (expected 'Ab'), list left: {renameLeft}");
                return false;
            }
            Note("Profile renamed with up, down and confirm alone: 'Smoke Rename' is now 'Ab'");

            // Changing what a button does: pick the first action, "press a button", press one
            _step = "changing a button with a controller";
            var pad = InputSystem.AddDevice<Gamepad>("Smoke Test Pad");
            var player = PlayerContainer.CreatePlayerFromProfile(profile, false);
            if (player == null)
            {
                Finish(false, "couldn't create a player for the button test");
                return false;
            }
            player.Bindings.AddDevice(pad);
            var first = player.Bindings[GameMode.FiveFretGuitar].OfType<YARG.Input.ButtonBinding>().First();

            bool buttonsLeft = false;
            YARG.Menu.ProfileList.ProfileControllerMenus.ShowButtons(player, () => buttonsLeft = true);
            await UniTask.Delay(TimeSpan.FromSeconds(0.8), ignoreTimeScale: true);
            await Screenshot("buttons.png");
            await Press(down);
            await Press(green, 1.2);     // the first action
            await Press(green, 1.5);     // "Press a button to use": now waiting for the controller
            InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState()
                .WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.North));
            await UniTask.Delay(TimeSpan.FromSeconds(0.5), ignoreTimeScale: true);
            InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
            await UniTask.Delay(TimeSpan.FromSeconds(2.5), ignoreTimeScale: true);

            bool bound = first.ContainsControl(pad.buttonNorth);
            bool inputsBack = player.InputsEnabled;
            DialogManager.Instance.ClearDialog();
            await UniTask.Delay(TimeSpan.FromSeconds(0.5), ignoreTimeScale: true);
            PlayerContainer.DisposePlayer(player);
            PlayerContainer.RemoveProfile(profile);
            InputSystem.RemoveDevice(pad);
            if (!bound || !inputsBack)
            {
                Finish(false, $"changing a button with a controller: button taken {bound}, player's buttons working again {inputsBack}, list left {buttonsLeft}");
                return false;
            }
            Note($"Button changed with a controller: '{first.Name}' is now the pad's {pad.buttonNorth.displayName} button");
            return true;
        }

        private async UniTask Screenshot(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return;
            }
            try
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, name));
                await UniTask.Delay(TimeSpan.FromSeconds(1), ignoreTimeScale: true);
            }
            catch (Exception e)
            {
                Note($"Screenshot {name} failed: {e.Message}");
            }
        }

        private void Note(string line)
        {
            _report.AppendLine(line);
            Debug.Log("[YARG Smoke Test] " + line);
        }

        private void Finish(bool pass, string reason)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;

            lock (_errorLock)
            {
                if (_errorCount == 0)
                {
                    Note("Errors logged by the game during the test: none");
                }
                else
                {
                    Note($"Errors logged by the game during the test: {_errorCount}");
                    foreach (string error in _errors)
                    {
                        _report.AppendLine("  " + error);
                    }
                }
            }

            string verdict = (pass ? "PASS: " : "FAIL: ") + reason;
            Note(verdict);
            try
            {
                File.WriteAllText(Path.Combine(_outDir, "result.txt"), _report.ToString());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            Application.Quit(pass ? 0 : 1);
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
