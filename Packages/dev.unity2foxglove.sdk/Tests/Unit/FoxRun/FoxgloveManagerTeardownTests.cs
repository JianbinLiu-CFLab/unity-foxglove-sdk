// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Unity.FoxgloveSDK.Components;
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
                "EndFoxRunTransportSession",
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
                "EndFoxRunTransportSession",
                "_replayCursorEndpoint?.Dispose()",
                "_certificateDistributor?.Dispose()",
                "FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(",
                "_runtime?.Dispose()",
                "_runtime = null",
                "EndFoxRunPublishSession",
                "FoxgloveProfiler.ResetGlobal(this)");
        }

        [Fact]
        public void RuntimeDisposeRetryReportsTransientFailureAndReleasesReference()
        {
            var attempts = 0;
            var releases = 0;
            var reports = new List<string>();

            FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(
                () =>
                {
                    attempts++;
                    if (attempts == 1)
                        throw new InvalidOperationException("first failure");
                },
                () => releases++,
                exception => reports.Add(exception.Message));

            Assert.Equal(2, attempts);
            Assert.Equal(1, releases);
            Assert.Equal(new[] { "first failure" }, reports);
        }

        [Fact]
        public void RuntimeDisposeRetryDoesNotRepeatSuccessfulDispose()
        {
            var attempts = 0;
            var releases = 0;
            var reports = new List<string>();

            FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(
                () => attempts++,
                () => releases++,
                exception => reports.Add(exception.Message));

            Assert.Equal(1, attempts);
            Assert.Equal(1, releases);
            Assert.Empty(reports);
        }

        [Fact]
        public void RuntimeDisposeRetryRethrowsFirstFailureAndReleasesReferenceWhenBothAttemptsFail()
        {
            var attempts = 0;
            var releases = 0;
            var reports = new List<string>();

            var failure = Assert.Throws<InvalidOperationException>(
                () => FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(
                    () =>
                    {
                        attempts++;
                        throw new InvalidOperationException(
                            attempts == 1 ? "first failure" : "retry failure");
                    },
                    () => releases++,
                    exception => reports.Add(exception.Message)));

            Assert.Equal("first failure", failure.Message);
            Assert.Equal(2, attempts);
            Assert.Equal(1, releases);
            Assert.Equal(new[] { "first failure", "retry failure" }, reports);
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
