// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Neutral runtime contracts shared by FoxRun transport providers.

using System;
using System.Collections.Generic;

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

    /// <summary>
    /// Provider-neutral observed state. This is deliberately separate from
    /// <see cref="FoxRunTransportLifecycleState"/>, which only gates whether
    /// a new frozen session may be captured.
    /// </summary>
    public enum FoxRunTransportObservedState : byte
    {
        Stopped = 0,
        Starting = 1,
        Ready = 2,
        Degraded = 3,
        Reconnecting = 4,
        Failed = 5
    }

    /// <summary>One stable, bounded Provider runtime diagnostic.</summary>
    public readonly struct FoxRunTransportDiagnostic :
        IEquatable<FoxRunTransportDiagnostic>
    {
        public const int MaximumCodeChars = 64;
        public const int MaximumMessageChars = 512;

        public FoxRunTransportDiagnostic(string code, string message)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException(
                    "Transport diagnostic code cannot be empty.",
                    nameof(code));
            var normalizedCode = code.Trim();
            if (normalizedCode.Length > MaximumCodeChars)
            {
                throw new ArgumentException(
                    "Transport diagnostic code exceeds the stable bound.",
                    nameof(code));
            }

            Code = normalizedCode;
            Message = BoundMessage(message);
        }

        public string Code { get; }
        public string Message { get; }

        public bool Equals(FoxRunTransportDiagnostic other)
            => string.Equals(Code, other.Code, StringComparison.Ordinal)
               && string.Equals(
                   Message,
                   other.Message,
                   StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is FoxRunTransportDiagnostic other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Code == null
                            ? 0
                            : StringComparer.Ordinal.GetHashCode(Code))
                        * 397)
                       ^ (Message == null
                           ? 0
                           : StringComparer.Ordinal.GetHashCode(Message));
            }
        }

        private static string BoundMessage(string message)
        {
            var value = string.IsNullOrWhiteSpace(message)
                ? "Unspecified transport diagnostic."
                : message.Trim();
            return value.Length <= MaximumMessageChars
                ? value
                : value.Substring(0, MaximumMessageChars);
        }
    }

    /// <summary>Observed readiness for one selected Provider direction.</summary>
    public readonly struct FoxRunTransportDirectionStatus
    {
        public FoxRunTransportDirectionStatus(
            FoxRunTransportDirection direction,
            bool selected,
            FoxRunTransportObservedState state,
            int observedContractCount,
            int readyContractCount,
            int failedContractCount,
            FoxRunTransportDiagnostic? diagnostic = null)
        {
            ValidateDirection(direction);
            ValidateState(state);
            if (observedContractCount < 0)
                throw new ArgumentOutOfRangeException(nameof(observedContractCount));
            if (readyContractCount < 0
                || readyContractCount > observedContractCount)
                throw new ArgumentOutOfRangeException(nameof(readyContractCount));
            if (failedContractCount < 0
                || failedContractCount > observedContractCount
                || failedContractCount
                > observedContractCount - readyContractCount)
                throw new ArgumentOutOfRangeException(nameof(failedContractCount));
            if (!selected
                && (state != FoxRunTransportObservedState.Stopped
                    || observedContractCount != 0
                    || readyContractCount != 0
                    || failedContractCount != 0
                    || diagnostic.HasValue))
            {
                throw new ArgumentException(
                    "An unselected transport direction must be an empty Stopped observation.",
                    nameof(selected));
            }

            Direction = direction;
            Selected = selected;
            State = state;
            ObservedContractCount = observedContractCount;
            ReadyContractCount = readyContractCount;
            FailedContractCount = failedContractCount;
            Diagnostic = diagnostic;
        }

        public FoxRunTransportDirection Direction { get; }
        public bool Selected { get; }
        public FoxRunTransportObservedState State { get; }
        public int ObservedContractCount { get; }
        public int ReadyContractCount { get; }
        public int FailedContractCount { get; }
        public FoxRunTransportDiagnostic? Diagnostic { get; }
        public bool IsReady => Selected && State == FoxRunTransportObservedState.Ready;

        public static FoxRunTransportDirectionStatus Unselected(
            FoxRunTransportDirection direction)
            => new FoxRunTransportDirectionStatus(
                direction,
                selected: false,
                FoxRunTransportObservedState.Stopped,
                0,
                0,
                0);

        private static void ValidateDirection(FoxRunTransportDirection direction)
        {
            if (direction != FoxRunTransportDirection.Publish
                && direction != FoxRunTransportDirection.Subscribe)
                throw new ArgumentOutOfRangeException(nameof(direction));
        }

        private static void ValidateState(FoxRunTransportObservedState state)
        {
            if (state < FoxRunTransportObservedState.Stopped
                || state > FoxRunTransportObservedState.Failed)
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    /// <summary>
    /// Immutable status captured from one frozen Provider session. Diagnostic
    /// codes are unique and the collection is always bounded.
    /// </summary>
    public readonly struct FoxRunTransportStatusSnapshot
    {
        public const int MaximumDiagnostics = 8;
        private readonly IReadOnlyList<FoxRunTransportDiagnostic> _diagnostics;

        public FoxRunTransportStatusSnapshot(
            FoxRunTransportId providerId,
            ulong generation,
            FoxRunTransportDirectionStatus publish,
            FoxRunTransportDirectionStatus subscribe,
            IEnumerable<FoxRunTransportDiagnostic> diagnostics = null)
        {
            _ = new FoxRunTransportId(providerId.Value);
            if (publish.Direction != FoxRunTransportDirection.Publish)
                throw new ArgumentException(
                    "Publish observation has the wrong direction.",
                    nameof(publish));
            if (subscribe.Direction != FoxRunTransportDirection.Subscribe)
                throw new ArgumentException(
                    "Subscribe observation has the wrong direction.",
                    nameof(subscribe));

            ProviderId = providerId;
            Generation = generation;
            Publish = publish;
            Subscribe = subscribe;
            State = Combine(publish, subscribe);

            var values = new List<FoxRunTransportDiagnostic>(
                MaximumDiagnostics);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            AddDiagnostic(values, codes, publish.Diagnostic);
            AddDiagnostic(values, codes, subscribe.Diagnostic);
            if (diagnostics != null)
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (values.Count >= MaximumDiagnostics)
                        break;
                    AddDiagnostic(values, codes, diagnostic);
                }
            }
            _diagnostics = Array.AsReadOnly(values.ToArray());
        }

        public FoxRunTransportId ProviderId { get; }
        public ulong Generation { get; }
        public FoxRunTransportObservedState State { get; }
        public FoxRunTransportDirectionStatus Publish { get; }
        public FoxRunTransportDirectionStatus Subscribe { get; }
        public IReadOnlyList<FoxRunTransportDiagnostic> Diagnostics =>
            _diagnostics ?? Array.Empty<FoxRunTransportDiagnostic>();

        private static void AddDiagnostic(
            ICollection<FoxRunTransportDiagnostic> values,
            ISet<string> codes,
            FoxRunTransportDiagnostic? candidate)
        {
            if (!candidate.HasValue)
                return;
            AddDiagnostic(values, codes, candidate.Value);
        }

        private static void AddDiagnostic(
            ICollection<FoxRunTransportDiagnostic> values,
            ISet<string> codes,
            FoxRunTransportDiagnostic candidate)
        {
            if (values.Count >= MaximumDiagnostics
                || string.IsNullOrEmpty(candidate.Code)
                || !codes.Add(candidate.Code))
                return;
            values.Add(candidate);
        }

        private static FoxRunTransportObservedState Combine(
            FoxRunTransportDirectionStatus publish,
            FoxRunTransportDirectionStatus subscribe)
        {
            var selected = 0;
            var ready = 0;
            var hasDegraded = false;
            var hasReconnecting = false;
            var hasStarting = false;
            var hasFailed = false;
            Observe(publish);
            Observe(subscribe);

            if (selected == 0)
                return FoxRunTransportObservedState.Stopped;
            if (ready == selected)
                return FoxRunTransportObservedState.Ready;
            if (hasDegraded || ready != 0)
                return FoxRunTransportObservedState.Degraded;
            if (hasFailed)
                return FoxRunTransportObservedState.Failed;
            if (hasReconnecting)
                return FoxRunTransportObservedState.Reconnecting;
            if (hasStarting)
                return FoxRunTransportObservedState.Starting;
            return FoxRunTransportObservedState.Stopped;

            void Observe(FoxRunTransportDirectionStatus direction)
            {
                if (!direction.Selected)
                    return;
                selected++;
                switch (direction.State)
                {
                    case FoxRunTransportObservedState.Ready:
                        ready++;
                        break;
                    case FoxRunTransportObservedState.Degraded:
                        hasDegraded = true;
                        break;
                    case FoxRunTransportObservedState.Reconnecting:
                        hasReconnecting = true;
                        break;
                    case FoxRunTransportObservedState.Starting:
                        hasStarting = true;
                        break;
                    case FoxRunTransportObservedState.Failed:
                        hasFailed = true;
                        break;
                }
            }
        }
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

    /// <summary>
    /// Optional observed-state surface for a frozen session. The Manager
    /// supplies the exact directions selected for that session.
    /// </summary>
    public interface IFoxRunTransportStatusSource
    {
        FoxRunTransportStatusSnapshot CaptureStatus(
            FoxRunTransportCapabilities selectedDirections);
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
