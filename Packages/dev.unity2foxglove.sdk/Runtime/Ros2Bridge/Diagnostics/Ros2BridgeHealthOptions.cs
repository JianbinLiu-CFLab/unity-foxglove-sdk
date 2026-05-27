// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Options snapshot for ROS2 Bridge health checks.

using System;
using System.Threading;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Where the health runner got the ros2 executable path.</summary>
    public enum Ros2BridgeRos2PathSource
    {
        Mock = 0,
        EditorPrefs = 1,
        CliOption = 2,
        Path = 3,
        Missing = 4
    }

    /// <summary>Immutable options passed into a ROS2 Bridge health run.</summary>
    public sealed class Ros2BridgeHealthOptions
    {
        public Ros2BridgeHealthOptions(
            bool liveMode = false,
            string host = "127.0.0.1",
            int port = 8767,
            string ros2ExecutablePath = "",
            Ros2BridgeRos2PathSource ros2PathSource = Ros2BridgeRos2PathSource.Mock,
            int commandTimeoutMs = 3000,
            int sidecarTimeoutMs = 1000,
            string unityVersion = "",
            string sdkVersion = "",
            CancellationToken cancellationToken = default)
        {
            LiveMode = liveMode;
            Host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            Port = port;
            Ros2ExecutablePath = ros2ExecutablePath ?? string.Empty;
            Ros2PathSource = ros2PathSource;
            CommandTimeoutMs = commandTimeoutMs < 1 ? 1 : commandTimeoutMs;
            SidecarTimeoutMs = sidecarTimeoutMs < 1 ? 1 : sidecarTimeoutMs;
            UnityVersion = unityVersion ?? string.Empty;
            SdkVersion = sdkVersion ?? string.Empty;
            CancellationToken = cancellationToken;
        }

        public bool LiveMode { get; }
        public string Host { get; }
        public int Port { get; }
        public string Ros2ExecutablePath { get; }
        public Ros2BridgeRos2PathSource Ros2PathSource { get; }
        public int CommandTimeoutMs { get; }
        public int SidecarTimeoutMs { get; }
        public string UnityVersion { get; }
        public string SdkVersion { get; }
        public CancellationToken CancellationToken { get; }
        public Action<Ros2BridgeHealthProgress> Progress { get; set; }

        public string EffectiveRos2Executable
            => string.IsNullOrWhiteSpace(Ros2ExecutablePath) ? "ros2" : Ros2ExecutablePath;

        public Ros2BridgeRos2PathSource EffectiveRos2PathSource
        {
            get
            {
                if (!LiveMode)
                    return Ros2BridgeRos2PathSource.Mock;
                if (!string.IsNullOrWhiteSpace(Ros2ExecutablePath))
                    return Ros2PathSource == Ros2BridgeRos2PathSource.Mock
                        ? Ros2BridgeRos2PathSource.CliOption
                        : Ros2PathSource;
                return Ros2BridgeRos2PathSource.Path;
            }
        }
    }
}
