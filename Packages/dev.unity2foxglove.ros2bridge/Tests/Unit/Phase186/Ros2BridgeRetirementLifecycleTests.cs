// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: RED-first ownership tests for bounded ROS2 Bridge worker retirement.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests
{
    public sealed partial class Ros2BridgeRetirementLifecycleTests
    {
        private static readonly FoxRunTransportId ProviderId =
            new FoxRunTransportId("unity2foxglove.ros2bridge.retirement-tests");

    }
}
