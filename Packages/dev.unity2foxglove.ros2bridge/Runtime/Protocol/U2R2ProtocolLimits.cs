// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Immutable U2R2 session limits and checked resource accounting.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    /// <remarks>Phase192 source sections: ResourceAccounting.cs</remarks>
    public sealed class U2R2ProtocolLimits
    {
        internal const int MaximumRosTopicNameLength = 255;
        internal const ulong MaximumJsonDepth = 64;

        private static readonly string[] Names =
        {
            "maxConnections",
            "maxDataSessions",
            "maxProbes",
            "maxContracts",
            "maxOutstandingRequests",
            "maxReplayEntries",
            "maxReplayBytes",
            "maxTombstones",
            "fixedFrameBytes",
            "maxHeaderBytes",
            "maxPayloadBytes",
            "maxTransientBytes",
            "maxInFlightBytes",
            "maxQueuedBytes",
            "maxTotalQueueDepth",
            "maxPerContractQueueDepth",
            "maxPerContractQueueBytes",
            "reservedControlQueueDepth",
            "reservedControlQueueBytes",
            "controlBurstLimit",
            "handshakeTimeoutMs",
            "partialFrameTimeoutMs",
            "readTimeoutMs",
            "writeTimeoutMs",
            "joinTimeoutMs",
            "shutdownTimeoutMs",
            "maxJsonDepth",
        };

        private readonly IReadOnlyDictionary<string, ulong> _values;

        private U2R2ProtocolLimits(Dictionary<string, ulong> values)
        {
            Validate(values);
            _values = new ReadOnlyDictionary<string, ulong>(
                new Dictionary<string, ulong>(values, StringComparer.Ordinal));
        }

        public ulong MaxConnections => Value("maxConnections");
        public ulong MaxDataSessions => Value("maxDataSessions");
        public ulong MaxProbes => Value("maxProbes");
        public ulong MaxContracts => Value("maxContracts");
        public ulong MaxOutstandingRequests => Value("maxOutstandingRequests");
        public ulong MaxReplayEntries => Value("maxReplayEntries");
        public ulong MaxReplayBytes => Value("maxReplayBytes");
        public ulong MaxTombstones => Value("maxTombstones");
        public ulong FixedFrameBytes => Value("fixedFrameBytes");
        public ulong MaxHeaderBytes => Value("maxHeaderBytes");
        public ulong MaxPayloadBytes => Value("maxPayloadBytes");
        public ulong MaxTransientBytes => Value("maxTransientBytes");
        public ulong MaxInFlightBytes => Value("maxInFlightBytes");
        public ulong MaxQueuedBytes => Value("maxQueuedBytes");
        public ulong MaxTotalQueueDepth => Value("maxTotalQueueDepth");
        public ulong MaxPerContractQueueDepth => Value("maxPerContractQueueDepth");
        public ulong MaxPerContractQueueBytes => Value("maxPerContractQueueBytes");
        public ulong ReservedControlQueueDepth => Value("reservedControlQueueDepth");
        public ulong ReservedControlQueueBytes => Value("reservedControlQueueBytes");
        public ulong ControlBurstLimit => Value("controlBurstLimit");
        public ulong HandshakeTimeoutMs => Value("handshakeTimeoutMs");
        public ulong PartialFrameTimeoutMs => Value("partialFrameTimeoutMs");
        public ulong ReadTimeoutMs => Value("readTimeoutMs");
        public ulong WriteTimeoutMs => Value("writeTimeoutMs");
        public ulong JoinTimeoutMs => Value("joinTimeoutMs");
        public ulong ShutdownTimeoutMs => Value("shutdownTimeoutMs");
        public ulong MaxJsonDepth => Value("maxJsonDepth");

        public static U2R2ProtocolLimits Default { get; } =
            CreateDefault();

        public static U2R2ProtocolLimits FromDiagnosticSnapshot(
            IReadOnlyDictionary<string, ulong> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            return new U2R2ProtocolLimits(
                values.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal));
        }

        public U2R2ProtocolLimits With(
            params (string Name, ulong Value)[] overrides)
        {
            if (overrides == null)
                throw new ArgumentNullException(nameof(overrides));
            var values = new Dictionary<string, ulong>(
                _values,
                StringComparer.Ordinal);
            foreach (var item in overrides)
            {
                if (!values.ContainsKey(item.Name))
                    ThrowInvalid("Unknown U2R2 limit: " + item.Name + ".");
                values[item.Name] = item.Value;
            }
            return new U2R2ProtocolLimits(values);
        }

        public IReadOnlyDictionary<string, ulong> ToDiagnosticSnapshot()
            => _values;

        private ulong Value(string name) => _values[name];

        private static U2R2ProtocolLimits CreateDefault()
        {
            const ulong fixedFrameBytes = 16;
            const ulong maxHeaderBytes = 64 * 1024;
            const ulong maxPayloadBytes = 64 * 1024 * 1024;
            var maximumFrame = checked(
                fixedFrameBytes + maxHeaderBytes + maxPayloadBytes);
            return FromDiagnosticSnapshot(
                new Dictionary<string, ulong>(StringComparer.Ordinal)
                {
                    ["maxConnections"] = 9,
                    ["maxDataSessions"] = 1,
                    ["maxProbes"] = 8,
                    ["maxContracts"] = 64,
                    ["maxOutstandingRequests"] = 8,
                    ["maxReplayEntries"] = 16,
                    ["maxReplayBytes"] = 4 * 1024 * 1024,
                    ["maxTombstones"] = 32,
                    ["fixedFrameBytes"] = fixedFrameBytes,
                    ["maxHeaderBytes"] = maxHeaderBytes,
                    ["maxPayloadBytes"] = maxPayloadBytes,
                    ["maxTransientBytes"] = maximumFrame * 2,
                    ["maxInFlightBytes"] = maximumFrame * 2,
                    ["maxQueuedBytes"] = maximumFrame * 4,
                    ["maxTotalQueueDepth"] = 128,
                    ["maxPerContractQueueDepth"] = 8,
                    ["maxPerContractQueueBytes"] = maximumFrame * 2,
                    ["reservedControlQueueDepth"] = 8,
                    ["reservedControlQueueBytes"] = 1024 * 1024,
                    ["controlBurstLimit"] = 2,
                    ["handshakeTimeoutMs"] = 5000,
                    ["partialFrameTimeoutMs"] = 2000,
                    ["readTimeoutMs"] = 5000,
                    ["writeTimeoutMs"] = 5000,
                    ["joinTimeoutMs"] = 5000,
                    ["shutdownTimeoutMs"] = 10000,
                    ["maxJsonDepth"] = 64,
                });
        }

        private static void Validate(Dictionary<string, ulong> values)
        {
            if (values.Count != Names.Length
                || Names.Any(name => !values.ContainsKey(name))
                || values.Keys.Any(name => Array.IndexOf(Names, name) < 0))
            {
                ThrowInvalid(
                    "The U2R2 limit snapshot must contain exactly the named limits.");
            }
            if (values.Values.Any(value => value == 0))
                ThrowInvalid("Every U2R2 limit must be nonzero.");
            if (values["maxHeaderBytes"] > uint.MaxValue
                || values["maxPayloadBytes"] > uint.MaxValue)
            {
                ThrowInvalid(
                    "U2R2 wire header and payload limits must fit unsigned 32-bit lengths.");
            }
            if (values["maxJsonDepth"] > MaximumJsonDepth)
            {
                ThrowInvalid(
                    "U2R2 JSON depth cannot exceed the protocol maximum of 64.");
            }
            if (values["maxDataSessions"] != 1)
                ThrowInvalid("maxDataSessions must be exactly one.");

            AddConfiguration(
                values["maxContracts"],
                values["maxTombstones"]);
            var roleTotal = AddConfiguration(
                values["maxDataSessions"],
                values["maxProbes"]);
            if (values["maxConnections"] < roleTotal)
            {
                ThrowInvalid(
                    "maxConnections must contain all data-session and probe leases.");
            }

            var maximumFrame = AddConfiguration(
                values["fixedFrameBytes"],
                AddConfiguration(
                    values["maxHeaderBytes"],
                    values["maxPayloadBytes"]));
            if (values["maxPerContractQueueBytes"] < maximumFrame
                || values["maxQueuedBytes"] < maximumFrame
                || values["maxTransientBytes"] < maximumFrame
                || values["maxInFlightBytes"] < maximumFrame)
            {
                ThrowInvalid(
                    "Every frame-holding byte budget must contain one maximum frame.");
            }
            if (values["reservedControlQueueDepth"]
                >= values["maxTotalQueueDepth"])
            {
                ThrowInvalid(
                    "Reserved control depth must leave at least one data slot.");
            }
            if (values["maxTotalQueueDepth"]
                < AddConfiguration(
                    values["reservedControlQueueDepth"],
                    values["maxPerContractQueueDepth"]))
            {
                ThrowInvalid(
                    "The total queue depth must contain the control reserve and one contract.");
            }
            if (values["maxQueuedBytes"]
                < AddConfiguration(
                    values["reservedControlQueueBytes"],
                    values["maxPerContractQueueBytes"]))
            {
                ThrowInvalid(
                    "The queued-byte budget must contain the control reserve and one contract.");
            }
            if (values["controlBurstLimit"]
                > values["reservedControlQueueDepth"])
            {
                ThrowInvalid(
                    "The control burst limit cannot exceed reserved control depth.");
            }
            if (values["maxReplayEntries"] < values["maxOutstandingRequests"])
            {
                ThrowInvalid(
                    "Replay entry capacity must contain every outstanding request.");
            }
        }

        private static ulong AddConfiguration(ulong left, ulong right)
        {
            try
            {
                return checked(left + right);
            }
            catch (OverflowException)
            {
                ThrowInvalid("U2R2 limit arithmetic overflowed.");
                return 0;
            }
        }

        private static void ThrowInvalid(string message)
            => throw new U2R2ProtocolException(
                "invalid_configuration",
                message,
                terminal: true);
    }
}
