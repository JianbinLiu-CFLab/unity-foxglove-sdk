using System;
using System.Linq;
using System.Threading;
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

            return RunServer(port);
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

            Console.WriteLine("\nAll checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    static int RunServer(int port)
    {
        Console.WriteLine($"=== FoxgloveSDK Manual Server Mode ===");
        Console.WriteLine($"Starting on ws://127.0.0.1:{port}");

        var runtime = new Unity.FoxgloveSDK.Core.FoxgloveRuntime();
        runtime.Start("Unity Foxglove SDK", "127.0.0.1", port);

        Console.WriteLine($"Server running. SessionId: {runtime.Session.SessionId}");
        Console.WriteLine("Open Foxglove → Open connection → ws://127.0.0.1:{0}", port);
        Console.WriteLine("Expected: connection succeeds, no topics listed.");
        Console.WriteLine("Press Ctrl+C to stop...");

        var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.Set();
        };

        done.Wait();
        Console.WriteLine("\nStopping...");
        runtime.Dispose();
        Console.WriteLine("Server stopped.");
        return 0;
    }
}
