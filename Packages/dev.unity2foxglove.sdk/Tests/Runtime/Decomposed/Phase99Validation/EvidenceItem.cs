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
    /// Validation type for Phase99EvidenceItem.
    /// </summary>
    public sealed class Phase99EvidenceItem
    {
        public Phase99EvidenceItem(
            string id,
            string area,
            string title,
            Phase99EvidenceStatus status,
            bool isCore,
            string command,
            string artifactPath,
            string notes)
        {
            Id = id ?? string.Empty;
            Area = area ?? string.Empty;
            Title = title ?? string.Empty;
            Status = status;
            IsCore = isCore;
            Command = command ?? string.Empty;
            ArtifactPath = artifactPath ?? string.Empty;
            Notes = notes ?? string.Empty;
        }

        [JsonProperty("id", Order = 1)]
        public string Id { get; }

        [JsonProperty("area", Order = 2)]
        public string Area { get; }

        [JsonProperty("title", Order = 3)]
        public string Title { get; }

        [JsonProperty("status", Order = 4)]
        [JsonConverter(typeof(StringEnumConverter))]
        public Phase99EvidenceStatus Status { get; }

        [JsonProperty("isCore", Order = 5)]
        public bool IsCore { get; }

        [JsonProperty("command", Order = 6)]
        public string Command { get; }

        [JsonProperty("artifactPath", Order = 7)]
        public string ArtifactPath { get; }

        [JsonProperty("notes", Order = 8)]
        public string Notes { get; }
    }
}
