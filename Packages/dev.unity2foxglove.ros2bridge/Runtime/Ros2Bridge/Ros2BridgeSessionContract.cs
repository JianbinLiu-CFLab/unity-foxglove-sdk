// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable generation-scoped Bridge publish/subscription identity.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed class Ros2BridgeSessionContract :
        IEquatable<Ros2BridgeSessionContract>
    {
        internal Ros2BridgeSessionContract(
            FoxRunTransportId providerId,
            FoxRunTransportDirection direction,
            string topic,
            string canonicalRosType,
            FoxRunResolvedQos qos,
            string bindingId,
            ulong contractId,
            ulong generation)
        {
            if (direction != FoxRunTransportDirection.Publish
                && direction != FoxRunTransportDirection.Subscribe)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException(
                    "A Bridge contract topic is required.",
                    nameof(topic));
            if (string.IsNullOrWhiteSpace(canonicalRosType))
            {
                throw new ArgumentException(
                    "A canonical ROS message type is required.",
                    nameof(canonicalRosType));
            }
            if (!Ros2BridgeFrame.IsValidResolvedQos(qos))
            {
                throw new ArgumentException(
                    "A Bridge contract requires resolved delivery policy.",
                    nameof(qos));
            }
            if (string.IsNullOrWhiteSpace(bindingId))
            {
                throw new ArgumentException(
                    "A generated Bridge binding ID is required.",
                    nameof(bindingId));
            }
            if (contractId == 0)
                throw new ArgumentOutOfRangeException(nameof(contractId));
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));

            ProviderId = providerId;
            Direction = direction;
            Topic = topic.Trim();
            CanonicalRosType = canonicalRosType.Trim();
            Qos = qos;
            BindingId = bindingId.Trim();
            ContractId = contractId;
            Generation = generation;
        }

        internal FoxRunTransportId ProviderId { get; }

        internal FoxRunTransportDirection Direction { get; }

        internal string Topic { get; }

        internal string CanonicalRosType { get; }

        internal FoxRunResolvedQos Qos { get; }

        internal string BindingId { get; }

        internal ulong ContractId { get; }

        internal ulong Generation { get; }

        public bool Equals(Ros2BridgeSessionContract other)
            => other != null
               && ProviderId.Equals(other.ProviderId)
               && Direction == other.Direction
               && string.Equals(
                   Topic,
                   other.Topic,
                   StringComparison.Ordinal)
               && string.Equals(
                   CanonicalRosType,
                   other.CanonicalRosType,
                   StringComparison.Ordinal)
               && Qos.Equals(other.Qos)
               && string.Equals(
                   BindingId,
                   other.BindingId,
                   StringComparison.Ordinal)
               && ContractId == other.ContractId
               && Generation == other.Generation;

        public override bool Equals(object obj)
            => Equals(obj as Ros2BridgeSessionContract);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ProviderId.GetHashCode();
                hash = (hash * 397) ^ Direction.GetHashCode();
                hash = (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(Topic);
                hash = (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(
                           CanonicalRosType);
                hash = (hash * 397) ^ Qos.GetHashCode();
                hash = (hash * 397)
                       ^ StringComparer.Ordinal.GetHashCode(BindingId);
                hash = (hash * 397) ^ ContractId.GetHashCode();
                hash = (hash * 397) ^ Generation.GetHashCode();
                return hash;
            }
        }

        internal bool HasSameDeclaredIdentity(
            Ros2BridgeSessionContract other)
            => other != null
               && ProviderId.Equals(other.ProviderId)
               && Direction == other.Direction
               && string.Equals(
                   Topic,
                   other.Topic,
                   StringComparison.Ordinal)
               && string.Equals(
                   CanonicalRosType,
                   other.CanonicalRosType,
                   StringComparison.Ordinal)
               && Qos.Equals(other.Qos)
               && string.Equals(
                   BindingId,
                   other.BindingId,
                   StringComparison.Ordinal);
    }

    internal sealed class Ros2BridgeSessionContractSnapshot
    {
        private readonly IReadOnlyList<Ros2BridgeSessionContract>
            _contracts;
        private readonly IReadOnlyDictionary<
            ulong,
            Ros2BridgeSessionContract> _byId;

        internal Ros2BridgeSessionContractSnapshot(
            ulong generation,
            IEnumerable<Ros2BridgeSessionContract> contracts)
        {
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            var frozen = (contracts
                    ?? throw new ArgumentNullException(nameof(contracts)))
                .ToArray();
            if (frozen.Any(contract => contract == null))
            {
                throw new ArgumentException(
                    "A Bridge contract snapshot cannot contain null.",
                    nameof(contracts));
            }
            if (frozen.Any(
                    contract => contract.Generation != generation))
            {
                throw new ArgumentException(
                    "Every Bridge contract must belong to the snapshot generation.",
                    nameof(contracts));
            }
            if (frozen
                .GroupBy(contract => contract.ContractId)
                .Any(group => group.Count() != 1))
            {
                throw new ArgumentException(
                    "Bridge wire contract IDs must be unique within one generation.",
                    nameof(contracts));
            }

            Array.Sort(
                frozen,
                (left, right) =>
                    left.ContractId.CompareTo(right.ContractId));
            Generation = generation;
            _contracts = Array.AsReadOnly(frozen);
            _byId = frozen.ToDictionary(
                contract => contract.ContractId);
        }

        internal ulong Generation { get; }

        internal IReadOnlyList<Ros2BridgeSessionContract> Contracts
            => _contracts;

        internal bool TryGet(
            ulong contractId,
            out Ros2BridgeSessionContract contract)
            => _byId.TryGetValue(contractId, out contract);
    }
}
