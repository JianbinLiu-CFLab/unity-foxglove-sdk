// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Immutable endpoint settings and reconnect-generation admission.

using System;
using System.Collections.Generic;
using System.Net;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed class Ros2BridgeSessionSettings
    {
        internal Ros2BridgeSessionSettings(
            string host,
            int port,
            ulong generation,
            U2R2ProtocolLimits limits)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));

            Host = NormalizeHost(host);
            Port = port;
            Generation = generation;
            Limits = limits
                ?? throw new ArgumentNullException(nameof(limits));
        }

        internal string Host { get; }

        internal int Port { get; }

        internal ulong Generation { get; }

        internal U2R2ProtocolLimits Limits { get; }

        private static string NormalizeHost(string host)
        {
            if (string.Equals(
                    host,
                    "localhost",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "127.0.0.1";
            }
            return IPAddress.TryParse(host, out var address)
                ? address.ToString()
                : host.Trim();
        }
    }

    internal sealed class Ros2BridgeReconnectSnapshot
    {
        internal Ros2BridgeReconnectSnapshot(
            Ros2BridgeSessionSettings settings,
            ulong attemptGeneration,
            Ros2BridgeSessionContractSnapshot contracts)
        {
            Settings = settings
                ?? throw new ArgumentNullException(nameof(settings));
            if (attemptGeneration == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attemptGeneration));
            }
            Contracts = contracts
                ?? throw new ArgumentNullException(nameof(contracts));
            AttemptGeneration = attemptGeneration;
        }

        internal Ros2BridgeSessionSettings Settings { get; }

        internal ulong AttemptGeneration { get; }

        internal Ros2BridgeSessionContractSnapshot Contracts { get; }
    }

    internal sealed class Ros2BridgeSessionState :
        IRos2BridgeInboundContractResolver
    {
        private readonly object _gate = new object();
        private readonly Ros2BridgeSessionSettings _settings;
        private readonly Dictionary<
            ulong,
            Ros2BridgeSessionContract> _local =
            new Dictionary<ulong, Ros2BridgeSessionContract>();
        private readonly Dictionary<string, ulong> _bindingIds =
            new Dictionary<string, ulong>(StringComparer.Ordinal);
        private readonly Dictionary<
            ulong,
            Ros2BridgeSessionContract> _attemptContracts =
            new Dictionary<ulong, Ros2BridgeSessionContract>();
        private readonly Dictionary<
            ulong,
            Ros2BridgeSessionContract> _tombstones =
            new Dictionary<ulong, Ros2BridgeSessionContract>();
        private readonly Queue<ulong> _tombstoneOrder =
            new Queue<ulong>();
        private readonly HashSet<ulong> _readySubscriptions =
            new HashSet<ulong>();
        private readonly HashSet<string> _readyPublishers =
            new HashSet<string>(StringComparer.Ordinal);

        private ulong _attemptGeneration;
        private Ros2BridgeV2SessionSnapshot _wireSession;
        private bool _stopped;

        internal Ros2BridgeSessionState(
            Ros2BridgeSessionSettings settings)
        {
            _settings = settings
                ?? throw new ArgumentNullException(nameof(settings));
        }

        internal Ros2BridgeSessionSettings Settings => _settings;

        internal Ros2BridgeSessionResult TryActivateLocal(
            Ros2BridgeSessionContract contract)
        {
            string reason;
            if (!ValidateContract(contract, out reason))
                return Ros2BridgeSessionResult.Reject(reason);
            lock (_gate)
            {
                if (_stopped)
                {
                    reason = "The Bridge session is stopped.";
                    return Ros2BridgeSessionResult.Unavailable(reason);
                }
                if (_local.TryGetValue(
                        contract.ContractId,
                        out var existing))
                {
                    if (existing.Equals(contract))
                    {
                        return Ros2BridgeSessionResult.Accepted();
                    }
                    reason =
                        "The Bridge contract ID conflicts with an active identity.";
                    return Ros2BridgeSessionResult.Reject(reason);
                }
                if (_bindingIds.TryGetValue(
                        contract.BindingId,
                        out var existingId))
                {
                    reason =
                        "The Bridge binding identity conflicts with an active contract.";
                    return Ros2BridgeSessionResult.Reject(reason);
                }

                _local.Add(contract.ContractId, contract);
                _bindingIds.Add(
                    contract.BindingId,
                    contract.ContractId);
                _tombstones.Remove(contract.ContractId);
                if (_wireSession != null)
                {
                    _attemptContracts[contract.ContractId] =
                        contract;
                }
                return Ros2BridgeSessionResult.Accepted();
            }
        }

        internal bool TryActivateLocal(
            Ros2BridgeSessionContract contract,
            out string reason)
        {
            var result = TryActivateLocal(contract);
            reason = result.Reason;
            return result.IsAccepted;
        }

        internal bool TryRevokeLocal(
            Ros2BridgeSessionContract contract,
            out string reason)
        {
            if (contract == null)
            {
                reason = "The Bridge contract is null.";
                return false;
            }
            lock (_gate)
            {
                if (!_local.TryGetValue(
                        contract.ContractId,
                        out var existing)
                    || !existing.Equals(contract))
                {
                    reason =
                        "The Bridge contract is already released or belongs elsewhere.";
                    return false;
                }

                _local.Remove(contract.ContractId);
                _bindingIds.Remove(contract.BindingId);
                _attemptContracts.Remove(contract.ContractId);
                _readySubscriptions.Remove(contract.ContractId);
                AddTombstoneLocked(contract);
                reason = string.Empty;
                return true;
            }
        }

        internal bool IsLocallyActive(
            Ros2BridgeSessionContract contract)
        {
            if (contract == null)
                return false;
            lock (_gate)
            {
                return _local.TryGetValue(
                           contract.ContractId,
                           out var current)
                       && current.Equals(contract);
            }
        }

        internal Ros2BridgeReconnectSnapshot BeginReconnect(
            Ros2BridgeSessionContractSnapshot contracts)
        {
            if (contracts == null)
                throw new ArgumentNullException(nameof(contracts));
            if (contracts.Generation != _settings.Generation)
            {
                throw new ArgumentException(
                    "The reconnect snapshot belongs to another session generation.",
                    nameof(contracts));
            }

            lock (_gate)
            {
                if (_stopped)
                {
                    throw new InvalidOperationException(
                        "The Bridge session is stopped.");
                }
                foreach (var contract in contracts.Contracts)
                {
                    if (!_local.TryGetValue(
                            contract.ContractId,
                            out var current)
                        || !current.Equals(contract))
                    {
                        throw new InvalidOperationException(
                            "The reconnect snapshot contains a released contract.");
                    }
                }
                if (_attemptGeneration == ulong.MaxValue)
                {
                    throw new InvalidOperationException(
                        "The Bridge reconnect generation is exhausted.");
                }
                _attemptGeneration++;
                _wireSession = null;
                _readySubscriptions.Clear();
                _readyPublishers.Clear();
                _attemptContracts.Clear();
                foreach (var contract in contracts.Contracts)
                {
                    _attemptContracts.Add(
                        contract.ContractId,
                        contract);
                }
                return new Ros2BridgeReconnectSnapshot(
                    _settings,
                    _attemptGeneration,
                    contracts);
            }
        }

        internal bool TryCompleteHandshake(
            ulong attemptGeneration,
            Ros2BridgeV2SessionSnapshot wireSession,
            out string reason)
        {
            if (wireSession == null)
            {
                reason = "The Bridge wire session is null.";
                return false;
            }
            lock (_gate)
            {
                if (!IsCurrentAttemptLocked(
                        attemptGeneration,
                        out reason))
                {
                    return false;
                }
                if (_attemptContracts.Count != 0
                    && !wireSession.HasCapability(
                        U2R2Capability.Subscribe))
                {
                    reason =
                        "The Bridge peer did not grant subscribe capability.";
                    return false;
                }
                _wireSession = wireSession;
                reason = string.Empty;
                return true;
            }
        }

        internal bool TryMarkSubscriptionReady(
            ulong attemptGeneration,
            Ros2BridgeSessionContract contract,
            out string reason)
        {
            if (contract == null)
            {
                reason = "The Bridge subscription contract is null.";
                return false;
            }
            lock (_gate)
            {
                if (!IsCurrentAttemptLocked(
                        attemptGeneration,
                        out reason))
                {
                    return false;
                }
                if (_wireSession == null)
                {
                    reason =
                        "The Bridge handshake has not completed.";
                    return false;
                }
                if (!_local.TryGetValue(
                        contract.ContractId,
                        out var local)
                    || !local.Equals(contract)
                    || !_attemptContracts.TryGetValue(
                        contract.ContractId,
                        out var replayed)
                    || !replayed.Equals(contract))
                {
                    reason =
                        "The Bridge subscription was released before it became ready.";
                    return false;
                }
                _readySubscriptions.Add(contract.ContractId);
                reason = string.Empty;
                return true;
            }
        }

        internal bool TryMarkPublisherReady(
            ulong attemptGeneration,
            string bindingId,
            out string reason)
        {
            if (string.IsNullOrWhiteSpace(bindingId))
            {
                reason =
                    "A Bridge publisher binding ID is required.";
                return false;
            }
            lock (_gate)
            {
                if (!IsCurrentAttemptLocked(
                        attemptGeneration,
                        out reason))
                {
                    return false;
                }
                if (_wireSession == null)
                {
                    reason =
                        "The Bridge handshake has not completed.";
                    return false;
                }
                _readyPublishers.Add(bindingId.Trim());
                reason = string.Empty;
                return true;
            }
        }

        internal bool IsPublisherReady(string bindingId)
        {
            if (string.IsNullOrWhiteSpace(bindingId))
                return false;
            lock (_gate)
                return _readyPublishers.Contains(bindingId);
        }

        internal bool IsSubscriptionReady(ulong contractId)
        {
            lock (_gate)
                return _readySubscriptions.Contains(contractId);
        }

        public Ros2BridgeSessionResult TryAcceptSubscriptionReady(
            U2R2Message message)
        {
            if (message == null)
            {
                return Ros2BridgeSessionResult.Fault(
                    "The Bridge subscription_ready response is null.");
            }
            if (message.Operation
                != U2R2Operation.SubscriptionReady)
            {
                return Ros2BridgeSessionResult.Fault(
                    "The Bridge response is not subscription_ready.");
            }
            lock (_gate)
            {
                if (_stopped || _wireSession == null)
                {
                    return Ros2BridgeSessionResult.Unavailable(
                        "The Bridge session is not ready.");
                }
                if (!string.Equals(
                        message.SessionId,
                        _wireSession.SessionId,
                        StringComparison.Ordinal)
                    || message.ConnectionGeneration
                    != _wireSession.ConnectionGeneration)
                {
                    return Ros2BridgeSessionResult.Fault(
                        "The Bridge subscription_ready response belongs to a stale session generation.");
                }
                if (!_local.TryGetValue(
                        message.ContractId,
                        out var local)
                    || !_attemptContracts.TryGetValue(
                        message.ContractId,
                        out var replayed)
                    || !replayed.Equals(local))
                {
                    return Ros2BridgeSessionResult.Fault(
                        "The Bridge subscription_ready response references an unknown contract.");
                }
                _readySubscriptions.Add(message.ContractId);
                return Ros2BridgeSessionResult.Accepted();
            }
        }

        public Ros2BridgeSessionResult TryResolveInbound(
            U2R2Message message,
            out Ros2BridgeSessionContract contract)
        {
            contract = null;
            if (message == null)
            {
                return Ros2BridgeSessionResult.Fault(
                    "The inbound Bridge message is null.");
            }
            lock (_gate)
            {
                if (_stopped || _wireSession == null)
                {
                    return Ros2BridgeSessionResult.Unavailable(
                        "The Bridge session is not ready.");
                }
                if (!string.Equals(
                        message.SessionId,
                        _wireSession.SessionId,
                        StringComparison.Ordinal)
                    || message.ConnectionGeneration
                    != _wireSession.ConnectionGeneration)
                {
                    return Ros2BridgeSessionResult.Fault(
                        "The inbound Bridge message belongs to a stale session generation.");
                }
                if (_tombstones.TryGetValue(
                        message.ContractId,
                        out var removed)
                    && IdentityMatches(message, removed))
                {
                    return Ros2BridgeSessionResult.Reject(
                        "The inbound Bridge message belongs to a released contract.");
                }
                if (!_local.TryGetValue(
                        message.ContractId,
                        out var current)
                    || !_attemptContracts.TryGetValue(
                        message.ContractId,
                        out var attempted)
                    || !attempted.Equals(current))
                {
                    return Ros2BridgeSessionResult.Fault(
                        "The inbound Bridge message references an unknown contract.");
                }
                if (!IdentityMatches(message, current))
                {
                    return Ros2BridgeSessionResult.Fault(
                        "The inbound Bridge message conflicts with its frozen contract.");
                }
                if (!_readySubscriptions.Contains(
                        current.ContractId))
                {
                    return Ros2BridgeSessionResult.Reject(
                        "The inbound Bridge message arrived before subscription_ready.");
                }
                contract = current;
                return Ros2BridgeSessionResult.Accepted();
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                _stopped = true;
                _wireSession = null;
                _local.Clear();
                _bindingIds.Clear();
                _attemptContracts.Clear();
                _readySubscriptions.Clear();
                _readyPublishers.Clear();
                _tombstones.Clear();
                _tombstoneOrder.Clear();
            }
        }

        private bool ValidateContract(
            Ros2BridgeSessionContract contract,
            out string reason)
        {
            if (contract == null)
            {
                reason = "The Bridge contract is null.";
                return false;
            }
            if (contract.Generation != _settings.Generation)
            {
                reason =
                    "The Bridge contract belongs to another session generation.";
                return false;
            }
            if (contract.Direction
                != Unity.FoxgloveSDK.Components
                    .FoxRunTransportDirection.Subscribe)
            {
                reason =
                    "The Bridge lease registry accepts subscription contracts only.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool IsCurrentAttemptLocked(
            ulong attemptGeneration,
            out string reason)
        {
            if (_stopped)
            {
                reason = "The Bridge session is stopped.";
                return false;
            }
            if (attemptGeneration == 0
                || attemptGeneration != _attemptGeneration)
            {
                reason =
                    "The Bridge reconnect attempt is no longer current.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private void AddTombstoneLocked(
            Ros2BridgeSessionContract contract)
        {
            if (_tombstones.ContainsKey(contract.ContractId))
                return;
            var capacity = checked((int)Math.Min(
                _settings.Limits.MaxContracts,
                checked((ulong)int.MaxValue)));
            while (_tombstones.Count >= capacity
                   && _tombstoneOrder.Count != 0)
            {
                _tombstones.Remove(
                    _tombstoneOrder.Dequeue());
            }
            _tombstones.Add(contract.ContractId, contract);
            _tombstoneOrder.Enqueue(contract.ContractId);
        }

        private static bool IdentityMatches(
            U2R2Message message,
            Ros2BridgeSessionContract contract)
            => message.ContractId == contract.ContractId
               && string.Equals(
                   message.Topic,
                   contract.Topic,
                   StringComparison.Ordinal)
               && string.Equals(
                   message.SchemaName,
                   contract.CanonicalRosType,
                   StringComparison.Ordinal);

    }
}
