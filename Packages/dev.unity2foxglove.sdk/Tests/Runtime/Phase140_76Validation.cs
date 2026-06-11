// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-76 source-shape regression coverage for Unity demo OpenH264 optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_76Validation.
    /// </summary>
    public static class Phase140_76Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-76: Unity Demo Editor and Experimental Scripts Optimization ===");
            _passed = 0;

            VerifyReadbackBuffersAreReused();
            VerifySidecarCopyBoundaryRemains();
            VerifyCaptureCameraCopyIsDirtyGuarded();
            VerifyProbeFrameLayoutIsCached();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-76: {_passed} checks passed.");
        }

        private static void VerifyReadbackBuffersAreReused()
        {
            var source = Read("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            var readback = Slice(source, "private void OnReadbackComplete", "    private bool EnsureSidecarStarted");
            Check(source.Contains("private byte[] _rgbBuffer;", StringComparison.Ordinal)
                  && source.Contains("private byte[] _i420Buffer;", StringComparison.Ordinal)
                  && source.Contains("private void EnsureFrameBuffers(int rgbBytes, int i420Bytes)", StringComparison.Ordinal),
                "140-76A-1: OpenH264 probe owns reusable RGB and I420 buffers");
            Check(readback.Contains("var rgbData = request.GetData<byte>();", StringComparison.Ordinal)
                  && readback.Contains("EnsureFrameBuffers(rgbData.Length, i420Bytes);", StringComparison.Ordinal)
                  && readback.Contains("rgbData.CopyTo(_rgbBuffer);", StringComparison.Ordinal)
                  && readback.Contains("TryConvertRgb24ToI420(_rgbBuffer, width, height, _i420Buffer", StringComparison.Ordinal)
                  && readback.Contains("sidecar.TrySubmitFrame(_i420Buffer)", StringComparison.Ordinal)
                  && !readback.Contains(".ToArray()", StringComparison.Ordinal)
                  && !readback.Contains("new byte[i420Bytes]", StringComparison.Ordinal),
                "140-76A-2: readback path avoids per-frame RGB/I420 managed array allocation");
        }

        private static void VerifySidecarCopyBoundaryRemains()
        {
            var sidecar = Read("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs");
            var submit = Slice(sidecar, "public bool TrySubmitFrame(byte[] i420Frame)", "    public bool TryDequeueAccessUnit");
            Check(submit.Contains("var copy = new byte[i420Frame.Length];", StringComparison.Ordinal)
                  && submit.Contains("Buffer.BlockCopy(i420Frame, 0, copy, 0, i420Frame.Length);", StringComparison.Ordinal)
                  && submit.Contains("_inputFrames.Enqueue(copy);", StringComparison.Ordinal),
                "140-76B-1: sidecar keeps defensive copy ownership boundary");
        }

        private static void VerifyCaptureCameraCopyIsDirtyGuarded()
        {
            var source = Read("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            var ensure = Slice(source, "private void EnsureCaptureResources", "    private void SyncCaptureCameraIfDirty");
            var sync = Slice(source, "private void SyncCaptureCameraIfDirty", "    private void CompletePendingReadback");
            Check(source.Contains("private bool _captureCameraDirty;", StringComparison.Ordinal)
                  && ensure.Contains("SyncCaptureCameraIfDirty(width, height);", StringComparison.Ordinal)
                  && !ensure.Contains("_captureCamera.CopyFrom(_sourceCamera);", StringComparison.Ordinal)
                  && sync.Contains("if (!_captureCameraDirty", StringComparison.Ordinal)
                  && sync.Contains("_captureCamera.CopyFrom(_sourceCamera);", StringComparison.Ordinal),
                "140-76C-1: capture camera CopyFrom is guarded by dirty/source-state checks");
        }

        private static void VerifyProbeFrameLayoutIsCached()
        {
            var source = Read("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            var layout = Slice(source, "private bool TryGetProbeFrameLayout", "    private static int PositiveDimension");
            Check(source.Contains("private bool _cachedProbeLayoutValid;", StringComparison.Ordinal)
                  && source.Contains("private int _cachedProbeLayoutSourceWidth;", StringComparison.Ordinal)
                  && source.Contains("private int _cachedProbeLayoutSourceHeight;", StringComparison.Ordinal)
                  && source.Contains("private int _cachedProbeI420Bytes;", StringComparison.Ordinal),
                "140-76D-1: probe frame layout cache fields exist");
            Check(layout.Contains("_cachedProbeLayoutSourceWidth == _width", StringComparison.Ordinal)
                  && layout.Contains("_cachedProbeLayoutSourceHeight == _height", StringComparison.Ordinal)
                  && layout.Contains("return _cachedProbeLayoutValid;", StringComparison.Ordinal)
                  && layout.Contains("OpenH264ProbeSidecarOptions.TryComputeFrameByteCount", StringComparison.Ordinal),
                "140-76D-2: frame layout recomputes only when serialized dimensions change");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_76Validation.cs", StringComparison.Ordinal),
                "140-76E-1: test project compiles Phase140_76Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-76\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_76Validation.Validate", StringComparison.Ordinal),
                "140-76E-2: validation registry exposes --phase140-76");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
