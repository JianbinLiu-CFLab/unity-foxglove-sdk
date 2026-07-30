using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_54Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-54 Tests ---");
            _passed = 0;

            VerifyVirtualLidarSkipsUnusedAcquisitionFrameMath();
            VerifyPhase138HSourceCache();
            VerifyPhase138PMergesSourceHygieneScans();
            VerifyPhase138MCameraSourceCache();
            VerifyPhase138RayDirectionSampling();
            VerifyRegistry();

            Console.WriteLine("Phase 164-54: " + _passed + " checks passed.\n");
        }

        private static void VerifyVirtualLidarSkipsUnusedAcquisitionFrameMath()
        {
            var job = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarBuildPointsJob.cs");
            var execute = SourceMethod(job, "public void Execute(int index)");
            var scheduler = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");
            var schedule = SourceMethod(scheduler, "public void SchedulePendingScan(");
            var lidar = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var publisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");

            Check(job.Contains("[ReadOnly] public bool ComputeAcquisitionFrame;", StringComparison.Ordinal)
                  && execute.Contains("if (ComputeAcquisitionFrame)", StringComparison.Ordinal)
                  && execute.Contains("output.HasAcquisitionFrame = 1;", StringComparison.Ordinal),
                "164-54A-1: LiDAR build job computes acquisition coordinates only on demand");
            Check(schedule.Contains("bool computeAcquisitionFrame", StringComparison.Ordinal)
                  && schedule.Contains("? CoordinateConverterFloat3.RigidWorldToLocal(worldPos, worldRot)", StringComparison.Ordinal)
                  && schedule.Contains(": float4x4.identity", StringComparison.Ordinal)
                  && schedule.Contains("ComputeAcquisitionFrame = computeAcquisitionFrame", StringComparison.Ordinal),
                "164-54A-2: LiDAR scheduler skips acquisition matrix setup when unused");
            Check(lidar.Contains("RequiresNativeAcquisitionFrame()", StringComparison.Ordinal)
                  && lidar.Contains("_pointCloudPublisher.RequiresVirtualLidarAcquisitionFrame", StringComparison.Ordinal)
                  && publisher.Contains("internal bool RequiresVirtualLidarAcquisitionFrame", StringComparison.Ordinal)
                  && publisher.Contains("CanQueueVirtualLidarPackedPointCloudFrame || EnableMotionCompensatedPointCloud2", StringComparison.Ordinal),
                "164-54A-3: LiDAR acquisition-frame demand is gated by PointCloud2 Native or deskew output");
        }

        private static void VerifyPhase138HSourceCache()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138HValidation.cs");
            var validate = SourceMethod(source, "public static void Validate()");
            var readText = SourceMethod(source, "private static string ReadText(");
            var tryReadText = SourceMethod(source, "private static string TryReadText(");

            Check(source.Contains("private static readonly Dictionary<string, string> SourceCache", StringComparison.Ordinal)
                  && validate.Contains("SourceCache.Clear();", StringComparison.Ordinal),
                "164-54B-1: Phase138H clears a source cache per validation run");
            Check(readText.Contains("SourceCache.TryGetValue", StringComparison.Ordinal)
                  && readText.Contains("SourceCache.Add(relativePath, text);", StringComparison.Ordinal)
                  && tryReadText.Contains("SourceCache.TryGetValue", StringComparison.Ordinal),
                "164-54B-2: Phase138H reuses repeated source reads");
        }

        private static void VerifyPhase138PMergesSourceHygieneScans()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138PValidation.cs");
            var sourceHygiene = SourceMethod(source, "private static void SourceHygieneHasNoPhase138OPollution()");
            var scan = SourceMethod(source, "private static SourceHygieneScanResult ScanTrackedSourceHygiene(");
            var readFile = SourceMethod(source, "private static SourceHygieneFile ReadSourceHygieneFile(");

            Check(sourceHygiene.Contains("var result = ScanTrackedSourceHygiene(pollutionRoots, forbidden);", StringComparison.Ordinal)
                  && sourceHygiene.Contains("result.Polluted", StringComparison.Ordinal)
                  && sourceHygiene.Contains("result.Utf8BomFiles", StringComparison.Ordinal)
                  && !source.Contains("private static bool TrackedSourceHasUtf8Bom()", StringComparison.Ordinal),
                "164-54C-1: Phase138P source hygiene uses one combined scan result");
            Check(scan.Contains("var scanRoots = new[] { \"Packages\", \"Scripts\", \"Unity2Foxglove/Assets/Samples\" };", StringComparison.Ordinal)
                  && scan.Contains("IsUnderAnyRoot(normalizedPath, pollutionRoots)", StringComparison.Ordinal)
                  && scan.Contains("ReadSourceHygieneFile(path, shouldCheckPollution)", StringComparison.Ordinal),
                "164-54C-2: Phase138P preserves BOM coverage while limiting pollution checks to intended roots");
            Check(readFile.Contains("FileStream", StringComparison.Ordinal)
                  && readFile.Contains("Span<byte>", StringComparison.Ordinal)
                  && readFile.Contains("stream.Read(header)", StringComparison.Ordinal)
                  && readFile.Contains("stream.Position = 0;", StringComparison.Ordinal)
                  && readFile.Contains("new StreamReader(stream, Encoding.UTF8", StringComparison.Ordinal)
                  && !source.Contains("File.ReadAllBytes(path)", StringComparison.Ordinal),
                "164-54C-3: Phase138P avoids separate full-file reads for BOM and text hygiene");
        }

        private static void VerifyPhase138MCameraSourceCache()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138MValidation.cs");
            var validate = SourceMethod(source, "public static void Validate()");
            var readCamera = SourceMethod(source, "private static string ReadCameraPublisherSources()");

            Check(source.Contains("private static readonly Dictionary<string, string> SourceCache", StringComparison.Ordinal)
                  && validate.Contains("SourceCache.Clear();", StringComparison.Ordinal),
                "164-54D-1: Phase138M clears a source cache per validation run");
            Check(readCamera.Contains("const string cacheKey = \"FoxgloveCameraPublisher*.cs\";", StringComparison.Ordinal)
                  && readCamera.Contains("SourceCache.TryGetValue(cacheKey, out var cached)", StringComparison.Ordinal)
                  && readCamera.Contains("SourceCache[cacheKey] = text;", StringComparison.Ordinal),
                "164-54D-2: Phase138M reuses concatenated camera publisher sources");
        }

        private static void VerifyPhase138RayDirectionSampling()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase138Validation.cs");
            var verifyRayGenerator = SourceMethod(source, "private static void VerifyRayGenerator()");

            Check(verifyRayGenerator.Contains("const int columnSampleStep = 4;", StringComparison.Ordinal)
                  && verifyRayGenerator.Contains("c += columnSampleStep", StringComparison.Ordinal)
                  && verifyRayGenerator.Contains("expectedSampledRays", StringComparison.Ordinal)
                  && verifyRayGenerator.Contains("sampled ray directions are finite and unit-length", StringComparison.Ordinal),
                "164-54E-1: Phase138 ray unit-length validation samples columns while keeping all rings");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-54\"", StringComparison.Ordinal), "164-54F-1: validation registry exposes Phase164-54");
            Check(project.Contains("Phase164_54Validation.cs", StringComparison.Ordinal), "164-54F-2: runtime validation project compiles Phase164-54");
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.SourceMethod(source, signature);

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
