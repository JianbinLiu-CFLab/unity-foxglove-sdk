// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Pure validation helpers for FoxgloveManager Inspector and runtime
// configuration values.

namespace Unity.FoxgloveSDK.Components
{
    internal static class ManagerConfigValidator
    {
        internal const int MinTcpPort = 1;
        internal const int MaxTcpPort = 65535;

        internal static bool IsValidTcpPort(int port)
            => port >= MinTcpPort && port <= MaxTcpPort;

        internal static int ClampTcpPort(int port)
        {
            if (port < MinTcpPort)
                return MinTcpPort;

            return port > MaxTcpPort ? MaxTcpPort : port;
        }

        internal static int ClampAtLeastOne(int value)
            => value < 1 ? 1 : value;

        internal static bool IsSupportedBindHost(string host)
        {
            try
            {
                Unity.FoxgloveSDK.Transport.TransportHostResolver.ResolveBindAddress(host);
                return true;
            }
            catch (System.FormatException)
            {
                return false;
            }
        }
    }
}
