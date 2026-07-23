// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTO for generated FoxRun topic contract metadata.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Generated metadata for one FoxRun topic contract.</summary>
    public sealed class FoxRunSchemaContractInfo
    {
        public string DeclaringType { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public string ContractHash { get; }
        public string BindingHash { get; }
        public string PolicyHash { get; }
        public string Mode { get; }
        public string Flow { get; }
        public byte[] ProtobufDescriptorSet { get; }
        public float Hz { get; }
        public float Tolerance { get; }
        public IReadOnlyList<FoxRunSchemaFieldInfo> Fields { get; }

        public FoxRunSchemaContractInfo(
            string declaringType,
            string topic,
            string schemaName,
            string encoding,
            string contractHash,
            string bindingHash,
            string policyHash,
            string mode,
            float hz,
            float tolerance,
            IReadOnlyList<FoxRunSchemaFieldInfo> fields,
            string flow = "Publish",
            byte[] protobufDescriptorSet = null)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            ContractHash = contractHash ?? string.Empty;
            BindingHash = bindingHash ?? string.Empty;
            PolicyHash = policyHash ?? string.Empty;
            Mode = mode ?? string.Empty;
            Flow = string.IsNullOrWhiteSpace(flow) ? "Publish" : flow;
            Hz = NormalizeHz(hz);
            Tolerance = NormalizeNonNegative(tolerance);
            Fields = new List<FoxRunSchemaFieldInfo>(fields ?? Array.Empty<FoxRunSchemaFieldInfo>()).AsReadOnly();
            ProtobufDescriptorSet = protobufDescriptorSet == null ? Array.Empty<byte>() : (byte[])protobufDescriptorSet.Clone();
        }

        private static float NormalizeHz(float value)
            => float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;

        private static float NormalizeNonNegative(float value)
            => float.IsNaN(value) || value < 0f ? 0f : value;
    }
}
