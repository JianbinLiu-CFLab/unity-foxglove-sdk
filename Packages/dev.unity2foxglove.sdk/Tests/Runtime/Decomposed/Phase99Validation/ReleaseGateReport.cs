// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 99 validation for ROS2 release evidence gate reporting.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{

    /// <summary>
    /// Validation type for Phase99ReleaseGateReport.
    /// </summary>
    public sealed class Phase99ReleaseGateReport
    {
        [JsonProperty("schemaVersion", Order = 1)]
        public int SchemaVersion { get; set; }

        [JsonProperty("generatedAtUtc", Order = 2)]
        public string GeneratedAtUtc { get; set; }

        [JsonProperty("commit", Order = 3)]
        public string Commit { get; set; }

        [JsonProperty("packageVersion", Order = 4)]
        public string PackageVersion { get; set; }

        [JsonProperty("unityVersion", Order = 5)]
        public string UnityVersion { get; set; }

        [JsonProperty("foxgloveVersion", Order = 6)]
        public string FoxgloveVersion { get; set; }

        [JsonProperty("rosDistro", Order = 7)]
        public string RosDistro { get; set; }

        [JsonProperty("rmwImplementation", Order = 8)]
        public string RmwImplementation { get; set; }

        [JsonProperty("os", Order = 9)]
        public string Os { get; set; }

        [JsonProperty("host", Order = 10)]
        public string Host { get; set; }

        [JsonProperty("port", Order = 11)]
        public int Port { get; set; }

        [JsonProperty("liveMode", Order = 12)]
        public bool LiveMode { get; set; }

        [JsonProperty("evidence", Order = 13)]
        public List<Phase99EvidenceItem> Evidence { get; set; }

        [JsonProperty("verdict", Order = 14)]
        [JsonConverter(typeof(StringEnumConverter))]
        public Phase99Verdict Verdict { get; set; }

        [JsonProperty("finalNotes", Order = 15)]
        public string FinalNotes { get; set; }
    }
}
