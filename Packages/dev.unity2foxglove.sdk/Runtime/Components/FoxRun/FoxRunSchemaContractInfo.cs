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
        public string WireSchemaName => SchemaName;
        public string LogicalSchemaName { get; }
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
        public bool PublishAvailable { get; }
        public bool SubscribeAvailable { get; }
        public string PublishUnavailableDiagnosticId { get; }
        public string PublishUnavailableReason { get; }
        public string SubscribeUnavailableDiagnosticId { get; }
        public string SubscribeUnavailableReason { get; }
        public string UnavailableDiagnosticId
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableDiagnosticId,
                SubscribeAvailable,
                SubscribeUnavailableDiagnosticId);
        public string UnavailableReason
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableReason,
                SubscribeAvailable,
                SubscribeUnavailableReason);

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
            byte[] protobufDescriptorSet = null,
            string logicalSchemaName = "",
            bool publishAvailable = true,
            bool subscribeAvailable = true,
            string unavailableDiagnosticId = "",
            string unavailableReason = "",
            string publishUnavailableDiagnosticId = null,
            string publishUnavailableReason = null,
            string subscribeUnavailableDiagnosticId = null,
            string subscribeUnavailableReason = null)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
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
            PublishAvailable = publishAvailable;
            SubscribeAvailable = subscribeAvailable;
            PublishUnavailableDiagnosticId = publishAvailable
                ? string.Empty
                : publishUnavailableDiagnosticId ?? unavailableDiagnosticId ?? string.Empty;
            PublishUnavailableReason = publishAvailable
                ? string.Empty
                : publishUnavailableReason ?? unavailableReason ?? string.Empty;
            SubscribeUnavailableDiagnosticId = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableDiagnosticId ?? unavailableDiagnosticId ?? string.Empty;
            SubscribeUnavailableReason = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableReason ?? unavailableReason ?? string.Empty;
        }

        private static float NormalizeHz(float value)
            => float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;

        private static float NormalizeNonNegative(float value)
            => float.IsNaN(value) || value < 0f ? 0f : value;

        private static string SharedUnavailableValue(
            bool publishAvailable,
            string publishValue,
            bool subscribeAvailable,
            string subscribeValue)
        {
            if (publishAvailable)
                return subscribeAvailable ? string.Empty : subscribeValue;
            if (subscribeAvailable)
                return publishValue;
            if (string.IsNullOrEmpty(publishValue))
                return subscribeValue;
            if (string.IsNullOrEmpty(subscribeValue))
                return publishValue;
            return string.Equals(publishValue, subscribeValue, StringComparison.Ordinal)
                ? publishValue
                : string.Empty;
        }
    }
}
