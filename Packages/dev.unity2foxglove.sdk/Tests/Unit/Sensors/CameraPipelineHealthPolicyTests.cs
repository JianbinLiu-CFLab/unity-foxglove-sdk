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
                CadenceAllowed = true,
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
                CadenceAllowed = true,
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

            input.CadenceAllowed = false;
            Assert.Equal(
                CameraPipelineHealthSkipReason.CadenceBudget,
                CameraPipelineHealthPolicy.Evaluate(input).SkipReason);

            input.CadenceAllowed = true;
            input.PendingReadbacks = 1;
            Assert.Equal(
                CameraPipelineHealthSkipReason.ReadbackQueueFull,
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

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
