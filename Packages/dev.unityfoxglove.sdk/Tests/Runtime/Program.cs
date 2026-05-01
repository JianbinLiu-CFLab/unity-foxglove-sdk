using System;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Tests;

class Program
{
    static int Main(string[] args)
    {
        var argList = args.ToList();

        if (argList.Contains("--serve"))
        {
            int port = 8765;
            var portIdx = argList.IndexOf("--port");
            if (portIdx >= 0 && portIdx + 1 < argList.Count)
                int.TryParse(argList[portIdx + 1], out port);

            var demo = argList.Contains("--demo");
            return RunServer(port, demo);
        }

        return RunTests();
    }

    static int RunTests()
    {
        Console.WriteLine("=== FoxgloveSDK Phase 0 + Phase 1 Validation ===\n");

        try
        {
            SkeletonValidation.Validate();
            Console.WriteLine();
            Phase1Validation.Validate();
            Console.WriteLine();
            Phase2Validation.Validate();

            Console.WriteLine("\nAll checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    static int RunServer(int port, bool demo)
    {
        Console.WriteLine($"=== FoxgloveSDK Manual Server Mode ===");
        Console.WriteLine($"Starting on ws://127.0.0.1:{port}");

        var runtime = new Unity.FoxgloveSDK.Core.FoxgloveRuntime();
        runtime.Start("Unity Foxglove SDK", "127.0.0.1", port);

        Console.WriteLine($"Server running. SessionId: {runtime.Session.SessionId}");
        Console.WriteLine("Open Foxglove → Open connection → ws://127.0.0.1:{0}", port);

        Timer heartbeat = null;
        if (demo)
        {
            var ch = new AdvertiseChannel
            {
                Id = 1,
                Topic = "/debug/heartbeat",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            };
            runtime.RegisterChannel(ch);
            Console.WriteLine("Demo: registered /debug/heartbeat (1 Hz)");

            ulong seq = 0;
            heartbeat = new Timer(_ =>
            {
                seq++;
                var payload = new
                {
                    seq,
                    unixTimeNs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL,
                    message = "hello foxglove"
                };
                var json = JsonConvert.SerializeObject(payload);
                runtime.Publish(1, Encoding.UTF8.GetBytes(json));
            }, null, 1000, 1000);
        }

        Console.WriteLine("Expected: connection succeeds, no topics listed.");
        if (demo)
            Console.WriteLine("Demo: /debug/heartbeat visible, subscribe to see messages.");
        Console.WriteLine("Press Ctrl+C to stop...");

        var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.Set();
        };

        done.Wait();
        Console.WriteLine("\nStopping...");
        heartbeat?.Dispose();
        runtime.Dispose();
        Console.WriteLine("Server stopped.");
        return 0;
    }
}
