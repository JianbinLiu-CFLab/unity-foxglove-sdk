using System;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
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
            var demo3d = argList.Contains("--demo3d");
            return RunServer(port, demo, demo3d);
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
            Console.WriteLine();
            Phase3Validation.Validate();
            Console.WriteLine();
            Phase4Validation.Validate();
            Console.WriteLine();
            Phase5Validation.Validate();
            Console.WriteLine();
            Phase6Validation.Validate();
            Console.WriteLine();
            Phase7Validation.Validate();
            Console.WriteLine();
            Phase8Validation.Validate();
            Console.WriteLine();
            Phase9Validation.Validate();
            Console.WriteLine();
            Phase10Validation.Validate();
            Console.WriteLine();
            Phase11Validation.Validate();
            Console.WriteLine();
            Phase12Validation.Validate();
            Console.WriteLine();
            Phase13Validation.Validate();
            Console.WriteLine();
            Phase14Validation.Validate();
            Console.WriteLine();
            Phase16Validation.Validate();

            Console.WriteLine("\nAll checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    static int RunServer(int port, bool demo, bool demo3d)
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

        Timer sceneTimer = null;
        if (demo3d)
        {
            runtime.RegisterSchemaChannel(1, "/tf", "foxglove.FrameTransform");
            runtime.RegisterSchemaChannel(2, "/scene", "foxglove.SceneUpdate");
            Console.WriteLine("Demo3D: registered /tf (FrameTransform) and /scene (SceneUpdate) at 1 Hz");

            ulong tfSeq = 0;
            sceneTimer = new Timer(_ =>
            {
                tfSeq++;
                var unixNs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL;
                var sec = unixNs / 1_000_000_000UL;
                var nsec = (uint)(unixNs % 1_000_000_000UL);

                var tf = new FrameTransformMessage
                {
                    Timestamp = new FoxgloveTime { Sec = sec, Nsec = nsec },
                    ParentFrameId = "unity_world",
                    ChildFrameId = "phase3_cube_frame",
                    Translation = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                    Rotation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                };
                runtime.PublishJson(1, tf, unixNs);

                var scene = new SceneUpdateMessage
                {
                    Entities = new System.Collections.Generic.List<SceneEntity>
                    {
                        new SceneEntity
                        {
                            Id = "phase3_cube",
                            FrameId = "phase3_cube_frame",
                            Timestamp = new FoxgloveTime { Sec = sec, Nsec = nsec },
                            Lifetime = new FoxgloveDuration(),
                            Cubes = new System.Collections.Generic.List<CubePrimitive>
                            {
                                new CubePrimitive
                                {
                                    Pose = new FoxglovePose
                                    {
                                        Position = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                                        Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                                    },
                                    Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                                    Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                                }
                            }
                        }
                    }
                };
                runtime.PublishJson(2, scene, unixNs);
            }, null, 1000, 1000);
        }

        Console.WriteLine("Expected: connection succeeds, no topics listed.");
        if (demo)
            Console.WriteLine("Demo: /debug/heartbeat visible, subscribe to see messages.");
        if (demo3d)
        {
            Console.WriteLine("Demo3D: /tf and /scene visible.");
            Console.WriteLine("  Foxglove → 3D panel → select /scene → green cube at origin.");
        }
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
        sceneTimer?.Dispose();
        runtime.Dispose();
        Console.WriteLine("Server stopped.");
        return 0;
    }
}
