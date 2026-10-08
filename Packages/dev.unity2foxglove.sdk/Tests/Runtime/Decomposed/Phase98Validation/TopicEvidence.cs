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
    /// Validation type for Phase98TopicEvidence.
    /// </summary>
    public sealed class Phase98TopicEvidence
    {
        public Phase98TopicEvidence(string topic, string schemaName, string status, int payloadBytes)
        {
            Topic = topic;
            SchemaName = schemaName;
            Status = status;
            PayloadBytes = payloadBytes;
        }

        [JsonProperty("topic")]
        public string Topic { get; }

        [JsonProperty("schemaName")]
        public string SchemaName { get; }

        [JsonProperty("status")]
        public string Status { get; }

        [JsonProperty("payloadBytes")]
        public int PayloadBytes { get; }
    }
}
