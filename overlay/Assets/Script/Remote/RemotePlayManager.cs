// YARG Resurrected - lets other phones join as controllers and microphones.
//
// Guests open the address shown on the main menu (same Wi-Fi) in their browser. Each phone
// becomes a virtual gamepad in Unity's Input System, which YARG treats like any controller:
// Profiles -> Add Device -> "<name> (phone)". A phone that turns on its mic also shows up as
// "Phone mic: <name>" in the microphone list.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace YARG.Remote
{
    public sealed partial class RemotePlayManager : MonoBehaviour, IRemoteServerHandler
    {
        public const int DEFAULT_PORT = 7777;
        public const string INTERFACE_NAME = "YARGRemote";
        private const string CERT_PASSWORD = "yarg";

        // Button bits sent by the phone page (must match remote.html).
        private const int BIT_GREEN = 0;
        private const int BIT_RED = 1;
        private const int BIT_YELLOW = 2;
        private const int BIT_BLUE = 3;
        private const int BIT_ORANGE = 4;
        private const int BIT_UP = 5;
        private const int BIT_DOWN = 6;
        private const int BIT_LEFT = 7;
        private const int BIT_RIGHT = 8;
        private const int BIT_START = 9;
        private const int BIT_SELECT = 10;

        public static RemotePlayManager Instance { get; private set; }

        private static readonly HashSet<InputDevice> _remoteDevices = new HashSet<InputDevice>();

        /// <summary>True for the virtual gamepads created for phones.</summary>
        public static bool IsRemoteDevice(InputDevice device)
        {
            return device != null &&
                (device.description.interfaceName == INTERFACE_NAME || _remoteDevices.Contains(device));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Instance != null)
            {
                return;
            }

            var host = new GameObject("YARG Remote Play");
            DontDestroyOnLoad(host);
            host.AddComponent<RemotePlayManager>();
#endif
        }

        private struct PadPacket
        {
            public PhoneState Phone;
            public uint Buttons;
            public byte Whammy;
            public double PlayTimeMs; // host clock (RemoteServer.NowMs)
        }

        private sealed class PhoneState
        {
            public readonly DelayEstimator Delay = new DelayEstimator();
            public readonly Queue<PadPacket> Held = new Queue<PadPacket>(); // main thread only
            public Gamepad Pad;                                             // main thread only
            public double HoldMs = DelayEstimator.DEFAULT_MS;               // fixed while a song plays
            public double LastEventTime;

            // Lobby (main thread only): what the phone asked to play, and the player made for it
            public RemoteClient Client;
            public string Role;
            public string Difficulty;
            public bool? Challenge; // vocals: asked for Challenge Mode (null = did not say)
            public bool RolePending;
            public YARG.Player.YargPlayer AutoPlayer;
        }

        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        private readonly ConcurrentQueue<PadPacket> _padPackets = new ConcurrentQueue<PadPacket>();
        private readonly ConcurrentDictionary<RemoteClient, PhoneState> _phones =
            new ConcurrentDictionary<RemoteClient, PhoneState>();
        private readonly ConcurrentDictionary<string, double> _holdByKey = new ConcurrentDictionary<string, double>();
        private int _padCount;
        private double _lastInputUpdateTime;

        /// <summary>
        /// Extra input delay (seconds) for a profile that plays through a phone joined over Wi-Fi:
        /// its controller's hold time, or its mic's network delay. 0 for everything else.
        /// Gameplay adds this to the profile's input calibration.
        /// </summary>
        public static double GetInputDelaySeconds(YARG.Input.ProfileBindings bindings)
        {
            var instance = Instance;
            if (instance == null || bindings == null)
            {
                return 0;
            }

            try
            {
                foreach (var pair in instance._phones)
                {
                    var phone = pair.Value;
                    if (phone.Pad != null && bindings.InputDevices.Contains(phone.Pad))
                    {
                        return phone.HoldMs / 1000.0;
                    }
                    if (bindings.Microphone is RemoteMicDevice mic && RemoteMics.IsFedBy(mic, pair.Key))
                    {
                        // Mic audio isn't held; add its usual network delay plus one 10 ms packet.
                        return (phone.HoldMs + 10) / 1000.0;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return 0;
        }

        /// <summary>
        /// The network hold (ms) measured for the phone that feeds <paramref name="mic"/>: a bit
        /// more than that connection's usual delay. A default if the phone is not connected.
        /// </summary>
        public static double GetMicNetworkHoldMs(RemoteMicDevice mic)
        {
            var instance = Instance;
            if (instance == null || mic == null)
            {
                return RemoteMics.DEFAULT_NETWORK_HOLD_MS;
            }

            try
            {
                foreach (var pair in instance._phones)
                {
                    if (RemoteMics.IsFedBy(mic, pair.Key))
                    {
                        return pair.Value.HoldMs;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return RemoteMics.DEFAULT_NETWORK_HOLD_MS;
        }

        private RemoteServer _server;
        private volatile int _tlsState; // 0 = unknown/testing, 1 = works, -1 = broken or no certificate
        private List<string> _addresses = new List<string>();
        private float _nextAddressRefresh;

        private Canvas _canvas;
        private TextMeshProUGUI _text;
        private RectTransform _panel;
        private string _lastText;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            try
            {
                InputSystem.RegisterLayoutMatcher("Gamepad", new InputDeviceMatcher().WithInterface(INTERFACE_NAME));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] Could not register layout matcher: " + e.Message);
            }
            InputSystem.onBeforeUpdate += OnBeforeInputUpdate;

            var certificate = LoadCertificate();
            _tlsState = certificate == null ? -1 : 0;

            _server = new RemoteServer(certificate, LoadPage(), this, message => Debug.Log("[YARG Remote] " + message));
            if (_server.Start(DEFAULT_PORT))
            {
                if (certificate != null)
                {
                    ThreadPool.QueueUserWorkItem(_ => _tlsState = _server.RunTlsSelfTest() ? 1 : -1);
                }
            }
            else
            {
                Debug.LogError("[YARG Remote] Could not start the phone server (no free port).");
            }

            CreateOverlay();
            RefreshAddresses();
        }

        private void OnDestroy()
        {
            InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
            _server?.Dispose();
            _server = null;

            foreach (var phone in _phones.Values)
            {
                RemovePadDevice(phone.Pad);
            }
            _phones.Clear();

            StopHotspot();
            SetWifiLock(false);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _server?.Dispose();
        }

        private void Update()
        {
            while (_mainThread.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (Time.unscaledTime >= _nextAddressRefresh)
            {
                RefreshAddresses();
            }

            // Between songs, follow each phone's measured delay. During a song it stays fixed.
            if (!SceneManager.GetSceneByName("Gameplay").isLoaded)
            {
                foreach (var pair in _phones)
                {
                    pair.Value.HoldMs = pair.Value.Delay.EstimateMs();
                    _holdByKey[pair.Key.Key] = pair.Value.HoldMs;
                }
            }

            SetWifiLock(_padCount > 0);
            UpdateLobby();
            UpdateOverlay();
        }

        #region Server callbacks (network threads)

        public void OnClientHello(RemoteClient client)
        {
            var phone = new PhoneState { Client = client };
            // A phone that reconnects mid-song keeps the hold time its player was started with.
            if (_holdByKey.TryGetValue(client.Key, out double remembered))
            {
                phone.HoldMs = remembered;
            }
            _phones[client] = phone;
            _mainThread.Enqueue(() => AddPad(client, phone));
        }

        public void OnClientText(RemoteClient client, string message)
        {
            var parts = message.Split('\n');
            switch (parts[0])
            {
                case "ping": // ping \n <phone time> \n <last round trip ms>
                    if (parts.Length > 2 && _phones.TryGetValue(client, out var pinged) &&
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double roundTripMs))
                    {
                        pinged.Delay.AddSample(roundTripMs / 2);
                    }
                    break;
                case "mic":
                    if (parts.Length > 1 && int.TryParse(parts[1], out int sampleRate) &&
                        sampleRate >= 8000 && sampleRate <= 192000)
                    {
                        RemoteMics.Start(client, sampleRate);
                        client.SendText("micok\n" + RemoteMics.DisplayNameFor(client.Name));
                        // A phone that joined as a singer gets its mic attached once it's streaming.
                        _mainThread.Enqueue(() => MarkRolePending(client));
                    }
                    break;
                case "role": // role \n <guitar|bass|vocals|none> \n <easy|medium|hard|expert> [\n <1|0> challenge]
                    if (parts.Length > 2)
                    {
                        string role = parts[1];
                        string difficulty = parts[2];
                        // Singers can ask for Vocals Challenge Mode; absent means "leave it as it is"
                        bool? challenge = parts.Length > 3 ? parts[3] == "1" : (bool?) null;
                        _mainThread.Enqueue(() => SetRole(client, role, difficulty, challenge));
                    }
                    break;
                case "micoff":
                    RemoteMics.Stop(client);
                    break;
            }
        }

        public void OnClientBinary(RemoteClient client, byte[] data, int count)
        {
            if (count < 1)
            {
                return;
            }

            switch (data[0])
            {
                // Controller state: [1][buttons u32 LE][whammy u8][touch time f64 LE, host clock ms]
                case 1:
                    if (count >= 6 && _phones.TryGetValue(client, out var phone))
                    {
                        double arrivalMs = RemoteServer.NowMs;
                        double touchMs = count >= 14 ? BitConverter.ToDouble(data, 6) : 0;
                        double playMs = RemoteTiming.PlayTimeMs(touchMs, arrivalMs, phone.HoldMs, out double delayMs);
                        if (delayMs >= 0)
                        {
                            phone.Delay.AddSample(delayMs);
                        }

                        _padPackets.Enqueue(new PadPacket
                        {
                            Phone = phone,
                            Buttons = (uint) (data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24)),
                            Whammy = data[5],
                            PlayTimeMs = playMs
                        });
                    }
                    break;
                case 2: // mic audio: [2][16-bit mono PCM, little endian]
                    RemoteMics.Feed(client, data, 1, count - 1);
                    break;
                // Mic audio with the phone's running sample count, so the host can tell when
                // packets were dropped: [3][index of the first sample u32 LE][16-bit mono PCM LE]
                case 3:
                    if (count >= 5)
                    {
                        long first = (uint) (data[1] | (data[2] << 8) | (data[3] << 16) | (data[4] << 24));
                        RemoteMics.Feed(client, data, 5, count - 5, first);
                    }
                    break;
            }
        }

        public void OnClientClosed(RemoteClient client)
        {
            RemoteMics.Stop(client);
            _mainThread.Enqueue(() => RemovePad(client));
        }

        #endregion

        #region Virtual gamepads (main thread)

        private void AddPad(RemoteClient client, PhoneState phone)
        {
            if (client.IsClosed || phone.Pad != null)
            {
                return;
            }

            Gamepad pad = null;
            try
            {
                pad = InputSystem.AddDevice(new InputDeviceDescription
                {
                    interfaceName = INTERFACE_NAME,
                    deviceClass = "Gamepad",
                    manufacturer = "YARG Resurrected",
                    product = client.Name + " (phone)",
                    serial = client.Key
                }) as Gamepad;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] AddDevice with description failed: " + e.Message);
            }

            if (pad == null)
            {
                try
                {
                    pad = InputSystem.AddDevice<Gamepad>("YARGPhone" + client.Number);
                }
                catch (Exception e)
                {
                    Debug.LogError("[YARG Remote] Could not create a gamepad for phone " + client.Number + ": " + e);
                    return;
                }
            }

            _remoteDevices.Add(pad);
            phone.Pad = pad;
            _padCount++;
            Debug.Log($"[YARG Remote] Phone {client.Number} '{client.Name}' is gamepad '{pad.displayName}'");
        }

        private void RemovePad(RemoteClient client)
        {
            if (_phones.TryRemove(client, out var phone))
            {
                OnPhoneLeft(phone);
            }
            if (phone != null && phone.Pad != null)
            {
                _padCount--;
                RemovePadDevice(phone.Pad);
                phone.Pad = null;
            }
        }

        private static void RemovePadDevice(Gamepad pad)
        {
            if (pad == null)
            {
                return;
            }

            _remoteDevices.Remove(pad);
            try
            {
                if (pad.added)
                {
                    InputSystem.RemoveDevice(pad);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] RemoveDevice failed: " + e.Message);
            }
        }

        // Runs right before the Input System processes its event queue. Each press is played once
        // its play time (touch time + hold) has come, stamped with exactly that time, so Wi-Fi
        // jitter doesn't reach the game.
        private void OnBeforeInputUpdate()
        {
            if (_phones.IsEmpty)
            {
                return;
            }

            double nowMs = RemoteServer.NowMs;
            double nowInput = InputState.currentTime;
            double clockOffset = nowInput - nowMs / 1000.0; // host clock -> Input System clock

            while (_padPackets.TryDequeue(out var packet))
            {
                packet.Phone.Held.Enqueue(packet);
            }

            foreach (var pair in _phones)
            {
                var phone = pair.Value;
                var pad = phone.Pad;
                if (pad == null || !pad.added)
                {
                    phone.Held.Clear();
                    continue;
                }

                while (phone.Held.Count > 0 && phone.Held.Peek().PlayTimeMs <= nowMs)
                {
                    var packet = phone.Held.Dequeue();

                    // Never before the previous update or the phone's previous event, never in the future.
                    double time = packet.PlayTimeMs / 1000.0 + clockOffset;
                    time = Math.Max(time, Math.Max(phone.LastEventTime, _lastInputUpdateTime));
                    time = Math.Min(time, nowInput);
                    phone.LastEventTime = time;

                    InputSystem.QueueStateEvent(pad, BuildState(packet.Buttons, packet.Whammy), time);
                }
            }

            _lastInputUpdateTime = nowInput;
        }

        // Layout matches YARG's "guitar" gamepad bindings (RB4InstrumentMapper/WiitarThing style):
        // frets A/B/Y/X/LB, strum D-pad up/down, Star Power Select, whammy right stick X.
        private static GamepadState BuildState(uint buttons, byte whammy)
        {
            var state = new GamepadState();
            state = state.WithButton(GamepadButton.South, (buttons & (1u << BIT_GREEN)) != 0);
            state = state.WithButton(GamepadButton.East, (buttons & (1u << BIT_RED)) != 0);
            state = state.WithButton(GamepadButton.North, (buttons & (1u << BIT_YELLOW)) != 0);
            state = state.WithButton(GamepadButton.West, (buttons & (1u << BIT_BLUE)) != 0);
            state = state.WithButton(GamepadButton.LeftShoulder, (buttons & (1u << BIT_ORANGE)) != 0);
            state = state.WithButton(GamepadButton.DpadUp, (buttons & (1u << BIT_UP)) != 0);
            state = state.WithButton(GamepadButton.DpadDown, (buttons & (1u << BIT_DOWN)) != 0);
            state = state.WithButton(GamepadButton.DpadLeft, (buttons & (1u << BIT_LEFT)) != 0);
            state = state.WithButton(GamepadButton.DpadRight, (buttons & (1u << BIT_RIGHT)) != 0);
            state = state.WithButton(GamepadButton.Start, (buttons & (1u << BIT_START)) != 0);
            state = state.WithButton(GamepadButton.Select, (buttons & (1u << BIT_SELECT)) != 0);

            // Whammy bar at rest = -1, fully pressed = +1 (like Xbox 360 guitars).
            state.rightStick = new Vector2(whammy / 127.5f - 1f, 0f);
            return state;
        }

        #endregion

        #region Loading

        private static byte[] LoadPage()
        {
            var asset = Resources.Load<TextAsset>("YargRemote/remote");
            if (asset == null)
            {
                Debug.LogError("[YARG Remote] Resources/YargRemote/remote.html is missing.");
                return Encoding.UTF8.GetBytes("<h1>YARG Remote page missing from this build.</h1>");
            }
            return asset.bytes;
        }

        private static X509Certificate2 LoadCertificate()
        {
            try
            {
                var asset = Resources.Load<TextAsset>("YargRemote/cert");
                if (asset == null)
                {
                    Debug.LogWarning("[YARG Remote] No certificate in this build; phone mics need https and won't work.");
                    return null;
                }

                var certificate = new X509Certificate2(asset.bytes, CERT_PASSWORD, X509KeyStorageFlags.Exportable);
                if (!certificate.HasPrivateKey)
                {
                    Debug.LogError("[YARG Remote] Certificate has no private key.");
                    return null;
                }
                return certificate;
            }
            catch (Exception e)
            {
                Debug.LogError("[YARG Remote] Could not load the certificate: " + e);
                return null;
            }
        }

        #endregion

        #region Addresses

        private void RefreshAddresses()
        {
            _nextAddressRefresh = Time.unscaledTime + 5f;
            try
            {
                _addresses = GetLocalAddresses();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] Could not read IP addresses: " + e.Message);
            }
        }

        // Private IPv4 addresses, Wi-Fi first, then hotspot. Mobile-data interfaces are skipped
        // because other phones can't reach them.
        private static List<string> GetLocalAddresses()
        {
            var wifi = new List<string>();
            var other = new List<string>();

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up &&
                        nic.OperationalStatus != OperationalStatus.Unknown)
                    {
                        continue;
                    }

                    string name = (nic.Name ?? "").ToLowerInvariant();
                    if (name.StartsWith("rmnet") || name.StartsWith("ccmni") || name.StartsWith("dummy") ||
                        name.StartsWith("lo") || name.StartsWith("tun") || name.StartsWith("ipsec") ||
                        name.StartsWith("p2p"))
                    {
                        continue;
                    }

                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        var address = unicast.Address;
                        if (address.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(address))
                        {
                            continue;
                        }

                        string text = address.ToString();
                        if (wifi.Contains(text) || other.Contains(text))
                        {
                            continue;
                        }

                        if (name.StartsWith("wlan"))
                        {
                            wifi.Add(text);
                        }
                        else
                        {
                            other.Add(text); // e.g. swlan0 = Samsung hotspot
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] Listing network interfaces failed: " + e.Message);
            }

            wifi.AddRange(other);

            if (wifi.Count == 0)
            {
                // Fallback: ask the OS which local address it would use for the default route.
                try
                {
                    using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        socket.Connect(IPAddress.Parse("8.8.8.8"), 53);
                        if (socket.LocalEndPoint is IPEndPoint endPoint && IsPrivate(endPoint.Address))
                        {
                            wifi.Add(endPoint.Address.ToString());
                        }
                    }
                }
                catch (Exception)
                {
                    // no network
                }
            }

            return wifi;
        }

        private static bool IsPrivate(IPAddress address)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10 ||
                (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                (b[0] == 192 && b[1] == 168);
        }

        #endregion

        #region Overlay

        private void CreateOverlay()
        {
            var canvasObject = new GameObject("YARG Remote Overlay");
            canvasObject.transform.SetParent(transform, false);

            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(canvasObject.transform, false);
            _panel = panelObject.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0.5f, 0f);
            _panel.anchorMax = new Vector2(0.5f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.anchoredPosition = new Vector2(0f, 96f); // just above YARG's button-hint bar

            var image = panelObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.7f);
            var button = panelObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OpenLobby);

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(panelObject.transform, false);
            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 8f);
            textRect.offsetMax = new Vector2(-24f, -8f);

            _text = textObject.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 30;
            _text.color = Color.white;
            _text.alignment = TextAlignmentOptions.Center;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.raycastTarget = false;
            _text.richText = true;
        }

        private void UpdateOverlay()
        {
            if (_canvas == null || _text == null)
            {
                return;
            }

            // Only on the menus, never over a song.
            bool inMenus = !SceneManager.GetSceneByName("Gameplay").isLoaded &&
                !SceneManager.GetSceneByName("CalibrationScene").isLoaded &&
                !SceneManager.GetSceneByName("ScoreScene").isLoaded &&
                SceneManager.GetSceneByName("MenuScene").isLoaded;
            if (_canvas.enabled != inMenus)
            {
                _canvas.enabled = inMenus;
            }
            if (!inMenus)
            {
                return;
            }

            string text = BuildOverlayText();
            if (text == _lastText)
            {
                return;
            }
            _lastText = text;

            _text.text = text;
            var size = _text.GetPreferredValues(text);
            _panel.sizeDelta = new Vector2(size.x + 56f, size.y + 20f);
        }

        private string BuildOverlayText()
        {
            if (_server == null || !_server.IsRunning)
            {
                return "PHONES: couldn't start the phone server";
            }

            int phones = _padCount;
            int mics = RemoteMics.ActiveCount;
            string counts = phones == 0 ? "none connected"
                : phones + (phones == 1 ? " phone" : " phones") + (mics > 0 ? $", {mics} mic{(mics == 1 ? "" : "s")}" : "");

            string address = PrimaryAddress();
            if (address == null)
            {
                return $"<b>PHONES</b>  Tap here to let friends join with their phones  |  {counts}";
            }

            return $"<b>PHONES</b>  Tap here for the join codes, or open <color=#FFD84D>{BuildUrl(address)}</color>  |  {counts}";
        }

        private string BuildUrl(string address)
        {
            string scheme = _tlsState == -1 ? "http" : "https";
            return $"{scheme}://{address}:{_server.Port}";
        }

        #endregion

        #region Wi-Fi low-latency lock

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _wifiLock;
#endif

        // While phones are connected, ask Android to keep Wi-Fi out of power-save, which otherwise
        // adds 100+ ms spikes to incoming packets.
        private void SetWifiLock(bool on)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (on == (_wifiLock != null))
            {
                return;
            }

            try
            {
                if (on)
                {
                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                    using (var wifi = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        // WIFI_MODE_FULL_LOW_LATENCY = 4 (Android 10+), WIFI_MODE_FULL_HIGH_PERF = 3
                        int mode = version.GetStatic<int>("SDK_INT") >= 29 ? 4 : 3;
                        var wifiLock = wifi.Call<AndroidJavaObject>("createWifiLock", mode, "YARG remote play");
                        wifiLock.Call("setReferenceCounted", false);
                        wifiLock.Call("acquire");
                        _wifiLock = wifiLock;
                    }
                }
                else
                {
                    _wifiLock.Call("release");
                    _wifiLock.Dispose();
                    _wifiLock = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[YARG Remote] Wi-Fi lock failed: " + e.Message);
                if (on)
                {
                    // Don't retry every frame.
                    _wifiLock = new AndroidJavaObject("java.lang.Object");
                }
                else
                {
                    _wifiLock = null;
                }
            }
#endif
        }

        #endregion
    }
}
