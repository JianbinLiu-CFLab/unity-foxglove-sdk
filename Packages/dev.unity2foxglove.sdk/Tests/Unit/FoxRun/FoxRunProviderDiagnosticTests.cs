// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed class FoxRunProviderDiagnosticTests
    {
        [Fact]
        public void StructuredFailureCapturesFieldsWithoutRetainingException()
        {
            FoxRunProviderDiagnostics.Clear();
            try
            {
                throw new InvalidOperationException("credential=secret payload=bytes");
            }
            catch (Exception exception)
            {
                FoxRunProviderDiagnostics.Record("provider", "Publish", exception, 7, "/topic");
            }

            var diagnostic = Assert.Single(FoxRunProviderDiagnostics.Snapshot());
            Assert.Equal("provider", diagnostic.ProviderId);
            Assert.Equal("Publish", diagnostic.Operation);
            Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.ExceptionType);
            Assert.NotEmpty(diagnostic.Stack);
            Assert.Equal((ulong)7, diagnostic.Generation);
            Assert.Equal("/topic", diagnostic.Topic);
            Assert.Null(diagnostic.GetType().GetProperty("Exception"));
        }

        [Fact]
        public void BeginningANewGenerationHidesPreviousProviderFailures()
        {
            FoxRunProviderDiagnostics.Clear();
            FoxRunProviderDiagnostics.Record(
                "provider",
                "Publish",
                new InvalidOperationException("old"),
                7,
                "/old");

            FoxRunProviderDiagnostics.BeginGeneration(8);

            Assert.Empty(FoxRunProviderDiagnostics.Snapshot());
            Assert.Empty(FoxRunProviderDiagnostics.Snapshot(7));
            Assert.Empty(FoxRunProviderDiagnostics.Snapshot(8));

            FoxRunProviderDiagnostics.Record(
                "provider",
                "Publish",
                new InvalidOperationException("new"),
                8,
                "/new");

            var snapshot = Assert.Single(FoxRunProviderDiagnostics.Snapshot());
            Assert.Equal((ulong)8, snapshot.Generation);
            Assert.Single(FoxRunProviderDiagnostics.Snapshot(8));
            Assert.Empty(FoxRunProviderDiagnostics.Snapshot(7));
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void LogHubWarnOnceUsesStableFailureIdentity()
        {
            var hub = new FoxgloveLogHub();
            var warnOnce = typeof(FoxgloveLogHub).GetMethod(
                "WarnOnce",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    typeof(IFoxgloveLogSource),
                    typeof(int),
                    typeof(Exception)
                },
                modifiers: null);
            var reportedFailures = typeof(FoxgloveLogHub).GetField(
                "_reportedFailures",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var warnWithKey = typeof(FoxgloveLogHub).GetMethod(
                "WarnOnce",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(string), typeof(string) },
                modifiers: null);

            Assert.NotNull(warnOnce);
            Assert.NotNull(reportedFailures);
            Assert.NotNull(warnWithKey);
            warnOnce.Invoke(
                hub,
                new object[]
                {
                    new Source(),
                    3,
                    new InvalidOperationException("first")
                });
            warnOnce.Invoke(
                hub,
                new object[]
                {
                    new Source(),
                    3,
                    new InvalidOperationException("second")
                });

            Assert.Equal(
                1,
                ((ICollection)reportedFailures.GetValue(hub)).Count);

            for (var index = 0; index < 300; index++)
            {
                warnWithKey.Invoke(
                    hub,
                    new object[] { "dynamic-" + index, "detail-" + index });
            }

            Assert.Equal(
                256,
                ((ICollection)reportedFailures.GetValue(hub)).Count);
        }

        private sealed class Source : IFoxgloveLogSource
        {
            public int FoxgloveLog_TopicCount => 0;

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
                => throw new ArgumentOutOfRangeException(nameof(index));

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
            {
            }
        }
#endif
    }
}
