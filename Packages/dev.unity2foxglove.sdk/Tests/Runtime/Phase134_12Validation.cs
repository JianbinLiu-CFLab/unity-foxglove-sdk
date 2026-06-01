// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-12 regression coverage for protobuf builders and typed publishers.

using System;
using System.IO;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Phase 134-12 regression checks for camera timestamping, sidecar wiring and shared helpers.
    /// </summary>
    public static class Phase134_12Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all assertions for phase 134-12.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-12: Protobuf Builders And Typed Publishers ===");
            _passed = 0;

            CameraPublisherDoesNotUseGlobalReadbackWait(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs",
                "primary camera publisher");
            CameraPublisherDoesNotUseGlobalReadbackWait(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs",
                "legacy compressed video publisher");
            CameraPublisherLocksRuntimeOutputMode();
            CameraPublisherCarriesRenderTimestamp();
            TimestampedVideoSidecarContractIsWired();
            CompressedCameraBuildersUseTimestampHelper();
            LaserScanRejectsInvalidAngles();
            PointCloudPendingFrameIsThreadSafe();
            TransformPublisherSanitizesAndSharesConversion();
            SceneCubePublisherReusesRos2Payload();

            Console.WriteLine($"Phase 134-12: {_passed} checks passed.");
        }

        /// <summary>
        /// Verifies camera publisher does not block on global readback wait APIs.
        /// </summary>
        private static void CameraPublisherDoesNotUseGlobalReadbackWait(string path, string label)
        {
            var source = Read(path);
            Check(!source.Contains("AsyncGPUReadback.WaitAllRequests()"),
                $"134-12A-1: {label} destroy path does not wait for global AsyncGPUReadback requests");
            Check(source.Contains("_cleanupWhenReadbacksDrain = _pendingRequests > 0;"),
                $"134-12A-2: {label} retains local pending-readback cleanup policy");
            Check(source.Contains("if (_pendingRequests == 0") && source.Contains("CleanupResources();"),
                $"134-12A-3: {label} still cleans resources immediately when no local readback is pending");
            Check(source.Contains("generation != _captureGeneration"),
                $"134-12A-4: {label} keeps generation guard for stale callbacks");
        }

        /// <summary>
        /// Verifies Play Mode output mode locking remains active for camera schema setup.
        /// </summary>
        private static void CameraPublisherLocksRuntimeOutputMode()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(source.Contains("private CameraOutputMode _runtimeOutputMode"),
                "134-12B-1: camera publisher tracks the Play Mode output mode");
            Check(source.Contains("Application.isPlaying && _runtimeOutputModeInitialized"),
                "134-12B-2: camera schema resolves through the locked Play Mode mode");
            Check(source.Contains("LockRuntimeOutputMode();") && source.IndexOf("LockRuntimeOutputMode();", StringComparison.Ordinal) < source.IndexOf("base.OnEnable();", StringComparison.Ordinal),
                "134-12B-3: camera locks output mode before base registration can advertise schema");
            Check(source.Contains("Camera output mode changes during Play Mode are ignored"),
                "134-12B-4: camera surfaces a clear warning for runtime mode switches");
        }

        /// <summary>
        /// Checks render-time timestamp propagation for primary and legacy camera paths.
        /// </summary>
        private static void CameraPublisherCarriesRenderTimestamp()
        {
            var camera = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            Check(camera.Contains("var renderUnixNs = CurrentLogTimeNs;") && camera.Contains("OnReadbackComplete(req, generation, renderUnixNs"),
                "134-12C-1: primary camera captures timestamp at render and passes it into readback callback");
            Check(camera.Contains("PublishJpegFrame(req, renderUnixNs") && camera.Contains("SubmitVideoFrame(req, renderUnixNs)"),
                "134-12C-2: primary camera uses render timestamp for JPEG and video frame submission");
            Check(camera.Contains("ITimestampedCameraVideoEncoderSidecar timestampedSidecar") && camera.Contains("TrySubmitFrame(frameBytes, renderUnixNs)"),
                "134-12C-3: primary camera submits video frames with render timestamps when sidecar supports it");
            Check(camera.Contains("PublishVideoAccessUnit(accessUnit.Data, accessUnit.TimestampNs, videoFormat)"),
                "134-12C-4: primary camera publishes encoded video with queued frame timestamp");

            var legacy = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCompressedVideoCameraPublisher.cs");
            Check(legacy.Contains("var renderUnixNs = CurrentLogTimeNs;") && legacy.Contains("TrySubmitFrame(frameBytes, renderUnixNs)"),
                "134-12C-5: legacy compressed video publisher carries render timestamp into the sidecar");
            Check(legacy.Contains("accessUnit.TimestampNs == 0UL ? CurrentLogTimeNs : accessUnit.TimestampNs"),
                "134-12C-6: legacy compressed video publisher drains timestamped access units");
        }

        /// <summary>
        /// Ensures timestamped encoder sidecar contract is present and implemented.
        /// </summary>
        private static void TimestampedVideoSidecarContractIsWired()
        {
            var contract = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/ICameraVideoEncoderSidecar.cs");
            Check(contract.Contains("interface ITimestampedCameraVideoEncoderSidecar : ICameraVideoEncoderSidecar"),
                "134-12D-1: video sidecar contract exposes timestamped frame submission");
            Check(contract.Contains("TrySubmitFrame(byte[] frame, ulong timestampNs)") && contract.Contains("TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit accessUnit)"),
                "134-12D-2: timestamped sidecar contract carries timestamps through dequeue");

            var accessUnit = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/EncodedVideoAccessUnit.cs");
            Check(accessUnit.Contains("public readonly struct EncodedVideoAccessUnit") && accessUnit.Contains("public ulong TimestampNs { get; }"),
                "134-12D-3: encoded video access units expose the render timestamp");

            foreach (var file in new[]
                     {
                         "FfmpegH264EncoderSidecar.cs",
                         "FfmpegH265EncoderSidecar.cs",
                         "OpenH264EncoderSidecar.cs",
                         "MediaFoundationH264EncoderSidecar.cs"
                     })
            {
                var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/" + file);
                Check(source.Contains("ITimestampedCameraVideoEncoderSidecar"),
                    $"134-12D-4: {file} implements timestamped sidecar contract");
                Check(source.Contains("new EncodedVideoAccessUnit") && source.Contains("timestampNs"),
                    $"134-12D-5: {file} enqueues encoded access units with timestamps");
            }
        }

        /// <summary>
        /// Ensures camera compressed builders use shared timestamp conversion helpers.
        /// </summary>
        private static void CompressedCameraBuildersUseTimestampHelper()
        {
            foreach (var path in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/CameraCompressedImageBuilder.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/CameraCompressedVideoBuilder.cs"
                     })
            {
                var source = Read(path);
                Check(source.Contains("Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs)"),
                    $"134-12E-1: {Path.GetFileName(path)} uses shared timestamp helper");
                Check(!source.Contains("unixNs / 1_000_000_000UL"),
                    $"134-12E-2: {Path.GetFileName(path)} no longer inlines timestamp conversion");
            }
        }

        /// <summary>
        /// Validates laser scan angle args and shared angle validation checks.
        /// </summary>
        private static void LaserScanRejectsInvalidAngles()
        {
            ThrowsArgument(() => LaserScanMessageBuilder.CreateJson(1UL, "laser", double.NaN, 0.5, new[] { 1.0 }),
                "134-12F-1: JSON LaserScan rejects non-finite start angles");
            ThrowsArgument(() => LaserScanMessageBuilder.CreateProtobuf(1UL, "laser", 1.0, double.PositiveInfinity, new[] { 1.0 }),
                "134-12F-2: protobuf LaserScan rejects non-finite end angles");
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/LaserScanMessageBuilder.cs");
            Check(source.Contains("ValidateAngles(startAngle, endAngle)"),
                "134-12F-3: LaserScan builder validates finite angles in both creation paths");
        }

        /// <summary>
        /// Verifies point cloud pending frame is written/read under a dedicated gate.
        /// </summary>
        private static void PointCloudPendingFrameIsThreadSafe()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            Check(source.Contains("private readonly object _pendingFrameGate = new object();"),
                "134-12G-1: point cloud publisher protects pending frames with an explicit gate");
            Check(source.Contains("lock (_pendingFrameGate)") && source.Contains("_pendingFrame = frame;"),
                "134-12G-2: SetFrame publishes pending frame under the gate");
            Check(source.Contains("pendingFrame = _pendingFrame;") && source.Contains("_pendingFrame = null;"),
                "134-12G-3: Update consumes and clears pending frame after demand gating");
        }

        /// <summary>
        /// Verifies transform publisher sanitization and shared conversion usage.
        /// </summary>
        private static void TransformPublisherSanitizesAndSharesConversion()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs");
            Check(source.Contains("ResolvedParentFrameId") && source.Contains("SanitizeFrameId(_parentFrameId"),
                "134-12H-1: transform publisher sanitizes parent frame id");
            Check(source.Contains("ParentFrameId = ResolvedParentFrameId"),
                "134-12H-2: transform JSON/protobuf paths use sanitized parent frame id");
            Check(source.Contains("private void ResolveTransform(out UVector3 position, out UQuaternion rotation)"),
                "134-12H-3: transform publisher shares coordinate conversion helper");
            Check(source.Contains("Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs)"),
                "134-12H-4: transform protobuf path uses shared timestamp helper");
            Check(!source.Contains("ParentFrameId = _parentFrameId") && !source.Contains("unixNs / 1_000_000_000UL"),
                "134-12H-5: transform publisher no longer uses raw parent frame id or inline timestamp conversion");
        }

        /// <summary>
        /// Verifies scene cube publisher reuses ROS2 payload and timestamp helpers.
        /// </summary>
        private static void SceneCubePublisherReusesRos2Payload()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveSceneCubePublisher.cs");
            Check(!source.Contains("PublishRos2SceneUpdate("),
                "134-12I-1: scene cube publisher removes private unused ROS2 helper");
            Check(source.Contains("ros2Payload != null || TryBuildRos2SceneUpdate(message, out ros2Payload)"),
                "134-12I-2: scene cube publisher reuses the existing ROS2 payload for bridge output");
            Check(source.Contains("Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs)"),
                "134-12I-3: scene cube protobuf path uses shared timestamp helper");
            Check(!source.Contains("unixNs / 1_000_000_000UL"),
                "134-12I-4: scene cube publisher no longer inlines timestamp conversion");
        }

        /// <summary>
        /// Reads a source file as UTF-8 text.
        /// </summary>
        private static string Read(string path) => File.ReadAllText(path);

        /// <summary>
        /// Verifies an action throws <see cref="ArgumentException" />.
        /// </summary>
        private static void ThrowsArgument(Action action, string label)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                Check(true, label);
                return;
            }

            throw new Exception("[FAIL] " + label);
        }

        /// <summary>
        /// Tracks check progress and throws on failures.
        /// </summary>
        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
