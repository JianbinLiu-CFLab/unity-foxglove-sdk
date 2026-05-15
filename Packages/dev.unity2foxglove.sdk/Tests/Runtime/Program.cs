// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test runner entry point — discovers and executes all Phase validation tests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Tests;

class Program
{
    /// <summary>
    /// Dispatches to test runner or interactive server mode based on
    /// command-line arguments. <c>--serve</c> starts a manual test
    /// server; default runs all validation phases.
    /// </summary>
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

        if (argList.Contains("--phase50"))
            return RunPhase50Only();

        if (argList.Contains("--phase16"))
            return RunPhase16Only();

        if (argList.Contains("--phase51"))
            return RunPhase51Only();

        if (argList.Contains("--phase53"))
            return RunPhase53Only();

        if (argList.Contains("--phase52"))
            return RunPhase52Only();

        if (argList.Contains("--phase54"))
            return RunPhase54Only();

        if (argList.Contains("--phase55"))
            return RunPhase55Only();

        if (argList.Contains("--phase56"))
            return RunPhase56Only();

        if (argList.Contains("--phase57"))
            return RunPhase57Only();

        if (argList.Contains("--phase65"))
            return RunPhase65Only();

        if (argList.Contains("--phase67"))
            return RunPhase67Only();

        if (argList.Contains("--phase68"))
            return RunPhase68Only();

        if (argList.Contains("--phase69"))
            return RunPhase69Only();

        if (argList.Contains("--phase70"))
            return RunPhase70Only();

        if (argList.Contains("--phase71"))
            return RunPhase71Only();

        if (argList.Contains("--phase72"))
            return RunPhase72Only();

        if (argList.Contains("--phase73"))
            return RunPhase73Only();

        if (argList.Contains("--phase74"))
            return RunPhase74Only();

        if (argList.Contains("--phase75"))
            return RunPhase75Only();

        if (argList.Contains("--phase76"))
            return RunPhase76Only();

        if (argList.Contains("--phase77"))
            return RunPhase77Only();

        if (argList.Contains("--phase78"))
            return RunPhase78Only();

        if (argList.Contains("--phase78-native-smoke"))
            return RunPhase78NativeSmoke();

        var phase68SmokeIdx = argList.IndexOf("--phase68-indexed-reader-smoke");
        if (phase68SmokeIdx >= 0)
            return RunPhase68IndexedReaderSmoke(argList, phase68SmokeIdx);

        if (argList.Contains("--phase13"))
            return RunPhase13Only();

        var phase44McapIdx = argList.IndexOf("--phase44-all-schemas-mcap");
        if (phase44McapIdx >= 0)
        {
            if (phase44McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase44-all-schemas-mcap requires an output path.");
                return 1;
            }

            try
            {
                Phase44Validation.GenerateAllSchemasMcap(argList[phase44McapIdx + 1]);
                Console.WriteLine($"Phase 44 all-schema smoke MCAP written: {argList[phase44McapIdx + 1]}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to generate Phase 44 all-schema smoke MCAP: {ex.Message}");
                return 1;
            }
        }

        return RunTests();
    }

    private static int RunPhase16Only()
    {
        try
        {
            Phase16Validation.Validate();
            Console.WriteLine("\nPhase 16 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase50Only()
    {
        try
        {
            Phase50Validation.Validate();
            Console.WriteLine("\nPhase 50 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase51Only()
    {
        try
        {
            Phase51Validation.Validate();
            Console.WriteLine("\nPhase 51 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase53Only()
    {
        try
        {
            Phase53Validation.Validate();
            Console.WriteLine("\nPhase 53 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase52Only()
    {
        try
        {
            Phase52Validation.Validate();
            Console.WriteLine("\nPhase 52 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase54Only()
    {
        try
        {
            Phase54Validation.Validate();
            Console.WriteLine("\nPhase 54 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase55Only()
    {
        try
        {
            Phase55Validation.Validate();
            Console.WriteLine("\nPhase 55 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase56Only()
    {
        try
        {
            Phase56Validation.Validate();
            Console.WriteLine("\nPhase 56 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase57Only()
    {
        try
        {
            Phase57Validation.Validate();
            Console.WriteLine("\nPhase 57 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase65Only()
    {
        try
        {
            Phase65Validation.Validate();
            Console.WriteLine("\nPhase 65 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase67Only()
    {
        try
        {
            Phase67Validation.Validate();
            Console.WriteLine("\nPhase 67 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase68Only()
    {
        try
        {
            Phase68Validation.Validate();
            Console.WriteLine("\nPhase 68 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase68IndexedReaderSmoke(List<string> argList, int optionIndex)
    {
        if (optionIndex + 1 >= argList.Count)
        {
            Console.Error.WriteLine("--phase68-indexed-reader-smoke requires an MCAP path.");
            return 1;
        }

        try
        {
            var topics = CollectOptionValues(argList, "--phase68-topic");
            var maxMessages = ReadIntOption(argList, "--phase68-max-messages", 5);
            var minMessages = ReadIntOption(argList, "--phase68-min-messages", 1);

            Phase68Validation.ValidateExternalMcapSmoke(
                argList[optionIndex + 1],
                topics,
                maxMessages,
                minMessages);
            Console.WriteLine("\nPhase 68 indexed reader smoke passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static List<string> CollectOptionValues(List<string> argList, string option)
    {
        var values = new List<string>();
        for (var i = 0; i < argList.Count; i++)
        {
            if (argList[i] != option)
                continue;

            if (i + 1 >= argList.Count)
                throw new ArgumentException($"{option} requires a value.");

            values.Add(argList[i + 1]);
            i++;
        }

        return values;
    }

    private static int ReadIntOption(List<string> argList, string option, int defaultValue)
    {
        var idx = argList.IndexOf(option);
        if (idx < 0)
            return defaultValue;

        if (idx + 1 >= argList.Count)
            throw new ArgumentException($"{option} requires an integer value.");

        if (!int.TryParse(argList[idx + 1], out var value))
            throw new ArgumentException($"{option} requires an integer value.");

        return value;
    }

    private static int RunPhase69Only()
    {
        try
        {
            Phase69Validation.Validate();
            Console.WriteLine("\nPhase 69 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase70Only()
    {
        try
        {
            Phase70Validation.Validate();
            Console.WriteLine("\nPhase 70 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase71Only()
    {
        try
        {
            Phase71Validation.Validate();
            Console.WriteLine("\nPhase 71 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase72Only()
    {
        try
        {
            Phase72Validation.Validate();
            Console.WriteLine("\nPhase 72 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase73Only()
    {
        try
        {
            Phase73Validation.Validate();
            Console.WriteLine("\nPhase 73 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase74Only()
    {
        try
        {
            Phase74Validation.Validate();
            Console.WriteLine("\nPhase 74 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase75Only()
    {
        try
        {
            Phase75Validation.Validate();
            Console.WriteLine("\nPhase 75 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase76Only()
    {
        try
        {
            Phase76Validation.Validate();
            Console.WriteLine("\nPhase 76 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase77Only()
    {
        try
        {
            Phase77Validation.Validate();
            Console.WriteLine("\nPhase 77 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase78Only()
    {
        try
        {
            Phase78Validation.Validate();
            Console.WriteLine("\nPhase 78 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase78NativeSmoke()
    {
        try
        {
            Phase78Validation.RunNativeSmoke();
            Console.WriteLine("\nPhase 78 native smoke completed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex}");
            return 1;
        }
    }

    private static int RunPhase13Only()
    {
        try
        {
            Phase13Validation.Validate();
            Console.WriteLine("\nPhase 13 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Runs all Phase validation classes sequentially and returns 0 on
    /// success or 1 on the first failure.
    /// </summary>
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
            Console.WriteLine();
            Phase17Validation.Validate();
            Console.WriteLine();
            Phase24DValidation.Validate();
            Console.WriteLine();
            Phase28Validation.Validate();
            Console.WriteLine();
            Phase31Validation.Validate();
            Console.WriteLine();
            Phase32Validation.Run();
            Console.WriteLine();
            Phase33Validation.Validate();
            Console.WriteLine();
            Phase34Validation.Validate();
            Console.WriteLine();
            Phase36Validation.Validate();
            Console.WriteLine();
            Phase37Validation.Validate();
            Console.WriteLine();
            Phase40Validation.Validate();
            Console.WriteLine();
            Phase41Validation.Validate();
            Console.WriteLine();
            Phase44Validation.Validate();
            Console.WriteLine();
            Phase48Validation.Validate();
            Console.WriteLine();
            Phase49Validation.Validate();
            Console.WriteLine();
            Phase50Validation.Validate();
            Console.WriteLine();
            Phase51Validation.Validate();
            Console.WriteLine();
            Phase52Validation.Validate();
            Console.WriteLine();
            Phase53Validation.Validate();
            Console.WriteLine();
            Phase54Validation.Validate();
            Console.WriteLine();
            Phase55Validation.Validate();
            Console.WriteLine();
            Phase56Validation.Validate();
            Console.WriteLine();
            Phase57Validation.Validate();
            Console.WriteLine();
            Phase65Validation.Validate();
            Console.WriteLine();
            Phase67Validation.Validate();
            Console.WriteLine();
            Phase68Validation.Validate();
            Console.WriteLine();
            Phase69Validation.Validate();
            Console.WriteLine();
            Phase70Validation.Validate();
            Console.WriteLine();
            Phase71Validation.Validate();
            Console.WriteLine();
            Phase72Validation.Validate();
            Console.WriteLine();
            Phase73Validation.Validate();
            Console.WriteLine();
            Phase74Validation.Validate();
            Console.WriteLine();
            Phase75Validation.Validate();
            Console.WriteLine();
            Phase76Validation.Validate();
            Console.WriteLine();
            Phase77Validation.Validate();
            Console.WriteLine();
            Phase78Validation.Validate();

            Console.WriteLine("\nAll checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Starts a long-running Foxglove WebSocket server for manual
    /// testing. <c>demo</c> publishes a heartbeat; <c>demo3d</c>
    /// publishes FrameTransform and SceneUpdate.
    /// </summary>
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
