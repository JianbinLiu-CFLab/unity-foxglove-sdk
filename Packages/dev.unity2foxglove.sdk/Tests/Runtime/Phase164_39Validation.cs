using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_39Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-39 Tests ---");
            _passed = 0;

            VerifyRos2BridgeFrameWriterUsesBatchedFixedHeader();
            VerifyHealthRunnerUsesPackageInterfaceCatalog();
            VerifyEditorPrefsMigrationIsCached();
            VerifySampleScriptsUseOneInterfaceCatalogCommand();
            VerifyTopicProfileAvoidsCommonCaseSlashAllocation();
            VerifyHealthProbeParsesHeaderWithoutReassemblingFrame();
            VerifyRegistry();

            Console.WriteLine("Phase 164-39: " + _passed + " checks passed.\n");
        }

        private static void VerifyRos2BridgeFrameWriterUsesBatchedFixedHeader()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs");
            var write = PhaseValidationSourceHelpers.SourceMethod(source, "private static void Write(Ros2BridgeFrame frame, Stream destination, byte[] headerBytes)");

            Check(source.Contains("private static readonly byte[] FramePrefix", StringComparison.Ordinal)
                  && source.Contains("[ThreadStatic]", StringComparison.Ordinal)
                  && source.Contains("private static byte[] _fixedHeaderBuffer", StringComparison.Ordinal),
                "164-39A-1: ROS2 bridge frame writer keeps a thread-local fixed-header buffer");
            Check(write.Contains("destination.Write(fixedHeader, 0, fixedHeader.Length)", StringComparison.Ordinal)
                  && !write.Contains("destination.WriteByte", StringComparison.Ordinal),
                "164-39A-2: ROS2 bridge frame writer emits the fixed U2R2 header with one stream write");
            Check(source.Contains("WriteUInt32LE(fixedHeader, 8, checked((uint)headerBytes.Length))", StringComparison.Ordinal)
                  && source.Contains("WriteUInt32LE(fixedHeader, 12, checked((uint)frame.PayloadLength))", StringComparison.Ordinal),
                "164-39A-3: ROS2 bridge frame writer patches per-frame lengths into the reusable header");
        }

        private static void VerifyHealthRunnerUsesPackageInterfaceCatalog()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthRunner.cs");
            var checkInterfaces = PhaseValidationSourceHelpers.SourceMethod(source, "private Ros2BridgeHealthCheckResult CheckInterfaces");

            Check(checkInterfaces.Contains("\"interface package foxglove_msgs\"", StringComparison.Ordinal)
                  && !checkInterfaces.Contains("\"interface show \" + schemaName", StringComparison.Ordinal),
                "164-39B-1: ROS2 bridge health runner uses one package interface catalog command");
            Check(source.Contains("private static HashSet<string> BuildInterfaceSet", StringComparison.Ordinal)
                  && checkInterfaces.Contains("availableInterfaces.Contains(schemaName)", StringComparison.Ordinal),
                "164-39B-2: ROS2 bridge health runner checks all bundled schema names from the catalog output");
            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && !checkInterfaces.Contains("checks.Last()", StringComparison.Ordinal)
                  && !checkInterfaces.Contains("result.Succeeded && !availableInterfaces.Contains", StringComparison.Ordinal),
                "173-054-B1: ROS2 bridge health runner avoids LINQ and dead success guards in interface checks");
            Check(checkInterfaces.Contains("Could not list foxglove_msgs interfaces", StringComparison.Ordinal)
                  && checkInterfaces.Contains("ros2 interface package foxglove_msgs", StringComparison.Ordinal),
                "164-39B-3: ROS2 bridge health runner reports catalog command failures directly");
        }

        private static void VerifyEditorPrefsMigrationIsCached()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2Bridge/Ros2BridgeEditorPrefs.cs");
            var migrate = PhaseValidationSourceHelpers.SourceMethod(source, "private static void MigrateLegacyRos2ExecutablePath");

            Check(source.Contains("private static bool _legacyMigrationChecked;", StringComparison.Ordinal)
                  && migrate.Contains("if (_legacyMigrationChecked)", StringComparison.Ordinal)
                  && migrate.Contains("_legacyMigrationChecked = true", StringComparison.Ordinal),
                "164-39C-1: ROS2 bridge EditorPrefs legacy migration runs at most once per editor session");
        }

        private static void VerifySampleScriptsUseOneInterfaceCatalogCommand()
        {
            var bash = Read("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.sh");
            var ps1 = Read("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.ps1");

            Check(bash.Contains("interfaces=\"$(ros2 interface package foxglove_msgs)\"", StringComparison.Ordinal)
                  && bash.Contains("grep -Fxq \"$schema\"", StringComparison.Ordinal)
                  && !bash.Contains("ros2 interface show \"$schema\"", StringComparison.Ordinal),
                "164-39D-1: bash ROS2 bridge sample preflight uses one interface catalog lookup");
            Check(ps1.Contains("\"interface\", \"package\", \"foxglove_msgs\"", StringComparison.Ordinal)
                  && ps1.Contains("$interfaces -ccontains $schema", StringComparison.Ordinal)
                  && !ps1.Contains("\"interface\", \"show\", $schema", StringComparison.Ordinal),
                "164-39D-2: PowerShell ROS2 bridge sample preflight uses one interface catalog lookup");
        }

        private static void VerifyTopicProfileAvoidsCommonCaseSlashAllocation()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeTopicProfile.cs");
            var collapse = PhaseValidationSourceHelpers.SourceMethod(source, "private static string CollapseSlashes");

            Check(source.Contains("private static bool ContainsConsecutiveSlashes", StringComparison.Ordinal)
                  && collapse.Contains("if (!ContainsConsecutiveSlashes(value))", StringComparison.Ordinal)
                  && collapse.Contains("return value;", StringComparison.Ordinal)
                  && collapse.IndexOf("return value;", StringComparison.Ordinal) < collapse.IndexOf("new char[value.Length]", StringComparison.Ordinal),
                "164-39E-1: topic slash collapse returns the common well-formed string before allocating");
        }

        private static void VerifyHealthProbeParsesHeaderWithoutReassemblingFrame()
        {
            var probe = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs");
            var codec = Read("Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthCodec.cs");
            var readHeader = PhaseValidationSourceHelpers.SourceMethod(probe, "private static byte[] ReadU2R2Header");

            Check(probe.Contains("ParseHealthPongHeader(responseHeader, requestId)", StringComparison.Ordinal)
                  && readHeader.Contains("Health pong payload must be empty.", StringComparison.Ordinal)
                  && !readHeader.Contains("Buffer.BlockCopy", StringComparison.Ordinal)
                  && !probe.Contains("ReadU2R2Frame", StringComparison.Ordinal),
                "164-39F-1: health probe parses pong headers without reassembling full frames");
            Check(codec.Contains("internal static Ros2BridgeHealthPong ParseHealthPongHeader(byte[] headerBytes", StringComparison.Ordinal)
                  && codec.Contains("ParseHealthPongHeader(JObject.Parse(headerJson), expectedRequestId)", StringComparison.Ordinal),
                "164-39F-2: health codec exposes an internal header-only pong parser");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-39\"", StringComparison.Ordinal), "164-39G-1: validation registry exposes Phase164-39");
            Check(project.Contains("Phase164_39Validation.cs", StringComparison.Ordinal), "164-39G-2: runtime validation project compiles Phase164-39");
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
