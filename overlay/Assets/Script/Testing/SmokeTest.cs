// YARG Resurrected - automated play test for the desktop builds (Windows, Mac, Linux).
//
// Does nothing unless the game is started with  -yarg-smoke-test . CI starts each build that way
// on a real machine of its own system, before the build is published:
//
//   YARG.x86_64 -yarg-smoke-test -yarg-smoke-songs <folder> -yarg-smoke-out <folder>
//
// It then plays the game by itself, like a person would:
//   1. waits for the main menu (so the game started, its native libraries loaded and the
//      menus work),
//   2. scans <folder> for songs (song cache, chart reading),
//   3. adds a bot guitarist on Expert and starts the first song found (audio: the song only
//      moves forward when it is playing),
//   4. checks the song time moves on and the bot hits notes,
//   5. writes result.txt (PASS or FAIL and why) plus menu.png and gameplay.png screenshots to
//      <folder>, and quits with exit code 0 (pass) or 1 (fail).
// If any step takes too long it fails with the step it was stuck on.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YARG.Core;
using YARG.Core.Game;
using YARG.Gameplay;
using YARG.Player;
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

        private readonly StringBuilder _report = new();
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
            Note($"Platform: {Application.platform}, {SystemInfo.operatingSystem}, Unity {Application.unityVersion}");
            Note($"Graphics: {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName})");
            Run().Forget();
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
                await Screenshot("menu.png");

                // 2. Songs
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
                await SongContainer.RunRefresh(false);
                Note($"Songs found: {SongContainer.Count}");
                var song = SongContainer.Songs.FirstOrDefault();
                if (song == null)
                {
                    Finish(false, "the song scan found no songs");
                    return;
                }
                Note($"Playing: {song.Name.Original} by {song.Artist.Original}");

                // 3. A bot guitarist, then start the song
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

                _step = "loading the song";
                GlobalVariables.State = PersistentState.Default;
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

                // 4. Play
                _step = "playing the song";
                double startTime = game.SongTime;
                int startFrame = Time.frameCount;
                float startReal = Time.realtimeSinceStartup;
                bool shot = false;
                while (game != null && game.SongTime - startTime < SONG_SECONDS_TO_PLAY)
                {
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

                Finish(true, "menu, song scan, song start, audio and gameplay all work");
            }
            catch (Exception e)
            {
                Finish(false, $"exception while {_step}: {e}");
            }
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
