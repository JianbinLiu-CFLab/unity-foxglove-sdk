using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_15Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-15 Tests ---");
            _passed = 0;

            VerifyCameraCaptureCopiesOnlyWhenDirty();
            VerifyReadbackTimingUsesSmallRingBuffer();
            VerifyVideoReadbackUsesReusableRgbScratch();
            VerifyJpegReadbackKeepsWorkerOwnedBufferBoundary();
            VerifyCameraInfoEditorUsesKnownTypeCache();
            VerifyRegistry();

            Console.WriteLine("Phase 164-15: " + _passed + " checks passed.\n");
        }

        private static void VerifyCameraCaptureCopiesOnlyWhenDirty()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraCaptureResources.cs");
            var ensure = PhaseValidationSourceHelpers.SourceMethod(source, "public void Ensure");
            var sync = PhaseValidationSourceHelpers.SourceMethod(source, "private void SyncCaptureCameraIfDirty");

            Check(source.Contains("private bool _captureCameraDirty = true;", StringComparison.Ordinal)
                  && source.Contains("private Camera _lastCopiedSourceCamera;", StringComparison.Ordinal),
                "164-15A-1: camera capture resources cache copied source-camera state");
            Check(ensure.Contains("SyncCaptureCameraIfDirty(width, height);", StringComparison.Ordinal)
                  && !ensure.Contains("CopyFrom(_sourceCamera)", StringComparison.Ordinal),
                "164-15A-2: capture Ensure delegates camera property sync instead of copying every frame");
            Check(sync.Contains("_captureCamera.CopyFrom(_sourceCamera);", StringComparison.Ordinal)
                  && sync.Contains("Mathf.Approximately(_lastFieldOfView, _sourceCamera.fieldOfView)", StringComparison.Ordinal)
                  && sync.Contains("_lastBackgroundColor == _sourceCamera.backgroundColor", StringComparison.Ordinal)
                  && sync.Contains("_captureCameraDirty = false;", StringComparison.Ordinal),
                "164-15A-3: capture camera CopyFrom is gated by dirty and property-change checks");
            Check(source.Contains("_captureCameraDirty = true;", StringComparison.Ordinal)
                  && source.Contains("_lastCopiedSourceCamera = null;", StringComparison.Ordinal),
                "164-15A-4: capture resource recreation and cleanup invalidate the camera-copy cache");
        }

        private static void VerifyReadbackTimingUsesSmallRingBuffer()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraReadbackTiming.cs");

            Check(source.Contains("private const int MaxTrackedRequests = 8;", StringComparison.Ordinal)
                  && source.Contains("private readonly ulong[] _requestKeys", StringComparison.Ordinal)
                  && source.Contains("private readonly long[] _requestTicks", StringComparison.Ordinal),
                "164-15B-1: camera readback timings use a fixed small ring buffer");
            Check(!source.Contains("Dictionary<ulong, long>", StringComparison.Ordinal)
                  && !source.Contains("lock (", StringComparison.Ordinal),
                "164-15B-2: camera readback timing avoids dictionary and lock overhead");
            Check(source.Contains("Array.Clear(_requestKeys", StringComparison.Ordinal)
                  && source.Contains("_nextSlot = 0;", StringComparison.Ordinal),
                "164-15B-3: camera readback timing clear resets ring state");
        }

        private static void VerifyVideoReadbackUsesReusableRgbScratch()
        {
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var publisherVideo = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Video.cs");
            var submit = PhaseValidationSourceHelpers.SourceMethod(pipeline, "public CameraVideoSubmitResult SubmitVideoFrame");
            var source = PhaseValidationSourceHelpers.SourceMethod(publisherVideo, "public void CopyTo");

            Check(pipeline.Contains("private byte[] _rgbScratch;", StringComparison.Ordinal)
                  && pipeline.Contains("private byte[] EnsureRgbScratch(int length)", StringComparison.Ordinal),
                "164-15C-1: video publish pipeline owns a reusable RGB scratch buffer");
            Check(submit.Contains("var ownedFrameBytes = EnsureRgbScratch(frameBytes.Length);", StringComparison.Ordinal)
                  && submit.Contains("frameBytes.CopyTo(ownedFrameBytes);", StringComparison.Ordinal)
                  && !submit.Contains("frameBytes.ToArray()", StringComparison.Ordinal),
                "164-15C-2: video submit path copies readback bytes into reusable scratch instead of allocating ToArray");
            Check(publisherVideo.Contains("void ICameraVideoFrameBytesSource.CopyTo(byte[] destination)", StringComparison.Ordinal)
                  || source.Contains("GetData<byte>().CopyTo(destination)", StringComparison.Ordinal),
                "164-15C-3: camera video readback source copies directly from AsyncGPUReadback data");
        }

        private static void VerifyJpegReadbackKeepsWorkerOwnedBufferBoundary()
        {
            var jpeg = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");
            var pipeline = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraJpegPublishPipeline.cs");

            Check(jpeg.Contains("frameBytes ??= req.GetData<byte>().ToArray();", StringComparison.Ordinal),
                "164-15D-1: async JPEG path keeps an owned readback byte array for worker lifetime safety");
            var passesFrameBytesToRequest =
                pipeline.Contains("frameBytes,\r\n                Math.Max(1, captureWidth)", StringComparison.Ordinal)
                || pipeline.Contains("frameBytes,\n                Math.Max(1, captureWidth)", StringComparison.Ordinal);
            Check(pipeline.Contains("if (frameBytes == null)", StringComparison.Ordinal)
                  && pipeline.Contains("var request = new JpegEncodeRequest(", StringComparison.Ordinal)
                  && passesFrameBytesToRequest
                  && !pipeline.Contains("frameBytes.ToArray()", StringComparison.Ordinal),
                "164-15D-2: JPEG queue contract still treats the supplied RGB buffer as the owned worker input");
        }

        private static void VerifyCameraInfoEditorUsesKnownTypeCache()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");
            var get = PhaseValidationSourceHelpers.SourceMethod(source, "private static System.Type GetObjectFieldType");

            Check(source.Contains("[\"_manager\"] = typeof(FoxgloveManager)", StringComparison.Ordinal)
                  && source.Contains("[\"_sourceCamera\"] = typeof(Camera)", StringComparison.Ordinal)
                  && source.Contains("[\"_imagePublisher\"] = typeof(FoxgloveCameraPublisher)", StringComparison.Ordinal)
                  && source.Contains("[\"_sensorUnitProfile\"] = typeof(SensorUnitProfile)", StringComparison.Ordinal),
                "164-15E-1: camera info editor seeds known object field types");
            Check(get.Contains("ObjectFieldTypeCache.TryGetValue(property.name, out var knownType)", StringComparison.Ordinal)
                  && !get.Contains("switch (property.name)", StringComparison.Ordinal),
                "164-15E-2: camera info editor avoids repeated switch and type lookups for known fields");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-15\"", StringComparison.Ordinal), "164-15F-1: validation registry exposes Phase164-15");
            Check(project.Contains("Phase164_15Validation.cs", StringComparison.Ordinal), "164-15F-2: runtime validation project compiles Phase164-15");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
