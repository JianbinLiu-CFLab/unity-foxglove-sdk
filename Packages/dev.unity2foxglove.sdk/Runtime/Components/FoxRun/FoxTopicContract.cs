// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime contract metadata for FoxRun topic routing.

using System;
using System.Threading;

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
        private readonly FoxTopicContract _logicalContract;
        private FoxTopicContract _protobufWireContract;
        private FoxTopicContract _jsonWireContract;
        private FoxTopicContract _messagePackWireContract;

        public FoxTopicContract(
            string topic,
            string schemaName,
            string encoding,
            string canonicalType,
            string stableFingerprint,
            FoxTopicVisibility visibility,
            FoxTopicWriterPolicy writerPolicy)
            : this(
                topic,
                schemaName,
                encoding,
                canonicalType,
                stableFingerprint,
                visibility,
                writerPolicy,
                logicalContract: null)
        {
        }

        private FoxTopicContract(
            string topic,
            string schemaName,
            string encoding,
            string canonicalType,
            string stableFingerprint,
            FoxTopicVisibility visibility,
            FoxTopicWriterPolicy writerPolicy,
            FoxTopicContract logicalContract)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));

            _logicalContract = logicalContract ?? this;
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

        /// <summary>
        /// Returns one stable contract view for the concrete serialized wire
        /// encoding. Repeated hot-path calls reuse the same immutable instance.
        /// </summary>
        public FoxTopicContract ForWireEncoding(FoxRunEncoding wireEncoding)
        {
            if (!ReferenceEquals(_logicalContract, this))
                return _logicalContract.ForWireEncoding(wireEncoding);

            var protocolEncoding =
                FoxRunEncodingResolver.ToProtocolEncoding(wireEncoding);
            var schemaName = wireEncoding == FoxRunEncoding.MessagePack
                ? string.Empty
                : SchemaName;
            if (string.Equals(Encoding, protocolEncoding, StringComparison.Ordinal)
                && string.Equals(SchemaName, schemaName, StringComparison.Ordinal))
            {
                return this;
            }

            ref var cached = ref WireContractSlot(wireEncoding);
            var existing = Volatile.Read(ref cached);
            if (existing != null)
                return existing;

            var created = new FoxTopicContract(
                Topic,
                schemaName,
                protocolEncoding,
                CanonicalType,
                StableFingerprint,
                Visibility,
                WriterPolicy,
                this);
            return Interlocked.CompareExchange(ref cached, created, null)
                   ?? created;
        }

        private ref FoxTopicContract WireContractSlot(
            FoxRunEncoding wireEncoding)
        {
            switch (wireEncoding)
            {
                case FoxRunEncoding.Protobuf:
                    return ref _protobufWireContract;
                case FoxRunEncoding.JSON:
                    return ref _jsonWireContract;
                case FoxRunEncoding.MessagePack:
                    return ref _messagePackWireContract;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(wireEncoding));
            }
        }
    }
}
