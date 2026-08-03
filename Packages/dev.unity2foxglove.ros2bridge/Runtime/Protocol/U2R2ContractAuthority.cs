// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Strict U2R2 contract parsing, readiness, sequencing, and removal.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public sealed class U2R2Qos
    {
        public U2R2Qos(
            string profile,
            string reliability,
            string durability,
            string history,
            uint depth)
        {
            U2R2CommandAdmission.ValidateQosValues(
                profile,
                reliability,
                durability,
                history,
                depth);
            Profile = profile;
            Reliability = reliability;
            Durability = durability;
            History = history;
            Depth = depth;
        }

        public string Profile { get; }
        public string Reliability { get; }
        public string Durability { get; }
        public string History { get; }
        public uint Depth { get; }

        public override bool Equals(object obj)
            => obj is U2R2Qos other
               && string.Equals(Profile, other.Profile, StringComparison.Ordinal)
               && string.Equals(
                   Reliability,
                   other.Reliability,
                   StringComparison.Ordinal)
               && string.Equals(
                   Durability,
                   other.Durability,
                   StringComparison.Ordinal)
               && string.Equals(History, other.History, StringComparison.Ordinal)
               && Depth == other.Depth;

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Profile);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Reliability);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Durability);
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(History);
                return hash * 397 ^ (int)Depth;
            }
        }
    }

    public enum U2R2ContractDirection
    {
        Publish = 1,
        Subscribe = 2,
    }

    public sealed class U2R2ContractIdentity : IEquatable<U2R2ContractIdentity>
    {
        public U2R2ContractIdentity(
            U2R2ContractKey key,
            U2R2ContractDirection direction,
            string topic,
            string schemaName,
            U2R2Qos qos)
        {
            if (direction != U2R2ContractDirection.Publish
                && direction != U2R2ContractDirection.Subscribe)
            {
                throw new U2R2ProtocolException(
                    "invalid_contract",
                    "The U2R2 contract direction is invalid.",
                    terminal: false);
            }
            U2R2CommandAdmission.ValidateIdentityFields(
                topic,
                schemaName,
                qos);
            Key = key;
            Direction = direction;
            Topic = topic;
            SchemaName = schemaName;
            Qos = qos;
        }

        public U2R2ContractKey Key { get; }
        public U2R2ContractDirection Direction { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public U2R2Qos Qos { get; }

        public bool Equals(U2R2ContractIdentity other)
            => other != null
               && Key == other.Key
               && Direction == other.Direction
               && string.Equals(Topic, other.Topic, StringComparison.Ordinal)
               && string.Equals(
                   SchemaName,
                   other.SchemaName,
                   StringComparison.Ordinal)
               && Equals(Qos, other.Qos);

        public override bool Equals(object obj)
            => Equals(obj as U2R2ContractIdentity);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Key.GetHashCode();
                hash = hash * 397 ^ (int)Direction;
                hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(Topic);
                hash =
                    hash * 397
                    ^ StringComparer.Ordinal.GetHashCode(SchemaName);
                return hash * 397 ^ Qos.GetHashCode();
            }
        }
    }

    internal static class U2R2CommandAdmission
    {
        private static readonly HashSet<string> Profiles =
            new(StringComparer.Ordinal)
            {
                "default",
                "sensor_data",
                "system_default",
            };

        private static readonly HashSet<string> Reliabilities =
            new(StringComparer.Ordinal)
            {
                "reliable",
                "best_effort",
                "system_default",
            };

        private static readonly HashSet<string> Durabilities =
            new(StringComparer.Ordinal)
            {
                "volatile",
                "transient_local",
                "system_default",
            };

        private static readonly HashSet<string> Histories =
            new(StringComparer.Ordinal)
            {
                "keep_last",
                "keep_all",
                "system_default",
            };

        private static readonly HashSet<string> QosFields =
            new(StringComparer.Ordinal)
            {
                "profile",
                "reliability",
                "durability",
                "history",
                "depth",
            };

        public static void ParseContract(
            JObject header,
            U2R2Operation operation,
            out string topic,
            out string schemaName,
            out U2R2Qos qos)
        {
            topic = string.Empty;
            schemaName = string.Empty;
            qos = null;
            var hasContractShape =
                operation == U2R2Operation.PreparePublisher
                || operation == U2R2Operation.Publish
                || operation == U2R2Operation.RegisterSubscription
                || operation == U2R2Operation.Message;
            if (!hasContractShape)
                return;

            topic = RequiredContractString(header, "topic");
            schemaName = RequiredContractString(header, "schemaName");
            ValidateTopic(topic);
            ValidateSchemaName(schemaName);

            if (operation == U2R2Operation.Message)
                return;

            var encoding = RequiredContractString(header, "encoding");
            if (!string.Equals(encoding, "cdr", StringComparison.Ordinal))
                ThrowInvalid("A U2R2 ROS contract requires cdr encoding.");
            qos = ParseQos(header["qos"]);
        }

        internal static void ValidateIdentityFields(
            string topic,
            string schemaName,
            U2R2Qos qos)
        {
            if (qos == null)
                ThrowInvalid("A U2R2 contract qos value is required.");
            ValidateTopic(topic ?? string.Empty);
            ValidateSchemaName(schemaName ?? string.Empty);
            ValidateQosValues(
                qos.Profile,
                qos.Reliability,
                qos.Durability,
                qos.History,
                qos.Depth);
        }

        internal static void ValidateQosValues(
            string profile,
            string reliability,
            string durability,
            string history,
            uint depth)
        {
            if (!Profiles.Contains(profile ?? string.Empty))
                ThrowInvalid("U2R2 qos field profile is invalid.");
            if (!Reliabilities.Contains(reliability ?? string.Empty))
                ThrowInvalid("U2R2 qos field reliability is invalid.");
            if (!Durabilities.Contains(durability ?? string.Empty))
                ThrowInvalid("U2R2 qos field durability is invalid.");
            if (!Histories.Contains(history ?? string.Empty))
                ThrowInvalid("U2R2 qos field history is invalid.");
            if ((string.Equals(history, "keep_last", StringComparison.Ordinal)
                 && depth == 0)
                || (!string.Equals(history, "keep_last", StringComparison.Ordinal)
                    && depth != 0))
            {
                ThrowInvalid(
                    "U2R2 qos depth is positive only for keep_last history.");
            }
        }

        private static U2R2Qos ParseQos(JToken token)
        {
            var value = token as JObject;
            if (value == null
                || value.Properties().Count() != QosFields.Count
                || value.Properties().Any(
                    property => !QosFields.Contains(property.Name)))
            {
                ThrowInvalid(
                    "U2R2 qos must be an exact five-axis object.");
            }

            var profile = RequiredQosString(value, "profile", Profiles);
            var reliability = RequiredQosString(
                value,
                "reliability",
                Reliabilities);
            var durability = RequiredQosString(
                value,
                "durability",
                Durabilities);
            var history = RequiredQosString(value, "history", Histories);
            var depthToken = value["depth"];
            var depth = 0U;
            if (depthToken?.Type != JTokenType.Integer
                || !uint.TryParse(
                    depthToken.ToString(Formatting.None),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out depth))
            {
                ThrowInvalid("U2R2 qos depth must be an unsigned 32-bit integer.");
            }
            if ((string.Equals(history, "keep_last", StringComparison.Ordinal)
                 && depth == 0)
                || (!string.Equals(history, "keep_last", StringComparison.Ordinal)
                    && depth != 0))
            {
                ThrowInvalid(
                    "U2R2 qos depth is positive only for keep_last history.");
            }
            return new U2R2Qos(
                profile,
                reliability,
                durability,
                history,
                depth);
        }

        private static string RequiredContractString(
            JObject header,
            string name)
        {
            var token = header?[name];
            if (token?.Type != JTokenType.String)
                ThrowInvalid("U2R2 contract field " + name + " must be a string.");
            var value = token.Value<string>();
            if (string.IsNullOrEmpty(value))
                ThrowInvalid("U2R2 contract field " + name + " cannot be empty.");
            return value;
        }

        private static string RequiredQosString(
            JObject qos,
            string name,
            HashSet<string> allowed)
        {
            var token = qos[name];
            if (token?.Type != JTokenType.String)
                ThrowInvalid("U2R2 qos field " + name + " must be a string.");
            var value = token.Value<string>();
            if (!allowed.Contains(value))
                ThrowInvalid("U2R2 qos field " + name + " is invalid.");
            return value;
        }

        private static void ValidateTopic(string topic)
        {
            if (topic.Length > U2R2ProtocolLimits.MaximumRosTopicNameLength)
            {
                ThrowInvalid("A U2R2 topic cannot exceed 255 ASCII characters.");
            }
            if (topic.Length < 2
                || topic[0] != '/'
                || topic[topic.Length - 1] == '/')
            {
                ThrowInvalid("A U2R2 topic must be a canonical absolute name.");
            }
            var segmentLength = 0;
            var segmentStart = true;
            for (var index = 1; index < topic.Length; index++)
            {
                var value = topic[index];
                if (value == '/')
                {
                    if (segmentLength == 0)
                        ThrowInvalid("A U2R2 topic cannot contain an empty segment.");
                    segmentLength = 0;
                    segmentStart = true;
                    continue;
                }
                if (!(value == '_'
                      || value >= 'a' && value <= 'z'
                      || value >= 'A' && value <= 'Z'
                      || value >= '0' && value <= '9'))
                {
                    ThrowInvalid("A U2R2 topic contains a non-canonical character.");
                }
                if (segmentStart
                    && value != '_'
                    && !(value >= 'a' && value <= 'z')
                    && !(value >= 'A' && value <= 'Z'))
                {
                    ThrowInvalid(
                        "Every U2R2 topic segment must start with an ASCII letter or underscore.");
                }
                segmentLength++;
                segmentStart = false;
            }
        }

        private static void ValidateSchemaName(string schemaName)
        {
            var parts = schemaName.Split('/');
            if (parts.Length != 3
                || !string.Equals(parts[1], "msg", StringComparison.Ordinal)
                || !IsPackage(parts[0])
                || !IsType(parts[2]))
            {
                ThrowInvalid(
                    "A U2R2 schemaName must be canonical package/msg/Type.");
            }
        }

        private static bool IsPackage(string value)
        {
            if (value.Length < 2
                || value.Length > 255
                || value[0] < 'a'
                || value[0] > 'z'
                || value[value.Length - 1] == '_'
                || value.Contains("__"))
                return false;
            return value.All(
                character => character == '_'
                             || character >= 'a' && character <= 'z'
                             || character >= '0' && character <= '9');
        }

        private static bool IsType(string value)
        {
            if (value.Length == 0
                || value.Length > 255
                || value[0] < 'A'
                || value[0] > 'Z')
                return false;
            return value.All(
                character => character >= 'a' && character <= 'z'
                             || character >= 'A' && character <= 'Z'
                             || character >= '0' && character <= '9');
        }

        private static void ThrowInvalid(string message)
            => throw new U2R2ProtocolException(
                "invalid_contract",
                message,
                terminal: false);
    }

    public enum U2R2MessageAdmission
    {
        Accepted = 1,
        LateTombstone = 2,
    }

    public sealed class U2R2ContractSequence
    {
        private ulong _lastAccepted;

        public U2R2ContractSequence(ulong startingSequence = 0)
        {
            _lastAccepted = startingSequence;
        }

        public ulong LastAccepted => _lastAccepted;
        public bool IsFaulted { get; private set; }

        public void Admit(ulong sequence)
        {
            if (IsFaulted)
            {
                throw new U2R2ProtocolException(
                    "contract_sequence_fault",
                    "The U2R2 contract sequence is already faulted.",
                    terminal: false);
            }
            if (_lastAccepted == ulong.MaxValue)
            {
                IsFaulted = true;
                throw new U2R2ProtocolException(
                    "contract_sequence_exhausted",
                    "The U2R2 contract sequence exhausted before wrap.",
                    terminal: false);
            }
            if (sequence != _lastAccepted + 1)
            {
                IsFaulted = true;
                throw new U2R2ProtocolException(
                    "contract_sequence_fault",
                    "The U2R2 contract sequence is not strictly monotonic.",
                    terminal: false);
            }
            _lastAccepted = sequence;
        }
    }

    public sealed class U2R2RegistrationAdmission : IDisposable
    {
        internal U2R2RegistrationAdmission(
            U2R2ContractAuthority owner,
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            Owner = owner;
            Identity = identity;
            Scheduler = scheduler;
            Replay = replay;
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public bool Replayed { get; internal set; }
        internal U2R2ContractAuthority Owner { get; }
        internal U2R2ContractIdentity Identity { get; }
        internal U2R2BoundedOutboundScheduler Scheduler { get; }
        internal U2R2RequestReplayAuthority Replay { get; }
        internal U2R2ReplayAdmission Response { get; }
        internal ulong ResponseRequestId => Response.RequestId;
        internal bool IsSettled { get; set; }

        public void Dispose()
            => Owner.AbandonRegistration(this);
    }

    public sealed class U2R2RemovalAdmission : IDisposable
    {
        internal U2R2RemovalAdmission(
            U2R2ContractAuthority owner,
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            Owner = owner;
            Identity = identity;
            Scheduler = scheduler;
            Replay = replay;
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public bool Replayed { get; internal set; }
        internal U2R2ContractAuthority Owner { get; }
        internal U2R2ContractIdentity Identity { get; }
        internal U2R2BoundedOutboundScheduler Scheduler { get; }
        internal U2R2RequestReplayAuthority Replay { get; }
        internal U2R2ReplayAdmission Response { get; }
        internal ulong ResponseRequestId => Response.RequestId;
        internal bool IsSettled { get; set; }

        public void Dispose()
            => Owner.AbandonRemoval(this);
    }

    public sealed class U2R2ContractAuthority
    {
        private enum ContractState
        {
            Registering = 1,
            Ready = 2,
            Removing = 3,
        }

        private sealed class ContractEntry
        {
            public U2R2ContractIdentity Identity { get; set; }
            public ContractState State { get; set; }
            public U2R2ContractSequence Sequence { get; } = new();
        }

        private readonly object _gate = new();
        private readonly U2R2ProtocolLimits _limits;
        private readonly Func<
            U2R2Operation,
            ulong,
            U2R2ProtocolException,
            U2R2OutboundFrame> _semanticErrorFrameFactory;
        private readonly Dictionary<U2R2ContractKey, ContractEntry> _contracts = new();
        private readonly Dictionary<U2R2ContractKey, U2R2ContractIdentity>
            _tombstones = new();
        private readonly Queue<U2R2ContractKey> _tombstoneOrder = new();
        private U2R2BoundedOutboundScheduler _boundScheduler;
        private U2R2RequestReplayAuthority _boundReplay;
        private bool _closed;

        public U2R2ContractAuthority(
            U2R2ProtocolLimits limits,
            Func<
                U2R2Operation,
                ulong,
                U2R2ProtocolException,
                U2R2OutboundFrame> semanticErrorFrameFactory)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _semanticErrorFrameFactory = semanticErrorFrameFactory
                ?? throw new ArgumentNullException(nameof(semanticErrorFrameFactory));
        }

        public ulong ContractCount
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_contracts.Count);
            }
        }

        public ulong TombstoneCount
        {
            get
            {
                lock (_gate)
                    return checked((ulong)_tombstones.Count);
            }
        }

        public bool IsClosed
        {
            get
            {
                lock (_gate)
                    return _closed;
            }
        }

        public U2R2RegistrationAdmission BeginRegistration(
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            lock (_gate)
            {
                EnsureOpen();
                EnsureAuthorityPairCompatible(scheduler, replay);
                if (response.Decision == U2R2ReplayDecision.ReplayCached)
                {
                    if (!replay.IsCachedFor(response, scheduler))
                    {
                        throw new InvalidOperationException(
                            "The replayed registration response belongs elsewhere.");
                    }
                    BindAuthorityPair(scheduler, replay);
                    return new U2R2RegistrationAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response)
                    {
                        Replayed = true,
                        IsSettled = true,
                    };
                }
                if (!replay.TryClaimForContract(response, scheduler))
                {
                    throw new InvalidOperationException(
                        "Registration requires the pending command response transaction.");
                }
                BindAuthorityPair(scheduler, replay);

                var inserted = false;
                try
                {
                    if (identity.Direction != U2R2ContractDirection.Subscribe)
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionReady,
                            new U2R2ProtocolException(
                                "invalid_contract",
                                "A register_subscription command requires subscribe direction.",
                                terminal: false));
                    }
                    if (_contracts.ContainsKey(identity.Key)
                        || _tombstones.ContainsKey(identity.Key))
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionReady,
                            new U2R2ProtocolException(
                                "invalid_contract",
                                "The U2R2 contract ID and generation are already bound.",
                                terminal: false));
                    }
                    if (checked((ulong)_contracts.Count) == _limits.MaxContracts)
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionReady,
                            new U2R2ProtocolException(
                                "capacity_exceeded",
                                "The U2R2 contract limit is exhausted.",
                                terminal: false));
                    }
                    _contracts.Add(
                        identity.Key,
                        new ContractEntry
                        {
                            Identity = identity,
                            State = ContractState.Registering,
                        });
                    inserted = true;
                    scheduler.ActivateContract(identity.Key);
                    return new U2R2RegistrationAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response);
                }
                catch
                {
                    if (inserted)
                    {
                        _contracts.Remove(identity.Key);
                        scheduler.RetireContract(identity.Key);
                    }
                    replay.TryAbandonClaimed(response);
                    throw;
                }
            }
        }

        public void CommitReady(
            U2R2RegistrationAdmission admission,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2OutboundFrame exactReadyFrame)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                if (admission.Replayed)
                {
                    if (!ReferenceEquals(admission.Owner, this)
                        || !ReferenceEquals(admission.Replay, replay)
                        || admission.ResponseRequestId != response.RequestId
                        || response.Decision != U2R2ReplayDecision.ReplayCached)
                    {
                        throw new InvalidOperationException(
                            "The replayed U2R2 registration belongs elsewhere.");
                    }
                    return;
                }
                if (!ReferenceEquals(admission.Owner, this)
                    || !ReferenceEquals(admission.Replay, replay)
                    || admission.ResponseRequestId != response.RequestId
                    || admission.IsSettled
                    || !_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Registering
                    || !entry.Identity.Equals(admission.Identity))
                {
                    throw new InvalidOperationException(
                        "The U2R2 registration admission is not pending.");
                }
                replay.CompleteFenced(
                    response,
                    exactReadyFrame,
                    admission.Identity.Key);
                entry.State = ContractState.Ready;
                admission.IsSettled = true;
            }
        }

        public void CancelRegistration(
            U2R2RegistrationAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                ValidateRegistrationTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Registering)
                {
                    throw new InvalidOperationException(
                        "The U2R2 registration admission is not pending.");
                }
                replay.CancelClaimed(response);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public void AbortRegistration(
            U2R2RegistrationAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2ProtocolException error)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            lock (_gate)
            {
                ValidateRegistrationTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Registering)
                {
                    throw new InvalidOperationException(
                        "The U2R2 registration admission is not pending.");
                }
                var exactErrorFrame = CreateSemanticErrorFrame(
                    U2R2Operation.SubscriptionReady,
                    response.RequestId,
                    error);
                replay.RejectClaimed(response, exactErrorFrame);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public U2R2MessageAdmission AdmitMessage(
            U2R2ContractIdentity identity,
            ulong sequence)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            lock (_gate)
            {
                EnsureOpen();
                if (_tombstones.TryGetValue(
                        identity.Key,
                        out var removedIdentity))
                {
                    EnsureMessageIdentity(identity, removedIdentity);
                    return U2R2MessageAdmission.LateTombstone;
                }
                if (!_contracts.TryGetValue(identity.Key, out var entry))
                {
                    throw new U2R2ProtocolException(
                        "unknown_contract",
                        "The U2R2 message references an unknown contract generation.",
                        terminal: true);
                }
                EnsureMessageIdentity(identity, entry.Identity);
                if (entry.State == ContractState.Registering)
                {
                    throw new U2R2ProtocolException(
                        "contract_not_ready",
                        "The U2R2 subscription_ready response is not committed.",
                        terminal: true);
                }
                if (entry.State == ContractState.Removing)
                    return U2R2MessageAdmission.LateTombstone;
                entry.Sequence.Admit(sequence);
                return U2R2MessageAdmission.Accepted;
            }
        }

        private static void EnsureMessageIdentity(
            U2R2ContractIdentity actual,
            U2R2ContractIdentity expected)
        {
            if (!expected.Equals(actual))
            {
                throw new U2R2ProtocolException(
                    "contract_identity_mismatch",
                    "The U2R2 message identity does not match its frozen contract.",
                    terminal: true);
            }
        }

        public U2R2RemovalAdmission BeginUnregister(
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            lock (_gate)
            {
                EnsureOpen();
                EnsureAuthorityPairCompatible(scheduler, replay);
                if (response.Decision == U2R2ReplayDecision.ReplayCached)
                {
                    if (!replay.IsCachedFor(response, scheduler))
                    {
                        throw new InvalidOperationException(
                            "The replayed unregister response belongs elsewhere.");
                    }
                    BindAuthorityPair(scheduler, replay);
                    return new U2R2RemovalAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response)
                    {
                        Replayed = true,
                        IsSettled = true,
                    };
                }
                if (!replay.TryClaimForContract(response, scheduler))
                {
                    throw new InvalidOperationException(
                        "Unregister requires the pending command response transaction.");
                }
                BindAuthorityPair(scheduler, replay);

                var removing = false;
                try
                {
                    if (!_contracts.TryGetValue(identity.Key, out var entry)
                        || entry.State != ContractState.Ready)
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionRemoved,
                            new U2R2ProtocolException(
                                "unknown_contract",
                                "The U2R2 unregister request references no ready contract.",
                                terminal: true));
                    }
                    if (!entry.Identity.Equals(identity))
                    {
                        throw CommitSemanticRejection(
                            replay,
                            response,
                            U2R2Operation.SubscriptionRemoved,
                            new U2R2ProtocolException(
                                "invalid_contract",
                                "The U2R2 unregister identity conflicts with the registered contract.",
                                terminal: false));
                    }
                    scheduler.RevokeContract(identity.Key);
                    entry.State = ContractState.Removing;
                    removing = true;
                    return new U2R2RemovalAdmission(
                        this,
                        identity,
                        scheduler,
                        replay,
                        response);
                }
                catch
                {
                    if (removing)
                    {
                        _contracts.Remove(identity.Key);
                        scheduler.RetireContract(identity.Key);
                    }
                    replay.TryAbandonClaimed(response);
                    throw;
                }
            }
        }

        internal void AbandonRegistration(U2R2RegistrationAdmission admission)
        {
            if (admission == null)
                return;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.IsSettled
                    || admission.Replayed)
                {
                    return;
                }
                admission.Replay.TryAbandonClaimed(admission.Response);
                _contracts.Remove(admission.Identity.Key);
                try
                {
                    admission.Scheduler.RetireContract(admission.Identity.Key);
                }
                catch
                {
                    // Dispose is a last-resort rollback and must not mask the
                    // exception that abandoned the transaction.
                }
                admission.IsSettled = true;
            }
        }

        internal void AbandonRemoval(U2R2RemovalAdmission admission)
        {
            if (admission == null)
                return;
            lock (_gate)
            {
                if (!ReferenceEquals(admission.Owner, this)
                    || admission.IsSettled
                    || admission.Replayed)
                {
                    return;
                }
                admission.Replay.TryAbandonClaimed(admission.Response);
                _contracts.Remove(admission.Identity.Key);
                try
                {
                    admission.Scheduler.RetireContract(admission.Identity.Key);
                }
                catch
                {
                    // Dispose is a last-resort rollback and must not mask the
                    // exception that abandoned the transaction.
                }
                admission.IsSettled = true;
            }
        }

        public bool TryCommitRemoved(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2OutboundFrame exactRemovedFrame)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                if (admission.Replayed)
                {
                    if (!ReferenceEquals(admission.Owner, this)
                        || !ReferenceEquals(admission.Scheduler, scheduler)
                        || !ReferenceEquals(admission.Replay, replay)
                        || admission.ResponseRequestId != response.RequestId
                        || response.Decision != U2R2ReplayDecision.ReplayCached)
                    {
                        throw new InvalidOperationException(
                            "The replayed U2R2 removal belongs elsewhere.");
                    }
                    return true;
                }
                if (!ReferenceEquals(admission.Owner, this)
                    || !ReferenceEquals(admission.Scheduler, scheduler)
                    || !ReferenceEquals(admission.Replay, replay)
                    || admission.ResponseRequestId != response.RequestId
                    || admission.IsSettled
                    || !_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Removing
                    || !entry.Identity.Equals(admission.Identity))
                {
                    throw new InvalidOperationException(
                        "The U2R2 removal admission is not pending.");
                }
                if (!scheduler.IsContractRevokedAndDrained(
                        admission.Identity.Key))
                {
                    return false;
                }
                replay.CompleteFenced(
                    response,
                    exactRemovedFrame,
                    admission.Identity.Key);
                _contracts.Remove(admission.Identity.Key);
                AddTombstone(admission.Identity, scheduler);
                admission.IsSettled = true;
                return true;
            }
        }

        public void CancelRemoval(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            lock (_gate)
            {
                ValidateRemovalTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Removing)
                {
                    throw new InvalidOperationException(
                        "The U2R2 removal admission is not pending.");
                }
                replay.CancelClaimed(response);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public void AbortRemoval(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2ProtocolException error)
        {
            if (admission == null)
                throw new ArgumentNullException(nameof(admission));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            lock (_gate)
            {
                ValidateRemovalTransaction(
                    admission,
                    scheduler,
                    replay,
                    response);
                if (admission.IsSettled)
                    return;
                if (!_contracts.TryGetValue(
                        admission.Identity.Key,
                        out var entry)
                    || entry.State != ContractState.Removing)
                {
                    throw new InvalidOperationException(
                        "The U2R2 removal admission is not pending.");
                }
                var exactErrorFrame = CreateSemanticErrorFrame(
                    U2R2Operation.SubscriptionRemoved,
                    response.RequestId,
                    error);
                replay.RejectClaimed(response, exactErrorFrame);
                _contracts.Remove(admission.Identity.Key);
                scheduler.RetireContract(admission.Identity.Key);
                admission.IsSettled = true;
            }
        }

        public void Close(
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay)
        {
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));
            lock (_gate)
            {
                EnsureAuthorityPairCompatible(scheduler, replay);
                BindAuthorityPair(scheduler, replay);
                if (_closed)
                    return;
                replay.Close();
                scheduler.Close();
                _contracts.Clear();
                _tombstones.Clear();
                _tombstoneOrder.Clear();
                _closed = true;
            }
        }

        private void EnsureAuthorityPairCompatible(
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay)
        {
            if (_boundScheduler == null && _boundReplay == null)
                return;
            if (!ReferenceEquals(_boundScheduler, scheduler)
                || !ReferenceEquals(_boundReplay, replay))
            {
                throw new InvalidOperationException(
                    "The U2R2 contract authority belongs to another scheduler and replay authority.");
            }
        }

        private void BindAuthorityPair(
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay)
        {
            EnsureAuthorityPairCompatible(scheduler, replay);
            if (_boundScheduler != null)
                return;
            _boundScheduler = scheduler;
            _boundReplay = replay;
        }

        private void ValidateRegistrationTransaction(
            U2R2RegistrationAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (!ReferenceEquals(admission.Owner, this)
                || !ReferenceEquals(admission.Scheduler, scheduler))
            {
                throw new InvalidOperationException(
                    "The U2R2 registration admission belongs elsewhere.");
            }
            if (!ReferenceEquals(admission.Replay, replay)
                || admission.ResponseRequestId != response.RequestId)
            {
                throw new InvalidOperationException(
                    "The U2R2 registration response transaction belongs elsewhere.");
            }
        }

        private void ValidateRemovalTransaction(
            U2R2RemovalAdmission admission,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            if (!ReferenceEquals(admission.Owner, this)
                || !ReferenceEquals(admission.Scheduler, scheduler))
            {
                throw new InvalidOperationException(
                    "The U2R2 removal admission belongs elsewhere.");
            }
            if (!ReferenceEquals(admission.Replay, replay)
                || admission.ResponseRequestId != response.RequestId)
            {
                throw new InvalidOperationException(
                    "The U2R2 removal response transaction belongs elsewhere.");
            }
        }

        private U2R2ProtocolException CommitSemanticRejection(
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response,
            U2R2Operation responseOperation,
            U2R2ProtocolException error)
        {
            replay.RejectClaimed(
                response,
                CreateSemanticErrorFrame(
                    responseOperation,
                    response.RequestId,
                    error));
            return error;
        }

        private U2R2OutboundFrame CreateSemanticErrorFrame(
            U2R2Operation responseOperation,
            ulong requestId,
            U2R2ProtocolException error)
        {
            var frame = _semanticErrorFrameFactory(
                responseOperation,
                requestId,
                error);
            if (frame == null || !frame.IsControl)
            {
                throw new InvalidOperationException(
                    "The semantic error factory must return a control frame.");
            }
            return frame;
        }

        private void AddTombstone(
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler)
        {
            if (!_tombstones.ContainsKey(identity.Key))
            {
                _tombstones.Add(identity.Key, identity);
                _tombstoneOrder.Enqueue(identity.Key);
            }
            while (checked((ulong)_tombstones.Count) > _limits.MaxTombstones)
            {
                var evicted = _tombstoneOrder.Dequeue();
                _tombstones.Remove(evicted);
                scheduler.ForgetContract(evicted);
            }
        }

        private void EnsureOpen()
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "The U2R2 contract authority is closed.");
            }
        }

        private static void ThrowCapacity(string message)
            => throw new U2R2ProtocolException(
                "capacity_exceeded",
                message,
                terminal: false);

        private static void ThrowInvalid(string message)
            => throw new U2R2ProtocolException(
                "invalid_contract",
                message,
                terminal: false);
    }
}
