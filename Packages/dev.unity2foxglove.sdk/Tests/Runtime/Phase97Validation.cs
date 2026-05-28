// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 97 validation for ROS2 Bridge health diagnostics.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase97Validation
    {
        private static readonly object EnvironmentGate = new object();
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 97: ROS2 Bridge Health Diagnostics ===");
            _passed = 0;

            VerifyEnumsAndSummary();
            VerifyOfflineHealthReport();
            VerifyMockedLiveHealthReport();
            VerifyU2R2HealthCodecBoundary();
            VerifySidecarSourceExpectations();
            VerifyInspectorSourceExpectations();
            VerifyCliWiring();

            Console.WriteLine($"Phase 97: {_passed} checks passed.");
        }

        public static Ros2BridgeHealthReport GenerateHealthReport(
            string outputPath,
            bool liveMode,
            string ros2Path,
            string host,
            int port)
        {
            var options = new Ros2BridgeHealthOptions(
                liveMode: liveMode,
                host: string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host,
                port: port <= 0 ? 8767 : port,
                ros2ExecutablePath: ros2Path ?? string.Empty,
                ros2PathSource: string.IsNullOrWhiteSpace(ros2Path)
                    ? Ros2BridgeRos2PathSource.Path
                    : Ros2BridgeRos2PathSource.CliOption);
            var report = new Ros2BridgeHealthRunner().Run(options);

            var fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");
            File.WriteAllText(fullPath, report.ToJson(), Encoding.UTF8);
            return report;
        }

        private static void VerifyEnumsAndSummary()
        {
            Check((int)Ros2BridgeHealthStatus.Pass == 0, "97A-1: Pass status value is stable");
            Check((int)Ros2BridgeHealthStatus.Warning == 1, "97A-2: Warning status value is stable");
            Check((int)Ros2BridgeHealthStatus.Fail == 2, "97A-3: Fail status value is stable");
            Check((int)Ros2BridgeHealthStatus.Skipped == 3, "97A-4: Skipped status value is stable");
            Check((int)Ros2BridgeHealthSummary.Ready == 0, "97A-5: Ready summary value is stable");
            Check((int)Ros2BridgeHealthSummary.NeedsSetup == 1, "97A-6: NeedsSetup summary value is stable");
            Check((int)Ros2BridgeHealthSummary.SidecarNotRunning == 2, "97A-7: SidecarNotRunning summary value is stable");
            Check((int)Ros2BridgeHealthSummary.Failed == 3, "97A-8: Failed summary value is stable");

            Check(Ros2BridgeHealthReport.ClassifySummary(new[]
            {
                Result("config", Ros2BridgeHealthStatus.Pass),
                Result("ros2.cli", Ros2BridgeHealthStatus.Pass)
            }) == Ros2BridgeHealthSummary.Ready, "97A-9: all pass maps to Ready");
            Check(Ros2BridgeHealthReport.ClassifySummary(new[]
            {
                Result("config", Ros2BridgeHealthStatus.Pass),
                Result("ros2.cli", Ros2BridgeHealthStatus.Fail)
            }) == Ros2BridgeHealthSummary.NeedsSetup, "97A-10: missing ROS2 maps to NeedsSetup");
            Check(Ros2BridgeHealthReport.ClassifySummary(new[]
            {
                Result("config", Ros2BridgeHealthStatus.Pass),
                Result("sidecar.ping", Ros2BridgeHealthStatus.Fail)
            }) == Ros2BridgeHealthSummary.SidecarNotRunning, "97A-11: sidecar-only failure maps to SidecarNotRunning");
            Check(Ros2BridgeHealthReport.ClassifySummary(new[]
            {
                Result("config", Ros2BridgeHealthStatus.Fail)
            }) == Ros2BridgeHealthSummary.Failed, "97A-12: configuration failure maps to Failed");
            Check(Ros2BridgeHealthReport.ClassifySummary(new[]
            {
                Result("config", Ros2BridgeHealthStatus.Pass),
                Result("ros2.cli", Ros2BridgeHealthStatus.Warning),
                Result("ros2.distro", Ros2BridgeHealthStatus.Skipped),
                Result("foxglove_msgs.package", Ros2BridgeHealthStatus.Skipped),
                Result("interfaces.catalog", Ros2BridgeHealthStatus.Skipped),
                Result("sidecar.self_report", Ros2BridgeHealthStatus.Skipped),
                Result("sidecar.ping", Ros2BridgeHealthStatus.Pass)
            }) == Ros2BridgeHealthSummary.Ready, "97A-13: sidecar-ready WSL CLI warning maps to Ready");
        }

        private static void VerifyOfflineHealthReport()
        {
            var commandRunner = new RecordingCommandRunner();
            var probe = new FakeProbe(true);
            var report = new Ros2BridgeHealthRunner(commandRunner, probe, FixedClock).Run(new Ros2BridgeHealthOptions(liveMode: false));

            Check(report.SchemaVersion == Ros2BridgeHealthReport.CurrentSchemaVersion, "97B-1: report schema version is stable");
            Check(!report.LiveMode && report.TimestampUnixMs == 0, "97B-2: offline report is deterministic");
            Check(report.Summary == Ros2BridgeHealthSummary.NeedsSetup, "97B-3: offline skipped live checks map to NeedsSetup");
            Check(report.Checks.Count == 7, "97B-4: report preserves ordered check count");
            Check(report.Checks[0].Id == "config" && report.Checks[0].Status == Ros2BridgeHealthStatus.Pass,
                "97B-5: configuration check runs offline");
            Check(report.Checks.Skip(1).All(c => c.Status == Ros2BridgeHealthStatus.Skipped),
                "97B-6: offline mode skips live-only checks");
            Check(commandRunner.Calls.Count == 0, "97B-7: offline report does not spawn ros2 commands");
            Check(!probe.Called, "97B-8: offline report does not connect to sidecar");

            var json = JObject.Parse(report.ToJson());
            Check(json["schemaVersion"]?.Value<int>() == 1 && json["checks"]?[0]?["id"]?.ToString() == "config",
                "97B-9: JSON output uses stable property names and order");
        }

        private static void VerifyMockedLiveHealthReport()
        {
            lock (EnvironmentGate)
            {
                var oldDistro = Environment.GetEnvironmentVariable("ROS_DISTRO");
                try
                {
                    Environment.SetEnvironmentVariable("ROS_DISTRO", "jazzy");
                    var ready = new Ros2BridgeHealthRunner(
                        new RecordingCommandRunner(successByDefault: true),
                        new FakeProbe(true, "unity2foxglove_ros2_bridge", "0.1.0"),
                        FixedClock).Run(new Ros2BridgeHealthOptions(liveMode: true));

                    Check(ready.Summary == Ros2BridgeHealthSummary.Ready, "97C-1: mocked healthy live report is Ready");
                    Check(ready.Environment.RosDistro == "jazzy", "97C-2: live report records ROS_DISTRO");
                    Check(ready.Checks.Single(c => c.Id == "interfaces.catalog").Message.Contains(FoxgloveRos2MsgSchemaCatalog.SourceFileCount.ToString()),
                        "97C-3: live interface check covers all 41 schemas");

                    var noRos2 = new Ros2BridgeHealthRunner(
                        new RecordingCommandRunner(successByDefault: false),
                        new FakeProbe(true),
                        FixedClock).Run(new Ros2BridgeHealthOptions(liveMode: true));
                    Check(noRos2.Summary == Ros2BridgeHealthSummary.NeedsSetup,
                        "97C-4: missing ros2 maps to NeedsSetup");
                    Check(noRos2.Checks.Single(c => c.Id == "foxglove_msgs.package").Status == Ros2BridgeHealthStatus.Skipped,
                        "97C-5: missing ros2 skips dependent package check");

                    var wslSidecarReady = new Ros2BridgeHealthRunner(
                        new LaunchFailureCommandRunner(),
                        new FakeProbe(true, "unity2foxglove_ros2_bridge", "0.1.0"),
                        FixedClock).Run(new Ros2BridgeHealthOptions(liveMode: true));
                    Check(wslSidecarReady.Summary == Ros2BridgeHealthSummary.Ready,
                        "97C-6: sidecar-ready report tolerates missing Windows ros2 CLI");
                    Check(wslSidecarReady.Checks.Single(c => c.Id == "ros2.cli").Status == Ros2BridgeHealthStatus.Warning,
                        "97C-7: missing Windows ros2 CLI is a warning when command launch fails");

                    var sidecarDown = new Ros2BridgeHealthRunner(
                        new RecordingCommandRunner(successByDefault: true),
                        new FakeProbe(false),
                        FixedClock).Run(new Ros2BridgeHealthOptions(liveMode: true));
                    Check(sidecarDown.Summary == Ros2BridgeHealthSummary.SidecarNotRunning,
                        "97C-8: sidecar-only failure maps to SidecarNotRunning");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ROS_DISTRO", oldDistro);
                }
            }
        }

        private static void VerifyU2R2HealthCodecBoundary()
        {
            var requestId = "phase97-test";
            var ping = Ros2BridgeU2R2HealthCodec.WriteHealthPing(requestId);
            Check(Encoding.UTF8.GetString(ping, 16, (int)ReadUInt32(ping, 8)).Contains("health_ping"),
                "97D-1: health ping writes JSON op");
            Check(ReadUInt32(ping, 12) == 0, "97D-2: health ping permits zero payload");

            var pong = Ros2BridgeU2R2HealthCodec.ParseHealthPong(
                Ros2BridgeU2R2HealthCodec.WriteHealthPongForTests(requestId),
                requestId);
            Check(pong.Status == "ok" && pong.SidecarName == "unity2foxglove_ros2_bridge",
                "97D-3: health pong parser accepts valid ok response");

            var error = Ros2BridgeU2R2HealthCodec.ParseHealthPong(
                Ros2BridgeU2R2HealthCodec.WriteHealthPongForTests(
                    requestId,
                    status: "error",
                    sidecarName: "",
                    sidecarVersion: "",
                    errorCode: "unsupported_protocol"),
                requestId);
            Check(error.Status == "error" && error.ErrorCode == "unsupported_protocol",
                "97D-4: health pong parser accepts stable error response");
            Check(Throws<FormatException>(() => Ros2BridgeU2R2HealthCodec.ParseHealthPong(
                    Ros2BridgeU2R2HealthCodec.WriteHealthPongForTests("wrong"),
                    requestId)),
                "97D-5: health pong parser rejects wrong request id");
            Check(Throws<ArgumentException>(() => new Ros2BridgeFrame(
                    "/tf",
                    "foxglove_msgs/msg/FrameTransform",
                    Ros2BridgeFrame.CdrEncoding,
                    1,
                    1,
                    Array.Empty<byte>())),
                "97D-6: publish frames still reject zero payload");
        }

        private static void VerifySidecarSourceExpectations()
        {
            var sidecar = ReadRepoText("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            Check(sidecar.Contains("health_ping") && sidecar.Contains("health_pong"),
                "97E-1: sidecar recognizes health ping/pong op");
            Check(sidecar.Contains("read_raw_frame") && sidecar.Contains("parse_publish_frame"),
                "97E-2: sidecar separates raw frame decode from publish validation");
            Check(sidecar.Contains("raw.payload.empty()") && sidecar.Contains("invalid payload length"),
                "97E-3: sidecar keeps zero-payload publish rejection");
            Check(sidecar.Contains("write_health_pong_ok") && sidecar.Contains("unsupported_protocol"),
                "97E-4: sidecar returns ok and stable health error responses");
            Check(sidecar.Contains("Do not create") || !SourceMethodContains(sidecar, "handle_health_ping", "create_generic_publisher"),
                "97E-5: health ping does not create ROS2 publishers");
        }

        private static void VerifyInspectorSourceExpectations()
        {
            var managerEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var ros2BridgeEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Ros2Bridge.cs");
            var diagnosticsEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");
            var drawer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge/Ros2BridgeHealthDrawer.cs");
            var prefs = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Ros2Bridge/Ros2BridgeEditorPrefs.cs");

            var bridgeMethod = SourceMethod(ros2BridgeEditor, "void DrawRos2BridgeSection");
            var diagnosticsMethod = SourceMethod(diagnosticsEditor, "void DrawDiagnosticsSection");
            Check(bridgeMethod.Contains("_ros2BridgeHealthDrawer.Draw", StringComparison.Ordinal)
                  && !diagnosticsMethod.Contains("_ros2BridgeHealthDrawer.Draw", StringComparison.Ordinal),
                "97F-1: Manager ROS2 Bridge section owns ROS2 Bridge health drawer");
            Check(drawer.Contains("ROS2 Bridge Health") && drawer.Contains("Check ROS2 Bridge"),
                "97F-2: Inspector exposes one-click health check");
            Check(drawer.Contains("Task.Run") && drawer.Contains("Progress = progress"),
                "97F-3: Inspector health check runs command/probe work in background");
            Check(drawer.Contains("Choose ros2") && prefs.Contains("EditorPrefs"),
                "97F-4: Inspector supports ros2 executable override through EditorPrefs");
        }

        private static void VerifyCliWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var csproj = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("\"--phase97\"", StringComparison.Ordinal)
                  && registry.Contains("Phase97Validation.Validate", StringComparison.Ordinal),
                "97G-1: Program dispatches --phase97");
            Check(program.Contains("--phase97-health") && program.Contains("--json"),
                "97G-2: Program dispatches health JSON output");
            Check(program.Contains("--phase97-live") && program.Contains("UNITY2FOXGLOVE_PHASE97_LIVE"),
                "97G-3: Program supports explicit live mode");
            Check(program.Contains("--ros2") && program.Contains("--host") && program.Contains("--port"),
                "97G-4: Program supports live ros2/host/port overrides");
            Check(csproj.Contains("Phase97Validation.cs"),
                "97G-5: Phase97 validation is included in test project");
        }

        private static Ros2BridgeHealthCheckResult Result(string id, Ros2BridgeHealthStatus status)
            => new Ros2BridgeHealthCheckResult(id, id, status, id);

        private static DateTimeOffset FixedClock()
            => DateTimeOffset.FromUnixTimeMilliseconds(1_700_097_000_000);

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private static bool Throws<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private static bool SourceMethodContains(string source, string methodName, string needle)
            => SourceMethod(source, methodName).Contains(needle);

        private static string SourceMethod(string source, string methodName)
        {
            var start = source.IndexOf(methodName, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var braceStart = source.IndexOf('{', start);
            if (braceStart < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return source.Substring(start);
        }

        private sealed class RecordingCommandRunner : IRos2BridgeCommandRunner
        {
            private readonly bool _successByDefault;

            public RecordingCommandRunner(bool successByDefault = true)
            {
                _successByDefault = successByDefault;
            }

            public List<string> Calls { get; } = new List<string>();

            public Ros2BridgeCommandResult Run(string executable, string arguments, int timeoutMs)
            {
                Calls.Add(executable + " " + arguments);
                if (!_successByDefault)
                    return new Ros2BridgeCommandResult(1, "", "missing ros2", false, "", 1);

                var stdout = arguments.StartsWith("pkg prefix", StringComparison.Ordinal)
                    ? "/opt/ros/jazzy"
                    : arguments.StartsWith("interface show", StringComparison.Ordinal)
                        ? "# " + arguments.Substring("interface show ".Length)
                        : "ros2 help";
                return new Ros2BridgeCommandResult(0, stdout, "", false, "", 1);
            }

            public Ros2BridgeCommandResult Run(
                string executable,
                string arguments,
                int timeoutMs,
                System.Threading.CancellationToken cancellationToken)
                => Run(executable, arguments, timeoutMs);
        }

        private sealed class LaunchFailureCommandRunner : IRos2BridgeCommandRunner
        {
            public Ros2BridgeCommandResult Run(string executable, string arguments, int timeoutMs)
                => new Ros2BridgeCommandResult(-1, "", "", false, "file not found", 1);

            public Ros2BridgeCommandResult Run(
                string executable,
                string arguments,
                int timeoutMs,
                System.Threading.CancellationToken cancellationToken)
                => Run(executable, arguments, timeoutMs);
        }

        private sealed class FakeProbe : IRos2BridgeHealthProbe
        {
            private readonly bool _succeeded;
            private readonly string _name;
            private readonly string _version;

            public FakeProbe(bool succeeded, string name = "unity2foxglove_ros2_bridge", string version = "0.1.0")
            {
                _succeeded = succeeded;
                _name = name;
                _version = version;
            }

            public bool Called { get; private set; }

            public Ros2BridgeProbeResult Ping(string host, int port, int timeoutMs)
            {
                Called = true;
                return _succeeded
                    ? new Ros2BridgeProbeResult(true, "ok", _name, _version, 1)
                    : new Ros2BridgeProbeResult(false, "connection refused", durationMs: 1);
            }

            public Ros2BridgeProbeResult Ping(
                string host,
                int port,
                int timeoutMs,
                System.Threading.CancellationToken cancellationToken)
                => Ping(host, port, timeoutMs);
        }
    }
}
