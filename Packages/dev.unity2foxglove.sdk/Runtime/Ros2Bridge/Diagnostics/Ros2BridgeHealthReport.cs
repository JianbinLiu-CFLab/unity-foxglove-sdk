// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Structured ROS2 Bridge health report model and JSON serialization.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Progress event for the Inspector while ROS2 Bridge diagnostics run.</summary>
    public sealed class Ros2BridgeHealthProgress
    {
        public Ros2BridgeHealthProgress(string currentCheckId, string message, int completed, int total)
        {
            CurrentCheckId = currentCheckId ?? string.Empty;
            Message = message ?? string.Empty;
            Completed = completed;
            Total = total;
        }

        public string CurrentCheckId { get; }
        public string Message { get; }
        public int Completed { get; }
        public int Total { get; }
    }

    /// <summary>One diagnostic check result in a ROS2 Bridge health report.</summary>
    public sealed class Ros2BridgeHealthCheckResult
    {
        public Ros2BridgeHealthCheckResult(
            string id,
            string title,
            Ros2BridgeHealthStatus status,
            string message,
            string remediation = "",
            string command = "",
            long durationMs = 0)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Status = status;
            Message = message ?? string.Empty;
            Remediation = remediation ?? string.Empty;
            Command = command ?? string.Empty;
            DurationMs = durationMs < 0 ? 0 : durationMs;
        }

        [JsonProperty("id", Order = 1)]
        public string Id { get; }

        [JsonProperty("title", Order = 2)]
        public string Title { get; }

        [JsonProperty("status", Order = 3)]
        public Ros2BridgeHealthStatus Status { get; }

        [JsonProperty("message", Order = 4)]
        public string Message { get; }

        [JsonProperty("remediation", Order = 5)]
        public string Remediation { get; }

        [JsonProperty("command", Order = 6)]
        public string Command { get; }

        [JsonProperty("durationMs", Order = 7)]
        public long DurationMs { get; }
    }

    /// <summary>Environment fields captured with a health report for reproducible setup diagnostics.</summary>
    public sealed class Ros2BridgeHealthEnvironmentSnapshot
    {
        public Ros2BridgeHealthEnvironmentSnapshot(
            string operatingSystem,
            string unityVersion,
            string rosDistro,
            string host,
            int port,
            Ros2BridgeRos2PathSource ros2PathSource,
            string ros2Executable)
        {
            OperatingSystem = operatingSystem ?? string.Empty;
            UnityVersion = unityVersion ?? string.Empty;
            RosDistro = rosDistro ?? string.Empty;
            Host = host ?? string.Empty;
            Port = port;
            Ros2PathSource = ros2PathSource;
            Ros2Executable = ros2Executable ?? string.Empty;
        }

        [JsonProperty("operatingSystem", Order = 1)]
        public string OperatingSystem { get; }

        [JsonProperty("unityVersion", Order = 2)]
        public string UnityVersion { get; }

        [JsonProperty("rosDistro", Order = 3)]
        public string RosDistro { get; }

        [JsonProperty("host", Order = 4)]
        public string Host { get; }

        [JsonProperty("port", Order = 5)]
        public int Port { get; }

        [JsonProperty("ros2PathSource", Order = 6)]
        public Ros2BridgeRos2PathSource Ros2PathSource { get; }

        [JsonProperty("ros2Executable", Order = 7)]
        public string Ros2Executable { get; }
    }

    /// <summary>Serializable ROS2 Bridge diagnostic report for offline and live health checks.</summary>
    public sealed class Ros2BridgeHealthReport
    {
        /// <summary>JSON schema version for the ROS2 Bridge health report payload.</summary>
        public const int CurrentSchemaVersion = 1;

        public Ros2BridgeHealthReport(
            bool liveMode,
            Ros2BridgeHealthEnvironmentSnapshot environment,
            IReadOnlyList<Ros2BridgeHealthCheckResult> checks,
            string sdkVersion = "",
            long timestampUnixMs = 0)
        {
            SchemaVersion = CurrentSchemaVersion;
            SdkVersion = sdkVersion ?? string.Empty;
            TimestampUnixMs = timestampUnixMs;
            LiveMode = liveMode;
            Environment = environment ?? new Ros2BridgeHealthEnvironmentSnapshot(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                Ros2BridgeRos2PathSource.Missing,
                string.Empty);
            Checks = checks?.ToArray() ?? Array.Empty<Ros2BridgeHealthCheckResult>();
            Summary = ClassifySummary(Checks);
        }

        [JsonProperty("schemaVersion", Order = 1)]
        public int SchemaVersion { get; }

        [JsonProperty("sdkVersion", Order = 2)]
        public string SdkVersion { get; }

        [JsonProperty("timestampUnixMs", Order = 3)]
        public long TimestampUnixMs { get; }

        [JsonProperty("liveMode", Order = 4)]
        public bool LiveMode { get; }

        [JsonProperty("summary", Order = 5)]
        public Ros2BridgeHealthSummary Summary { get; }

        [JsonProperty("environment", Order = 6)]
        public Ros2BridgeHealthEnvironmentSnapshot Environment { get; }

        [JsonProperty("checks", Order = 7)]
        public IReadOnlyList<Ros2BridgeHealthCheckResult> Checks { get; }

        public string ToJson(bool indented = true)
            => JsonConvert.SerializeObject(this, indented ? Formatting.Indented : Formatting.None);

        public static Ros2BridgeHealthSummary ClassifySummary(IReadOnlyList<Ros2BridgeHealthCheckResult> checks)
        {
            if (checks == null || checks.Count == 0)
                return Ros2BridgeHealthSummary.Failed;

            var failures = checks.Where(c => c.Status == Ros2BridgeHealthStatus.Fail).ToArray();
            if (failures.Length == 0)
            {
                if (HasReadySidecar(checks) && checks.All(IsReadyWithOptionalCliDiagnostics))
                    return Ros2BridgeHealthSummary.Ready;

                return checks.Any(c => c.Status == Ros2BridgeHealthStatus.Warning || IsBlockingSkipped(c))
                    ? Ros2BridgeHealthSummary.NeedsSetup
                    : Ros2BridgeHealthSummary.Ready;
            }

            if (failures.All(c => c.Id.StartsWith("sidecar.", StringComparison.Ordinal)))
                return Ros2BridgeHealthSummary.SidecarNotRunning;

            if (failures.Any(c => c.Id.StartsWith("ros2.", StringComparison.Ordinal)
                                  || c.Id.StartsWith("foxglove_msgs.", StringComparison.Ordinal)
                                  || c.Id.StartsWith("interfaces.", StringComparison.Ordinal)))
            {
                return Ros2BridgeHealthSummary.NeedsSetup;
            }

            return Ros2BridgeHealthSummary.Failed;
        }

        private static bool IsBlockingSkipped(Ros2BridgeHealthCheckResult check)
            => check.Status == Ros2BridgeHealthStatus.Skipped
               && !string.Equals(check.Id, "sidecar.self_report", StringComparison.Ordinal);

        private static bool HasReadySidecar(IReadOnlyList<Ros2BridgeHealthCheckResult> checks)
            => checks.Any(c => string.Equals(c.Id, "sidecar.ping", StringComparison.Ordinal)
                               && c.Status == Ros2BridgeHealthStatus.Pass);

        private static bool IsReadyWithOptionalCliDiagnostics(Ros2BridgeHealthCheckResult check)
        {
            if (check.Status == Ros2BridgeHealthStatus.Pass)
                return true;
            if (string.Equals(check.Id, "sidecar.self_report", StringComparison.Ordinal))
                return true;

            return check.Status != Ros2BridgeHealthStatus.Fail
                   && (check.Id.StartsWith("ros2.", StringComparison.Ordinal)
                       || check.Id.StartsWith("foxglove_msgs.", StringComparison.Ordinal)
                       || check.Id.StartsWith("interfaces.", StringComparison.Ordinal));
        }
    }
}
