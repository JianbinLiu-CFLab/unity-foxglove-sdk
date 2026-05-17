// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Ordered ROS2 Bridge health checks for CLI evidence and Inspector UX.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    public sealed class Ros2BridgeHealthRunner
    {
        private const string ConfigId = "config";
        private const string Ros2CliId = "ros2.cli";
        private const string Ros2DistroId = "ros2.distro";
        private const string FoxgloveMsgsId = "foxglove_msgs.package";
        private const string InterfacesId = "interfaces.catalog";
        private const string SidecarSelfId = "sidecar.self_report";
        private const string SidecarPingId = "sidecar.ping";

        private readonly IRos2BridgeCommandRunner _commandRunner;
        private readonly IRos2BridgeHealthProbe _healthProbe;
        private readonly Func<DateTimeOffset> _clock;

        public Ros2BridgeHealthRunner(
            IRos2BridgeCommandRunner commandRunner = null,
            IRos2BridgeHealthProbe healthProbe = null,
            Func<DateTimeOffset> clock = null)
        {
            _commandRunner = commandRunner ?? new ProcessRos2BridgeCommandRunner();
            _healthProbe = healthProbe ?? new Ros2BridgeU2R2HealthProbe();
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public Ros2BridgeHealthReport Run(Ros2BridgeHealthOptions options)
        {
            options ??= new Ros2BridgeHealthOptions();
            var checks = new List<Ros2BridgeHealthCheckResult>();
            var rosDistro = string.Empty;

            checks.Add(CheckConfiguration(options));
            if (checks.Last().Status == Ros2BridgeHealthStatus.Fail)
            {
                return BuildReport(options, checks, rosDistro);
            }

            if (!options.LiveMode)
            {
                checks.Add(Skipped(Ros2CliId, "ROS2 CLI", "Live mode disabled; ros2 was not inspected."));
                checks.Add(Skipped(Ros2DistroId, "ROS2 Distro", "Skipped because live ROS2 checks are disabled."));
                checks.Add(Skipped(FoxgloveMsgsId, "foxglove_msgs", "Skipped because live ROS2 checks are disabled."));
                checks.Add(Skipped(InterfacesId, "ROS2 Interfaces", "Skipped because live ROS2 checks are disabled."));
                checks.Add(Skipped(SidecarSelfId, "Sidecar Self Report", "Optional sidecar executable self-report is not implemented."));
                checks.Add(Skipped(SidecarPingId, "Sidecar", "Skipped because live sidecar checks are disabled."));
                return BuildReport(options, checks, rosDistro);
            }

            var executable = options.EffectiveRos2Executable;
            var cli = RunRos2Command(
                Ros2CliId,
                "ROS2 CLI",
                executable,
                "--help",
                options.CommandTimeoutMs,
                failRemediation: "Configure a Windows ros2 executable only if you want Inspector CLI checks; WSL sidecars can still be valid.",
                warnOnLaunchFailure: true);
            checks.Add(cli);

            var ros2Available = cli.Status == Ros2BridgeHealthStatus.Pass;
            if (!ros2Available)
            {
                checks.Add(Skipped(Ros2DistroId, "ROS2 Distro", "Skipped because the local ros2 CLI was not available."));
                checks.Add(Skipped(FoxgloveMsgsId, "foxglove_msgs", "Skipped because the local ros2 CLI was not available."));
                checks.Add(Skipped(InterfacesId, "ROS2 Interfaces", "Skipped because the local ros2 CLI was not available."));
            }
            else
            {
                rosDistro = Environment.GetEnvironmentVariable("ROS_DISTRO") ?? string.Empty;
                checks.Add(CheckRosDistro(rosDistro));

                var package = RunRos2Command(
                    FoxgloveMsgsId,
                    "foxglove_msgs",
                    executable,
                    "pkg prefix foxglove_msgs",
                    options.CommandTimeoutMs,
                    passMessage: "foxglove_msgs package found.",
                    failRemediation: "Install ros-jazzy-foxglove-msgs or source a workspace that builds foxglove_msgs.",
                    failCommand: "ros2 pkg prefix foxglove_msgs");
                checks.Add(package);

                checks.Add(package.Status == Ros2BridgeHealthStatus.Pass
                    ? CheckInterfaces(options, executable)
                    : Skipped(InterfacesId, "ROS2 Interfaces", "Skipped because foxglove_msgs was not found."));
            }

            checks.Add(Skipped(SidecarSelfId, "Sidecar Self Report", "Optional sidecar executable self-report is not implemented."));
            checks.Add(CheckSidecarPing(options));
            return BuildReport(options, checks, rosDistro);
        }

        private Ros2BridgeHealthCheckResult CheckConfiguration(Ros2BridgeHealthOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Ros2BridgeTcpClient.ValidateLoopbackHost(options.Host);
                if (options.Port <= 0 || options.Port > 65535)
                    throw new ArgumentOutOfRangeException(nameof(options.Port), "ROS2 Bridge port must be in 1..65535.");
                if (FoxgloveRos2MsgSchemaCatalog.SourceFileCount != 41
                    || FoxgloveRos2MsgSchemaCatalog.Entries.Count != 41)
                {
                    throw new InvalidOperationException("Bundled foxglove_msgs schema catalog must contain 41 entries.");
                }

                stopwatch.Stop();
                return new Ros2BridgeHealthCheckResult(
                    ConfigId,
                    "Configuration",
                    Ros2BridgeHealthStatus.Pass,
                    "ROS2 Bridge configuration is valid.",
                    durationMs: stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new Ros2BridgeHealthCheckResult(
                    ConfigId,
                    "Configuration",
                    Ros2BridgeHealthStatus.Fail,
                    ex.Message,
                    "Use a loopback host such as 127.0.0.1 and a port in 1..65535.",
                    durationMs: stopwatch.ElapsedMilliseconds);
            }
        }

        private Ros2BridgeHealthCheckResult CheckRosDistro(string rosDistro)
        {
            if (!string.IsNullOrWhiteSpace(rosDistro))
            {
                return new Ros2BridgeHealthCheckResult(
                    Ros2DistroId,
                    "ROS2 Distro",
                    Ros2BridgeHealthStatus.Pass,
                    "ROS_DISTRO=" + rosDistro.Trim());
            }

            return new Ros2BridgeHealthCheckResult(
                Ros2DistroId,
                "ROS2 Distro",
                Ros2BridgeHealthStatus.Warning,
                "ROS_DISTRO is not set in this process.",
                "Source your ROS2 setup script before running live checks.",
                "source /opt/ros/jazzy/setup.bash");
        }

        private Ros2BridgeHealthCheckResult CheckInterfaces(Ros2BridgeHealthOptions options, string executable)
        {
            var stopwatch = Stopwatch.StartNew();
            var entries = FoxgloveRos2MsgSchemaCatalog.Entries;
            var missing = new List<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                var schemaName = entries[i].SchemaName;
                options.Progress?.Invoke(new Ros2BridgeHealthProgress(
                    InterfacesId,
                    $"Checking interfaces {i + 1}/{entries.Count}",
                    i,
                    entries.Count));
                var result = _commandRunner.Run(executable, "interface show " + schemaName, options.CommandTimeoutMs);
                if (!result.Succeeded)
                    missing.Add(schemaName + " (" + CompactFailure(result) + ")");
            }

            options.Progress?.Invoke(new Ros2BridgeHealthProgress(
                InterfacesId,
                $"Checking interfaces {entries.Count}/{entries.Count}",
                entries.Count,
                entries.Count));
            stopwatch.Stop();

            if (missing.Count == 0)
            {
                return new Ros2BridgeHealthCheckResult(
                    InterfacesId,
                    "ROS2 Interfaces",
                    Ros2BridgeHealthStatus.Pass,
                    $"All {entries.Count} foxglove_msgs interfaces are available.",
                    durationMs: stopwatch.ElapsedMilliseconds);
            }

            return new Ros2BridgeHealthCheckResult(
                InterfacesId,
                "ROS2 Interfaces",
                Ros2BridgeHealthStatus.Fail,
                $"{entries.Count - missing.Count}/{entries.Count} interfaces available. First missing: {missing[0]}",
                "Install foxglove_msgs or source the workspace that provides all bundled interfaces.",
                "ros2 interface show foxglove_msgs/msg/FrameTransform",
                stopwatch.ElapsedMilliseconds);
        }

        private Ros2BridgeHealthCheckResult CheckSidecarPing(Ros2BridgeHealthOptions options)
        {
            var result = _healthProbe.Ping(options.Host, options.Port, options.SidecarTimeoutMs);
            if (result.Succeeded)
            {
                var suffix = string.IsNullOrWhiteSpace(result.SidecarVersion)
                    ? string.Empty
                    : " (" + result.SidecarVersion + ")";
                return new Ros2BridgeHealthCheckResult(
                    SidecarPingId,
                    "Sidecar",
                    Ros2BridgeHealthStatus.Pass,
                    "Connected to " + result.SidecarName + suffix + ".",
                    durationMs: result.DurationMs);
            }

            return new Ros2BridgeHealthCheckResult(
                SidecarPingId,
                "Sidecar",
                Ros2BridgeHealthStatus.Fail,
                string.IsNullOrWhiteSpace(result.Message) ? "Sidecar health ping failed." : result.Message,
                "Start the ROS2 Bridge sidecar on loopback, then run the check again.",
                "ros2 run unity2foxglove_ros2_bridge unity2foxglove_ros2_bridge --host 127.0.0.1 --port 8767 --payload-format cdr-with-encapsulation",
                result.DurationMs);
        }

        private Ros2BridgeHealthCheckResult RunRos2Command(
            string id,
            string title,
            string executable,
            string arguments,
            int timeoutMs,
            string passMessage = "",
            string failRemediation = "Source ROS2 setup or configure the ros2 executable path.",
            string failCommand = "ros2 --help",
            bool warnOnLaunchFailure = false)
        {
            var result = _commandRunner.Run(executable, arguments, timeoutMs);
            if (result.Succeeded)
            {
                return new Ros2BridgeHealthCheckResult(
                    id,
                    title,
                    Ros2BridgeHealthStatus.Pass,
                    string.IsNullOrWhiteSpace(passMessage) ? $"{title} check passed." : passMessage,
                    durationMs: result.DurationMs);
            }

            var status = warnOnLaunchFailure && IsLaunchFailure(result)
                ? Ros2BridgeHealthStatus.Warning
                : Ros2BridgeHealthStatus.Fail;
            return new Ros2BridgeHealthCheckResult(
                id,
                title,
                status,
                CompactFailure(result),
                failRemediation,
                failCommand,
                result.DurationMs);
        }

        private Ros2BridgeHealthReport BuildReport(
            Ros2BridgeHealthOptions options,
            IReadOnlyList<Ros2BridgeHealthCheckResult> checks,
            string rosDistro)
        {
            var environment = new Ros2BridgeHealthEnvironmentSnapshot(
                Environment.OSVersion.ToString(),
                options.UnityVersion,
                rosDistro,
                options.Host,
                options.Port,
                options.EffectiveRos2PathSource,
                options.LiveMode ? options.EffectiveRos2Executable : string.Empty);

            return new Ros2BridgeHealthReport(
                options.LiveMode,
                environment,
                checks,
                options.SdkVersion,
                options.LiveMode ? _clock().ToUnixTimeMilliseconds() : 0);
        }

        private static Ros2BridgeHealthCheckResult Skipped(string id, string title, string message)
            => new Ros2BridgeHealthCheckResult(id, title, Ros2BridgeHealthStatus.Skipped, message);

        private static bool IsLaunchFailure(Ros2BridgeCommandResult result)
            => result != null
               && !result.TimedOut
               && result.ExitCode == -1
               && !string.IsNullOrWhiteSpace(result.Error);

        private static string CompactFailure(Ros2BridgeCommandResult result)
        {
            var message = result.FailureMessage ?? string.Empty;
            message = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (message.Length > 240)
                message = message.Substring(0, 240) + "...";
            return string.IsNullOrWhiteSpace(message) ? "Command failed." : message;
        }
    }
}
