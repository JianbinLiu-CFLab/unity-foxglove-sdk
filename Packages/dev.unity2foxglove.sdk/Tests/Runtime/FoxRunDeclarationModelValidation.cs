// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 183A validation migrated to the open Provider declaration model.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunDeclarationModelValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 183A: Provider declaration and duplex policy ===");
            _passed = 0;

            VerifyPublicDeclarationSurface();
            VerifyTransportIdentity();
            VerifyDirectionalSessionFreeze();
            VerifyDeliveryPolicy();
            VerifyDescriptorAndGeneratorBoundary();

            Console.WriteLine($"Phase 183A: {_passed} checks passed.");
        }

        private static void VerifyPublicDeclarationSurface()
        {
            var memberProperties = typeof(FoxRunAttribute)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.DeclaringType == typeof(FoxRunAttribute))
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Check(
                memberProperties.Contains(nameof(FoxRunAttribute.PublishTransportIds))
                && memberProperties.Contains(nameof(FoxRunAttribute.SubscribeTransportId))
                && memberProperties.Contains(nameof(FoxRunAttribute.Reliability))
                && memberProperties.Contains(nameof(FoxRunAttribute.Durability))
                && memberProperties.Contains(nameof(FoxRunAttribute.History))
                && memberProperties.Contains(nameof(FoxRunAttribute.Depth)),
                "183A-A1: member declarations expose open directional Provider IDs and neutral delivery axes");

            Check(
                !memberProperties.Contains("Source")
                && !memberProperties.Contains("Targets")
                && !memberProperties.Contains("QoS"),
                "183A-A2: closed endpoint and Provider-specific profile aliases are absent");

            var aggregateProperties = typeof(FoxRunMessageAttribute)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.DeclaringType == typeof(FoxRunMessageAttribute))
                .Select(property => property.Name)
                .ToArray();
            Check(
                aggregateProperties.Contains(nameof(FoxRunMessageAttribute.PublishTransportIds))
                && !aggregateProperties.Contains("Targets"),
                "183A-A3: aggregate declarations use the same open publish Provider selection");
        }

        private static void VerifyTransportIdentity()
        {
            var websocket = new FoxRunTransportId(
                FoxgloveWebSocketTransport.Id);
            Check(
                websocket == FoxgloveWebSocketTransport.TransportId
                && websocket.Value == "foxglove.websocket",
                "183A-B1: the built-in WebSocket Provider has a stable identity");
            Check(
                FoxRunTransportId.TryCreate(
                    "example.transport",
                    out var custom)
                && custom.Value == "example.transport",
                "183A-B2: new Providers do not require a core enum change");
            Check(
                !FoxRunTransportId.TryCreate("Everything", out _)
                && !FoxRunTransportId.TryCreate("ros2_native", out _)
                && !FoxRunTransportId.TryCreate(" example.transport", out _),
                "183A-B3: IDs use strict reverse-domain grammar");
        }

        private static void VerifyDirectionalSessionFreeze()
        {
            var publishState = new FoxRunPublishSessionState();
            var initialPublish = publishState.BeginIfNeeded(
                new[]
                {
                    new FoxRunTransportId("z.provider"),
                    FoxgloveWebSocketTransport.TransportId,
                    new FoxRunTransportId("a.provider")
                },
                FoxRunEncoding.MessagePack,
                30f,
                FoxRunDeliveryPolicy.ProviderDefault);
            var repeatedPublish = publishState.BeginIfNeeded(
                new[] { new FoxRunTransportId("other.provider") },
                FoxRunEncoding.JSON,
                5f,
                FoxRunDeliveryPolicy.ProviderDefault);
            Check(
                ReferenceEquals(initialPublish, repeatedPublish)
                && initialPublish.SessionGeneration == 1
                && initialPublish.PublishTransportIds
                    .Select(id => id.Value)
                    .SequenceEqual(
                        new[]
                        {
                            "a.provider",
                            "foxglove.websocket",
                            "z.provider"
                        })
                && initialPublish.WebSocketEncoding
                   == FoxRunEncoding.MessagePack,
                "183A-C1: publish selection is canonical and frozen for one session");

            publishState.End();
            var nextPublish = publishState.BeginIfNeeded(
                new[] { FoxgloveWebSocketTransport.TransportId },
                FoxRunEncoding.Protobuf,
                float.NaN,
                FoxRunDeliveryPolicy.ProviderDefault);
            Check(
                nextPublish.SessionGeneration == 2
                && nextPublish.DefaultPublishRateHz == 10f,
                "183A-C2: a new publish session recaptures and normalizes policy");

            var subscriptionState = new FoxRunSubscriptionSessionState();
            var initialSubscribe = subscriptionState.BeginIfNeeded(
                new FoxRunTransportId("example.source"),
                FoxRunEncoding.JSON,
                FoxRunDeliveryPolicy.ProviderDefault,
                0,
                -1,
                0);
            var repeatedSubscribe = subscriptionState.BeginIfNeeded(
                FoxgloveWebSocketTransport.TransportId,
                FoxRunEncoding.Protobuf,
                FoxRunDeliveryPolicy.ProviderDefault,
                100,
                100,
                1024);
            Check(
                ReferenceEquals(initialSubscribe, repeatedSubscribe)
                && initialSubscribe.DefaultProvider.Value
                   == "example.source"
                && initialSubscribe.TransportAdmissionRateLimitHz == 1
                && initialSubscribe.DefaultSubscribeRateHz == 1
                && initialSubscribe.MaxPayloadBytes == 1,
                "183A-C3: subscribe source and bounds are frozen and fail-safe");
        }

        private static void VerifyDeliveryPolicy()
        {
            var policy = new FoxRunDeliveryPolicy(
                FoxRunDeliveryReliability.BestEffort,
                FoxRunDeliveryDurability.Volatile,
                FoxRunDeliveryHistory.KeepLast,
                5);
            Check(
                policy.Reliability
                   == FoxRunDeliveryReliability.BestEffort
                && policy.History == FoxRunDeliveryHistory.KeepLast
                && policy.Depth == 5,
                "183A-D1: neutral delivery policy preserves explicit intent");
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
                "183A-D2: invalid history/depth combinations fail closed");
        }

        private static void VerifyDescriptorAndGeneratorBoundary()
        {
            var root = TestRepoRootLocator.FindRepoRoot();
            var descriptor = File.ReadAllText(
                Path.Combine(
                    root,
                    "Packages/dev.unity2foxglove.sdk/Editor/Shared/"
                    + "FoxRunDescriptor/FoxRunGenerationDescriptorConstants.cs"));
            var model = File.ReadAllText(
                Path.Combine(
                    root,
                    "Packages/dev.unity2foxglove.sdk/Editor/Shared/"
                    + "FoxRunDescriptor/FoxRunGenerationModel.cs"));
            var generator = File.ReadAllText(
                Path.Combine(
                    root,
                    "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/"
                    + "src/FoxgloveLogSourceGenerator.cs"));
            Check(
                descriptor.Contains("DescriptorVersion = 6", StringComparison.Ordinal)
                && model.Contains("PublishTransportIds", StringComparison.Ordinal)
                && model.Contains("SubscribeTransportId", StringComparison.Ordinal),
                "183A-E1: descriptor v6 carries direction-specific Provider IDs");
            Check(
                !generator.Contains("Ros2Native", StringComparison.Ordinal)
                && !generator.Contains("Ros2Bridge", StringComparison.Ordinal)
                && !generator.Contains("U2R2", StringComparison.Ordinal),
                "183A-E2: the core generator contains only neutral emission");
        }

        private static bool Throws<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
