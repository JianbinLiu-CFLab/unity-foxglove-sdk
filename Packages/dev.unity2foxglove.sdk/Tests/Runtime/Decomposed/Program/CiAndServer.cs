// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test runner entry point - discovers and executes all Phase validation tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Tests;

/// <summary>Console entry point for runtime validation and manual smoke servers.</summary>
partial class Program
{
    internal static List<string> CollectValidationFailures(
        IEnumerable<(string Name, Func<int> Run)> validations)
    {
        if (validations == null)
            throw new ArgumentNullException(nameof(validations));

        var failures = new List<string>();
        foreach (var validation in validations)
        {
            if (validation.Run() != 0)
                failures.Add(validation.Name);
        }

        return failures;
    }

    /// <summary>
    /// Runs every registry entry classified as CI-safe and collects all
    /// failures before returning. The fast default sweep intentionally omits
    /// some expensive or historical selectors; this explicit gate makes that
    /// coverage visible and prevents the first failure from hiding later ones.
    /// </summary>
    private static int RunAllCiSafeValidations()
    {
        var validations = PhaseValidationRegistry.CiSafeValidations().ToList();
        var failures = new List<string>();

        Console.WriteLine("=== FoxgloveSDK all CI-safe validation registry ===\n");
        foreach (var validation in validations)
        {
            if (RunValidation(validation) != 0)
                failures.Add(validation.Name);
        }

        var passed = validations.Count - failures.Count;
        Console.WriteLine(
            $"\nCI-safe registry coverage: passed={passed} failed={failures.Count} total={validations.Count}");
        if (failures.Count == 0)
        {
            Console.WriteLine("All CI-safe validations passed.");
            return 0;
        }

        Console.Error.WriteLine("Failed CI-safe validations: " + string.Join("; ", failures));
        return 1;
    }

    /// <summary>Runs the manual Phase139B loopback server used by browser and Python acceptance probes.</summary>
    private static int RunPhase139BRemoteDataLoaderServer(List<string> args)
    {
        var mcapPath = ValueAfter(args, "--mcap");
        if (string.IsNullOrEmpty(mcapPath))
        {
            Console.Error.WriteLine("--phase139b-remote-data-loader-server requires --mcap PATH.");
            return 1;
        }

        var port = 8876;
        var portValue = ValueAfter(args, "--port");
        if (!string.IsNullOrEmpty(portValue) && !int.TryParse(portValue, out port))
        {
            Console.Error.WriteLine("--port must be an integer.");
            return 1;
        }

        var maxDataBytes = RemoteMcapDataSourcePrototype.DefaultMaxInMemoryDataBytes;
        var maxDataBytesValue = ValueAfter(args, "--max-data-bytes");
        if (!string.IsNullOrEmpty(maxDataBytesValue) && !long.TryParse(maxDataBytesValue, out maxDataBytes))
        {
            Console.Error.WriteLine("--max-data-bytes must be an integer.");
            return 1;
        }

        var options = new RemoteMcapHttpOptions
        {
            Host = ValueAfter(args, "--host") ?? "127.0.0.1",
            Port = port,
            McapPath = mcapPath,
            SourceId = ValueAfter(args, "--source-id") ?? "local-mcap",
            ManifestName = ValueAfter(args, "--name") ?? "Unity2Foxglove MCAP",
            RequiredBearerToken = ValueAfter(args, "--token") ?? string.Empty,
            MaxInMemoryDataBytes = maxDataBytes
        };

        using (var server = RemoteMcapHttpServer.Start(options))
        {
            var ready = JsonConvert.SerializeObject(new
            {
                baseUrl = server.BaseUrl,
                manifestUrl = server.BaseUrl + "/v1/manifest",
                dataUrl = server.BaseUrl + options.DataRoute,
                remoteFileUrl = server.BaseUrl + options.DirectFileRoute
            });
            Console.WriteLine("PHASE139B_SERVER_READY=" + ready);
            Console.Out.Flush();

            using var done = new ManualResetEventSlim(false);
            ConsoleCancelEventHandler handler = (_, e) =>
            {
                e.Cancel = true;
                done.Set();
            };
            Console.CancelKeyPress += handler;
            try
            {
                done.Wait();
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        return 0;
    }

    /// <summary>Returns the command-line value immediately following a flag, or null when absent.</summary>
    private static string ValueAfter(List<string> args, string flag)
    {
        var idx = args.IndexOf(flag);
        return idx >= 0 && idx + 1 < args.Count
            ? args[idx + 1]
            : null;
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
        var stopping = 0;

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

                long seq = 0;
                heartbeat = new Timer(_ =>
                {
                    if (Volatile.Read(ref stopping) != 0)
                        return;

                    var nextSeq = (ulong)Interlocked.Increment(ref seq);
                    var payload = new
                    {
                        seq = nextSeq,
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

                long tfSeq = 0;
                sceneTimer = new Timer(_ =>
                {
                    if (Volatile.Read(ref stopping) != 0)
                        return;

                    Interlocked.Increment(ref tfSeq);
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

            using var done = new ManualResetEventSlim(false);
            ConsoleCancelEventHandler handler = (_, e) =>
            {
                e.Cancel = true;
                done.Set();
            };
            Console.CancelKeyPress += handler;
            try
            {
                done.Wait();
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
            return 0;
        }
        finally
        {
            Console.WriteLine("\nStopping...");
            Interlocked.Exchange(ref stopping, 1);
            DisposeTimerAndWait(heartbeat);
            DisposeTimerAndWait(sceneTimer);
            runtime.Dispose();
            Console.WriteLine("Server stopped.");
        }
    }

    private static void DisposeTimerAndWait(Timer timer)
    {
        if (timer == null)
            return;

        using var disposed = new ManualResetEvent(false);
        if (timer.Dispose(disposed))
            disposed.WaitOne(TimeSpan.FromSeconds(5));
    }
}

