// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-079 runtime review regression checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Numerics;
using Foxglove.Schemas;
using Foxglove.Schemas.Video;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Sensors.Lidar;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{


public sealed partial class RuntimeReviewFindingTests
    {
        [Fact]
        public void CameraSourceFallbackUsesUnityNullSemantics()
        {
            var publisher = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Assert.Contains("var sourceCamera = _captureResources.SourceCamera;", publisher, StringComparison.Ordinal);
            Assert.Contains("return sourceCamera != null ? sourceCamera : GetComponent<Camera>();", publisher, StringComparison.Ordinal);
            Assert.DoesNotContain("_captureResources.SourceCamera ?? GetComponent<Camera>()", publisher, StringComparison.Ordinal);
        }

        [Fact]
        public void SensorGenerationAndConfigurationContractsRemainBoundToRuntimeState()
        {
            var managerClock = Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveSharedSensorClock.cs");
            var imu = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");

            Assert.Contains("public int Generation", managerClock, StringComparison.Ordinal);
            Assert.Contains("SharedSensorClockGeneration", imu, StringComparison.Ordinal);
            Assert.Contains("SharedSensorClockGeneration", lidar, StringComparison.Ordinal);
            Assert.Contains("NormalizeSerializedNumericConfiguration();", lidar, StringComparison.Ordinal);
            Assert.Contains("RebuildScanConfiguration();", lidar, StringComparison.Ordinal);
            Assert.Contains("Configuration changes during Play are deferred until the component is disabled and re-enabled.", lidar, StringComparison.Ordinal);
        }

        [Fact]
        public void LidarDeskewBindsMotionCompensationToSensorTransform()
        {
            var lidar = Text("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            Assert.Contains("SetMotionCompensationTransform(transform);", lidar, StringComparison.Ordinal);
            Assert.Contains("LidarScanTiming.NextScanStartPhysSeconds(_activeScanStartPhysSeconds, _scanPeriod)", lidar, StringComparison.Ordinal);
        }

        [Fact]
        public void VideoInputAdmissionAndJpegOrientationContractsRemainExplicit()
        {
            var policy = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraPipelineHealthPolicy.cs");
            var pipeline = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var asyncJpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegWorkerPayloads.cs");
            var syncJpeg = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");

            Assert.Contains("VideoInputQueueFull", policy, StringComparison.Ordinal);
            Assert.Contains("InputQueueDepth", pipeline, StringComparison.Ordinal);
            Assert.Contains("flipVertical: true", asyncJpeg, StringComparison.Ordinal);
            Assert.Contains("Rgb24Orientation.FlipRowsInPlace", syncJpeg, StringComparison.Ordinal);
        }

        private sealed class FakeVideoSidecar : ICameraVideoEncoderSidecar
        {
            private readonly Queue<byte[]> _units = new Queue<byte[]>();

            internal FakeVideoSidecar(int count)
            {
                for (var i = 0; i < count; i++)
                    _units.Enqueue(new byte[] { (byte)i });
            }

            public int Remaining => _units.Count;
            public bool IsRunning => true;
            public int OutputQueueDepth => _units.Count;
            public int MaxOutputQueue => 8;
            public int InputQueueDepth => 0;
            public int MaxInputQueue => 8;
            public string LastDiagnosticLine => "";
            public string LastError => "";
            public bool TrySubmitFrame(byte[] frame) => true;
            public bool TryDequeueAccessUnit(out byte[] accessUnit)
            {
                if (_units.Count == 0)
                {
                    accessUnit = null;
                    return false;
                }

                accessUnit = _units.Dequeue();
                return true;
            }
            public void Dispose() { }
        }

        private sealed class FakeTimestampedVideoSidecar : ITimestampedCameraVideoEncoderSidecar
        {
            private readonly Queue<EncodedVideoAccessUnit> _units;

            internal FakeTimestampedVideoSidecar(IEnumerable<EncodedVideoAccessUnit> units)
            {
                _units = new Queue<EncodedVideoAccessUnit>(units);
            }

            public int Remaining => _units.Count;
            public bool IsRunning => true;
            public int OutputQueueDepth => _units.Count;
            public int MaxOutputQueue => 8;
            public int InputQueueDepth => 0;
            public int MaxInputQueue => 8;
            public string LastDiagnosticLine => "";
            public string LastError => "";
            public bool TrySubmitFrame(byte[] frame) => true;
            public bool TrySubmitFrame(byte[] frame, ulong timestampNs) => true;
            public bool TryDequeueAccessUnit(out byte[] accessUnit)
            {
                if (!TryDequeueEncodedAccessUnit(out var timestamped))
                {
                    accessUnit = null;
                    return false;
                }

                accessUnit = timestamped.Data;
                return true;
            }
            public bool TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit)
            {
                if (_units.Count == 0)
                {
                    accessUnit = default;
                    return false;
                }

                accessUnit = _units.Dequeue();
                return true;
            }
            public void Dispose() { }
        }

        private readonly struct CameraAuthorityProbe
        {
            internal CameraAuthorityProbe(uint width, uint height, double fx, double fy, double cx, double cy, bool orthographic)
            {
                Width = width;
                Height = height;
                Fx = fx;
                Fy = fy;
                Cx = cx;
                Cy = cy;
                Orthographic = orthographic;
            }

            internal uint Width { get; }
            internal uint Height { get; }
            internal double Fx { get; }
            internal double Fy { get; }
            internal double Cx { get; }
            internal double Cy { get; }
            internal bool Orthographic { get; }
        }

        private static string Text(string relativePath)
        {
            var path = PathOf(relativePath);
            Assert.True(File.Exists(path), "Required source file was not found: " + relativePath);
            return File.ReadAllText(path);
        }

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Unity2Foxglove"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Packages")))
                        return dir.FullName;
                    dir = dir.Parent;
                }

                Assert.Fail("Could not locate repository root for sensor review tests.");
                return string.Empty;
            }
        }
    }
}
