// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Narrow neutral extension points for schemas, ordinary payloads, and MCAP.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// One captured generated topic handed to a Provider-owned physical
    /// emitter. The source exposes only deterministic direct accessors.
    /// </summary>
    public readonly struct FoxRunGeneratedTransportPublishRequest
    {
        public FoxRunGeneratedTransportPublishRequest(
            IFoxRunGeneratedTransportSource source,
            int topicIndex,
            string topic,
            ulong logTimeNs)
        {
            Source = source
                     ?? throw new ArgumentNullException(nameof(source));
            if (topicIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(topicIndex));
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException(
                    "Topic cannot be empty.",
                    nameof(topic));

            TopicIndex = topicIndex;
            Topic = topic;
            LogTimeNs = logTimeNs;
        }

        public IFoxRunGeneratedTransportSource Source { get; }
        public int TopicIndex { get; }
        public string Topic { get; }
        public ulong LogTimeNs { get; }
    }

    /// <summary>
    /// Optional session seam implemented only by Providers which consume
    /// generated member/type-shape access. The core never knows their wire
    /// representation.
    /// </summary>
    public interface IFoxRunGeneratedTransportSession
    {
        FoxRunTransportPublishResult PublishGenerated(
            in FoxRunGeneratedTransportPublishRequest request);
    }

    public readonly struct FoxRunGeneratedTransportTargetResult
    {
        internal FoxRunGeneratedTransportTargetResult(
            FoxRunTransportId transportId,
            FoxRunTransportPublishResult result)
        {
            TransportId = transportId;
            State = result.State;
            Reason = result.Reason ?? string.Empty;
        }

        public FoxRunTransportId TransportId { get; }
        public FoxRunTransportRouteResultState State { get; }
        public string Reason { get; }
    }

    public readonly struct FoxRunGeneratedTransportFanoutResult
    {
        private readonly
            IReadOnlyList<FoxRunGeneratedTransportTargetResult>
            _targetResults;

        internal FoxRunGeneratedTransportFanoutResult(
            IReadOnlyList<FoxRunGeneratedTransportTargetResult> targetResults,
            int matched,
            int accepted,
            int rejected,
            int unavailable,
            int failed)
        {
            _targetResults = targetResults;
            Matched = matched;
            Accepted = accepted;
            Rejected = rejected;
            Unavailable = unavailable;
            Failed = failed;
        }

        public IReadOnlyList<FoxRunGeneratedTransportTargetResult>
            TargetResults =>
                _targetResults
                ?? Array.Empty<FoxRunGeneratedTransportTargetResult>();
        public int Matched { get; }
        public int Accepted { get; }
        public int Rejected { get; }
        public int Unavailable { get; }
        public int Failed { get; }
        public bool AnyAccepted => Accepted > 0;
        public bool AllAccepted => Matched > 0 && Accepted == Matched;
    }

    internal static class FoxRunGeneratedTransportFanout
    {
        private const int MaximumFailureReasonLength = 512;

        internal static string FormatFailure(
            in FoxRunGeneratedTransportFanoutResult result)
        {
            var message = "Generated Provider fanout failed: "
                          + result.Rejected
                          + " rejected, "
                          + result.Unavailable
                          + " unavailable, "
                          + result.Failed
                          + " failed.";
            var targetResults = result.TargetResults;
            for (var index = 0; index < targetResults.Count; index++)
            {
                var target = targetResults[index];
                if (target.State == FoxRunTransportRouteResultState.Accepted
                    || string.IsNullOrWhiteSpace(target.Reason))
                {
                    continue;
                }

                var reason = target.Reason.Length
                             <= MaximumFailureReasonLength
                    ? target.Reason
                    : target.Reason.Substring(
                        0,
                        MaximumFailureReasonLength);
                return message
                       + " First failure: "
                       + target.TransportId
                       + ": "
                       + reason;
            }

            return message;
        }

        internal static FoxRunGeneratedTransportFanoutResult Publish(
            IReadOnlyList<IFoxRunTransportSession> sessions,
            IReadOnlyList<string> explicitTransportIds,
            IReadOnlyList<FoxRunTransportId> inheritedTransportIds,
            in FoxRunGeneratedTransportPublishRequest request,
            string suppressedTransportId = "",
            ulong suppressedGeneration = 0)
        {
            var matched = 0;
            var accepted = 0;
            var rejected = 0;
            var unavailable = 0;
            var failed = 0;
            var selectedCount = CountSelected(
                sessions,
                explicitTransportIds,
                inheritedTransportIds,
                suppressedTransportId,
                suppressedGeneration);
            if (selectedCount == 0)
            {
                return new FoxRunGeneratedTransportFanoutResult(
                    Array.Empty<FoxRunGeneratedTransportTargetResult>(),
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            var targetResults =
                new FoxRunGeneratedTransportTargetResult[selectedCount];
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (!(session is IFoxRunGeneratedTransportSession generated)
                    || IsSuppressed(
                        session,
                        suppressedTransportId,
                        suppressedGeneration)
                    || !Selects(
                        session.Id,
                        explicitTransportIds,
                        inheritedTransportIds))
                {
                    continue;
                }

                matched++;
                FoxRunTransportPublishResult result;
                try
                {
                    result = generated.PublishGenerated(in request);
                }
                catch (Exception exception)
                {
                    result = FoxRunTransportPublishResult.Failed(
                        exception.Message);
                }

                switch (result.State)
                {
                    case FoxRunTransportRouteResultState.Accepted:
                        accepted++;
                        break;
                    case FoxRunTransportRouteResultState.Rejected:
                        rejected++;
                        break;
                    case FoxRunTransportRouteResultState.Unavailable:
                        unavailable++;
                        break;
                    case FoxRunTransportRouteResultState.Failed:
                        failed++;
                        break;
                    default:
                        result = FoxRunTransportPublishResult.Failed(
                            "Provider returned an invalid route result state.");
                        failed++;
                        break;
                }
                targetResults[matched - 1] =
                    new FoxRunGeneratedTransportTargetResult(
                        session.Id,
                        result);
            }

            return new FoxRunGeneratedTransportFanoutResult(
                targetResults,
                matched,
                accepted,
                rejected,
                unavailable,
                failed);
        }

        private static int CountSelected(
            IReadOnlyList<IFoxRunTransportSession> sessions,
            IReadOnlyList<string> explicitTransportIds,
            IReadOnlyList<FoxRunTransportId> inheritedTransportIds,
            string suppressedTransportId,
            ulong suppressedGeneration)
        {
            if (sessions == null)
                return 0;

            var count = 0;
            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (session is IFoxRunGeneratedTransportSession
                    && !IsSuppressed(
                        session,
                        suppressedTransportId,
                        suppressedGeneration)
                    && Selects(
                        session.Id,
                        explicitTransportIds,
                        inheritedTransportIds))
                {
                    count++;
                }
            }
            return count;
        }

        private static bool IsSuppressed(
            IFoxRunTransportSession session,
            string transportId,
            ulong generation)
            => session != null
               && generation != 0
               && !string.IsNullOrEmpty(transportId)
               && session.Generation == generation
               && string.Equals(
                   session.Id.Value,
                   transportId,
                   StringComparison.Ordinal);

        private static bool Selects(
            FoxRunTransportId id,
            IReadOnlyList<string> explicitTransportIds,
            IReadOnlyList<FoxRunTransportId> inheritedTransportIds)
        {
            if (explicitTransportIds != null)
            {
                for (var index = 0;
                     index < explicitTransportIds.Count;
                     index++)
                {
                    if (string.Equals(
                            explicitTransportIds[index],
                            id.Value,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }

            if (inheritedTransportIds == null)
                return false;
            for (var index = 0;
                 index < inheritedTransportIds.Count;
                 index++)
            {
                if (inheritedTransportIds[index] == id)
                    return true;
            }
            return false;
        }
    }

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

    internal static class FoxRunOrdinaryTransportFanout
    {
        internal static FoxRunOrdinaryTransportFanoutResult Publish(
            IReadOnlyList<IFoxRunTransportSession> sessions,
            in FoxRunOrdinaryPayloadRequest request)
        {
            var matched = 0;
            var accepted = 0;
            var rejected = 0;
            var unavailable = 0;
            var failed = 0;
            if (sessions == null)
            {
                return new FoxRunOrdinaryTransportFanoutResult(
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            for (var index = 0; index < sessions.Count; index++)
            {
                var session = sessions[index];
                if (!(session is IFoxRunOrdinaryPayloadMapper mapper))
                    continue;
                matched++;
                FoxRunTransportPublishResult result;
                try
                {
                    if (!mapper.TryMap(
                            in request,
                            out var contribution,
                            out var reason))
                    {
                        result =
                            FoxRunTransportPublishResult.Rejected(reason);
                    }
                    else
                    {
                        var route = new FoxRunTransportPublishRoute(
                            request.StablePublisherId,
                            request.Topic,
                            contribution.LogicalSchemaName,
                            contribution.Payload,
                            request.LogTimeNs,
                            request.Sequence,
                            request.DeliveryPolicy,
                            contribution.MessageEncoding,
                            contribution.SchemaEncoding);
                        result = session.Publish(in route);
                    }
                }
                catch (Exception exception)
                {
                    result =
                        FoxRunTransportPublishResult.Failed(
                            exception.Message);
                }

                switch (result.State)
                {
                    case FoxRunTransportRouteResultState.Accepted:
                        accepted++;
                        break;
                    case FoxRunTransportRouteResultState.Rejected:
                        rejected++;
                        break;
                    case FoxRunTransportRouteResultState.Unavailable:
                        unavailable++;
                        break;
                    case FoxRunTransportRouteResultState.Failed:
                        failed++;
                        break;
                }
            }

            return new FoxRunOrdinaryTransportFanoutResult(
                matched,
                accepted,
                rejected,
                unavailable,
                failed);
        }
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
