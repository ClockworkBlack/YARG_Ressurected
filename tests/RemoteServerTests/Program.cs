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

            // Alice's phone reconnects: keeps number 1, the old socket is dropped
            var alice2 = await Connect($"wss://127.0.0.1:{port}/ws");
            await SendText(alice2, "hello\nkey-1\nAlice");
            Check(await ReceiveText(alice2) == "welcome\n1\n1\n1\nAlice", "reconnect keeps player number");
            Expect(handler, "hello 1 Alice https", "closed 1");

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
