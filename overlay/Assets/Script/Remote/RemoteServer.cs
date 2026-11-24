// YARG Resurrected - "phone as a controller / mic" server.
//
// Plain .NET only (no UnityEngine), so it can be compiled and tested outside Unity
// (see tests/RemoteServerTests). One TCP port serves both:
//   * HTTPS (if a certificate is available) - needed because browsers only allow the
//     microphone on secure pages, and
//   * plain HTTP - the controller still works if TLS isn't available.
// The first byte of each connection tells them apart (0x16 = TLS handshake).
//
// Routes:  GET /        the phone page
//          GET /health  "ok" (used by the TLS self-test)
//          GET /ws      WebSocket used by the page
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace YARG.Remote
{
    /// <summary>
    /// Receives events from <see cref="RemoteServer"/>. Every method is called on a network thread.
    /// </summary>
    public interface IRemoteServerHandler
    {
        /// <summary>A phone joined (sent its name). <see cref="RemoteClient.Number"/> is set.</summary>
        void OnClientHello(RemoteClient client);

        /// <summary>A text message other than the built-in hello/ping.</summary>
        void OnClientText(RemoteClient client, string message);

        /// <summary>A binary message. <paramref name="data"/> is reused after this returns.</summary>
        void OnClientBinary(RemoteClient client, byte[] data, int count);

        /// <summary>A phone that had joined disconnected.</summary>
        void OnClientClosed(RemoteClient client);
    }

    /// <summary>
    /// One connected phone (one WebSocket).
    /// </summary>
    public sealed class RemoteClient
    {
        private readonly TcpClient _tcp;
        private readonly Stream _stream;
        private readonly object _writeLock = new object();
        private volatile bool _closed;

        public int Number { get; internal set; }
        public string Key { get; internal set; } = "";
        public string Name { get; internal set; } = "";
        public bool Secure { get; }
        public string RemoteAddress { get; }
        public bool IsClosed => _closed;
        public bool HasJoined => Number > 0;

        internal RemoteClient(TcpClient tcp, Stream stream, bool secure, string remoteAddress)
        {
            _tcp = tcp;
            _stream = stream;
            Secure = secure;
            RemoteAddress = remoteAddress;
        }

        public bool SendText(string text)
        {
            var payload = Encoding.UTF8.GetBytes(text);
            return SendFrame(0x1, payload, payload.Length);
        }

        internal bool SendFrame(int opcode, byte[] payload, int count)
        {
            lock (_writeLock)
            {
                if (_closed)
                {
                    return false;
                }

                try
                {
                    var header = new byte[10];
                    int headerLength;
                    header[0] = (byte) (0x80 | (opcode & 0x0F));
                    if (count < 126)
                    {
                        header[1] = (byte) count;
                        headerLength = 2;
                    }
                    else if (count <= 0xFFFF)
                    {
                        header[1] = 126;
                        header[2] = (byte) (count >> 8);
                        header[3] = (byte) count;
                        headerLength = 4;
                    }
                    else
                    {
                        header[1] = 127;
                        long length = count;
                        for (int i = 0; i < 8; i++)
                        {
                            header[9 - i] = (byte) (length >> (8 * i));
                        }
                        headerLength = 10;
                    }

                    _stream.Write(header, 0, headerLength);
                    if (count > 0)
                    {
                        _stream.Write(payload, 0, count);
                    }
                    _stream.Flush();
                    return true;
                }
                catch (Exception)
                {
                    Close();
                    return false;
                }
            }
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;

            try
            {
                _tcp.Close();
            }
            catch (Exception)
            {
                // already gone
            }
        }
    }

    public sealed class RemoteServer : IDisposable
    {
        public const string WEBSOCKET_PATH = "/ws";
        public const int MAX_NAME_LENGTH = 20;

        private const string WEBSOCKET_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int MAX_MESSAGE_BYTES = 1 << 20;
        private const int MAX_HEADER_BYTES = 16 * 1024;
        private const int IDLE_TIMEOUT_MS = 20000;

        private readonly X509Certificate2 _certificate;
        private readonly byte[] _page;
        private readonly IRemoteServerHandler _handler;
        private readonly Action<string> _log;

        private readonly List<RemoteClient> _clients = new List<RemoteClient>();
        private readonly Dictionary<string, int> _numbersByKey = new Dictionary<string, int>();
        private int _lastNumber;

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        public int Port { get; private set; }

        /// <summary>True when a certificate was supplied, so https:// links can work.</summary>
        public bool TlsConfigured => _certificate != null;

        public bool IsRunning => _running;

        public RemoteServer(X509Certificate2 certificate, byte[] page, IRemoteServerHandler handler, Action<string> log)
        {
            _certificate = certificate;
            _page = page ?? Encoding.UTF8.GetBytes("<h1>YARG Remote</h1>");
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// Listens on the first free port from <paramref name="firstPort"/> upwards.
        /// </summary>
        public bool Start(int firstPort, int attempts = 10)
        {
            for (int i = 0; i < attempts && _listener == null; i++)
            {
                int port = firstPort + i;
                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    _listener = listener;
                    Port = port;
                }
                catch (SocketException e)
                {
                    _log($"Port {port} unavailable: {e.Message}");
                }
            }

            if (_listener == null)
            {
                return false;
            }

            _running = true;
            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "YARG Remote accept"
            };
            _acceptThread.Start();
            _log($"Listening on port {Port} (TLS {(TlsConfigured ? "on" : "off")})");
            return true;
        }

        public int ClientCount
        {
            get
            {
                lock (_clients)
                {
                    int count = 0;
                    foreach (var client in _clients)
                    {
                        if (client.HasJoined && !client.IsClosed)
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch (Exception)
            {
                // ignore
            }

            RemoteClient[] clients;
            lock (_clients)
            {
                clients = _clients.ToArray();
            }
            foreach (var client in clients)
            {
                client.Close();
            }
        }

        /// <summary>
        /// Connects to this server over TLS from the same device. Returns false if TLS doesn't work,
        /// so the UI can fall back to the http:// link. Blocking; call from a worker thread.
        /// </summary>
        public bool RunTlsSelfTest(int timeoutMs = 8000)
        {
            if (_certificate == null || !_running)
            {
                return false;
            }

            try
            {
                using (var tcp = new TcpClient())
                {
                    var connect = tcp.BeginConnect(IPAddress.Loopback, Port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(timeoutMs))
                    {
                        _log("TLS self-test: connect timed out");
                        return false;
                    }
                    tcp.EndConnect(connect);
                    tcp.ReceiveTimeout = timeoutMs;
                    tcp.SendTimeout = timeoutMs;

                    using (var ssl = new SslStream(tcp.GetStream(), false, (s, c, ch, e) => true))
                    {
                        ssl.AuthenticateAsClient("yarg.local");
                        var request = Encoding.ASCII.GetBytes("GET /health HTTP/1.1\r\nHost: yarg.local\r\nConnection: close\r\n\r\n");
                        ssl.Write(request, 0, request.Length);
                        ssl.Flush();

                        var reader = new StreamReader(ssl, Encoding.ASCII);
                        string status = reader.ReadLine();
                        bool ok = status != null && status.Contains(" 200 ");
                        _log("TLS self-test: " + (ok ? "OK" : "bad response: " + status));
                        return ok;
                    }
                }
            }
            catch (Exception e)
            {
                _log("TLS self-test failed: " + e);
                return false;
            }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient tcp;
                try
                {
                    tcp = _listener.AcceptTcpClient();
                }
                catch (Exception e)
                {
                    if (!_running)
                    {
                        break;
                    }
                    _log("Accept failed: " + e.Message);
                    Thread.Sleep(200);
                    continue;
                }

                var thread = new Thread(() => HandleConnection(tcp))
                {
                    IsBackground = true,
                    Name = "YARG Remote client"
                };
                thread.Start();
            }
        }

        private void HandleConnection(TcpClient tcp)
        {
            RemoteClient client = null;
            Stream stream = null;
            string remote = "?";

            try
            {
                remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
                tcp.NoDelay = true;
                tcp.ReceiveTimeout = IDLE_TIMEOUT_MS;
                tcp.SendTimeout = 5000;

                // Peek at the first byte: 0x16 starts a TLS handshake, anything else is plain HTTP.
                var first = new byte[1];
                if (tcp.Client.Receive(first, 0, 1, SocketFlags.Peek) <= 0)
                {
                    return;
                }

                stream = tcp.GetStream();
                bool secure = false;
                if (first[0] == 0x16)
                {
                    if (_certificate == null)
                    {
                        return;
                    }

                    var ssl = new SslStream(stream, false);
                    ssl.AuthenticateAsServer(_certificate, false, SslProtocols.Tls12, false);
                    stream = ssl;
                    secure = true;
                }

                var request = ReadRequest(stream);
                if (request == null)
                {
                    return;
                }

                if (request.Path == WEBSOCKET_PATH && request.IsWebSocketUpgrade)
                {
                    if (!AcceptWebSocket(stream, request))
                    {
                        return;
                    }

                    client = new RemoteClient(tcp, stream, secure, remote);
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }
                    RunWebSocket(client, stream);
                }
                else
                {
                    ServeHttp(stream, request);
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
            {
                // Normal disconnects and timeouts.
            }
            catch (AuthenticationException e)
            {
                // Usually a browser rejecting the self-signed certificate before the user taps "Proceed".
                _log($"TLS handshake from {remote} failed: {e.Message}");
            }
            catch (Exception e)
            {
                _log($"Connection from {remote} failed: {e}");
            }
            finally
            {
                if (client != null)
                {
                    lock (_clients)
                    {
                        _clients.Remove(client);
                    }
                    client.Close();
                    if (client.HasJoined)
                    {
                        SafeCall(() => _handler.OnClientClosed(client));
                    }
                }

                try
                {
                    stream?.Dispose();
                }
                catch (Exception)
                {
                    // ignore
                }
                try
                {
                    tcp.Close();
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }

        #region HTTP

        private sealed class HttpRequest
        {
            public string Method = "";
            public string Path = "";
            public readonly Dictionary<string, string> Headers =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public bool IsWebSocketUpgrade =>
                Headers.TryGetValue("Upgrade", out var upgrade) &&
                upgrade.Trim().Equals("websocket", StringComparison.OrdinalIgnoreCase);
        }

        private static HttpRequest ReadRequest(Stream stream)
        {
            var bytes = new List<byte>(512);
            var one = new byte[1];
            while (true)
            {
                if (bytes.Count >= MAX_HEADER_BYTES)
                {
                    return null;
                }

                int read = stream.Read(one, 0, 1);
                if (read <= 0)
                {
                    return null;
                }
                bytes.Add(one[0]);

                int n = bytes.Count;
                if (n >= 4 && bytes[n - 4] == '\r' && bytes[n - 3] == '\n' && bytes[n - 2] == '\r' && bytes[n - 1] == '\n')
                {
                    break;
                }
            }

            string head = Encoding.ASCII.GetString(bytes.ToArray());
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return null;
            }

            var request = new HttpRequest { Method = requestLine[0] };
            string path = requestLine[1];
            int query = path.IndexOf('?');
            request.Path = query >= 0 ? path.Substring(0, query) : path;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    break;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                request.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            return request;
        }

        private void ServeHttp(Stream stream, HttpRequest request)
        {
            if (request.Method != "GET" && request.Method != "HEAD")
            {
                WriteResponse(stream, "405 Method Not Allowed", "text/plain", Encoding.ASCII.GetBytes("Method not allowed"), request);
                return;
            }

            switch (request.Path)
            {
                case "/":
                case "/index.html":
                    WriteResponse(stream, "200 OK", "text/html; charset=utf-8", _page, request);
                    break;
                case "/health":
                    WriteResponse(stream, "200 OK", "text/plain", Encoding.ASCII.GetBytes("ok"), request);
                    break;
                case "/favicon.ico":
                    WriteResponse(stream, "204 No Content", "text/plain", new byte[0], request);
                    break;
                default:
                    WriteResponse(stream, "404 Not Found", "text/plain", Encoding.ASCII.GetBytes("Not found"), request);
                    break;
            }
        }

        private static void WriteResponse(Stream stream, string status, string contentType, byte[] body, HttpRequest request)
        {
            var header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            header.Append("Content-Type: ").Append(contentType).Append("\r\n");
            header.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            header.Append("Cache-Control: no-store\r\n");
            header.Append("X-Content-Type-Options: nosniff\r\n");
            header.Append("Connection: close\r\n\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (request.Method != "HEAD" && body.Length > 0)
            {
                stream.Write(body, 0, body.Length);
            }
            stream.Flush();
        }

        #endregion

        #region WebSocket

        public static string ComputeWebSocketAccept(string key)
        {
            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + WEBSOCKET_GUID));
                return Convert.ToBase64String(hash);
            }
        }

        private static bool AcceptWebSocket(Stream stream, HttpRequest request)
        {
            if (!request.Headers.TryGetValue("Sec-WebSocket-Key", out var key) || string.IsNullOrWhiteSpace(key))
            {
                WriteResponse(stream, "400 Bad Request", "text/plain", Encoding.ASCII.GetBytes("Missing Sec-WebSocket-Key"), request);
                return false;
            }

            string response =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + ComputeWebSocketAccept(key) + "\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(response);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            return true;
        }

        private void RunWebSocket(RemoteClient client, Stream stream)
        {
            var header = new byte[8];
            var mask = new byte[4];
            var payload = new byte[4096];
            var fragments = new MemoryStream();
            int fragmentOpcode = 0;

            while (_running && !client.IsClosed)
            {
                ReadExactly(stream, header, 0, 2);
                bool fin = (header[0] & 0x80) != 0;
                int opcode = header[0] & 0x0F;
                bool masked = (header[1] & 0x80) != 0;
                long length = header[1] & 0x7F;

                if (length == 126)
                {
                    ReadExactly(stream, header, 0, 2);
                    length = (header[0] << 8) | header[1];
                }
                else if (length == 127)
                {
                    ReadExactly(stream, header, 0, 8);
                    length = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        length = (length << 8) | header[i];
                    }
                }

                if (!masked)
                {
                    throw new IOException("Client WebSocket frames must be masked");
                }
                if (length < 0 || length > MAX_MESSAGE_BYTES)
                {
                    throw new IOException("WebSocket message too large");
                }

                ReadExactly(stream, mask, 0, 4);

                int count = (int) length;
                if (payload.Length < count)
                {
                    payload = new byte[Math.Max(count, payload.Length * 2)];
                }
                ReadExactly(stream, payload, 0, count);
                for (int i = 0; i < count; i++)
                {
                    payload[i] ^= mask[i & 3];
                }

                switch (opcode)
                {
                    case 0x8: // close
                        client.SendFrame(0x8, payload, Math.Min(count, 2));
                        return;
                    case 0x9: // ping
                        client.SendFrame(0xA, payload, count);
                        break;
                    case 0xA: // pong
                        break;
                    case 0x0: // continuation
                        if (fragments.Length + count > MAX_MESSAGE_BYTES)
                        {
                            throw new IOException("WebSocket message too large");
                        }
                        fragments.Write(payload, 0, count);
                        if (fin)
                        {
                            var whole = fragments.ToArray();
                            fragments.SetLength(0);
                            Dispatch(client, fragmentOpcode, whole, whole.Length);
                        }
                        break;
                    case 0x1: // text
                    case 0x2: // binary
                        if (fin)
                        {
                            Dispatch(client, opcode, payload, count);
                        }
                        else
                        {
                            fragmentOpcode = opcode;
                            fragments.SetLength(0);
                            fragments.Write(payload, 0, count);
                        }
                        break;
                    default:
                        throw new IOException("Unknown WebSocket opcode " + opcode);
                }
            }
        }

        private void Dispatch(RemoteClient client, int opcode, byte[] data, int count)
        {
            if (opcode == 0x2)
            {
                if (client.HasJoined)
                {
                    SafeCall(() => _handler.OnClientBinary(client, data, count));
                }
                return;
            }

            string text = Encoding.UTF8.GetString(data, 0, count);
            string[] parts = text.Split('\n');

            if (!client.HasJoined)
            {
                // First message must be: hello \n <key> \n <name>
                if (parts.Length >= 3 && parts[0] == "hello")
                {
                    Join(client, parts[1], parts[2]);
                }
                return;
            }

            if (parts[0] == "ping")
            {
                client.SendText("pong\n" + (parts.Length > 1 ? parts[1] : ""));
                return;
            }

            SafeCall(() => _handler.OnClientText(client, text));
        }

        private void Join(RemoteClient client, string rawKey, string rawName)
        {
            string key = SanitizeKey(rawKey);
            RemoteClient previous = null;

            lock (_clients)
            {
                // A phone reconnecting before its old socket timed out: drop the old one.
                foreach (var other in _clients)
                {
                    if (other != client && other.HasJoined && other.Key == key && !other.IsClosed)
                    {
                        previous = other;
                        break;
                    }
                }

                if (!_numbersByKey.TryGetValue(key, out int number))
                {
                    number = ++_lastNumber;
                    _numbersByKey[key] = number;
                }

                client.Key = key;
                client.Name = SanitizeName(rawName, number);
                client.Number = number;
            }

            previous?.Close();

            client.SendText($"welcome\n{client.Number}\n{(client.Secure ? 1 : 0)}\n{(TlsConfigured ? 1 : 0)}\n{client.Name}");
            _log($"Phone {client.Number} '{client.Name}' joined from {client.RemoteAddress} ({(client.Secure ? "https" : "http")})");
            SafeCall(() => _handler.OnClientHello(client));
        }

        public static string SanitizeKey(string key)
        {
            var builder = new StringBuilder();
            foreach (char c in key ?? "")
            {
                if (builder.Length >= 64)
                {
                    break;
                }
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')
                {
                    builder.Append(c);
                }
            }
            return builder.Length > 0 ? builder.ToString() : Guid.NewGuid().ToString("N");
        }

        public static string SanitizeName(string name, int number)
        {
            var builder = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (builder.Length >= MAX_NAME_LENGTH)
                {
                    break;
                }
                // No control characters, and nothing that could be read as rich-text markup.
                if (!char.IsControl(c) && c != '<' && c != '>' && c != '\\')
                {
                    builder.Append(c);
                }
            }

            string clean = builder.ToString().Trim();
            return clean.Length > 0 ? clean : "Phone " + number;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read <= 0)
                {
                    throw new IOException("Connection closed");
                }
                offset += read;
                count -= read;
            }
        }

        #endregion

        private void SafeCall(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                _log("Handler error: " + e);
            }
        }
    }
}
