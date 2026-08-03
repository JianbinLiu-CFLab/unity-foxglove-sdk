// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Profiling
// Purpose: Phase151B source-shape tests for profiler marker instrumentation.

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Profiling
{
    public sealed class ProfilerMarkerInstrumentationTests
    {
        private static readonly (string Marker, string RelativePath)[] RequiredMarkers =
        {
            ("FoxglovePublisher.Tick", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs"),
            ("FoxgloveManager.PublishJson", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs"),
            ("FoxgloveManager.PublishProto", "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs"),
            ("VirtualLidar.Update", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs"),
            ("VirtualLidar.ScheduleScan", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs"),
            ("VirtualLidar.BuildPoints.Schedule", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs"),
            ("VirtualLidar.Publish", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanFramePublisher.cs"),
            ("VirtualImu.Publish", "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs"),
            ("PointCloudWorker.EncodeDraco", "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs"),
            ("PointCloudWorker.EncodePackedPointCloud", "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudWorkerEncoders.cs"),
            ("WsSendQueue.Enqueue", "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs"),
            ("WsSendQueue.Flush", "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs"),
            ("WsFrameCodec.Encode", "Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsFrameCodec.cs"),
        };

        [Fact]
        public void Phase151BMarkersUseBoundedLiteralNames()
        {
            foreach (var (marker, relativePath) in RequiredMarkers)
            {
                var text = Read(relativePath);
                Assert.Contains("\"" + marker + "\"", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ProfilerMarkersDoNotUseDynamicNames()
        {
            var runtimeRoot = Path.Combine(RepoRoot, "Packages", "dev.unity2foxglove.sdk", "Runtime");
            var violations = new List<string>();

            foreach (var file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("new ProfilerMarker($", StringComparison.Ordinal)
                    || text.Contains("new ProfilerMarker(string.", StringComparison.Ordinal)
                    || text.Contains("new ProfilerMarker(\" +", StringComparison.Ordinal)
                    || text.Contains("BeginSample($", StringComparison.Ordinal)
                    || text.Contains("Sample($", StringComparison.Ordinal))
                {
                    violations.Add(Path.GetRelativePath(RepoRoot, file));
                }
            }

            Assert.Empty(violations);
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not find repository root.");
            }
        }
    }
}
