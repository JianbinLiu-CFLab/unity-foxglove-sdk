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

#if false
    /// <remarks>Phase192 source sections: SequenceAndAdmissions.cs, ContractAuthority.State.cs, ContractAuthority.Registration.cs, ContractAuthority.Removal.cs, ContractAuthority.Lifecycle.cs</remarks>
    public sealed partial class U2R2ContractAuthority
    {
    }
#endif
}
