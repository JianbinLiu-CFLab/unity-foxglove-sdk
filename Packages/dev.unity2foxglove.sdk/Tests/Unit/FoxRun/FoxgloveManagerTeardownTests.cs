// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxgloveManagerTeardownTests
    {
        private const string ManagerSourcePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";

        [Fact]
        public void DisableTeardownRunsEveryMandatoryStepInOrderAndRethrowsFirstFatal()
        {
            var calls = new List<string>();
            var method = TeardownMethod("RunDisable");

            var failure = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(
                    null,
                    new object[]
                    {
                        Step(calls, "subscription"),
                        Step(calls, "bridge", new OutOfMemoryException("disable-primary")),
                        Step(calls, "server", new InvalidOperationException("disable-secondary")),
                        Step(calls, "publish"),
                        Step(calls, "output-watch"),
                        Step(calls, "profiler")
                    }));

            var primary = Assert.IsType<OutOfMemoryException>(failure.InnerException);
            Assert.Equal("disable-primary", primary.Message);
            Assert.Equal(
                new[]
                {
                    "subscription",
                    "bridge",
                    "server",
                    "publish",
                    "output-watch",
                    "profiler"
                },
                calls);

            AssertLifecycleUsesHelperInOrder(
                "private void OnDisable()",
                "FoxgloveManagerTeardownState.RunDisable(",
                "EndFoxRunSubscriptionSession",
                "_ros2BridgeRuntime?.Stop()",
                "StopServer(restoreLivePublishers: true)",
                "EndFoxRunPublishSession",
                "_connectionState.OutputModeWatchInitialized = false",
                "FoxgloveProfiler.ResetGlobal(this)");
        }

        [Fact]
        public void DestroyTeardownRunsEveryMandatoryStepInOrderAndRethrowsFirstFatal()
        {
            var calls = new List<string>();
            var method = TeardownMethod("RunDestroy");

            var failure = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(
                    null,
                    new object[]
                    {
                        Step(calls, "subscription"),
                        Step(calls, "server"),
                        Step(calls, "bridge", new OutOfMemoryException("destroy-primary")),
                        Step(calls, "replay", new InvalidOperationException("destroy-secondary")),
                        Step(calls, "certificate"),
                        Step(calls, "runtime"),
                        Step(calls, "publish"),
                        Step(calls, "profiler")
                    }));

            var primary = Assert.IsType<OutOfMemoryException>(failure.InnerException);
            Assert.Equal("destroy-primary", primary.Message);
            Assert.Equal(
                new[]
                {
                    "subscription",
                    "server",
                    "bridge",
                    "replay",
                    "certificate",
                    "runtime",
                    "publish",
                    "profiler"
                },
                calls);

            AssertLifecycleUsesHelperInOrder(
                "private void OnDestroy()",
                "FoxgloveManagerTeardownState.RunDestroy(",
                "EndFoxRunSubscriptionSession",
                "StopServer(restoreLivePublishers: true)",
                "_ros2BridgeRuntime?.Dispose()",
                "_replayCursorEndpoint?.Dispose()",
                "_certificateDistributor?.Dispose()",
                "_runtime?.Dispose()",
                "EndFoxRunPublishSession",
                "FoxgloveProfiler.ResetGlobal(this)");
        }

        private static MethodInfo TeardownMethod(string name)
        {
            var type = typeof(FoxgloveManagerTeardownTests).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxgloveManagerTeardownState");
            Assert.NotNull(type);
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method;
        }

        private static Action Step(
            ICollection<string> calls,
            string name,
            Exception failure = null)
            => () =>
            {
                calls.Add(name);
                if (failure != null)
                    throw failure;
            };

        private static void AssertLifecycleUsesHelperInOrder(
            string signature,
            params string[] expected)
        {
            var source = TestSources.Text(ManagerSourcePath);
            var lifecycle = TestSources.ExtractMethod(source, signature);
            var previous = -1;
            foreach (var value in expected)
            {
                var current = lifecycle.IndexOf(value, StringComparison.Ordinal);
                Assert.True(
                    current > previous,
                    signature + " must contain `" + value + "` after the previous teardown step.");
                previous = current;
            }
        }
    }
}
