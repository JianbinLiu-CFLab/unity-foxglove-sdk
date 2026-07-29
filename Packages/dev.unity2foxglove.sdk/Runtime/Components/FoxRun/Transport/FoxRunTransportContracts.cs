// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Neutral runtime contracts shared by FoxRun transport providers.

using System;

namespace Unity.FoxgloveSDK.Components
{
    [Flags]
    public enum FoxRunTransportCapabilities
    {
        Publish = 1,
        Subscribe = 2
    }

    public enum FoxRunTransportLifecycleState
    {
        Unavailable = 0,
        Available = 1,
        Starting = 2,
        Active = 3,
        Stopping = 4,
        Faulted = 5
    }

    public enum FoxRunTransportDirection
    {
        Publish = 1,
        Subscribe = 2
    }

    public enum FoxRunTransportRouteResultState
    {
        Accepted = 1,
        Rejected = 2,
        Unavailable = 3,
        Failed = 4
    }

    public enum FoxRunDeliveryReliability
    {
        ProviderDefault = 0,
        Reliable = 1,
        BestEffort = 2,
        SystemDefault = 3
    }

    public enum FoxRunDeliveryDurability
    {
        ProviderDefault = 0,
        Volatile = 1,
        TransientLocal = 2,
        SystemDefault = 3
    }

    public enum FoxRunDeliveryHistory
    {
        ProviderDefault = 0,
        KeepLast = 1,
        KeepAll = 2,
        SystemDefault = 3
    }

    /// <summary>Portable delivery intent; providers reject unsupported axes.</summary>
    public readonly struct FoxRunDeliveryPolicy : IEquatable<FoxRunDeliveryPolicy>
    {
        public FoxRunDeliveryPolicy(
            FoxRunDeliveryReliability reliability,
            FoxRunDeliveryDurability durability,
            FoxRunDeliveryHistory history,
            int depth)
        {
            if (depth < 0)
                throw new ArgumentOutOfRangeException(nameof(depth));
            if (history != FoxRunDeliveryHistory.KeepLast && depth != 0)
                throw new ArgumentException(
                    "Depth is valid only with KeepLast history.",
                    nameof(depth));
            if (history == FoxRunDeliveryHistory.KeepLast && depth == 0)
                throw new ArgumentException(
                    "KeepLast history requires a positive depth.",
                    nameof(depth));

            Reliability = reliability;
            Durability = durability;
            History = history;
            Depth = depth;
        }

        public FoxRunDeliveryReliability Reliability { get; }
        public FoxRunDeliveryDurability Durability { get; }
        public FoxRunDeliveryHistory History { get; }
        public int Depth { get; }

        public static FoxRunDeliveryPolicy ProviderDefault { get; } =
            new FoxRunDeliveryPolicy(
                FoxRunDeliveryReliability.ProviderDefault,
                FoxRunDeliveryDurability.ProviderDefault,
                FoxRunDeliveryHistory.ProviderDefault,
                0);

        public bool Equals(FoxRunDeliveryPolicy other)
            => Reliability == other.Reliability
               && Durability == other.Durability
               && History == other.History
               && Depth == other.Depth;

        public override bool Equals(object obj)
            => obj is FoxRunDeliveryPolicy other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Reliability;
                hash = (hash * 397) ^ (int)Durability;
                hash = (hash * 397) ^ (int)History;
                return (hash * 397) ^ Depth;
            }
        }
    }

    /// <summary>Immutable provider-neutral route for one outbound payload.</summary>
    public readonly struct FoxRunTransportPublishRoute
    {
        public FoxRunTransportPublishRoute(
            string stableMemberId,
            string topic,
            string logicalSchemaName,
            ReadOnlyMemory<byte> payload,
            ulong logTimeNs,
            ulong sequence,
            FoxRunDeliveryPolicy deliveryPolicy,
            string messageEncoding = "",
            string schemaEncoding = "")
        {
            StableMemberId = Require(stableMemberId, nameof(stableMemberId));
            Topic = Require(topic, nameof(topic));
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Payload = payload;
            LogTimeNs = logTimeNs;
            Sequence = sequence;
            DeliveryPolicy = deliveryPolicy;
            MessageEncoding = messageEncoding ?? string.Empty;
            SchemaEncoding = schemaEncoding ?? string.Empty;
        }

        public string StableMemberId { get; }
        public string Topic { get; }
        public string LogicalSchemaName { get; }
        public ReadOnlyMemory<byte> Payload { get; }
        public ulong LogTimeNs { get; }
        public ulong Sequence { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
        public string MessageEncoding { get; }
        public string SchemaEncoding { get; }

        private static string Require(string value, string parameter)
            => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Value cannot be empty.", parameter)
                : value;
    }

    /// <summary>Immutable provider-neutral route for one inbound binding.</summary>
    public readonly struct FoxRunTransportSubscribeRoute
    {
        public FoxRunTransportSubscribeRoute(
            string stableMemberId,
            string topic,
            string logicalSchemaName,
            int maxPayloadBytes,
            FoxRunDeliveryPolicy deliveryPolicy,
            Action<ReadOnlyMemory<byte>, ulong, ulong> onPayload,
            string messageEncoding = "")
        {
            if (string.IsNullOrWhiteSpace(stableMemberId))
                throw new ArgumentException("Stable member ID cannot be empty.", nameof(stableMemberId));
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic cannot be empty.", nameof(topic));
            if (maxPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            StableMemberId = stableMemberId;
            Topic = topic;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            MaxPayloadBytes = maxPayloadBytes;
            DeliveryPolicy = deliveryPolicy;
            OnPayload = onPayload ?? throw new ArgumentNullException(nameof(onPayload));
            MessageEncoding = messageEncoding ?? string.Empty;
        }

        public string StableMemberId { get; }
        public string Topic { get; }
        public string LogicalSchemaName { get; }
        public int MaxPayloadBytes { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
        public Action<ReadOnlyMemory<byte>, ulong, ulong> OnPayload { get; }
        public string MessageEncoding { get; }
    }

    public readonly struct FoxRunTransportPublishResult
    {
        private FoxRunTransportPublishResult(
            FoxRunTransportRouteResultState state,
            string reason)
        {
            State = state;
            Reason = reason ?? string.Empty;
        }

        public FoxRunTransportRouteResultState State { get; }
        public string Reason { get; }

        public static FoxRunTransportPublishResult Accepted()
            => new FoxRunTransportPublishResult(
                FoxRunTransportRouteResultState.Accepted,
                string.Empty);

        public static FoxRunTransportPublishResult Rejected(string reason)
            => CreateFailure(FoxRunTransportRouteResultState.Rejected, reason);

        public static FoxRunTransportPublishResult Unavailable(string reason)
            => CreateFailure(FoxRunTransportRouteResultState.Unavailable, reason);

        public static FoxRunTransportPublishResult Failed(string reason)
            => CreateFailure(FoxRunTransportRouteResultState.Failed, reason);

        private static FoxRunTransportPublishResult CreateFailure(
            FoxRunTransportRouteResultState state,
            string reason)
            => new FoxRunTransportPublishResult(
                state,
                string.IsNullOrWhiteSpace(reason) ? "Unspecified transport failure." : reason);
    }

    public interface IFoxRunTransportSubscriptionLease : IDisposable
    {
        FoxRunTransportId Id { get; }
        ulong Generation { get; }
    }

    public readonly struct FoxRunTransportSubscribeResult
    {
        private FoxRunTransportSubscribeResult(
            FoxRunTransportRouteResultState state,
            IFoxRunTransportSubscriptionLease lease,
            string reason)
        {
            if (state == FoxRunTransportRouteResultState.Accepted && lease == null)
                throw new ArgumentNullException(nameof(lease));
            if (state != FoxRunTransportRouteResultState.Accepted && lease != null)
                throw new ArgumentException("Rejected subscription cannot return a lease.", nameof(lease));

            State = state;
            Lease = lease;
            Reason = reason ?? string.Empty;
        }

        public FoxRunTransportRouteResultState State { get; }
        public IFoxRunTransportSubscriptionLease Lease { get; }
        public string Reason { get; }

        public static FoxRunTransportSubscribeResult Accepted(
            IFoxRunTransportSubscriptionLease lease)
            => new FoxRunTransportSubscribeResult(
                FoxRunTransportRouteResultState.Accepted,
                lease,
                string.Empty);

        public static FoxRunTransportSubscribeResult Rejected(string reason)
            => Failure(FoxRunTransportRouteResultState.Rejected, reason);

        public static FoxRunTransportSubscribeResult Unavailable(string reason)
            => Failure(FoxRunTransportRouteResultState.Unavailable, reason);

        public static FoxRunTransportSubscribeResult Failed(string reason)
            => Failure(FoxRunTransportRouteResultState.Failed, reason);

        private static FoxRunTransportSubscribeResult Failure(
            FoxRunTransportRouteResultState state,
            string reason)
            => new FoxRunTransportSubscribeResult(
                state,
                null,
                string.IsNullOrWhiteSpace(reason) ? "Unspecified transport failure." : reason);
    }

    /// <summary>A frozen provider implementation used only by one Manager session.</summary>
    public interface IFoxRunTransportSession : IDisposable
    {
        FoxRunTransportId Id { get; }
        FoxRunTransportCapabilities Capabilities { get; }
        ulong Generation { get; }
        FoxRunTransportPublishResult Publish(in FoxRunTransportPublishRoute route);
        FoxRunTransportSubscribeResult Subscribe(in FoxRunTransportSubscribeRoute route);
    }

    /// <summary>Manager-local registration endpoint implemented by provider companions.</summary>
    public interface IFoxRunTransportProvider
    {
        FoxRunTransportId Id { get; }
        FoxRunTransportCapabilities Capabilities { get; }
        FoxRunTransportLifecycleState LifecycleState { get; }

        bool TryCaptureSession(
            ulong generation,
            out IFoxRunTransportSession session,
            out string reason);
    }
}
