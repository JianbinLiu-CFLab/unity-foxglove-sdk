// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{

    class Phase13FakeClock : IFoxgloveClock
    {
        public ulong NowNs { get; private set; }

        public void AdvanceNs(ulong deltaNs)
        {
            NowNs += deltaNs;
        }
    }
}
