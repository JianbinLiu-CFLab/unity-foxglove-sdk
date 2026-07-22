// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Immutable, deterministic lock model for a static FoxRun ROS2 interface package.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunRos2InterfaceContractLock
    {
        public FoxRunRos2InterfaceContractLock(
            string declaringType,
            string memberName,
            string topic,
            string dtoIdentity,
            string payloadMessageName,
            string envelopeMessageName,
            string messageDigest,
            string envelopeDigest)
        {
            DeclaringType = Required(declaringType, nameof(declaringType));
            MemberName = Required(memberName, nameof(memberName));
            Topic = Required(topic, nameof(topic));
            DtoIdentity = Required(dtoIdentity, nameof(dtoIdentity));
            PayloadMessageName = Required(payloadMessageName, nameof(payloadMessageName));
            EnvelopeMessageName = Required(envelopeMessageName, nameof(envelopeMessageName));
            MessageDigest = RequiredDigest(messageDigest, nameof(messageDigest));
            EnvelopeDigest = RequiredDigest(envelopeDigest, nameof(envelopeDigest));
        }

        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string DtoIdentity { get; }
        public string PayloadMessageName { get; }
        public string EnvelopeMessageName { get; }
        public string MessageDigest { get; }
        public string EnvelopeDigest { get; }

        internal string StableKey => DeclaringType + "\u001f" + MemberName + "\u001f" + Topic;

        internal static string Required(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A lock value is required.", parameterName);
            return value;
        }

        internal static string RequiredDigest(string value, string parameterName)
        {
            value = Required(value, parameterName);
            if (value.Length != 64 || value.Any(character => !IsLowerHex(character)))
                throw new ArgumentException("A lock digest must be a lowercase SHA-256 hex string.", parameterName);
            return value;
        }

        private static bool IsLowerHex(char character)
            => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
    }

    public sealed class FoxRunRos2InterfaceLock
    {
        public FoxRunRos2InterfaceLock(
            int lockSchemaVersion,
            int interfaceSchemaVersion,
            string unityPackageId,
            string rosPackageName,
            int interfaceRevision,
            string generatorVersion,
            int namingPolicyVersion,
            string interfaceDigest,
            IReadOnlyList<FoxRunRos2InterfaceContractLock> contracts)
        {
            if (lockSchemaVersion != FoxRunRos2InterfaceIdentity.LockSchemaVersion)
                throw new ArgumentOutOfRangeException(nameof(lockSchemaVersion));
            if (interfaceSchemaVersion != FoxRunRos2InterfaceIdentity.InterfaceSchemaVersion)
                throw new ArgumentOutOfRangeException(nameof(interfaceSchemaVersion));
            if (!string.Equals(unityPackageId, FoxRunRos2InterfaceIdentity.UnityPackageId, StringComparison.Ordinal))
                throw new ArgumentException("The lock must identify the project-owned static interface package.", nameof(unityPackageId));
            if (!FoxRunRos2InterfaceIdentity.IsValidRosPackageName(rosPackageName))
                throw new ArgumentException("ROS package name is invalid.", nameof(rosPackageName));
            if (interfaceRevision < 1)
                throw new ArgumentOutOfRangeException(nameof(interfaceRevision));
            if (!FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision(rosPackageName, out var revision)
                || revision != interfaceRevision)
            {
                throw new ArgumentException("ROS package name and interface revision must agree.", nameof(rosPackageName));
            }

            LockSchemaVersion = lockSchemaVersion;
            InterfaceSchemaVersion = interfaceSchemaVersion;
            UnityPackageId = unityPackageId;
            RosPackageName = rosPackageName;
            InterfaceRevision = interfaceRevision;
            GeneratorVersion = FoxRunRos2InterfaceContractLock.Required(generatorVersion, nameof(generatorVersion));
            NamingPolicyVersion = namingPolicyVersion;
            if (namingPolicyVersion != FoxRunRos2InterfaceIdentity.NamingPolicyVersion)
                throw new ArgumentOutOfRangeException(nameof(namingPolicyVersion));
            InterfaceDigest = FoxRunRos2InterfaceContractLock.RequiredDigest(interfaceDigest, nameof(interfaceDigest));
            Contracts = NormalizeContracts(contracts);
        }

        public int LockSchemaVersion { get; }
        public int InterfaceSchemaVersion { get; }
        public string UnityPackageId { get; }
        public string RosPackageName { get; }
        public int InterfaceRevision { get; }
        public string GeneratorVersion { get; }
        public int NamingPolicyVersion { get; }
        public string InterfaceDigest { get; }
        public IReadOnlyList<FoxRunRos2InterfaceContractLock> Contracts { get; }

        public static FoxRunRos2InterfaceLock Parse(string json)
        {
            try
            {
                var root = JObject.Parse(json ?? string.Empty);
                var contracts = (root["contracts"] as JArray ?? throw new FormatException("Lock contracts array is required."))
                    .Select(token => ParseContract(token as JObject ?? throw new FormatException("Lock contract must be an object.")))
                    .ToArray();
                return new FoxRunRos2InterfaceLock(
                    RequiredInt(root, "lockSchemaVersion"),
                    RequiredInt(root, "interfaceSchemaVersion"),
                    RequiredString(root, "unityPackageId"),
                    RequiredString(root, "rosPackageName"),
                    RequiredInt(root, "interfaceRevision"),
                    RequiredString(root, "generatorVersion"),
                    RequiredInt(root, "namingPolicyVersion"),
                    RequiredString(root, "interfaceDigest"),
                    contracts);
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is Newtonsoft.Json.JsonException)
            {
                throw new FormatException("FoxRun ROS2 interface lock is malformed.", exception);
            }
        }

        private static IReadOnlyList<FoxRunRos2InterfaceContractLock> NormalizeContracts(
            IReadOnlyList<FoxRunRos2InterfaceContractLock> contracts)
        {
            var ordered = (contracts ?? Array.Empty<FoxRunRos2InterfaceContractLock>())
                .Select(contract => contract ?? throw new ArgumentException("Lock contracts cannot contain null.", nameof(contracts)))
                .OrderBy(contract => contract.StableKey, StringComparer.Ordinal)
                .ToList();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var payloadContracts = new Dictionary<string, FoxRunRos2InterfaceContractLock>(StringComparer.OrdinalIgnoreCase);
            var envelopeContracts = new Dictionary<string, FoxRunRos2InterfaceContractLock>(StringComparer.OrdinalIgnoreCase);
            foreach (var contract in ordered)
            {
                if (!identities.Add(contract.StableKey))
                    throw new ArgumentException("Lock contains a duplicate contract identity.", nameof(contracts));

                if (envelopeContracts.TryGetValue(contract.PayloadMessageName, out _)
                    || payloadContracts.TryGetValue(contract.EnvelopeMessageName, out _))
                {
                    throw new ArgumentException(
                        "Lock contains a payload message name that collides with an envelope message name.",
                        nameof(contracts));
                }

                if (payloadContracts.TryGetValue(contract.PayloadMessageName, out var existingPayload))
                    EnsureSharedMessagePairIsConsistent(existingPayload, contract, nameof(contracts));
                else
                    payloadContracts.Add(contract.PayloadMessageName, contract);

                if (envelopeContracts.TryGetValue(contract.EnvelopeMessageName, out var existingEnvelope))
                    EnsureSharedMessagePairIsConsistent(existingEnvelope, contract, nameof(contracts));
                else
                    envelopeContracts.Add(contract.EnvelopeMessageName, contract);
            }

            return ordered.AsReadOnly();
        }

        private static void EnsureSharedMessagePairIsConsistent(
            FoxRunRos2InterfaceContractLock existing,
            FoxRunRos2InterfaceContractLock candidate,
            string parameterName)
        {
            if (!string.Equals(existing.PayloadMessageName, candidate.PayloadMessageName, StringComparison.Ordinal)
                || !string.Equals(existing.EnvelopeMessageName, candidate.EnvelopeMessageName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Lock contains case-colliding or inconsistently paired ROS2 message names.",
                    parameterName);
            }

            if (!string.Equals(existing.DtoIdentity, candidate.DtoIdentity, StringComparison.Ordinal)
                || !string.Equals(existing.MessageDigest, candidate.MessageDigest, StringComparison.Ordinal)
                || !string.Equals(existing.EnvelopeDigest, candidate.EnvelopeDigest, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Lock maps one ROS2 message pair to incompatible DTO or message content.",
                    parameterName);
            }
        }

        private static FoxRunRos2InterfaceContractLock ParseContract(JObject value)
            => new FoxRunRos2InterfaceContractLock(
                RequiredString(value, "declaringType"),
                RequiredString(value, "memberName"),
                RequiredString(value, "topic"),
                RequiredString(value, "dtoIdentity"),
                RequiredString(value, "payloadMessageName"),
                RequiredString(value, "envelopeMessageName"),
                RequiredString(value, "messageDigest"),
                RequiredString(value, "envelopeDigest"));

        private static string RequiredString(JObject value, string propertyName)
        {
            var token = value?[propertyName];
            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                throw new FormatException("Lock property '" + propertyName + "' is required.");
            return (string)token;
        }

        private static int RequiredInt(JObject value, string propertyName)
        {
            var token = value?[propertyName];
            if (token == null || token.Type != JTokenType.Integer)
                throw new FormatException("Lock property '" + propertyName + "' must be an integer.");
            return checked((int)token);
        }
    }
}
