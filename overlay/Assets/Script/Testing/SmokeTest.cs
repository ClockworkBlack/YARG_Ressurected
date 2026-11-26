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
                var profile = new YargProfile
                {
                    Name = "Smoke Test Bot",
                    IsBot = true,
                    GameMode = GameMode.FiveFretGuitar,
                    CurrentInstrument = Instrument.FiveFretGuitar,
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
                SettingsManager.Settings.GuitarVoice.SetValueWithoutNotify(GuitarVoiceMode.Rock);
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

                int tracks = GuitarVoiceLoader.LastTracksAdded;
                if (tracks < 1)
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
                while (game != null && game.SongTime - startTime < SONG_SECONDS_TO_PLAY)
                {
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
                string[] wanted = { "blackhole", "cable output", "monitor", "loopback", "default", "pulse" };
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
            if (DifficultySelectMenu.Current != null)
            {
                MenuManager.Instance.PopMenu();
            }
            await UniTask.Delay(TimeSpan.FromSeconds(0.5), ignoreTimeScale: true);
            return true;
        }

        #endregion

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
