using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_35Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-35 Tests ---");
            _passed = 0;

            VerifyImuSchemaRegistrationCachesByRegistry();
            VerifyImuNativeHandlerSnapshotStaysOutsideDrainLoop();
            VerifyLidarCrossingsUseFixedArray();
            VerifyPointCloudSmokeKeepsFreshFramesForPendingSlotSafety();
            VerifyManualAcceptanceStatusUpdatesAtPublishCadence();
            VerifySmokeLoaderRestoresOnlyInsertedPaths();
            VerifyRegistry();

            Console.WriteLine("Phase 164-35: " + _passed + " checks passed.\n");
        }

        private static void VerifyImuSchemaRegistrationCachesByRegistry()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var ensure = MethodBody(source, "private void EnsureSchemaRegistered()");
            var onEnable = MethodBody(source, "private void OnEnable()");

            Check(source.Contains("private ISchemaRegistry _schemaRegisteredRegistry;", StringComparison.Ordinal),
                "164-35A-1: VirtualImu stores schema registration cache per live registry");
            Check(onEnable.Contains("_schemaRegisteredRegistry = null;", StringComparison.Ordinal),
                "164-35A-2: VirtualImu clears schema registration cache on enable");
            Check(ensure.Contains("ReferenceEquals(_schemaRegisteredRegistry, schemas)", StringComparison.Ordinal)
                  && ensure.IndexOf("ReferenceEquals(_schemaRegisteredRegistry, schemas)", StringComparison.Ordinal)
                  < ensure.IndexOf("schemas.TryGetSchema", StringComparison.Ordinal),
                "164-35A-3: VirtualImu avoids repeated schema lookup after the same registry is confirmed");
            Check(ensure.Contains("_schemaRegisteredRegistry = schemas;", StringComparison.Ordinal)
                  && ensure.Contains("ProtobufSchemaRegistryLoader.FromBytes", StringComparison.Ordinal),
                "164-35A-4: VirtualImu records cache after existing or newly registered IMU schema");
        }

        private static void VerifyImuNativeHandlerSnapshotStaysOutsideDrainLoop()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var update = MethodBody(source, "private void Update()");
            var handlerIndex = update.IndexOf("var nativeFrameHandler = ImuNativeFrameReady;", StringComparison.Ordinal);
            var loopIndex = update.IndexOf("while (_queue.Count > 0)", StringComparison.Ordinal);

            Check(handlerIndex >= 0 && loopIndex >= 0 && handlerIndex < loopIndex,
                "164-35B-1: VirtualImu snapshots native handler once before draining queued samples");
        }

        private static void VerifyLidarCrossingsUseFixedArray()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidarScanScheduler.cs");

            Check(source.Contains("private int[] _scanCrossings = new int[4];", StringComparison.Ordinal)
                  && source.Contains("EnsureScanCrossingCapacity(_scanCrossingCount + 1);", StringComparison.Ordinal)
                  && source.Contains("private int _scanCrossingCount;", StringComparison.Ordinal),
                "164-35C-1: LiDAR scheduler uses grow-only crossing array storage");
            Check(!source.Contains("List<int> _scanCrossings", StringComparison.Ordinal)
                  && !source.Contains("_scanCrossings.Add(", StringComparison.Ordinal)
                  && !source.Contains("crossed more revolutions than the fixed crossing buffer supports", StringComparison.Ordinal),
                "164-35C-2: LiDAR scheduler does not use List add/clear or fixed crossing-cap throws on the hot path");
        }

        private static void VerifyPointCloudSmokeKeepsFreshFramesForPendingSlotSafety()
        {
            var slot = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudPendingFrameSlot.cs");
            var smoke = Read("Unity2Foxglove/Assets/Scripts/PointCloud/PointCloudSmokeSource.cs");
            var fanout = Read("Unity2Foxglove/Assets/Scripts/PointCloud/Phase88PointCloudFanoutSource.cs");

            Check(slot.Contains("private PointCloudFrame _frame;", StringComparison.Ordinal)
                  && slot.Contains("_frame = frame;", StringComparison.Ordinal),
                "164-35D-1: point-cloud pending slot retains the submitted frame reference");
            Check(smoke.Contains("var frame = new PointCloudFrame", StringComparison.Ordinal)
                  && fanout.Contains("var frame = new PointCloudFrame", StringComparison.Ordinal),
                "164-35D-2: smoke sources keep allocating fresh frames instead of reusing a retained pending object");
        }

        private static void VerifyManualAcceptanceStatusUpdatesAtPublishCadence()
        {
            var source = Read("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase155and156ManualAcceptance.cs");
            var update = MethodBody(source, "private void Update()");

            Check(source.Contains("StatusMessageUpdateIntervalSeconds = 0.5f", StringComparison.Ordinal)
                  && source.Contains("private float nextStatusMessageUpdateTime;", StringComparison.Ordinal),
                "164-35E-1: Phase155/156 manual status has an explicit 2 Hz update cadence");
            Check(update.Contains("if (t >= nextStatusMessageUpdateTime)", StringComparison.Ordinal)
                  && update.Contains("statusMessage = \"phase155 frame \" + Time.frameCount;", StringComparison.Ordinal),
                "164-35E-2: Phase155/156 manual status string allocation is gated");
        }

        private static void VerifySmokeLoaderRestoresOnlyInsertedPaths()
        {
            var source = Read("Scripts/smoke/test_core_smoke_scripts.py");
            var loader = PythonFunctionBody(source, "def load_smoke_module");

            Check(loader.Contains("inserted_paths = []", StringComparison.Ordinal)
                  && loader.Contains("sys.path.remove(inserted_path)", StringComparison.Ordinal),
                "164-35F-1: smoke module loader restores only paths it inserted");
            Check(!loader.Contains("original_path = list(sys.path)", StringComparison.Ordinal)
                  && !loader.Contains("sys.path[:] = original_path", StringComparison.Ordinal),
                "164-35F-2: smoke module loader avoids full sys.path copy/restore in the common path");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-35\"", StringComparison.Ordinal), "164-35G-1: validation registry exposes Phase164-35");
            Check(project.Contains("Phase164_35Validation.cs", StringComparison.Ordinal), "164-35G-2: runtime validation project compiles Phase164-35");
        }

        private static string MethodBody(string source, string signature)
        {
            var signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
            if (signatureStart < 0)
                throw new Exception("[FAIL] missing method signature: " + signature);

            var bodyStart = source.IndexOf('{', signatureStart);
            if (bodyStart < 0)
                throw new Exception("[FAIL] missing method body: " + signature);

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }

            throw new Exception("[FAIL] unterminated method body: " + signature);
        }

        private static string PythonFunctionBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] missing Python function: " + signature);

            var next = source.IndexOf("\ndef ", start + signature.Length, StringComparison.Ordinal);
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
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
