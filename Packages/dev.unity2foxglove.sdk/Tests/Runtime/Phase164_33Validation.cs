using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_33Validation
    {
        private const string SampleRoot =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1";
        private const string PackageSampleRoot =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-33 Tests ---");
            _passed = 0;

            VerifyAcceptanceRvizConfigsUseLowerFrameRate();
            VerifyAcceptancePointDisplaysUseSmallQueues();
            VerifyPhase128TfQosIsExplicit();
            VerifyPhase138cDoesNotCarryDisabledTfDisplay();
            VerifyRegistry();

            Console.WriteLine("Phase 164-33: " + _passed + " checks passed.\n");
        }

        private static void VerifyAcceptanceRvizConfigsUseLowerFrameRate()
        {
            foreach (var relative in new[]
            {
                "RViz2 Standard Visualization Acceptance/rviz2_phase128_tf_laserscan.rviz",
                "RViz2 PointCloud2 Acceptance/rviz2_phase129_pointcloud2.rviz",
                "RViz2 MarkerArray Acceptance/rviz2_phase130_markerarray.rviz",
                "ROS2 Standard Message Expansion/rviz2_phase132_standard_messages.rviz",
            })
            {
                Check(Read(PackageSampleRoot + "/" + relative).Contains("Frame Rate: 10", StringComparison.Ordinal),
                    "164-33A-package: " + relative + " uses 10 Hz acceptance rendering");
                Check(Read(SampleRoot + "/" + relative).Contains("Frame Rate: 10", StringComparison.Ordinal),
                    "164-33A-imported: " + relative + " uses 10 Hz acceptance rendering");
            }

            Check(Read(SampleRoot + "/RViz2 Standard Visualization v1/rviz2_phase131_standard_visualization.rviz")
                    .Contains("Frame Rate: 30", StringComparison.Ordinal),
                "164-33A-demo: Phase131 demo config keeps 30 Hz rendering");
            Check(Read(SampleRoot + "/Virtual LiDAR PointCloud2 Digital Twin/rviz2_phase138c_pointcloud2.rviz")
                    .Contains("Frame Rate: 30", StringComparison.Ordinal),
                "164-33A-lidar: Phase138c LiDAR demo config keeps 30 Hz rendering");
        }

        private static void VerifyAcceptancePointDisplaysUseSmallQueues()
        {
            VerifySmallQueues(PackageSampleRoot, "package");
            VerifySmallQueues(SampleRoot, "imported");
        }

        private static void VerifySmallQueues(string root, string label)
        {
            var pointCloud = Read(root + "/RViz2 PointCloud2 Acceptance/rviz2_phase129_pointcloud2.rviz");
            var markerArray = Read(root + "/RViz2 MarkerArray Acceptance/rviz2_phase130_markerarray.rviz");

            Check(DisplayBlock(pointCloud, "Name: PointCloud2 /points").Contains("Depth: 2", StringComparison.Ordinal)
                  && DisplayBlock(pointCloud, "Name: PointCloud2 /points").Contains("Queue Size: 2", StringComparison.Ordinal),
                "164-33B-1: " + label + " Phase129 PointCloud2 acceptance display uses bounded queue/depth 2");
            Check(DisplayBlock(markerArray, "Name: MarkerArray /markers").Contains("Depth: 2", StringComparison.Ordinal)
                  && DisplayBlock(markerArray, "Name: MarkerArray /markers").Contains("Queue Size: 2", StringComparison.Ordinal),
                "164-33B-2: " + label + " Phase130 MarkerArray acceptance display uses bounded queue/depth 2");
        }

        private static void VerifyPhase128TfQosIsExplicit()
        {
            VerifyPhase128TfQosIsExplicit(PackageSampleRoot, "package");
            VerifyPhase128TfQosIsExplicit(SampleRoot, "imported");
        }

        private static void VerifyPhase128TfQosIsExplicit(string root, string label)
        {
            var config = Read(root + "/RViz2 Standard Visualization Acceptance/rviz2_phase128_tf_laserscan.rviz");
            var tf = DisplayBlock(config, "Name: TF");

            Check(tf.Contains("Topic:", StringComparison.Ordinal)
                  && tf.Contains("Depth: 5", StringComparison.Ordinal)
                  && tf.Contains("Durability Policy: Volatile", StringComparison.Ordinal)
                  && tf.Contains("History Policy: Keep Last", StringComparison.Ordinal)
                  && tf.Contains("Reliability Policy: Reliable", StringComparison.Ordinal)
                  && tf.Contains("Value: /tf", StringComparison.Ordinal),
                "164-33C-1: " + label + " Phase128 TF display uses explicit QoS like later RViz configs");
        }

        private static void VerifyPhase138cDoesNotCarryDisabledTfDisplay()
        {
            VerifyPhase138cDoesNotCarryDisabledTfDisplay(PackageSampleRoot, "package");
            VerifyPhase138cDoesNotCarryDisabledTfDisplay(SampleRoot, "imported");
        }

        private static void VerifyPhase138cDoesNotCarryDisabledTfDisplay(string root, string label)
        {
            var config = Read(root + "/Virtual LiDAR PointCloud2 Digital Twin/rviz2_phase138c_pointcloud2.rviz");

            Check(!config.Contains("Class: rviz_default_plugins/TF", StringComparison.Ordinal)
                  && !config.Contains("Enabled: false", StringComparison.Ordinal)
                  && config.Contains("Name: PointCloud2 /points", StringComparison.Ordinal)
                  && config.Contains("Fixed Frame: os_lidar", StringComparison.Ordinal),
                "164-33D-1: " + label + " Phase138c RViz config removes disabled TF display while preserving LiDAR view");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-33\"", StringComparison.Ordinal), "164-33E-1: validation registry exposes Phase164-33");
            Check(project.Contains("Phase164_33Validation.cs", StringComparison.Ordinal), "164-33E-2: runtime validation project compiles Phase164-33");
        }

        private static string DisplayBlock(string config, string nameMarker)
        {
            var marker = "      " + nameMarker;
            var name = config.IndexOf(marker, StringComparison.Ordinal);
            if (name < 0)
                return string.Empty;

            var start = config.LastIndexOf("\n    - ", name, StringComparison.Ordinal);
            if (start < 0)
                start = 0;
            var end = config.IndexOf("\n    - ", name + marker.Length, StringComparison.Ordinal);
            if (end < 0)
                end = config.IndexOf("\n  Enabled:", name + marker.Length, StringComparison.Ordinal);
            return end < 0 ? config.Substring(start) : config.Substring(start, end - start);
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
