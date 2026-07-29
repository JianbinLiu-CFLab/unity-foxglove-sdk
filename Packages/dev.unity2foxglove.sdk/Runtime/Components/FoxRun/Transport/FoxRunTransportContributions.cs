// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Narrow neutral extension points for schemas, ordinary payloads, and MCAP.

using System;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxRunTransportSchemaRequest
    {
        public FoxRunTransportSchemaRequest(
            string logicalSchemaName,
            Type valueType)
        {
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        }

        public string LogicalSchemaName { get; }
        public Type ValueType { get; }
    }

    public readonly struct FoxRunTransportSchemaContribution
    {
        public FoxRunTransportSchemaContribution(
            string stableSchemaId,
            string schemaName,
            string schemaEncoding,
            ReadOnlyMemory<byte> schemaData)
        {
            if (string.IsNullOrWhiteSpace(stableSchemaId))
                throw new ArgumentException("Stable schema ID cannot be empty.", nameof(stableSchemaId));
            StableSchemaId = stableSchemaId;
            SchemaName = schemaName ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
            SchemaData = schemaData;
        }

        public string StableSchemaId { get; }
        public string SchemaName { get; }
        public string SchemaEncoding { get; }
        public ReadOnlyMemory<byte> SchemaData { get; }
    }

    public interface IFoxRunTransportSchemaContributor
    {
        FoxRunTransportId Id { get; }

        bool TryResolveSchema(
            in FoxRunTransportSchemaRequest request,
            out FoxRunTransportSchemaContribution contribution,
            out string reason);
    }

    public readonly struct FoxRunOrdinaryPayloadRequest
    {
        public FoxRunOrdinaryPayloadRequest(
            string stablePublisherId,
            string topic,
            string logicalSchemaName,
            object value,
            ulong logTimeNs,
            ulong sequence,
            FoxRunDeliveryPolicy deliveryPolicy)
        {
            if (string.IsNullOrWhiteSpace(stablePublisherId))
                throw new ArgumentException(
                    "Stable publisher ID cannot be empty.",
                    nameof(stablePublisherId));
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic cannot be empty.", nameof(topic));

            StablePublisherId = stablePublisherId;
            Topic = topic;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Value = value ?? throw new ArgumentNullException(nameof(value));
            LogTimeNs = logTimeNs;
            Sequence = sequence;
            DeliveryPolicy = deliveryPolicy;
        }

        public string StablePublisherId { get; }
        public string Topic { get; }
        public string LogicalSchemaName { get; }
        public object Value { get; }
        public ulong LogTimeNs { get; }
        public ulong Sequence { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
    }

    public readonly struct FoxRunOrdinaryPayloadContribution
    {
        public FoxRunOrdinaryPayloadContribution(
            string logicalSchemaName,
            ReadOnlyMemory<byte> payload,
            string messageEncoding,
            string schemaEncoding)
        {
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Payload = payload;
            MessageEncoding = messageEncoding ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
        }

        public string LogicalSchemaName { get; }
        public ReadOnlyMemory<byte> Payload { get; }
        public string MessageEncoding { get; }
        public string SchemaEncoding { get; }
    }

    public interface IFoxRunOrdinaryPayloadMapper
    {
        string StableMapperId { get; }

        bool TryMap(
            in FoxRunOrdinaryPayloadRequest request,
            out FoxRunOrdinaryPayloadContribution contribution,
            out string reason);
    }

    /// <summary>Allocation-free aggregate for one ordinary-publisher Provider fanout.</summary>
    public readonly struct FoxRunOrdinaryTransportFanoutResult
    {
        internal FoxRunOrdinaryTransportFanoutResult(
            int matched,
            int accepted,
            int rejected,
            int unavailable,
            int failed)
        {
            Matched = matched;
            Accepted = accepted;
            Rejected = rejected;
            Unavailable = unavailable;
            Failed = failed;
        }

        public int Matched { get; }
        public int Accepted { get; }
        public int Rejected { get; }
        public int Unavailable { get; }
        public int Failed { get; }
        public bool AnyAccepted => Accepted > 0;
        public bool AllAccepted => Matched > 0 && Accepted == Matched;
    }

    /// <summary>
    /// Provider-owned MCAP contribution copied into a caller/session-local
    /// decode-options snapshot. No static runtime decoder registry exists.
    /// </summary>
    public interface IFoxRunMcapDecoderContribution
    {
        string StableDecoderId { get; }
        IMcapMessageDecoderFactory CreateFactory();
    }
}
