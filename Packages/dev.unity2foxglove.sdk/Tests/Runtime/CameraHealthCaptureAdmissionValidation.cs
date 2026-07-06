// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 172 validation for camera health-based capture admission.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class CameraHealthCaptureAdmissionValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 172 Tests ---");
            _passCount = 0;

            VerifyPolicyShape();
            VerifyPublisherAdmissionBeforeRender();
            VerifyDiagnosticsAndInspectorSurface();
            VerifyVideoQueueContinuityBoundary();

            Console.WriteLine("Phase 172: " + _passCount + " checks passed.\n");
        }

        private static void VerifyPolicyShape()
        {
            var policy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraPipelineHealthPolicy.cs");

            Check(policy.Contains("internal enum CameraPipelineHealthMode", StringComparison.Ordinal)
                  && policy.Contains("Balanced", StringComparison.Ordinal)
                  && policy.Contains("Conservative", StringComparison.Ordinal)
                  && policy.Contains("Aggressive", StringComparison.Ordinal)
                  && policy.Contains("Off", StringComparison.Ordinal)
                  && policy.Contains("CameraPipelineHealthPolicy", StringComparison.Ordinal)
                  && !policy.Contains("CadenceAllowed", StringComparison.Ordinal)
                  && !policy.Contains("CadenceBudget", StringComparison.Ordinal)
                  && !policy.Contains("TotalDroppedDataFrames", StringComparison.Ordinal)
                  && !policy.Contains("CameraBackpressurePolicy", StringComparison.Ordinal),
                "172-1: pure health policy exists, has distinct modes, and avoids cadence/transport-drop coupling");
        }

        private static void VerifyPublisherAdmissionBeforeRender()
        {
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var diagnostics = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Diagnostics.cs");

            var healthIndex = publisher.IndexOf("AllowCameraCaptureByHealthPolicy(profile)", StringComparison.Ordinal);
            var rateIndex = publisher.IndexOf("AllowCameraCaptureBySourceRate(renderUnixNs)", StringComparison.Ordinal);
            var renderIndex = publisher.IndexOf("_captureResources.CaptureCamera.Render();", StringComparison.Ordinal);
            Check(healthIndex >= 0
                  && rateIndex >= 0
                  && renderIndex >= 0
                  && healthIndex > rateIndex
                  && healthIndex < renderIndex
                  && diagnostics.Contains("CameraPipelineHealthPolicy.Evaluate", StringComparison.Ordinal),
                "172-2: shared camera health admission runs after cadence and before Camera.Render");

            Check(publisher.Contains("private CameraPipelineHealthMode _cameraHealthMode = CameraPipelineHealthMode.Balanced;", StringComparison.Ordinal),
                "172-3: camera health mode defaults to balanced");
        }

        private static void VerifyDiagnosticsAndInspectorSurface()
        {
            var cameraDiagnostics = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraPublishDiagnostics.cs");
            var video = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs");
            var editor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Check(cameraDiagnostics.Contains("RecordHealthSkip", StringComparison.Ordinal)
                  && cameraDiagnostics.Contains("renderPressureSkip=", StringComparison.Ordinal)
                  && cameraDiagnostics.Contains("videoOutputSkip=", StringComparison.Ordinal)
                  && cameraDiagnostics.Contains("skips(", StringComparison.Ordinal),
                "172-4: diagnostics expose health skip reasons without transport-drop pollution");

            Check(editor.Contains("private SerializedProperty _cameraHealthMode;", StringComparison.Ordinal)
                  && editor.Contains("Camera Health Mode", StringComparison.Ordinal),
                "172-5: inspector exposes camera health mode next to camera output controls");

            Check(video.Contains("LogOption.NoStacktrace", StringComparison.Ordinal)
                  && !video.Contains("Debug.Log(message)", StringComparison.Ordinal),
                "172-6: video diagnostics avoid stacktrace logging during subjective performance runs");
        }

        private static void VerifyVideoQueueContinuityBoundary()
        {
            var sidecarFiles = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/FfmpegH265EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderSidecar.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs"
            };

            Check(sidecarFiles.All(path =>
                !ReadRepoText(path).Contains("while (_outputCount >= _maxOutputQueue && _outputAccessUnits.TryDequeue(out _))", StringComparison.Ordinal)),
                "172-7: encoded video output queues do not silently drop old completed access units");

            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");
            var pipeline = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            Check(session.Contains("OutputQueueDepth", StringComparison.Ordinal)
                  && pipeline.Contains("OutputQueueDepth", StringComparison.Ordinal)
                  && pipeline.Contains("MaxOutputQueue", StringComparison.Ordinal),
                "172-8: video output pressure is observable by the capture admission path");
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
