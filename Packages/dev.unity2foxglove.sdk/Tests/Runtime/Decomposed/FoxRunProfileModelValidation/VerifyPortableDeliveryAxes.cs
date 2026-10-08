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
    public static partial class FoxRunProfileModelValidation
    {
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
            stream.TryEnqueueOwnedAfterAdmission(1, disposed.Add);
            stream.TryEnqueueOwnedAfterAdmission(2, disposed.Add);
            ticks = 20;
            var boundaryAdmitted = stream.TryAdmitInput();
            stream.TryEnqueueOwnedAfterAdmission(3, disposed.Add);

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
