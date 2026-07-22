// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-090 review regressions for runtime and optional ROS2 package findings.

using System;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-090")]
    [Trait("Domain", "Review")]
    public sealed class Phase173090ReviewTests
    {
        [Fact]
        public void Ros2TimeSourcesDoNotRecreateClocksAfterDispose()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs",
                "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs",
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs"
            })
            {
                var source = Text(path);
                Assert.Contains("private int disposed", source, StringComparison.Ordinal);
                Assert.Contains("Volatile.Read(ref disposed)", source, StringComparison.Ordinal);
                Assert.Contains("Interlocked.Exchange(ref disposed, 1)", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RemoteManifestTimestampGuardNoLongerChecksUnreachableInt64Range()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Remote/RemoteMcapOfficialManifestSerializer.cs");

            Assert.DoesNotContain("seconds > long.MaxValue", source, StringComparison.Ordinal);
            Assert.Contains("MaxDateTimeOffsetUnixSeconds", source, StringComparison.Ordinal);

            var manifest = new RemoteMcapManifest();
            manifest.Sources.Add(new RemoteMcapSource
            {
                DataUrl = "/data",
                StartTimeNs = 1_500_000_000UL,
                EndTimeNs = 1_500_000_001UL
            });
            var json = RemoteMcapOfficialManifestSerializer.Serialize(manifest);
            Assert.Contains("\"startTime\":\"1970-01-01T00:00:01.5Z\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void ChannelGenerationValidationUsesVolatileRead()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Channels.cs");

            Assert.Contains("using System.Threading;", source, StringComparison.Ordinal);
            Assert.Contains("Volatile.Read(ref _connectionState.ChannelSessionGeneration)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FoxgloveSchemaAttributeDoesNotFlowToSubclasses()
        {
            var inherited = Attribute.GetCustomAttribute(
                typeof(DerivedSchemaDto),
                typeof(FoxgloveSchemaAttribute),
                inherit: true);

            Assert.Null(inherited);
            Assert.Equal("review.Base", Attribute.GetCustomAttribute(
                typeof(BaseSchemaDto),
                typeof(FoxgloveSchemaAttribute)) is FoxgloveSchemaAttribute attr ? attr.SchemaName : null);
        }

        [Fact]
        public void SessionTimeBroadcasterInvalidRatesUseTenHertzBoundary()
        {
            var broadcaster = new SessionTimeBroadcaster();
            var startTicks = TimeSpan.TicksPerSecond * 3L;
            var defaultWindow = TimeSpan.TicksPerSecond / 10;

            Assert.True(broadcaster.TryReserveBroadcast(startTicks, float.NegativeInfinity));
            Assert.False(broadcaster.TryReserveBroadcast(startTicks + defaultWindow - 1, float.NegativeInfinity));
            Assert.True(broadcaster.TryReserveBroadcast(startTicks + defaultWindow, float.NegativeInfinity));
        }

        [Fact]
        public void CameraHealthPolicyStillTreatsZeroReadbackLimitAsQueuePressure()
        {
            var result = CameraPipelineHealthPolicy.Evaluate(new CameraPipelineHealthInput
            {
                Mode = CameraPipelineHealthMode.Balanced,
                PendingReadbacks = 1,
                MaxPendingReadbacks = 0,
                MaxEncodeQueueDepth = 2,
                MaxCompletedQueueDepth = 2,
                MaxVideoOutputQueueDepth = 1,
                Width = 640,
                Height = 480
            });

            Assert.False(result.AllowCapture);
            Assert.Equal(CameraPipelineHealthSkipReason.ReadbackQueueFull, result.SkipReason);
        }

        [Fact]
        public void CameraPublisherWarnsOnceForInvalidHealthLimits()
        {
            var diagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Diagnostics.cs");
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Assert.Contains("_cameraHealthLimitWarningIssued", publisher, StringComparison.Ordinal);
            Assert.Contains("WarnIfCameraHealthLimitsInvalid", diagnostics, StringComparison.Ordinal);
            Assert.Contains("Camera health queue limits must be positive", diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarRosetteTestsNameProbeFrequencies()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Sensors/LidarProfileAndPatternTests.cs");

            Assert.Contains("RosetteAzimuthFrequencyRatio", source, StringComparison.Ordinal);
            Assert.Contains("RosettePositiveAzimuthProbeFrequency", source, StringComparison.Ordinal);
            Assert.Contains("RosettePositiveElevationProbeFrequency", source, StringComparison.Ordinal);
        }

        [FoxgloveSchema("review.Base")]
        private class BaseSchemaDto
        {
        }

        private sealed class DerivedSchemaDto : BaseSchemaDto
        {
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root from test base directory.");
        }
    }
}
