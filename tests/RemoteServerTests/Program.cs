// Tests RemoteServer.cs (the phone remote server) outside Unity.
//   dotnet run --project tests/RemoteServerTests -- <cert.bytes>
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YARG.Remote;

namespace RemoteServerTests
{
    internal sealed class Handler : IRemoteServerHandler
    {
        public readonly BlockingCollection<string> Events = new BlockingCollection<string>();

        public void OnClientHello(RemoteClient c) =>
            Events.Add($"hello {c.Number} {c.Name} {(c.Secure ? "https" : "http")}");

        public void OnClientText(RemoteClient c, string m) =>
            Events.Add($"text {c.Number} {m.Replace('\n', '|')}");

        public void OnClientBinary(RemoteClient c, byte[] d, int n) =>
            Events.Add($"binary {c.Number} {n} {d[0]}");

        public void OnClientClosed(RemoteClient c) =>
            Events.Add($"closed {c.Number}");
    }

    internal static class Program
    {
        private static int _checks;

        private static void Check(bool condition, string what)
        {
            _checks++;
            if (!condition)
            {
                throw new Exception("FAILED: " + what);
            }
            Console.WriteLine("ok   " + what);
        }

        private static void Expect(Handler handler, params string[] expected)
        {
            var want = new HashSet<string>(expected);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (want.Count > 0)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !handler.Events.TryTake(out var ev, left))
                {
                    throw new Exception("FAILED: timed out waiting for: " + string.Join(", ", want));
                }
                if (!want.Remove(ev))
                {
                    throw new Exception($"FAILED: unexpected event '{ev}', waiting for: {string.Join(", ", want)}");
                }
            }
            _checks++;
            Console.WriteLine("ok   events: " + string.Join(", ", expected));
        }

        // The next event, which must come within 5 seconds
        private static string Take(Handler handler)
        {
            if (!handler.Events.TryTake(out var ev, TimeSpan.FromSeconds(5)))
            {
                throw new Exception("FAILED: timed out waiting for an event");
            }
            return ev;
        }

        private static void ExpectInOrder(Handler handler, params string[] expected)
        {
            foreach (var want in expected)
            {
                var ev = Take(handler);
                if (ev != want)
                {
                    throw new Exception($"FAILED: expected '{want}' next, got '{ev}' (wanted, in order: {string.Join(", ", expected)})");
                }
            }
            _checks++;
            Console.WriteLine("ok   events in order: " + string.Join(", ", expected));
        }

        // True once the server has ended the connection (with or without a closing handshake)
        private static async Task<bool> ClosedByServer(ClientWebSocket ws)
        {
            var buffer = new byte[256];
            using var cts = new CancellationTokenSource(5000);
            try
            {
                while (true)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return true;
                    }
                }
            }
            catch (WebSocketException)
            {
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static Task SendText(ClientWebSocket ws, string text) =>
            ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, CancellationToken.None);

        private static async Task<string> ReceiveText(ClientWebSocket ws)
        {
            var buffer = new byte[4096];
            using var cts = new CancellationTokenSource(5000);
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        private static async Task<ClientWebSocket> Connect(string url)
        {
            var ws = new ClientWebSocket();
            ws.Options.RemoteCertificateValidationCallback = (a, b, c, d) => true;
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            return ws;
        }

        private static async Task<int> Main(string[] args)
        {
            try
            {
                StrumBounceTests.Run(Check);
                GuitarVoiceTests.Run(Check, args.Length > 1 ? args[1] : null);
                await Run(args[0]);
                Console.WriteLine($"ALL {_checks} CHECKS PASSED");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
        }

        private static async Task Run(string certPath)
        {
            var cert = new X509Certificate2(File.ReadAllBytes(certPath), "yarg", X509KeyStorageFlags.Exportable);
            Check(cert.HasPrivateKey, "certificate has a private key");

            const string pageText = "<html>test page</html>";
            var handler = new Handler();
            using var server = new RemoteServer(cert, Encoding.UTF8.GetBytes(pageText), handler,
                m => Console.WriteLine("     [server] " + m));

            // Port 17777 is taken, so the server must move on to the next one.
            var blocker = new System.Net.Sockets.TcpListener(IPAddress.Any, 17777);
            blocker.Start();
            Check(server.Start(17777), "server starts");
            Check(server.Port == 17778, "skips a busy port (got " + server.Port + ")");
            blocker.Stop();
            int port = server.Port;

            using (var http = new HttpClient())
            {
                Check(await http.GetStringAsync($"http://127.0.0.1:{port}/") == pageText, "http page");
                var missing = await http.GetAsync($"http://127.0.0.1:{port}/nope");
                Check(missing.StatusCode == HttpStatusCode.NotFound, "404 for unknown path");
                Check(await http.GetStringAsync($"http://127.0.0.1:{port}/health") == "ok", "health");
            }

            var insecure = new HttpClientHandler { ServerCertificateCustomValidationCallback = (a, b, c, d) => true };
            using (var https = new HttpClient(insecure))
            {
                Check(await https.GetStringAsync($"https://127.0.0.1:{port}/") == pageText, "https page");
            }

            Check(server.RunTlsSelfTest(), "TLS self-test");

            // Phone 1 over wss
            var alice = await Connect($"wss://127.0.0.1:{port}/ws");
            await SendText(alice, "hello\nkey-1\nAlice");
            var welcome = await ReceiveText(alice);
            Check(welcome == "welcome\n1\n1\n1\nAlice", "welcome over wss: " + welcome.Replace('\n', '|'));
            Expect(handler, "hello 1 Alice https");

            await alice.SendAsync(new ArraySegment<byte>(new byte[] { 1, 0x41, 0, 0, 0, 200 }), WebSocketMessageType.Binary, true, CancellationToken.None);
            Expect(handler, "binary 1 6 1");

            var audio = new byte[1 + 1920];
            audio[0] = 2;
            await alice.SendAsync(new ArraySegment<byte>(audio), WebSocketMessageType.Binary, true, CancellationToken.None);
            Expect(handler, "binary 1 1921 2");

            var big = new byte[70000];
            big[0] = 2;
            await alice.SendAsync(new ArraySegment<byte>(big), WebSocketMessageType.Binary, true, CancellationToken.None);
            Expect(handler, "binary 1 70000 2");

            double before = RemoteServer.NowMs;
            await SendText(alice, "ping\n123");
            var pong = (await ReceiveText(alice)).Split('\n');
            Check(pong.Length == 3 && pong[0] == "pong" && pong[1] == "123", "ping/pong");
            double hostMs = double.Parse(pong[2], System.Globalization.CultureInfo.InvariantCulture);
            Check(hostMs >= before && hostMs <= RemoteServer.NowMs, "pong carries the host clock");

            await SendText(alice, "ping\n124\n7.5");
            Check((await ReceiveText(alice)).StartsWith("pong\n124\n"), "ping with round trip");
            Expect(handler, "text 1 ping|124|7.5");

            await SendText(alice, "mic\n48000");
            Expect(handler, "text 1 mic|48000");

            // A message split into two frames
            await alice.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("mic")), WebSocketMessageType.Text, false, CancellationToken.None);
            await alice.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("off")), WebSocketMessageType.Text, true, CancellationToken.None);
            Expect(handler, "text 1 micoff");

            // Phone 2 over plain ws, with a name that needs cleaning
            var bob = await Connect($"ws://127.0.0.1:{port}/ws");
            await SendText(bob, "hello\nkey-2\n<b>Bob</b>\u0007");
            var welcome2 = await ReceiveText(bob);
            Check(welcome2 == "welcome\n2\n0\n1\nbBob/b", "welcome over ws: " + welcome2.Replace('\n', '|'));
            Expect(handler, "hello 2 bBob/b http");

            // Messages before hello are ignored
            var early = await Connect($"ws://127.0.0.1:{port}/ws");
            await early.SendAsync(new ArraySegment<byte>(new byte[] { 1, 1, 0, 0, 0, 0 }), WebSocketMessageType.Binary, true, CancellationToken.None);
            await SendText(early, "mic\n48000");
            await Task.Delay(300);
            Check(handler.Events.Count == 0, "no events before hello");
            Check(server.ClientCount == 2, "two phones joined (" + server.ClientCount + ")");
            early.Abort();

            // Alice's phone reconnects: keeps number 1, the old socket is dropped. The game hears of
            // the old connection closing first, so the phone's controller is handed over in order
            // (the other way round, mid-song, the player was left without a controller).
            var alice2 = await Connect($"wss://127.0.0.1:{port}/ws");
            await SendText(alice2, "hello\nkey-1\nAlice");
            Check(await ReceiveText(alice2) == "welcome\n1\n1\n1\nAlice", "reconnect keeps player number");
            ExpectInOrder(handler, "closed 1", "hello 1 Alice https");

            // The old connection is told why (so a second tab of the same phone stays off instead of
            // taking the connection back), anything more from it is ignored, and then it is closed
            Check(await ReceiveText(alice) == "replaced", "old connection is told it was replaced");
            await alice.SendAsync(new ArraySegment<byte>(new byte[] { 1, 1, 0, 0, 0, 0 }), WebSocketMessageType.Binary, true, CancellationToken.None);
            await SendText(alice, "mic\n48000");
            await Task.Delay(300);
            Check(handler.Events.Count == 0, "a replaced connection is not heard any more");
            Check(server.ClientCount == 2, "a replaced connection is not counted (" + server.ClientCount + ")");
            Check(await ClosedByServer(alice), "old connection is closed");

            // Phone without a name gets a default one
            var nameless = await Connect($"ws://127.0.0.1:{port}/ws");
            await SendText(nameless, "hello\nkey-3\n   ");
            Check(await ReceiveText(nameless) == "welcome\n3\n0\n1\nPhone 3", "default name");
            Expect(handler, "hello 3 Phone 3 http");

            // Clean close
            await bob.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            Expect(handler, "closed 2");

            // Abrupt disconnect
            nameless.Abort();
            Expect(handler, "closed 3");

            // Reconnecting while the old connection is still open, many times: always "closed" for
            // the old connection, then "hello" for the new one, and each connection's close only once
            // (this came out the wrong way round about half the time)
            const int rounds = 30;
            int inOrder = 0;
            for (int i = 0; i < rounds; i++)
            {
                var first = await Connect($"ws://127.0.0.1:{port}/ws");
                await SendText(first, $"hello\nround-{i}\nRay");
                await ReceiveText(first);
                var joined = Take(handler);
                int n = int.Parse(joined.Split(' ')[1]);
                var again = await Connect($"ws://127.0.0.1:{port}/ws");
                await SendText(again, $"hello\nround-{i}\nRay");
                await ReceiveText(again);
                if (Take(handler) == $"closed {n}" && Take(handler) == $"hello {n} Ray http")
                {
                    inOrder++;
                }
                first.Abort();
                again.Abort();
                if (Take(handler) != $"closed {n}")
                {
                    throw new Exception("FAILED: the new connection's close was not reported");
                }
            }
            await Task.Delay(300);
            Check(inOrder == rounds && handler.Events.Count == 0,
                $"reconnects are announced in order, each close once ({inOrder} of {rounds})");

            // QR codes: expected modules come from a reference encoder whose output a real QR
            // decoder (OpenCV) read back correctly.
            var qrVectors = new (string text, string rows)[]
            {
                ("HELLO", "111111100101101111111/100000100111001000001/101110101101101011101/101110100101001011101/101110100010101011101/100000100000101000001/111111101010101111111/000000001101100000000/111011111111011000100/000100001000001000010/101000100010100011111/110010001010001000010/101001100110101010100/000000001101010100110/111111101001011100111/100000101111110110000/101110101001011100111/101110100010001100110/101110101110100010101/100000101100001010010/111111101100101100111"),
                ("https://192.168.43.1:7777", "1111111001001001101111111/1000001000111001001000001/1011101011101000001011101/1011101001110001101011101/1011101000100111101011101/1000001001000011001000001/1111111010101010101111111/0000000011101100100000000/1110111110110111111000100/1101010000110110001100001/1100011011000010100110111/1111100000010111000100010/0100101010001111001101011/0010010101011010001101001/1000111000011110101100111/0101010010101100111010010/1001101000011110111111000/0000000011110110100011011/1111111011100101101011011/1000001010001111100011000/1011101011000110111111000/1011101001100110010111100/1011101010000100100010001/1000001011101110101011010/1111111011000110100100011"),
                ("WIFI:T:WPA;S:AndroidShare_1234;P:abcdefgh1234;;", "11111110001000110011001111111/10000010010001111101001000001/10111010100100111000101011101/10111010010010110010001011101/10111010010010110001001011101/10000010001111110001101000001/11111110101010101010101111111/00000000100101100100000000000/11101111100011001100011000100/10100001010111001101101101110/10100110101111000000100101111/00001101111011010111111101011/01000010001111001000111111011/11011000011101001010110001110/01001011001001001100001100011/11110000000001100111000110000/10111111000101010111111101100/00100100110000000110111100010/10110010100000101010010110111/01100101111001010110111101001/10000110001101100111111111011/00000000110001111111100011100/11111110100010100111101010011/10000010101111100100100010001/10111010101001110110111111110/10111010011001100111100100011/10111010100001101110100111001/10000010101001011110100100010/11111110100001100111101011111"),
                (new string('y', 106), "1111111001010101010101010101001111111/1000001001011101110111011101101000001/1011101010001000100010001000101011101/1011101000101010101010101010101011101/1011101001010101010101010101001011101/1000001001011101110111011101101000001/1111111010101010101010101010101111111/0000000011110111011101110111000000000/1110111110110101010101010101011000100/0110100010001010101010101010101001101/0101011100000010001000100010000111111/0010010111010111011101110111000110000/0100011100010101010101010101011101000/1110110011001010101010101010101001101/1001011000000010001000100010000111111/0101110001110111011101110111000110000/1101011011110101010101010101011101000/1011010011001010101010101010101001101/0010101111000010001000100010000111111/1010000000010111011101110111000110000/1111001100010101010101010101011101000/1000010000001010101010101010101001101/1000111110000010001000100010000111111/0011100010110111011101110111000110000/0010111111110101010101010101011101000/0110110100101010101010101010101001101/1001111101100010001000100010000111111/0110000000010111011101110111000110000/1000011011110101010101010101111111000/0000000010001010101010101010100011101/1111111011100010001000100011101011111/1000001010110111011101110110100010011/1011101011110101010101010100111111000/1011101000001010101010101010000011111/1011101010000010001000100010010111111/1000001010110111011101110110111000010/1111111011010101010101010101111001011"),
            };
            foreach (var (text, rows) in qrVectors)
            {
                var modules = RemoteQr.Encode(Encoding.UTF8.GetBytes(text));
                var expected = rows.Split('/');
                bool same = modules != null && modules.GetLength(0) == expected.Length;
                for (int y = 0; same && y < expected.Length; y++)
                {
                    for (int x = 0; x < expected.Length; x++)
                    {
                        if (modules[y, x] != (expected[y][x] == '1'))
                        {
                            same = false;
                            break;
                        }
                    }
                }
                Check(same, $"QR code matches reference ({text.Length} bytes, {expected.Length}x{expected.Length})");
            }
            Check(RemoteQr.Encode(new byte[RemoteQr.MAX_BYTES + 1]) == null, "QR refuses data that is too long");

            // Timing
            var estimator = new DelayEstimator();
            Check(estimator.EstimateMs() == DelayEstimator.DEFAULT_MS, "default hold before any samples");
            for (int i = 0; i < 18; i++) estimator.AddSample(8);
            estimator.AddSample(12);
            estimator.AddSample(300); // one Wi-Fi hiccup must not set the hold time
            Check(estimator.EstimateMs() == 12 + DelayEstimator.MARGIN_MS, "hold = 90th percentile + margin (" + estimator.EstimateMs() + ")");
            estimator.AddSample(-5);
            estimator.AddSample(double.NaN);
            Check(estimator.EstimateMs() == 12 + DelayEstimator.MARGIN_MS, "bad samples ignored");
            var slow = new DelayEstimator();
            for (int i = 0; i < 10; i++) slow.AddSample(400);
            Check(slow.EstimateMs() == DelayEstimator.MAX_MS, "hold is capped");
            var fast = new DelayEstimator();
            for (int i = 0; i < 10; i++) fast.AddSample(0.5);
            Check(fast.EstimateMs() == DelayEstimator.MIN_MS, "hold has a floor");

            Check(RemoteTiming.PlayTimeMs(1000, 1009, 15, out double d1) == 1015 && d1 == 9, "press played at touch + hold");
            Check(RemoteTiming.PlayTimeMs(1000, 1040, 15, out double d2) == 1015 && d2 == 40, "late press keeps its true time");
            Check(RemoteTiming.PlayTimeMs(0, 1009, 15, out double d3) == 1009 && d3 == -1, "unsynced phone uses arrival time");
            Check(RemoteTiming.PlayTimeMs(1100, 1009, 15, out double d4) == 1009 && d4 == -1, "future touch time rejected");
            Check(RemoteTiming.PlayTimeMs(100, 1009, 15, out double d5) == 1009 && d5 == -1, "stale touch time rejected");
            Check(RemoteTiming.PlayTimeMs(double.NaN, 1009, 15, out double d6) == 1009 && d6 == -1, "NaN touch time rejected");

            Check(RemoteServer.ComputeWebSocketAccept("dGhlIHNhbXBsZSBub25jZQ==") == "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=",
                "RFC 6455 accept key");
        }
    }
}
