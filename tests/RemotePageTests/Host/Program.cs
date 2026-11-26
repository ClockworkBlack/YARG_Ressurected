// The game's side for tests/RemotePageTests: the real RemoteServer serving the real phone page,
// answering the page the way RemotePlayManager does, and printing every protocol event as one
// JSON line. stdin commands: "ingame on|off", "kick <n>", "send <n> <text with \n escapes>", "quit".
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using YARG.Remote;

internal sealed class Host : IRemoteServerHandler
{
    public volatile bool InSong;
    public readonly ConcurrentDictionary<int, RemoteClient> Clients = new ConcurrentDictionary<int, RemoteClient>();
    private readonly ConcurrentDictionary<RemoteClient, MicStats> _mics = new ConcurrentDictionary<RemoteClient, MicStats>();
    private static readonly object _out = new object();

    private sealed class MicStats
    {
        public int Rate;
        public long Samples;
        public long Packets;
        public long NextFirst = -1;
        public long Gaps;
        public long Crossings;
        public double SumSquares;
        public short Last;
        public long WindowSamples;
    }

    public static void Emit(object o)
    {
        string json = JsonSerializer.Serialize(o);
        lock (_out)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    public void OnClientHello(RemoteClient c)
    {
        Clients[c.Number] = c;
        Emit(new { ev = "hello", n = c.Number, name = c.Name, key = c.Key, secure = c.Secure, t = RemoteServer.NowMs });
    }

    public void OnClientText(RemoteClient c, string m)
    {
        var p = m.Split('\n');
        Emit(new { ev = "text", n = c.Number, msg = m, t = RemoteServer.NowMs });
        switch (p[0])
        {
            case "role":
                if (p.Length > 2)
                {
                    c.SendText(InSong && p[1] != "none" ? "rolewait" : "roleok\n" + p[1]);
                }
                break;
            case "mic":
                if (p.Length > 1 && int.TryParse(p[1], out int rate))
                {
                    _mics[c] = new MicStats { Rate = rate };
                    c.SendText("micok\nPhone mic: " + c.Name);
                }
                break;
            case "micoff":
                _mics.TryRemove(c, out _);
                break;
        }
    }

    public void OnClientBinary(RemoteClient c, byte[] d, int n)
    {
        double arrival = RemoteServer.NowMs;
        switch (d[0])
        {
            case 1:
            {
                uint buttons = (uint) (d[1] | (d[2] << 8) | (d[3] << 16) | (d[4] << 24));
                double touch = n >= 14 ? BitConverter.ToDouble(d, 6) : 0;
                Emit(new { ev = "pad", n = c.Number, len = n, buttons, whammy = (int) d[5], touch, arrival, t = arrival });
                break;
            }
            case 2:
            case 3:
            {
                int offset = d[0] == 3 ? 5 : 1;
                long first = d[0] == 3 ? (uint) (d[1] | (d[2] << 8) | (d[3] << 16) | (d[4] << 24)) : -1;
                int samples = (n - offset) / 2;
                if (!_mics.TryGetValue(c, out var s))
                {
                    Emit(new { ev = "micorphan", n = c.Number, type = (int) d[0], samples });
                    break;
                }
                if (s.NextFirst >= 0 && first >= 0 && first != s.NextFirst)
                {
                    s.Gaps++;
                }
                if (first >= 0)
                {
                    s.NextFirst = (first + samples) & 0xFFFFFFFFL;
                }
                for (int i = 0; i < samples; i++)
                {
                    short v = (short) (d[offset + i * 2] | (d[offset + i * 2 + 1] << 8));
                    if ((s.Last < 0) != (v < 0)) s.Crossings++;
                    s.Last = v;
                    s.SumSquares += (double) v * v;
                }
                s.Samples += samples;
                s.WindowSamples += samples;
                s.Packets++;
                if (s.Packets <= 3 || s.WindowSamples >= s.Rate / 2)
                {
                    double secs = s.WindowSamples / (double) s.Rate;
                    double hz = secs > 0 ? s.Crossings / 2.0 / secs : 0;
                    double rms = s.WindowSamples > 0 ? Math.Sqrt(s.SumSquares / s.WindowSamples) / 32768.0 : 0;
                    Emit(new { ev = "mic", n = c.Number, type = (int) d[0], first, samples, packetSamples = samples,
                        totalPackets = s.Packets, totalSamples = s.Samples, gaps = s.Gaps, hz, rms, rate = s.Rate, t = arrival });
                    if (s.WindowSamples >= s.Rate / 2)
                    {
                        s.WindowSamples = 0;
                        s.Crossings = 0;
                        s.SumSquares = 0;
                    }
                }
                break;
            }
            default:
                Emit(new { ev = "binary?", n = c.Number, type = (int) d[0], len = n });
                break;
        }
    }

    public void OnClientClosed(RemoteClient c)
    {
        Clients.TryRemove(new KeyValuePair<int, RemoteClient>(c.Number, c));
        _mics.TryRemove(c, out _);
        Emit(new { ev = "closed", n = c.Number, t = RemoteServer.NowMs });
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        byte[] page = File.ReadAllBytes(args[0]);
        X509Certificate2 cert = args.Length > 1 ? new X509Certificate2(File.ReadAllBytes(args[1]), "yarg", X509KeyStorageFlags.Exportable) : null;
        int firstPort = args.Length > 2 ? int.Parse(args[2]) : 17900;

        var host = new Host();
        using var server = new RemoteServer(cert, page, host, m => Host.Emit(new { ev = "log", msg = m }));
        if (!server.Start(firstPort, 50))
        {
            Host.Emit(new { ev = "error", msg = "no port" });
            return 1;
        }
        Host.Emit(new { ev = "listening", port = server.Port });

        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            var parts = line.Split(' ', 3);
            switch (parts[0])
            {
                case "ingame":
                    host.InSong = parts.Length > 1 && parts[1] == "on";
                    Host.Emit(new { ev = "ack", cmd = line });
                    break;
                case "kick":
                    if (host.Clients.TryGetValue(int.Parse(parts[1]), out var kicked))
                    {
                        kicked.Close();
                    }
                    Host.Emit(new { ev = "ack", cmd = line });
                    break;
                case "send":
                    if (host.Clients.TryGetValue(int.Parse(parts[1]), out var to))
                    {
                        to.SendText(parts[2].Replace("\\n", "\n"));
                    }
                    Host.Emit(new { ev = "ack", cmd = line });
                    break;
                case "quit":
                    return 0;
            }
        }
        return 0;
    }
}
