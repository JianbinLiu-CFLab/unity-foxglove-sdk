using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_17Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-17 Tests ---");
            _passed = 0;

            VerifyPointCloudLayoutScanCanEarlyExit();
            VerifyQoSReducerReusesSamplingStateAndLayout();
            VerifyLaserScanJsonUsesCapacityHintedCopies();
            VerifyRegistry();

            Console.WriteLine("Phase 164-17: " + _passed + " checks passed.\n");
        }

        private static void VerifyPointCloudLayoutScanCanEarlyExit()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/PointCloud/PointCloudPackedDataBuilder.cs");
            var from = PhaseValidationSourceHelpers.SourceMethod(source, "public static PointCloudLayout From");

            Check(from.Contains("layout.HasIntensity && layout.HasReflectivity && layout.HasRing && layout.HasTimeOffset", StringComparison.Ordinal)
                  && from.Contains("break;", StringComparison.Ordinal),
                "164-17A-1: point-cloud layout scan exits once all optional fields are present");
        }

        private static void VerifyQoSReducerReusesSamplingStateAndLayout()
        {
            var qos = Read("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/PointCloudQoS.cs");
            var reducer = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudQoSReducer.cs");
            var prepare = PhaseValidationSourceHelpers.SourceMethod(reducer, "internal PointCloudFrame PrepareFrameForQoS");

            Check(qos.Contains("internal static void BuildUniformSampleIndices(int sourceCount, int targetCount, List<int> indices)", StringComparison.Ordinal)
                  && qos.Contains("indices.Clear();", StringComparison.Ordinal),
                "164-17B-1: PointCloudQoS exposes an allocation-free uniform sampling overload");
            Check(reducer.Contains("private readonly List<int> _uniformSampleIndices = new List<int>();", StringComparison.Ordinal)
                  && prepare.Contains("PointCloudQoS.BuildUniformSampleIndices(pointCount, pointBudget, _uniformSampleIndices);", StringComparison.Ordinal)
                  && prepare.Contains("PointCloudQoS.BuildUniformSampleIndices(_voxelSampleIndices.Count, pointBudget, _uniformSampleIndices);", StringComparison.Ordinal),
                "164-17B-2: QoS reducer reuses uniform sample index storage");
            Check(prepare.Contains("copy.Points.Capacity = Math.Min(_voxelSampleIndices.Count, pointBudget);", StringComparison.Ordinal)
                  && prepare.Contains("copy.Points.Capacity = Math.Min(pointCount, pointBudget);", StringComparison.Ordinal)
                  && prepare.Contains("copy.Points.Capacity = count;", StringComparison.Ordinal)
                  && !prepare.Contains("copy.Points.Capacity = Math.Min(pointCount, pointBudget);\r\n\r\n            if (useVoxelGrid)", StringComparison.Ordinal)
                  && !prepare.Contains("copy.Points.Capacity = Math.Min(pointCount, pointBudget);\n\n            if (useVoxelGrid)", StringComparison.Ordinal),
                "164-17B-3: QoS reducer pre-sizes copied point lists after the final sampling path is known");
            Check(prepare.Contains("packedLayout = sourceLayout;", StringComparison.Ordinal)
                  && !prepare.Contains("packedLayout = PointCloudPackedDataBuilder.BuildLayout(copy);", StringComparison.Ordinal),
                "164-17B-4: QoS reducer reuses the source layout for sampled copies");
        }

        private static void VerifyLaserScanJsonUsesCapacityHintedCopies()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/LaserScanMessageBuilder.cs");
            var mutable = PhaseValidationSourceHelpers.SourceMethod(source, "private static List<double> ToMutableList");

            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && !mutable.Contains("values.ToList()", StringComparison.Ordinal),
                "164-17C-1: LaserScan JSON builder avoids LINQ ToList on the publish path");
            Check(mutable.Contains("new List<double>(values.Count)", StringComparison.Ordinal)
                  && mutable.Contains("list.Add(values[i]);", StringComparison.Ordinal),
                "164-17C-2: LaserScan JSON builder keeps copy isolation with a capacity-hinted list");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-17\"", StringComparison.Ordinal), "164-17D-1: validation registry exposes Phase164-17");
            Check(project.Contains("Phase164_17Validation.cs", StringComparison.Ordinal), "164-17D-2: runtime validation project compiles Phase164-17");
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
