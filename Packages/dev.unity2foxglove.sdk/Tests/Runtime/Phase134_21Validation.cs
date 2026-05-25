// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-21 validation for ROS2 For Unity adapter facade behavior.

using System;
using Unity2Foxglove.Ros2ForUnity;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_21Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyFactoryReturnsUnavailableSingleton();
            VerifyUnavailableFacadeIsRepeatableAndDisposable();
            VerifyUnavailableFacadeNormalizesBlankNames();

            Console.WriteLine($"Phase134_21Validation: PASS ({_passed} checks)");
        }

        private static void VerifyFactoryReturnsUnavailableSingleton()
        {
            var first = Unity2FoxgloveRos2ContextFactory.Create();
            var second = Unity2FoxgloveRos2ContextFactory.Create();

            Check(ReferenceEquals(first, second), "134-21-A1: factory returns stable unavailable singleton");
            Check(!first.IsAvailable
                  && first.Status == Unity2FoxgloveRos2Status.Unavailable
                  && first.StatusMessage.Contains("runtime", StringComparison.OrdinalIgnoreCase),
                "134-21-A2: unavailable singleton reports explicit runtime status");
        }

        private static void VerifyUnavailableFacadeIsRepeatableAndDisposable()
        {
            var context = Unity2FoxgloveRos2ContextFactory.Create();
            var callbackCount = 0;

            for (var i = 0; i < 8; i++)
            {
                var node = context.CreateNode("unity2foxglove_phase134_21_" + i);
                var publisher = node.CreatePublisher<string>("/unity2foxglove/phase134_21/out_" + i);
                var subscription = node.CreateSubscription<string>(
                    "/unity2foxglove/phase134_21/in_" + i,
                    _ => callbackCount++);

                Check(node.Name == "unity2foxglove_phase134_21_" + i,
                    "134-21-B1: unavailable node preserves explicit name iteration " + i);
                Check(publisher.Topic == "/unity2foxglove/phase134_21/out_" + i,
                    "134-21-B2: unavailable publisher preserves explicit topic iteration " + i);
                Check(subscription.Topic == "/unity2foxglove/phase134_21/in_" + i,
                    "134-21-B3: unavailable subscription preserves explicit topic iteration " + i);
                Check(!publisher.TryPublish("payload", out var error) && !string.IsNullOrWhiteSpace(error),
                    "134-21-B4: unavailable publisher remains no-op with error iteration " + i);

                subscription.Dispose();
                subscription.Dispose();
                publisher.Dispose();
                publisher.Dispose();
                node.Dispose();
                node.Dispose();
            }

            context.Dispose();
            context.Dispose();

            Check(callbackCount == 0,
                "134-21-B5: unavailable subscriptions never invoke callbacks during repeated creation");
        }

        private static void VerifyUnavailableFacadeNormalizesBlankNames()
        {
            var context = Unity2FoxgloveRos2ContextFactory.Create();
            var node = context.CreateNode(" ");
            var publisher = node.CreatePublisher<object>(null);
            var subscription = node.CreateSubscription<object>(string.Empty, _ => { });

            Check(node.Name == "unity2foxglove_unavailable",
                "134-21-C1: unavailable node normalizes blank node names");
            Check(publisher.Topic == "/unity2foxglove/unavailable",
                "134-21-C2: unavailable publisher normalizes blank topics");
            Check(subscription.Topic == "/unity2foxglove/unavailable",
                "134-21-C3: unavailable subscription normalizes blank topics");

            subscription.Dispose();
            publisher.Dispose();
            node.Dispose();
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }
    }
}
