// YARG Resurrected - the "Play with phones" screen (join codes, game network) and automatic
// players for phones that pick an instrument on their page.
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Game;
using YARG.Input;
using YARG.Menu.Persistent;
using YARG.Player;

namespace YARG.Remote
{
    public sealed partial class RemotePlayManager
    {
        #region Players for phones

        private static bool InGameplay => SceneManager.GetSceneByName("Gameplay").isLoaded;

        private PhoneState FindPhone(RemoteClient client)
        {
            return _phones.TryGetValue(client, out var phone) ? phone : null;
        }

        private void SetRole(RemoteClient client, string role, string difficulty, bool? challenge = null)
        {
            var phone = FindPhone(client);
            if (phone == null)
            {
                return;
            }

            phone.Role = role;
            phone.Difficulty = difficulty;
            phone.Challenge = challenge;
            phone.RolePending = true;

            if (role != "none" && InGameplay)
            {
                client.SendText("rolewait");
            }
        }

        private void MarkRolePending(RemoteClient client)
        {
            var phone = FindPhone(client);
            if (phone != null && phone.Role != null)
            {
                phone.RolePending = true;
            }
        }

        // Players can't be added in the middle of a song, so requests wait for the menus.
        private void ApplyPendingRoles()
        {
            if (_phones.IsEmpty || InGameplay)
            {
                return;
            }

            foreach (var pair in _phones)
            {
                var phone = pair.Value;
                if (!phone.RolePending || phone.Pad == null || !phone.Pad.added)
                {
                    continue;
                }

                phone.RolePending = false;
                try
                {
                    ApplyRole(phone);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void ApplyRole(PhoneState phone)
        {
            var client = phone.Client;
            if (phone.Role == null || phone.Role == "none")
            {
                return;
            }

            bool vocals = phone.Role == "vocals";
            var mode = vocals ? GameMode.Vocals : GameMode.FiveFretGuitar;
            var instrument = vocals ? Instrument.Vocals
                : phone.Role == "bass" ? Instrument.FiveFretBass : Instrument.FiveFretGuitar;
            var difficulty = phone.Difficulty switch
            {
                "easy" => Difficulty.Easy,
                "hard" => Difficulty.Hard,
                "expert" => Difficulty.Expert,
                _ => Difficulty.Medium
            };

            // The player that already uses this phone, if any (made earlier, or set up by the host).
            YargPlayer player = null;
            foreach (var existing in PlayerContainer.Players)
            {
                if (existing.Bindings.ContainsDevice(phone.Pad))
                {
                    player = existing;
                    break;
                }
            }

            bool created = false;
            if (player != null && player.Profile.GameMode != mode)
            {
                if (player != phone.AutoPlayer && player.Profile.Name != client.Name)
                {
                    // The host set this phone up by hand for something else; leave it alone.
                    client.SendText("roleno");
                    return;
                }

                PlayerContainer.DisposePlayer(player);
                player = null;
            }

            if (player == null)
            {
                // Reuse this person's profile from last time, or make one.
                YargProfile profile = null;
                foreach (var candidate in PlayerContainer.Profiles)
                {
                    if (!candidate.IsBot && candidate.GameMode == mode && candidate.Name == client.Name &&
                        !PlayerContainer.IsProfileTaken(candidate))
                    {
                        profile = candidate;
                        break;
                    }
                }

                if (profile == null)
                {
                    profile = new YargProfile
                    {
                        Name = client.Name,
                        GameMode = mode,
                    };
                    PlayerContainer.AddProfile(profile);
                }

                player = PlayerContainer.CreatePlayerFromProfile(profile, false);
                if (player == null)
                {
                    Debug.LogWarning("[YARG Remote] Could not create a player for " + client.Name);
                    return;
                }

                player.Bindings.AddDevice(phone.Pad);
                if (!player.Bindings.ContainsBindingsForDevice(phone.Pad))
                {
                    player.Bindings.SetDefaultBinds(phone.Pad, GamepadBindingMode.RB4InstrumentMapper_Guitar);
                }

                phone.AutoPlayer = player;
                created = true;
            }

            if (player.Profile.Name == client.Name)
            {
                phone.AutoPlayer = player; // leaves with the phone
            }

            player.Profile.CurrentInstrument = instrument;
            player.Profile.PreferredInstrument = instrument;
            player.Profile.CurrentDifficulty = difficulty;
            player.Profile.DifficultyFallback = difficulty;

            // A singer can ask for Vocals Challenge Mode on the phone's join screen. (With
            // several singers, the one who picks the modifiers on the host decides for all,
            // as with the other vocal modifiers.)
            if (vocals && phone.Challenge.HasValue)
            {
                if (phone.Challenge.Value)
                {
                    player.Profile.AddSingleModifier(YARG.Core.Game.Modifier.VocalsChallenge);
                }
                else
                {
                    player.Profile.RemoveModifiers(YARG.Core.Game.Modifier.VocalsChallenge);
                }
            }

            bool micReady = true;
            if (vocals && !(player.Bindings.Microphone is RemoteMicDevice))
            {
                var mic = RemoteMics.Create(RemoteMics.ID_BASE + client.Number, RemoteMics.DisplayNameFor(client.Name));
                if (mic != null)
                {
                    player.Bindings.AddMicrophone(mic);
                }
                else
                {
                    micReady = false; // tried again when the phone's mic starts streaming
                }
            }

            client.SendText("roleok\n" + phone.Role);
            if (created || (vocals && micReady))
            {
                string what = vocals ? "vocals" : phone.Role == "bass" ? "bass" : "guitar";
                ToastManager.ToastSuccess($"{client.Name} joined on {what}");
            }
        }

        private void OnPhoneLeft(PhoneState phone)
        {
            // Between songs, a phone that leaves takes its automatic player with it, so the next
            // song isn't waiting on someone who's gone. Mid-song the player stays; the phone
            // usually reconnects within seconds.
            var player = phone.AutoPlayer;
            phone.AutoPlayer = null;
            if (player == null || InGameplay)
            {
                return;
            }

            try
            {
                bool stillThere = false;
                foreach (var existing in PlayerContainer.Players)
                {
                    if (existing == player)
                    {
                        stillThere = true;
                        break;
                    }
                }
                if (stillThere)
                {
                    PlayerContainer.DisposePlayer(player);
                    ToastManager.ToastMessage($"{phone.Client.Name} left");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        #endregion

        #region Game network (local-only hotspot)

        private const int HOTSPOT_OFF = 0;
        private const int HOTSPOT_STARTING = 1;
        private const int HOTSPOT_ON = 2;
        private const int HOTSPOT_FAILED = 3;

        private int _hotspotState;
        private string _hotspotSsid = "";
        private string _hotspotPassword = "";
        private string _hotspotMessage = "";
        private string _hotspotAddress;
        private List<string> _addressesBeforeHotspot = new List<string>();
        private float _nextHotspotPoll;

        /// <summary>The address guests should use: the game network's if it's on, else Wi-Fi's.</summary>
        private string PrimaryAddress()
        {
            if (_hotspotState == HOTSPOT_ON && _hotspotAddress != null)
            {
                return _hotspotAddress;
            }
            return _addresses.Count > 0 ? _addresses[0] : null;
        }

        private void ToggleHotspot()
        {
            if (_hotspotState == HOTSPOT_ON || _hotspotState == HOTSPOT_STARTING)
            {
                StopHotspot();
                _hotspotMessage = "Game network stopped.";
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int sdk;
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    sdk = version.GetStatic<int>("SDK_INT");
                }

                // Android 13+ asks for "Nearby devices"; older versions ask for Location.
                string permission = sdk >= 33
                    ? "android.permission.NEARBY_WIFI_DEVICES"
                    : UnityEngine.Android.Permission.FineLocation;

                if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
                {
                    StartHotspotNow();
                    return;
                }

                _hotspotMessage = "Allow the permission so YARG can start a Wi-Fi network for your friends.";
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => _mainThread.Enqueue(StartHotspotNow);
                callbacks.PermissionDenied += _ => _mainThread.Enqueue(() =>
                    _hotspotMessage = "Permission denied. You can still use \"Phone hotspot settings\" and turn on the normal hotspot.");
                UnityEngine.Android.Permission.RequestUserPermission(permission, callbacks);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _hotspotMessage = "Couldn't start the game network: " + e.Message;
            }
#else
            _hotspotMessage = "The game network is only available on Android.";
#endif
        }

        private void StartHotspotNow()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _addressesBeforeHotspot = new List<string>(_addresses);
                _hotspotAddress = null;
                _hotspotMessage = "Starting the game network...";
                _hotspotState = HOTSPOT_STARTING;

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var hotspot = new AndroidJavaClass("in.yarg.resurrected.YargLocalHotspot"))
                {
                    hotspot.CallStatic("start", activity);
                }
                _nextHotspotPoll = 0f;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _hotspotState = HOTSPOT_FAILED;
                _hotspotMessage = "Couldn't start the game network: " + e.Message;
            }
#endif
        }

        private void StopHotspot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_hotspotState == HOTSPOT_OFF)
            {
                return;
            }

            try
            {
                using (var hotspot = new AndroidJavaClass("in.yarg.resurrected.YargLocalHotspot"))
                {
                    hotspot.CallStatic("stop");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] Stopping the game network failed: " + e.Message);
            }
#endif
            _hotspotState = HOTSPOT_OFF;
            _hotspotAddress = null;
            _hotspotSsid = "";
            _hotspotPassword = "";
        }

        private void PollHotspot()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_hotspotState != HOTSPOT_STARTING && _hotspotState != HOTSPOT_ON)
            {
                return;
            }
            if (Time.unscaledTime < _nextHotspotPoll)
            {
                return;
            }
            _nextHotspotPoll = Time.unscaledTime + 0.5f;

            try
            {
                using (var hotspot = new AndroidJavaClass("in.yarg.resurrected.YargLocalHotspot"))
                {
                    int state = hotspot.CallStatic<int>("getState");
                    if (state == HOTSPOT_ON)
                    {
                        if (_hotspotState != HOTSPOT_ON)
                        {
                            _hotspotSsid = hotspot.CallStatic<string>("getSsid") ?? "";
                            _hotspotPassword = hotspot.CallStatic<string>("getPassword") ?? "";
                            _hotspotMessage = "Game network is on. It has no internet, which is fine for playing.";
                        }
                        _hotspotState = HOTSPOT_ON;
                    }
                    else if (state == HOTSPOT_FAILED)
                    {
                        _hotspotState = HOTSPOT_FAILED;
                        _hotspotMessage = hotspot.CallStatic<string>("getError") ?? "Couldn't start the game network.";
                    }
                    else if (state == HOTSPOT_OFF && _hotspotState == HOTSPOT_ON)
                    {
                        _hotspotState = HOTSPOT_OFF;
                        _hotspotAddress = null;
                        _hotspotMessage = "Android stopped the game network.";
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] Polling the game network failed: " + e.Message);
            }

            // The game network's address is the one that wasn't there before it started.
            if (_hotspotState == HOTSPOT_ON && _hotspotAddress == null)
            {
                RefreshAddresses();
                _nextAddressRefresh = Time.unscaledTime + 1f;
                foreach (var address in _addresses)
                {
                    if (!_addressesBeforeHotspot.Contains(address))
                    {
                        _hotspotAddress = address;
                        break;
                    }
                }
            }
#endif
        }

        private void OpenHotspotSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // There's no official "hotspot settings" screen, so try the usual one, then fall back.
            string[][] targets =
            {
                new[] { "com.android.settings", "com.android.settings.TetherSettings" },
                new[] { "com.android.settings", "com.android.settings.Settings$TetherSettingsActivity" },
            };

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                foreach (var target in targets)
                {
                    try
                    {
                        using (var intent = new AndroidJavaObject("android.content.Intent"))
                        {
                            intent.Call<AndroidJavaObject>("setClassName", target[0], target[1]).Dispose();
                            activity.Call("startActivity", intent);
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        // try the next one
                    }
                }

                try
                {
                    using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.WIRELESS_SETTINGS"))
                    {
                        activity.Call("startActivity", intent);
                    }
                }
                catch (Exception e)
                {
                    _hotspotMessage = "Couldn't open settings: " + e.Message;
                }
            }
#endif
        }

        #endregion

        #region "Play with phones" screen

        private GameObject _lobby;
        private RawImage _wifiQrImage;
        private RawImage _urlQrImage;
        private TextMeshProUGUI _wifiCaption;
        private TextMeshProUGUI _wifiDetail;
        private TextMeshProUGUI _urlCaption;
        private TextMeshProUGUI _urlDetail;
        private TextMeshProUGUI _lobbyStatus;
        private TextMeshProUGUI _hotspotButtonLabel;
        private string _wifiQrContent;
        private string _urlQrContent;
        private float _nextLobbyRefresh;

        private void OpenLobby()
        {
            if (_lobby == null)
            {
                CreateLobby();
            }
            _lobby.SetActive(true);
            _nextLobbyRefresh = 0f;
        }

        private void CloseLobby()
        {
            if (_lobby != null)
            {
                _lobby.SetActive(false);
            }
        }

        private void UpdateLobby()
        {
            ApplyPendingRoles();
            PollHotspot();

            if (_lobby == null || !_lobby.activeSelf)
            {
                return;
            }

            if (InGameplay)
            {
                CloseLobby();
                return;
            }

            if (Time.unscaledTime < _nextLobbyRefresh)
            {
                return;
            }
            _nextLobbyRefresh = Time.unscaledTime + 0.5f;

            string address = PrimaryAddress();
            string url = address != null && _server != null && _server.IsRunning ? BuildUrl(address) : null;
            bool hotspotOn = _hotspotState == HOTSPOT_ON;

            // Left: the Wi-Fi to join
            if (hotspotOn)
            {
                _wifiCaption.text = "<b>1. Join this Wi-Fi</b>\n<size=70%>Scan with the phone's camera</size>";
                _wifiDetail.text = $"Network: <color=#FFD84D>{Escape(_hotspotSsid)}</color>\nPassword: <color=#FFD84D>{Escape(_hotspotPassword)}</color>";
                SetQr(_wifiQrImage, ref _wifiQrContent, BuildWifiQr(_hotspotSsid, _hotspotPassword));
            }
            else
            {
                _wifiCaption.text = "<b>1. Be on the same Wi-Fi</b>";
                _wifiDetail.text = address != null
                    ? "Friends on the same Wi-Fi as this phone only need the code on the right.\n\nNo shared Wi-Fi? Tap <b>Start game network</b>."
                    : "This phone isn't on Wi-Fi.\n\nTap <b>Start game network</b> to make one for your friends.";
                SetQr(_wifiQrImage, ref _wifiQrContent, null);
            }

            // Right: the page to open
            if (url != null)
            {
                _urlCaption.text = hotspotOn
                    ? "<b>2. Then open this page</b>\n<size=70%>Scan again once connected</size>"
                    : "<b>2. Open this page</b>\n<size=70%>Scan with the phone's camera</size>";
                string hint = _tlsState == -1
                    ? "Controllers only on this phone (the secure link isn't working, so phone mics are off)."
                    : "The browser will warn that it's not private. Tap <b>Advanced</b>, then <b>Proceed</b>.";
                _urlDetail.text = $"<color=#FFD84D>{url}</color>\n<size=75%>{hint}</size>";
                SetQr(_urlQrImage, ref _urlQrContent, url);
            }
            else
            {
                _urlCaption.text = "<b>2. Open the page</b>";
                _urlDetail.text = hotspotOn ? "Waiting for the game network's address..." : "The address shows here once this phone is on a network.";
                SetQr(_urlQrImage, ref _urlQrContent, null);
            }

            _hotspotButtonLabel.text = _hotspotState == HOTSPOT_ON ? "Stop game network"
                : _hotspotState == HOTSPOT_STARTING ? "Starting..." : "Start game network";

            // Status: who's here
            var status = new StringBuilder();
            if (!string.IsNullOrEmpty(_hotspotMessage))
            {
                status.Append(_hotspotMessage).Append('\n');
            }

            int count = 0;
            foreach (var pair in _phones)
            {
                var phone = pair.Value;
                if (phone.Pad == null)
                {
                    continue;
                }

                status.Append(count == 0 ? "Connected: " : ", ");
                status.Append(Escape(pair.Key.Name));
                string role = phone.Role == "vocals" ? "vocals" : phone.Role == "bass" ? "bass"
                    : phone.Role == "guitar" ? "guitar" : null;
                status.Append(" (");
                if (role != null)
                {
                    status.Append(role).Append(", ");
                }
                status.Append(Mathf.RoundToInt((float) phone.HoldMs)).Append(" ms)");
                count++;
            }
            if (count == 0)
            {
                status.Append("No phones connected yet.");
            }
            _lobbyStatus.text = status.ToString();
        }

        // Names come from other people's phones; keep them from being read as text markup.
        private static string Escape(string text)
        {
            return (text ?? "").Replace("<", "").Replace(">", "");
        }

        // The standard "join this Wi-Fi" code that phone cameras understand.
        private static string BuildWifiQr(string ssid, string password)
        {
            return $"WIFI:T:WPA;S:{EscapeWifi(ssid)};P:{EscapeWifi(password)};;";
        }

        private static string EscapeWifi(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (c == '\\' || c == ';' || c == ',' || c == ':' || c == '"')
                {
                    builder.Append('\\');
                }
                builder.Append(c);
            }
            return builder.ToString();
        }

        private static void SetQr(RawImage image, ref string current, string content)
        {
            if (content == current)
            {
                return;
            }
            current = content;

            if (image.texture != null)
            {
                Destroy(image.texture);
                image.texture = null;
            }

            bool[,] modules = content != null ? RemoteQr.Encode(Encoding.UTF8.GetBytes(content)) : null;
            if (modules == null)
            {
                image.enabled = false;
                return;
            }

            const int quiet = 4;
            int size = modules.GetLength(0);
            int full = size + quiet * 2;
            var pixels = new Color32[full * full];
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = white;
            }
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (modules[y, x])
                    {
                        // Texture rows start at the bottom
                        pixels[(full - 1 - (y + quiet)) * full + x + quiet] = black;
                    }
                }
            }

            var texture = new Texture2D(full, full, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            image.texture = texture;
            image.enabled = true;
        }

        private void CreateLobby()
        {
            _lobby = new GameObject("Lobby");
            _lobby.transform.SetParent(_canvas.transform, false);
            var root = _lobby.AddComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            _lobby.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.08f, 0.96f); // also blocks touches behind it

            var title = MakeText(root, new Vector2(0.5f, 0.93f), new Vector2(1600f, 90f), 54, TextAlignmentOptions.Center);
            title.text = "<b>PLAY WITH PHONES</b>";

            // Left column
            _wifiCaption = MakeText(root, new Vector2(0.27f, 0.80f), new Vector2(800f, 110f), 36, TextAlignmentOptions.Center);
            _wifiQrImage = MakeQr(root, new Vector2(0.27f, 0.555f));
            _wifiDetail = MakeText(root, new Vector2(0.27f, 0.555f), new Vector2(760f, 360f), 30, TextAlignmentOptions.Center);

            // Right column
            _urlCaption = MakeText(root, new Vector2(0.73f, 0.80f), new Vector2(800f, 110f), 36, TextAlignmentOptions.Center);
            _urlQrImage = MakeQr(root, new Vector2(0.73f, 0.555f));
            _urlDetail = MakeText(root, new Vector2(0.73f, 0.555f), new Vector2(760f, 360f), 30, TextAlignmentOptions.Center);

            _lobbyStatus = MakeText(root, new Vector2(0.5f, 0.215f), new Vector2(1760f, 110f), 28, TextAlignmentOptions.Center);
            _lobbyStatus.color = new Color(0.75f, 0.8f, 0.9f);

            _hotspotButtonLabel = MakeButton(root, new Vector2(0.22f, 0.085f), "Start game network",
                new Color(1f, 0.85f, 0.3f), Color.black, ToggleHotspot);
            MakeButton(root, new Vector2(0.5f, 0.085f), "Phone hotspot settings",
                new Color(0.15f, 0.2f, 0.3f), Color.white, OpenHotspotSettings);
            MakeButton(root, new Vector2(0.78f, 0.085f), "Close",
                new Color(0.15f, 0.2f, 0.3f), Color.white, CloseLobby);

            // With a QR code showing, the text under it moves below the code.
            _wifiDetailRect = _wifiDetail.rectTransform;
            _urlDetailRect = _urlDetail.rectTransform;
        }

        private RectTransform _wifiDetailRect;
        private RectTransform _urlDetailRect;

        private void LateUpdate()
        {
            if (_lobby == null || !_lobby.activeSelf)
            {
                return;
            }

            PlaceDetail(_wifiDetailRect, _wifiQrImage);
            PlaceDetail(_urlDetailRect, _urlQrImage);
        }

        private static void PlaceDetail(RectTransform detail, RawImage qr)
        {
            // Under the code when there is one, otherwise in its place.
            float y = qr.enabled ? 0.335f : 0.555f;
            var size = qr.enabled ? new Vector2(860f, 130f) : new Vector2(760f, 360f);
            if (!Mathf.Approximately(detail.anchorMin.y, y))
            {
                detail.anchorMin = new Vector2(detail.anchorMin.x, y);
                detail.anchorMax = new Vector2(detail.anchorMax.x, y);
                detail.sizeDelta = size;
            }
        }

        private static TextMeshProUGUI MakeText(RectTransform parent, Vector2 anchor, Vector2 size, float fontSize,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.richText = true;
            text.raycastTarget = false;
            return text;
        }

        private static RawImage MakeQr(RectTransform parent, Vector2 anchor)
        {
            var go = new GameObject("QR");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(340f, 340f);

            var image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private static TextMeshProUGUI MakeButton(RectTransform parent, Vector2 anchor, string label, Color background,
            Color textColor, Action onClick)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(500f, 100f);

            go.AddComponent<Image>().color = background;
            var button = go.AddComponent<Button>();
            button.onClick.AddListener(() =>
            {
                try
                {
                    onClick();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });

            var text = MakeText(rect, new Vector2(0.5f, 0.5f), new Vector2(480f, 90f), 34, TextAlignmentOptions.Center);
            text.text = label;
            text.color = textColor;
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        #endregion
    }
}
