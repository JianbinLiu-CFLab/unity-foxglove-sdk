// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime contract metadata for FoxRun topic routing.

using System;

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxTopicVisibility
    {
        LocalOnly = 0,
        Exported = 1
    }

    public enum FoxTopicWriterPolicy
    {
        SingleWriter = 0,
        MultiWriter = 1
    }

    /// <summary>Stable metadata for one FoxRun-authored topic.</summary>
    public sealed class FoxTopicContract
    {
        public FoxTopicContract(
            string topic,
            string schemaName,
            string encoding,
            string canonicalType,
            string stableFingerprint,
            FoxTopicVisibility visibility,
            FoxTopicWriterPolicy writerPolicy)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));

            Topic = topic;
            SchemaName = schemaName ?? string.Empty;
            Encoding = string.IsNullOrWhiteSpace(encoding) ? "json" : encoding;
            CanonicalType = canonicalType ?? string.Empty;
            StableFingerprint = stableFingerprint ?? string.Empty;
            Visibility = visibility;
            WriterPolicy = writerPolicy;
        }

        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public string CanonicalType { get; }
        public string StableFingerprint { get; }
        public FoxTopicVisibility Visibility { get; }
        public FoxTopicWriterPolicy WriterPolicy { get; }
    }
}
