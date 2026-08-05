// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 172 camera health-based capture admission checks.

using System;
using System.IO;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "172")]
    [Trait("Domain", "Sensors")]
    public sealed class CameraPipelineHealthPolicyTests
    {
        [Fact]
        public void BalancedModeSkipsBusyPipelineBeforeCapture()
        {
            var healthy = new CameraPipelineHealthInput
            {
                Mode = CameraPipelineHealthMode.Balanced,
                PendingReadbacks = 0,
                MaxPendingReadbacks = 2,
                EncodeQueueDepth = 0,
                MaxEncodeQueueDepth = 2,
                CompletedQueueDepth = 0,
                MaxCompletedQueueDepth = 2,
                VideoOutputQueueDepth = 0,
                MaxVideoOutputQueueDepth = 2,
                Width = 640,
                Height = 480,
                MaxPixelsPerFrame = 640 * 480
            };

            Assert.True(CameraPipelineHealthPolicy.Evaluate(healthy).AllowCapture);

            var readbackBusy = healthy;
            readbackBusy.PendingReadbacks = 2;
            Assert.Equal(
                CameraPipelineHealthSkipReason.ReadbackQueueFull,
                CameraPipelineHealthPolicy.Evaluate(readbackBusy).SkipReason);

            var encodeBusy = healthy;
            encodeBusy.EncodeQueueDepth = 1;
            Assert.Equal(
                CameraPipelineHealthSkipReason.EncodeQueueFull,
                CameraPipelineHealthPolicy.Evaluate(encodeBusy).SkipReason);

            var completedBusy = healthy;
            completedBusy.CompletedQueueDepth = 1;
            Assert.Equal(
                CameraPipelineHealthSkipReason.CompletedQueueFull,
                CameraPipelineHealthPolicy.Evaluate(completedBusy).SkipReason);

            var videoBusy = healthy;
            videoBusy.VideoOutputQueueDepth = 2;
            Assert.Equal(
                CameraPipelineHealthSkipReason.VideoOutputQueueFull,
                CameraPipelineHealthPolicy.Evaluate(videoBusy).SkipReason);

            var cooling = healthy;
            cooling.RenderPressureCooldownActive = true;
            Assert.Equal(
                CameraPipelineHealthSkipReason.RenderPressureCooldown,
                CameraPipelineHealthPolicy.Evaluate(cooling).SkipReason);
        }

        [Fact]
        public void OffModeStillHonorsCadenceAndHardBudgetsOnly()
        {
            var input = new CameraPipelineHealthInput
            {
                Mode = CameraPipelineHealthMode.Off,
                PendingReadbacks = 0,
                MaxPendingReadbacks = 1,
                EncodeQueueDepth = 99,
                MaxEncodeQueueDepth = 1,
                CompletedQueueDepth = 99,
                MaxCompletedQueueDepth = 1,
                VideoOutputQueueDepth = 99,
                MaxVideoOutputQueueDepth = 1,
                RenderPressureCooldownActive = true,
                Width = 640,
                Height = 480,
                MaxPixelsPerFrame = 640 * 480
            };

            Assert.True(CameraPipelineHealthPolicy.Evaluate(input).AllowCapture);

            input.PendingReadbacks = 1;
            Assert.Equal(
                CameraPipelineHealthSkipReason.ReadbackQueueFull,
                CameraPipelineHealthPolicy.Evaluate(input).SkipReason);
        }

        [Fact]
        public void HealthPolicyDoesNotOwnSourceCadenceGate()
        {
            var policyText = Text("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraPipelineHealthPolicy.cs");
            var publisherDiagnostics = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Diagnostics.cs");

            Assert.DoesNotContain("CadenceAllowed", policyText, StringComparison.Ordinal);
            Assert.DoesNotContain("CadenceBudget", policyText, StringComparison.Ordinal);
            Assert.Contains("AllowCameraCaptureBySourceRate", publisherDiagnostics, StringComparison.Ordinal);
            Assert.Contains("RecordRateSkip", publisherDiagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public void CameraHealthModesHaveDistinctQueuePressureThresholds()
        {
            var input = new CameraPipelineHealthInput
            {
                Mode = CameraPipelineHealthMode.Conservative,
                PendingReadbacks = 0,
                MaxPendingReadbacks = 2,
                EncodeQueueDepth = 0,
                MaxEncodeQueueDepth = 2,
                CompletedQueueDepth = 0,
                MaxCompletedQueueDepth = 2,
                VideoOutputQueueDepth = 1,
                MaxVideoOutputQueueDepth = 4,
                Width = 640,
                Height = 480
            };

            Assert.Equal(
                CameraPipelineHealthSkipReason.VideoOutputQueueFull,
                CameraPipelineHealthPolicy.Evaluate(input).SkipReason);

            input.Mode = CameraPipelineHealthMode.Balanced;
            Assert.True(CameraPipelineHealthPolicy.Evaluate(input).AllowCapture);

            input.VideoOutputQueueDepth = 2;
            Assert.Equal(
                CameraPipelineHealthSkipReason.VideoOutputQueueFull,
                CameraPipelineHealthPolicy.Evaluate(input).SkipReason);

            input.Mode = CameraPipelineHealthMode.Aggressive;
            Assert.True(CameraPipelineHealthPolicy.Evaluate(input).AllowCapture);

            input.VideoOutputQueueDepth = 4;
            Assert.Equal(
                CameraPipelineHealthSkipReason.VideoOutputQueueFull,
                CameraPipelineHealthPolicy.Evaluate(input).SkipReason);
        }

        [Fact]
        public void BalancedModeAvoidsSingleItemQueuePressureOscillationWhenQueueHasHeadroom()
        {
            var input = new CameraPipelineHealthInput
            {
                Mode = CameraPipelineHealthMode.Balanced,
                PendingReadbacks = 0,
                MaxPendingReadbacks = 2,
                EncodeQueueDepth = 1,
                MaxEncodeQueueDepth = 4,
                CompletedQueueDepth = 0,
                MaxCompletedQueueDepth = 4,
                VideoOutputQueueDepth = 0,
                MaxVideoOutputQueueDepth = 4,
                Width = 640,
                Height = 480
            };

            Assert.True(CameraPipelineHealthPolicy.Evaluate(input).AllowCapture);

            input.EncodeQueueDepth = 2;
            Assert.Equal(
                CameraPipelineHealthSkipReason.EncodeQueueFull,
                CameraPipelineHealthPolicy.Evaluate(input).SkipReason);
        }

        [Fact]
        public void H264OutputQueuesExposePressureInsteadOfDroppingOldAccessUnits()
        {
            var interfaceText = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/ICameraVideoEncoderSidecar.cs");
            Assert.Contains("OutputQueueDepth", interfaceText, StringComparison.Ordinal);
            Assert.Contains("MaxOutputQueue", interfaceText, StringComparison.Ordinal);

            foreach (var relativePath in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs"
                     })
            {
                var sidecar = Text(relativePath).Replace("\r\n", "\n", StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "while (_outputCount >= _maxOutputQueue && _outputAccessUnits.TryDequeue(out _))",
                    sidecar,
                    StringComparison.Ordinal);
                Assert.Contains("OutputQueueDepth", sidecar, StringComparison.Ordinal);
                Assert.Contains("MaxOutputQueue", sidecar, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CameraModeChangeResetsBothExecutableChecks()
        {
            var editor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs")
                .Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Contains(
                "ApplyTopicForModeChange(_topic, oldMode, newMode);\n                ResetFfmpegCheck();\n                ResetOpenH264Check();",
                editor,
                StringComparison.Ordinal);
        }

        [Fact]
        public void FfmpegShutdownUsesOneSharedDeadlinePerCodec()
        {
            foreach (var relativePath in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                         "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs"
                     })
            {
                var sidecar = Text(relativePath);
                Assert.Contains("DateTime.UtcNow.AddMilliseconds(ShutdownTimeoutMs)", sidecar, StringComparison.Ordinal);
                Assert.Contains("process.WaitForExit(RemainingMilliseconds(deadlineUtc))", sidecar, StringComparison.Ordinal);
                Assert.Contains("task.Wait(RemainingMilliseconds(deadlineUtc))", sidecar, StringComparison.Ordinal);
                Assert.DoesNotContain("task.Wait(ShutdownTimeoutMs)", sidecar, StringComparison.Ordinal);
            }
        }

        private static string Text(string relativePath)
        {
            var path = PathOf(relativePath);
            Assert.True(
                File.Exists(path),
                "Required source file for camera health source-shape test was not found: "
                + relativePath
                + " resolved to "
                + path);

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

                Assert.Fail(
                    "Could not locate repository root for camera health source-shape tests from "
                    + AppContext.BaseDirectory
                    + ". Expected README.md, Unity2Foxglove/, and Packages/.");
                return string.Empty;
            }
        }
    }
}
