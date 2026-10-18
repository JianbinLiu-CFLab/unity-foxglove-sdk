// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 98 validation for ROS2 Bridge sample and launch kit evidence.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{

    /// <summary>
    /// Validation type for Phase98AllSchemaEvidence.
    /// </summary>
    public sealed class Phase98AllSchemaEvidence
    {
        [JsonProperty("sentFrames")]
        public int SentFrames { get; set; }

        [JsonProperty("totalWireBytes")]
        public long TotalWireBytes { get; set; }

        [JsonProperty("firstSchema")]
        public string FirstSchema { get; set; }

        [JsonProperty("lastSchema")]
        public string LastSchema { get; set; }
    }
}
