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
    /// Validation type for Phase98LiveEvidence.
    /// </summary>
    public sealed class Phase98LiveEvidence
    {
        [JsonProperty("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonProperty("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; }

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; }

        [JsonProperty("healthReportPath")]
        public string HealthReportPath { get; set; }

        [JsonProperty("healthSummary")]
        public string HealthSummary { get; set; }

        [JsonProperty("productTopics")]
        public Phase98TopicEvidence[] ProductTopics { get; set; }

        [JsonProperty("optionalDracoTopic")]
        public Phase98TopicEvidence OptionalDracoTopic { get; set; }

        [JsonProperty("allSchema")]
        public Phase98AllSchemaEvidence AllSchema { get; set; }

        [JsonProperty("ros2GraphObservation")]
        public string Ros2GraphObservation { get; set; }

        [JsonProperty("ros2Commands")]
        public string[] Ros2Commands { get; set; }
    }
}
