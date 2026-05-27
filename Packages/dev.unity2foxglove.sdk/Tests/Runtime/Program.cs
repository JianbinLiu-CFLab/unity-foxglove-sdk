// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test runner entry point - discovers and executes all Phase validation tests.

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

        if (TryRunRegisteredValidation(argList, out var registeredValidationExitCode))
            return registeredValidationExitCode;

        if (argList.Contains("--phase97-health"))
            return RunPhase97Health(argList);

        var phase98SampleSendAllIdx = argList.IndexOf("--phase98-sample-send-all");
        if (phase98SampleSendAllIdx >= 0)
        {
            if (phase98SampleSendAllIdx + 2 >= argList.Count)
            {
                Console.Error.WriteLine("--phase98-sample-send-all requires host and port.");
                return 1;
            }

            if (!int.TryParse(argList[phase98SampleSendAllIdx + 2], out var port))
            {
                Console.Error.WriteLine("--phase98-sample-send-all port must be an integer.");
                return 1;
            }

            return RunPhase98SampleSendAll(argList[phase98SampleSendAllIdx + 1], port);
        }

        if (argList.Contains("--phase98-live"))
            return RunPhase98Live(argList);

        if (argList.Contains("--phase99-live"))
            return RunPhase99Live(argList);

        var phase94BridgeSendIdx = argList.IndexOf("--phase94-bridge-send");
        if (phase94BridgeSendIdx >= 0)
        {
            if (phase94BridgeSendIdx + 2 >= argList.Count)
            {
                Console.Error.WriteLine("--phase94-bridge-send requires host and port.");
                return 1;
            }

            if (!int.TryParse(argList[phase94BridgeSendIdx + 2], out var port))
            {
                Console.Error.WriteLine("--phase94-bridge-send port must be an integer.");
                return 1;
            }

            return RunPhase94BridgeSend(argList[phase94BridgeSendIdx + 1], port);
        }

        var phase91McapIdx = argList.IndexOf("--phase91-ros2-cdr-mcap");
        if (phase91McapIdx >= 0)
        {
            if (phase91McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase91-ros2-cdr-mcap requires an output path.");
                return 1;
            }

            return RunPhase91Ros2CdrMcap(argList[phase91McapIdx + 1]);
        }

        var phase92McapIdx = argList.IndexOf("--phase92-ros2-product-mcap");
        if (phase92McapIdx >= 0)
        {
            if (phase92McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase92-ros2-product-mcap requires an output path.");
                return 1;
            }

            return RunPhase92Ros2ProductMcap(argList[phase92McapIdx + 1]);
        }

        var phase93McapIdx = argList.IndexOf("--phase93-ros2-full-mcap");
        if (phase93McapIdx >= 0)
        {
            if (phase93McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase93-ros2-full-mcap requires an output path.");
                return 1;
            }

            return RunPhase93Ros2FullMcap(argList[phase93McapIdx + 1]);
        }

        var phase93InspectIdx = argList.IndexOf("--phase93-inspect-mcap");
        if (phase93InspectIdx >= 0)
        {
            if (phase93InspectIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase93-inspect-mcap requires an input path.");
                return 1;
            }

            return RunPhase93InspectMcap(argList[phase93InspectIdx + 1]);
        }

        var phase68SmokeIdx = argList.IndexOf("--phase68-indexed-reader-smoke");
        if (phase68SmokeIdx >= 0)
            return RunPhase68IndexedReaderSmoke(argList, phase68SmokeIdx);

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
        return RunTests(argList.Contains("--local-evidence"));
    }

    private static bool TryRunRegisteredValidation(List<string> argList, out int exitCode)
    {
        if (argList.Contains("--list-validations"))
        {
            foreach (var validation in PhaseValidationRegistry.All)
            {
                var flags = string.Join(", ", validation.AllFlags());
                if (string.IsNullOrEmpty(flags))
                    flags = "(default only)";
                Console.WriteLine($"{flags} [{validation.Category}] {validation.Name}");
            }

            exitCode = 0;
            return true;
        }

        var selected = PhaseValidationRegistry.Find(argList);
        if (selected == null)
        {
            exitCode = 0;
            return false;
        }

        exitCode = RunValidation(selected);
        return true;
    }

    private static int RunValidation(PhaseValidationCase validation)
    {
        try
        {
            validation.Run();
            Console.WriteLine($"\n{validation.Name} checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {validation.Name}: {ex.Message}");
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

    private static string ReadStringOption(List<string> argList, string option, string defaultValue)
    {
        var idx = argList.IndexOf(option);
        if (idx < 0)
            return defaultValue;

        if (idx + 1 >= argList.Count)
            throw new ArgumentException($"{option} requires a value.");

        return argList[idx + 1];
    }

    private static int RunPhase91Ros2CdrMcap(string outputPath)
    {
        try
        {
            Phase91Validation.GenerateRos2CdrMcap(outputPath);
            Console.WriteLine($"Phase 91 ROS2 CDR MCAP written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase92Ros2ProductMcap(string outputPath)
    {
        try
        {
            Phase92Validation.GenerateRos2ProductMcap(outputPath);
            Console.WriteLine($"Phase 92 ROS2 product MCAP written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase93Ros2FullMcap(string outputPath)
    {
        try
        {
            Phase93Validation.GenerateRos2FullSchemaMcap(outputPath);
            Console.WriteLine($"Phase 93 ROS2 full-schema MCAP written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase93InspectMcap(string inputPath)
    {
        try
        {
            Phase93Validation.InspectRos2FullSchemaMcap(inputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase94BridgeSend(string host, int port)
    {
        try
        {
            Phase94Validation.RunBridgeSendSmoke(host, port);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase97Health(List<string> argList)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase97-health requires --json <path>.");
                return 1;
            }

            var liveMode = argList.Contains("--phase97-live")
                || string.Equals(
                    Environment.GetEnvironmentVariable("UNITY2FOXGLOVE_PHASE97_LIVE"),
                    "1",
                    StringComparison.Ordinal);
            var ros2Path = ReadStringOption(argList, "--ros2", "");
            var host = ReadStringOption(argList, "--host", "127.0.0.1");
            var port = ReadIntOption(argList, "--port", 8767);
            var report = Phase97Validation.GenerateHealthReport(jsonPath, liveMode, ros2Path, host, port);

            Console.WriteLine($"Phase 97 health report written: {jsonPath}");
            Console.WriteLine($"Summary: {report.Summary}");
            if (liveMode && report.Summary != Unity.FoxgloveSDK.Ros2Bridge.Ros2BridgeHealthSummary.Ready)
                return 1;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase98SampleSendAll(string host, int port)
    {
        try
        {
            var summary = Phase98Validation.SendAllSchemaSamples(host, port);
            Console.WriteLine($"[phase98] sent frames={summary.SentFrames} totalWireBytes={summary.TotalWireBytes}");
            Console.WriteLine($"[phase98] firstSchema={summary.FirstSchema}");
            Console.WriteLine($"[phase98] lastSchema={summary.LastSchema}");
            Console.WriteLine("[phase98] PASS all-schema sample sender");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase98Live(List<string> argList)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase98-live requires --json <path>.");
                return 1;
            }

            var ros2Path = ReadStringOption(argList, "--ros2", "");
            var host = ReadStringOption(argList, "--host", "127.0.0.1");
            var port = ReadIntOption(argList, "--port", 8767);
            var evidence = Phase98Validation.GenerateLiveEvidence(jsonPath, host, port, ros2Path);

            Console.WriteLine($"Phase 98 live evidence written: {jsonPath}");
            Console.WriteLine($"Health: {evidence.HealthSummary}");
            Console.WriteLine($"Product topics: {evidence.ProductTopics?.Length ?? 0}");
            Console.WriteLine($"All-schema frames: {evidence.AllSchema?.SentFrames ?? 0}");
            return string.Equals(evidence.HealthSummary, "Ready", StringComparison.Ordinal) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase99Live(List<string> argList)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase99-live requires --json <path>.");
                return 1;
            }

            var evidenceDir = ReadStringOption(argList, "--evidence-dir", "");
            var ros2Path = ReadStringOption(argList, "--ros2", "");
            var host = ReadStringOption(argList, "--host", "127.0.0.1");
            var port = ReadIntOption(argList, "--port", 8767);
            var report = Phase99Validation.GenerateLiveReport(jsonPath, evidenceDir, host, port, ros2Path);

            Console.WriteLine($"Phase 99 release gate report written: {jsonPath}");
            Console.WriteLine($"Verdict: {report.Verdict}");
            Console.WriteLine($"Evidence items: {report.Evidence?.Count ?? 0}");
            return report.Verdict == Phase99Verdict.Blocked ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Runs all Phase validation classes sequentially and returns 0 on
    /// success or 1 on the first failure.
    /// </summary>
    static int RunTests(bool includeLocalEvidence)
    {
        Console.WriteLine(includeLocalEvidence
            ? "=== FoxgloveSDK CI-safe + local evidence validation ===\n"
            : "=== FoxgloveSDK CI-safe validation ===\n");

        foreach (var validation in PhaseValidationRegistry.DefaultValidations(includeLocalEvidence))
        {
            var result = RunValidation(validation);
            if (result != 0)
                return result;
        }

        Console.WriteLine("\nAll checks passed.");
        return 0;
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
        Timer heartbeat = null;
        Timer sceneTimer = null;

        try
        {
            runtime.Start("Unity Foxglove SDK", "127.0.0.1", port);

            Console.WriteLine($"Server running. SessionId: {runtime.Session.SessionId}");
            Console.WriteLine("Open Foxglove -> Open connection -> ws://127.0.0.1:{0}", port);

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
                Console.WriteLine("  Foxglove -> 3D panel -> select /scene -> green cube at origin.");
            }
            Console.WriteLine("Press Ctrl+C to stop...");

            var done = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                done.Set();
            };

            done.Wait();
            return 0;
        }
        finally
        {
            Console.WriteLine("\nStopping...");
            heartbeat?.Dispose();
            sceneTimer?.Dispose();
            runtime.Dispose();
            Console.WriteLine("Server stopped.");
        }
    }
}
