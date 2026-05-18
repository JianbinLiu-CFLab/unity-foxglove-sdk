// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Probe abstraction for U2R2 ROS2 Bridge sidecar health checks.

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Probes the ROS2 Bridge sidecar health endpoint over the U2R2 loopback connection.</summary>
    public interface IRos2BridgeHealthProbe
    {
        Ros2BridgeProbeResult Ping(string host, int port, int timeoutMs);
    }

    /// <summary>Sidecar health ping result returned to the bridge health report.</summary>
    public sealed class Ros2BridgeProbeResult
    {
        public Ros2BridgeProbeResult(
            bool succeeded,
            string message,
            string sidecarName = "",
            string sidecarVersion = "",
            long durationMs = 0)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            SidecarName = sidecarName ?? string.Empty;
            SidecarVersion = sidecarVersion ?? string.Empty;
            DurationMs = durationMs < 0 ? 0 : durationMs;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public string SidecarName { get; }
        public string SidecarVersion { get; }
        public long DurationMs { get; }
    }
}
