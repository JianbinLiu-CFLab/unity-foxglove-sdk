// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 105 Phase100 cleanup and comment governance validation.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase105Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 105: Phase100 Cleanup Comment Governance ===");
            _passed = 0;

            VerifyScriptHeaders();
            VerifyDiagnosticsCommentTargets();
            VerifyRuntimeProtocolCommentTargets();
            VerifyDefaultsAndConstantCommentTargets();
            VerifyEditorAndSampleCommentTargets();
            VerifyPhase100CleanupScope();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 105: {_passed} checks passed.");
        }

        private static void VerifyScriptHeaders()
        {
            CheckPurposeHeader("Tools/ros2_bridge/unity2foxglove_ros2_bridge/launch/unity2foxglove_bridge.launch.py",
                "105A-1: ROS2 launch file has a Purpose header");
            CheckPurposeHeader("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.ps1",
                "105A-2: PowerShell bridge sample script has a Purpose header");
            CheckPurposeHeader("Tools/ros2_bridge/unity2foxglove_ros2_bridge/scripts/run_bridge_sample.sh",
                "105A-3: bash bridge sample script has a Purpose header");
        }

        private static void VerifyDiagnosticsCommentTargets()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/IRos2BridgeCommandRunner.cs",
                "public interface IRos2BridgeCommandRunner",
                "105B-1: command runner interface documents ROS2 CLI boundary",
                "ROS2", "Process");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/IRos2BridgeCommandRunner.cs",
                "public sealed class Ros2BridgeCommandResult",
                "105B-2: command result DTO documents timeout and launch errors",
                "timeout", "error");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/IRos2BridgeHealthProbe.cs",
                "public interface IRos2BridgeHealthProbe",
                "105B-3: health probe interface documents U2R2 sidecar ping boundary",
                "U2R2", "sidecar");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthReport.cs",
                "public sealed class Ros2BridgeHealthReport",
                "105B-4: health report documents offline/live diagnostics",
                "diagnostic");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthReport.cs",
                "public const int CurrentSchemaVersion = 1;",
                "105B-5: health report schema version documents JSON compatibility",
                "JSON");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeHealthRunner.cs",
                "public sealed class Ros2BridgeHealthRunner",
                "105B-6: health runner documents offline/live sidecar workflow",
                "offline", "live");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthCodec.cs",
                "public const int ProtocolVersion = 1;",
                "105B-7: U2R2 health protocol version documents wire compatibility",
                "U2R2");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs",
                "public sealed class Ros2BridgeU2R2HealthProbe",
                "105B-8: U2R2 health probe documents loopback diagnostics",
                "loopback", "Inspector");
        }

        private static void VerifyRuntimeProtocolCommentTargets()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs",
                "public sealed class Ros2BridgeRuntime",
                "105C-1: bridge runtime summary documents queue/reconnect lifecycle",
                "queue", "reconnect");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeTcpClient.cs",
                "public sealed class Ros2BridgeTcpClient",
                "105C-2: TCP client summary documents loopback-only guardrail",
                "loopback");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs",
                "public const string CdrEncoding = \"cdr\";",
                "105C-3: bridge frame CDR encoding constant documents wire meaning",
                "CDR");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrameWriter.cs",
                "public const int MaxHeaderBytes = 64 * 1024;",
                "105C-4: U2R2 max header size documents protocol units",
                "U2R2", "bytes");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeQosProfile.cs",
                "public enum Ros2BridgeQosPreset",
                "105C-5: QoS presets document ROS2 Bridge Inspector meaning",
                "ROS2", "Inspector");
            CheckGroupCommentBefore(
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp",
                "constexpr uint16_t kVersion = 1;",
                "105C-6: C++ sidecar U2R2 constants have a protocol group comment",
                "U2R2", "frame");
        }

        private static void VerifyDefaultsAndConstantCommentTargets()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryManifest.cs",
                "public static class OpenH264OfficialBinaryManifest",
                "105D-1: OpenH264 manifest documents pinned Cisco binary metadata",
                "Cisco", "binary");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryManifest.cs",
                "public const string DownloadUrl",
                "105D-2: OpenH264 download URL documents official source",
                "Cisco");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputMode.cs",
                "public const string JpegTopic",
                "105D-3: camera default topic constants document product defaults",
                "Default");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudOutputMode.cs",
                "public const string RawTopic",
                "105D-4: point-cloud default topic constants document product defaults",
                "Default");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Publishing/Ros2PublisherSchemaNames.cs",
                "public const string FrameTransform",
                "105D-5: ROS2 publisher schema constants document official foxglove_msgs names",
                "foxglove_msgs");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Utilities/PointCloudQoS.cs",
                "public const int XyzPackedStrideBytes",
                "105D-6: point-cloud byte constants document packed field units",
                "bytes");
            CheckGroupCommentBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapWriter.cs",
                "internal const byte OpcodeHeader = 0x01;",
                "105D-7: MCAP opcode table has source record-table comment",
                "MCAP", "opcode");
            CheckGroupCommentBefore(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/MediaFoundationH264EncoderSidecar.cs",
                "private static class MfGuids",
                "105D-8: Media Foundation GUID block documents Media Foundation and CodecAPI source",
                "Media Foundation", "CodecAPI");
        }

        private static void VerifyEditorAndSampleCommentTargets()
        {
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge/Ros2BridgeHealthDrawer.cs",
                "internal sealed class Ros2BridgeHealthDrawer",
                "105E-1: ROS2 Bridge health drawer summary documents Inspector UI role",
                "Inspector");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge/Ros2BridgeEditorPrefs.cs",
                "internal static class Ros2BridgeEditorPrefs",
                "105E-2: ROS2 Bridge editor prefs summary documents persisted ros2 path",
                "ros2", "path");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs",
                "public sealed class Ros2BridgeSampleController",
                "105E-3: ROS2 sample controller summary documents visible sample behavior",
                "sample");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSamplePointCloud.cs",
                "public sealed class Ros2BridgeSamplePointCloud",
                "105E-4: ROS2 sample point cloud summary documents emitted sample data",
                "point");
            CheckSummaryBefore(
                "Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleLaserScan.cs",
                "public sealed class Ros2BridgeSampleLaserScan",
                "105E-5: ROS2 sample laser scan summary documents emitted sample data",
                "laser");
        }

        private static void VerifyPhase100CleanupScope()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(!program.Contains("--phase101", StringComparison.Ordinal)
                  && !program.Contains("--phase102", StringComparison.Ordinal)
                  && !program.Contains("--phase103", StringComparison.Ordinal)
                  && !program.Contains("--phase104", StringComparison.Ordinal),
                "105F-1: runtime validation stays detached from superseded embedded rclcpp phases");
            Check(!project.Contains("Phase101Validation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase102Validation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase103Validation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase104Validation.cs", StringComparison.Ordinal),
                "105F-2: test project does not compile embedded rclcpp validation files");
            Check(registry.Contains("--phase105", StringComparison.Ordinal),
                "105F-3: Phase105 remains an independent Phase100 cleanup gate");
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("--phase105", StringComparison.Ordinal)
                  && registry.Contains("Phase105Validation.Validate", StringComparison.Ordinal),
                "105G-1: registry dispatches --phase105");
            Check(registry.IndexOf("Phase100Validation.Validate", StringComparison.Ordinal)
                  < registry.IndexOf("Phase105Validation.Validate", StringComparison.Ordinal),
                "105G-2: full runtime validation calls Phase105 after Phase100");
            Check(project.Contains("Phase105Validation.cs"),
                "105G-3: Phase105 validation is included in the runtime test project");
        }

        private static void CheckPurposeHeader(string relativePath, string message)
        {
            var text = ReadRepoText(relativePath);
            var header = string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Take(12));
            Check(header.Contains("Purpose:", StringComparison.Ordinal), message);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private static void CheckSummaryBefore(string relativePath, string declaration, string message, params string[] requiredTerms)
        {
            var text = ReadRepoText(relativePath);
            var window = WindowBefore(text, declaration, 16, message);
            var ok = window.Contains("/// <summary>", StringComparison.Ordinal)
                     && requiredTerms.All(term => window.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(ok, message);
        }

        private static void CheckGroupCommentBefore(string relativePath, string declaration, string message, params string[] requiredTerms)
        {
            var text = ReadRepoText(relativePath);
            var window = WindowBefore(text, declaration, 10, message);
            var ok = (window.Contains("//", StringComparison.Ordinal) || window.Contains("///", StringComparison.Ordinal))
                     && requiredTerms.All(term => window.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(ok, message);
        }

        private static string WindowBefore(string text, string declaration, int lookbackLines, string checkMessage)
        {
            var normalized = text.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');
            var index = Array.FindIndex(lines, line => line.Contains(declaration, StringComparison.Ordinal));
            if (index < 0)
                throw new InvalidOperationException(checkMessage + " (missing declaration: " + declaration + ")");

            var start = Math.Max(0, index - lookbackLines);
            return string.Join("\n", lines.Skip(start).Take(index - start));
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase105 file: " + relativePath, path);
            return File.ReadAllText(path);
        }
    }
}
