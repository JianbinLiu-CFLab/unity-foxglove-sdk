// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Migrated Phase184 behavior evidence on the ROS-free Provider model.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Keeps the five historical Phase184 selections executable after the
    /// Phase186 breaking boundary. ROS-specific continuity is certified by
    /// the optional Provider packages; this core validation now exercises the
    /// equivalent open Provider contracts.
    /// </summary>
    public static class FoxRunProfileModelValidation
    {
        private static readonly HashSet<string>
            ForbiddenArtifactDirectories =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    "bin",
                    "obj",
                    "Library",
                    "Temp",
                    "Logs",
                    "node_modules",
                    "__pycache__",
                    ".cache",
                    ".gradle",
                    ".nuget"
                };

        private static int _passed;

        public static void ValidatePhase184A()
        {
            Begin("Phase 184A migrated: open Provider declaration API");
            VerifyCleanDeclarationApi();
            VerifyTransportIdentity();
            VerifyDeliveryPolicyLegality();
            End("Phase 184A");
        }

        public static void ValidatePhase184B()
        {
            Begin("Phase 184B migrated: frozen Provider sessions");
            VerifyPublishSessionFreeze();
            VerifySubscriptionSessionFreeze();
            End("Phase 184B");
        }

        public static void ValidatePhase184C()
        {
            Begin("Phase 184C migrated: neutral delivery policy");
            VerifyRosFreeCoreBoundary();
            VerifyPortableDeliveryAxes();
            End("Phase 184C");
        }

        public static void ValidatePhase184D()
        {
            Begin("Phase 184D migrated: isolated Provider fanout");
            VerifyProviderOriginScoping();
            VerifyObserverIsolation();
            VerifyWriterOwnership();
            End("Phase 184D");
        }

        public static void ValidatePhase184E()
        {
            Begin("Phase 184E: bounded FoxRun input streams");
            VerifyBoundedStreamBehavior();
            VerifyNoPackageTestOrSampleArtifactDirectories();
            End("Phase 184E");
        }

        private static void VerifyCleanDeclarationApi()
        {
            var fieldProperties =
                PublicPropertyNames(typeof(FoxRunAttribute));
            var aggregateProperties =
                PublicPropertyNames(typeof(FoxRunMessageAttribute));
            var expectedFieldProperties =
                new HashSet<string>(
                    new[]
                    {
                        "Topic",
                        "Hz",
                        "SchemaName",
                        "Policy",
                        "Mode",
                        "PublishTransportIds",
                        "SubscribeTransportId",
                        "Encoding",
                        "Reliability",
                        "Durability",
                        "History",
                        "Depth",
                        "ProtobufFieldNumber",
                        "Tolerance",
                        "OnlyIf"
                    },
                    StringComparer.Ordinal);
            var expectedAggregateProperties =
                new HashSet<string>(
                    new[]
                    {
                        "Topic",
                        "Hz",
                        "SchemaName",
                        "Policy",
                        "PublishTransportIds",
                        "Encoding",
                        "Reliability",
                        "Durability",
                        "History",
                        "Depth",
                        "Tolerance",
                        "OnlyIf"
                    },
                    StringComparer.Ordinal);

            Check(
                fieldProperties.SetEquals(expectedFieldProperties),
                "Structural 184A-1: field declarations expose only scheduling, Provider IDs, encoding, and neutral delivery axes");
            Check(
                aggregateProperties.SetEquals(
                    expectedAggregateProperties),
                "Structural 184A-2: aggregate declarations expose the matching publish-only Provider grammar");

            var retired = new[]
            {
                "Source",
                "Targets",
                "QoS",
                "Ros2Qos",
                "PublishMode",
                "RateHz"
            };
            Check(
                retired.All(
                    name =>
                        !fieldProperties.Contains(name)
                        && !aggregateProperties.Contains(name)),
                "Structural 184A-3: closed endpoints and ROS-specific QoS aliases remain absent");
            Check(
                Enum.GetNames(typeof(FoxRunPolicy))
                    .SequenceEqual(
                        new[]
                        {
                            "FixedRate",
                            "Change",
                            "Trigger"
                        })
                && Convert.ToInt32(FoxRunPolicy.Trigger) == 4,
                "Structural 184A-4: scheduling vocabulary and reserved enum slots remain stable");
        }

        private static void VerifyTransportIdentity()
        {
            var first =
                new FoxRunTransportId("example.transport");
            var second =
                new FoxRunTransportId("example.transport");
            Check(
                first == second
                && first.GetHashCode() == second.GetHashCode()
                && first.ToString() == "example.transport",
                "Behavioral 184A-5: Provider identity uses stable ordinal value semantics");
            Check(
                FoxgloveWebSocketTransport.Id
                    == "foxglove.websocket"
                && FoxgloveWebSocketTransport.TransportId
                    == new FoxRunTransportId(
                        FoxgloveWebSocketTransport.Id),
                "Structural 184A-6: the built-in WebSocket Provider has one stable public ID");
            Check(
                !FoxRunTransportId.TryCreate(
                    "closed-enum",
                    out _)
                && !FoxRunTransportId.TryCreate(
                    "Example.Transport",
                    out _)
                && !FoxRunTransportId.TryCreate(
                    " example.transport",
                    out _),
                "Behavioral 184A-7: malformed or noncanonical Provider IDs fail closed");
        }

        private static void VerifyDeliveryPolicyLegality()
        {
            var policy =
                new FoxRunDeliveryPolicy(
                    FoxRunDeliveryReliability.Reliable,
                    FoxRunDeliveryDurability.Volatile,
                    FoxRunDeliveryHistory.KeepLast,
                    10);
            Check(
                policy.Reliability
                    == FoxRunDeliveryReliability.Reliable
                && policy.Durability
                    == FoxRunDeliveryDurability.Volatile
                && policy.History
                    == FoxRunDeliveryHistory.KeepLast
                && policy.Depth == 10,
                "Behavioral 184A-8: a portable explicit delivery policy retains every axis");
            Check(
                Throws<ArgumentException>(
                    () => new FoxRunDeliveryPolicy(
                        FoxRunDeliveryReliability.Reliable,
                        FoxRunDeliveryDurability.Volatile,
                        FoxRunDeliveryHistory.KeepAll,
                        1))
                && Throws<ArgumentException>(
                    () => new FoxRunDeliveryPolicy(
                        FoxRunDeliveryReliability.Reliable,
                        FoxRunDeliveryDurability.Volatile,
                        FoxRunDeliveryHistory.KeepLast,
                        0)),
                "Behavioral 184A-9: invalid history/depth combinations fail before Provider capture");
        }

        private static void VerifyPublishSessionFreeze()
        {
            var state = new FoxRunPublishSessionState();
            var delivery =
                new FoxRunDeliveryPolicy(
                    FoxRunDeliveryReliability.BestEffort,
                    FoxRunDeliveryDurability.Volatile,
                    FoxRunDeliveryHistory.KeepLast,
                    5);
            var first = state.BeginIfNeeded(
                new[]
                {
                    new FoxRunTransportId("provider.z"),
                    new FoxRunTransportId("provider.a")
                },
                FoxRunEncoding.MessagePack,
                20f,
                delivery);
            var repeated = state.BeginIfNeeded(
                new[]
                {
                    new FoxRunTransportId("provider.other")
                },
                FoxRunEncoding.JSON,
                1f,
                FoxRunDeliveryPolicy.ProviderDefault);

            Check(
                ReferenceEquals(first, repeated)
                && first.SessionGeneration == 1
                && first.PublishTransportIds
                    .Select(value => value.Value)
                    .SequenceEqual(
                        new[]
                        {
                            "provider.a",
                            "provider.z"
                        })
                && first.WebSocketEncoding
                    == FoxRunEncoding.MessagePack
                && first.DefaultPublishRateHz == 20f
                && first.DefaultDeliveryPolicy.Equals(delivery),
                "Behavioral 184B-1: publish Provider IDs and defaults are canonical and frozen for one session");

            var ended = state.End();
            var second = state.BeginIfNeeded(
                new[]
                {
                    new FoxRunTransportId("provider.other")
                },
                FoxRunEncoding.JSON,
                float.NaN,
                FoxRunDeliveryPolicy.ProviderDefault);
            Check(
                !ended.SessionActive
                && second.SessionActive
                && second.SessionGeneration == 2
                && second.DefaultPublishRateHz == 10f
                && second.PublishTransportIds.Single().Value
                    == "provider.other",
                "Behavioral 184B-2: ending releases the snapshot and recapture advances one generation");
        }

        private static void VerifySubscriptionSessionFreeze()
        {
            var state = new FoxRunSubscriptionSessionState();
            var source =
                new FoxRunTransportId("provider.input");
            var first = state.BeginIfNeeded(
                source,
                FoxRunEncoding.MessagePack,
                FoxRunDeliveryPolicy.ProviderDefault,
                0,
                -1,
                0);
            var repeated = state.BeginIfNeeded(
                new FoxRunTransportId("provider.other"),
                FoxRunEncoding.JSON,
                FoxRunDeliveryPolicy.ProviderDefault,
                100,
                100,
                100);

            Check(
                ReferenceEquals(first, repeated)
                && first.SubscriptionsEnabled
                && first.SessionGeneration == 1
                && first.DefaultProvider == source
                && first.WebSocketEncoding
                    == FoxRunEncoding.MessagePack
                && first.TransportAdmissionRateLimitHz == 1
                && first.DefaultSubscribeRateHz == 1
                && first.MaxPayloadBytes == 1,
                "Behavioral 184B-3: Subscribe freezes one Provider and clamps every admission bound");

            var ended = state.End();
            Check(
                !ended.SubscriptionsEnabled
                && ended.SessionGeneration == 1
                && ended.DefaultProvider
                    == FoxgloveWebSocketTransport.TransportId,
                "Behavioral 184B-4: Subscribe end returns to an inert built-in snapshot without changing generation");
        }

        private static void VerifyRosFreeCoreBoundary()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            var retiredTypes = new[]
            {
                "Unity.FoxgloveSDK.Components.FoxRunEndpoint",
                "Unity.FoxgloveSDK.Components.FoxRunResolvedQos",
                "Unity.FoxgloveSDK.Components.FoxRunQosProfile",
                "Unity.FoxgloveSDK.Components.FoxRunQosReliability",
                "Unity.FoxgloveSDK.Components.FoxRunQosDurability",
                "Unity.FoxgloveSDK.Components.FoxRunQosHistory"
            };
            Check(
                retiredTypes.All(
                    name => assembly.GetType(name, false) == null),
                "Structural 184C-1: core exposes no ROS endpoint, profile, or QoS type");
            Check(
                typeof(FoxRunDeliveryPolicy).Assembly
                    == assembly
                && typeof(FoxRunTransportId).Assembly
                    == assembly,
                "Structural 184C-2: core retains only neutral Provider identity and delivery intent");
        }

        private static void VerifyPortableDeliveryAxes()
        {
            var systemDefault =
                new FoxRunDeliveryPolicy(
                    FoxRunDeliveryReliability.SystemDefault,
                    FoxRunDeliveryDurability.SystemDefault,
                    FoxRunDeliveryHistory.SystemDefault,
                    0);
            var keepAll =
                new FoxRunDeliveryPolicy(
                    FoxRunDeliveryReliability.BestEffort,
                    FoxRunDeliveryDurability.TransientLocal,
                    FoxRunDeliveryHistory.KeepAll,
                    0);
            Check(
                systemDefault.History
                    == FoxRunDeliveryHistory.SystemDefault
                && keepAll.History
                    == FoxRunDeliveryHistory.KeepAll
                && keepAll.Durability
                    == FoxRunDeliveryDurability.TransientLocal,
                "Behavioral 184C-3: SystemDefault and KeepAll remain real transport-neutral values");
            Check(
                FoxRunDeliveryPolicy.ProviderDefault.Reliability
                    == FoxRunDeliveryReliability.ProviderDefault
                && FoxRunDeliveryPolicy.ProviderDefault.Durability
                    == FoxRunDeliveryDurability.ProviderDefault
                && FoxRunDeliveryPolicy.ProviderDefault.History
                    == FoxRunDeliveryHistory.ProviderDefault
                && FoxRunDeliveryPolicy.ProviderDefault.Depth == 0,
                "Behavioral 184C-4: omission remains a distinct ProviderDefault policy");
        }

        private static void VerifyProviderOriginScoping()
        {
            var bus = new FoxTopicBus();
            var contract = Contract(
                "/phase184/provider-origin",
                FoxTopicWriterPolicy.SingleWriter);
            var providerA = 0;
            var providerB = 0;
            bus.SubscribeResult<int>(
                contract.Topic,
                "provider.a",
                _ =>
                {
                    providerA++;
                    return true;
                });
            bus.SubscribeResult<int>(
                contract.Topic,
                "provider.b",
                _ =>
                {
                    providerB++;
                    return true;
                });
            var payload = 42;
            var first = bus.PublishToResultSubscribers(
                contract,
                184UL,
                in payload,
                "provider.a",
                sequence: 7);
            var second = bus.PublishToResultSubscribers(
                contract,
                185UL,
                in payload,
                "provider.b",
                sequence: 8);

            Check(
                first.AllSucceeded
                && second.AllSucceeded
                && providerA == 1
                && providerB == 1,
                "Behavioral 184D-1: result-bearing Provider routes receive only their exact origin");
        }

        private static void VerifyObserverIsolation()
        {
            var bus = new FoxTopicBus();
            var contract = Contract(
                "/phase184/observer",
                FoxTopicWriterPolicy.SingleWriter);
            var observerCalls = 0;
            var targetCalls = 0;
            bus.Subscribe<int>(
                contract.Topic,
                _ =>
                {
                    observerCalls++;
                    throw new InvalidOperationException(
                        "observer failure");
                });
            bus.SubscribeResult<int>(
                contract.Topic,
                "provider.target",
                _ =>
                {
                    targetCalls++;
                    return true;
                });
            var payload = 1;
            bus.PublishToObservers(
                contract,
                1UL,
                in payload,
                "local");
            var result = bus.PublishToResultSubscribers(
                contract,
                1UL,
                in payload,
                "provider.target");

            Check(
                observerCalls == 1
                && targetCalls == 1
                && result.AllSucceeded,
                "Behavioral 184D-2: observer faults cannot change a selected Provider verdict");
        }

        private static void VerifyWriterOwnership()
        {
            var bus = new FoxTopicBus();
            var single = Contract(
                "/phase184/single-writer",
                FoxTopicWriterPolicy.SingleWriter);
            var multi = Contract(
                "/phase184/multi-writer",
                FoxTopicWriterPolicy.MultiWriter);
            var first = bus.Register(single, "writer.a");
            var rejected = bus.Register(single, "writer.b");
            var multiA = bus.Register(multi, "writer.a");
            var multiB = bus.Register(multi, "writer.b");
            Check(
                first.Accepted
                && !rejected.Accepted
                && multiA.Accepted
                && multiB.Accepted
                && bus.IsRegistered(multi, "writer.a")
                && bus.IsRegistered(multi, "writer.b"),
                "Behavioral 184D-3: single-writer ownership rejects collisions while identical multi-writer contracts coexist");
        }

        private static void VerifyBoundedStreamBehavior()
        {
            var defaults = new FoxRunStreamOptions();
            Check(
                defaults.Capacity == 1024
                && defaults.MaxInputHz == 1000d
                && defaults.MaxBatch == 128
                && defaults.Overflow
                    == FoxRunStreamOverflowPolicy.DropOldest
                && typeof(FoxRunStream<int>)
                    .GetProperty("Latest") == null,
                "Structural 184E-1: stream defaults are finite and expose no racy raw Latest reference");

            long ticks = 0;
            var disposed = new List<int>();
            using var stream =
                new FoxRunStream<int>(
                    new FoxRunStreamOptions(
                        2,
                        10d,
                        2,
                        FoxRunStreamOverflowPolicy.DropOldest),
                    () => ticks,
                    timestampFrequency: 100);

            Check(
                stream.TryAdmitInput(),
                "Behavioral 184E-2: the first stream input is admitted");
            Check(
                !stream.TryAdmitInput(),
                "Behavioral 184E-3: an immediate duplicate arrival is rate-limited");
            ticks = 10;
            Check(
                stream.TryAdmitInput(),
                "Behavioral 184E-4: the next admission boundary is accepted");
            stream.TryEnqueueOwned(1, disposed.Add);
            stream.TryEnqueueOwned(2, disposed.Add);
            ticks = 20;
            var boundaryAdmitted = stream.TryAdmitInput();
            stream.TryEnqueueOwned(3, disposed.Add);

            Check(
                boundaryAdmitted
                && disposed.SequenceEqual(new[] { 1 })
                && stream.Count == 2
                && stream.Stats.DroppedOldest == 1
                && stream.Stats.RateDropped == 1
                && stream.Stats.HighWater == 2,
                "Behavioral 184E-5: DropOldest stays bounded with monotonic diagnostics");
            Check(
                stream.TryTakeLatest(out var latest)
                && latest.Value == 3,
                "Behavioral 184E-6: TryTakeLatest transfers ownership of the newest sample");
            latest.Dispose();
            latest.Dispose();
            Check(
                disposed.SequenceEqual(new[] { 1, 2, 3 })
                && stream.Count == 0
                && stream.Stats.Cleared == 1
                && stream.Stats.Taken == 1,
                "Behavioral 184E-7: displaced, cleared, and leased values dispose exactly once");
        }

        private static void
            VerifyNoPackageTestOrSampleArtifactDirectories()
        {
            var repoRoot =
                TestRepoRootLocator.FindRepoRoot()
                ?? throw new DirectoryNotFoundException(
                    "Could not locate repository root.");
            var violations = new List<string>();
            foreach (var package in Directory.EnumerateDirectories(
                         Path.Combine(repoRoot, "Packages"),
                         "dev.unity2foxglove.*",
                         SearchOption.TopDirectoryOnly))
            {
                foreach (var relative in new[]
                         {
                             "Tests",
                             "Samples~"
                         })
                {
                    var root = Path.Combine(package, relative);
                    if (!Directory.Exists(root))
                        continue;
                    foreach (var directory in
                             Directory.EnumerateDirectories(
                                 root,
                                 "*",
                                 SearchOption.AllDirectories))
                    {
                        if (ForbiddenArtifactDirectories.Contains(
                                Path.GetFileName(directory)))
                        {
                            violations.Add(
                                Path.GetRelativePath(
                                        repoRoot,
                                        directory)
                                    .Replace(
                                        Path.DirectorySeparatorChar,
                                        '/'));
                        }
                    }
                }
            }

            Check(
                violations.Count == 0,
                "Structural 184E-8: maintained package tests and samples contain no build/cache directories"
                + (violations.Count == 0
                    ? string.Empty
                    : ": "
                      + string.Join(
                          "; ",
                          violations.Take(12))));
        }

        private static HashSet<string> PublicPropertyNames(
            Type type)
            => type.GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .Where(
                    property =>
                        property.DeclaringType == type)
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

        private static FoxTopicContract Contract(
            string topic,
            FoxTopicWriterPolicy writerPolicy)
            => new(
                topic,
                "Demo.Value",
                "json",
                "int32",
                "demo-value-v1",
                FoxTopicVisibility.Exported,
                writerPolicy);

        private static bool Throws<TException>(
            Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Begin(string name)
        {
            Console.WriteLine("\n--- " + name + " ---");
            _passed = 0;
        }

        private static void End(string name)
            => Console.WriteLine(
                name
                + ": "
                + _passed
                + " checks passed.\n");

        private static void Check(
            bool condition,
            string label)
        {
            if (!condition)
                throw new InvalidOperationException(
                    "[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
