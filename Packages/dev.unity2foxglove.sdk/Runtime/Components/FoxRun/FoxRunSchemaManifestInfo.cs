// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTO for generated Provider-neutral FoxRun metadata.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunSchemaManifestInfo
    {
        public int ManifestVersion { get; }
        public string PackageName { get; }
        public string GeneratorName { get; }
        public int GeneratorMajorVersion { get; }
        public string GlobalManifestHash { get; }
        public string FoxRunManifestHash { get; }
        public IReadOnlyList<FoxRunSchemaTypeInfo> Types { get; }
        public string SubscriptionManifestHash { get; }
        public IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo>
            SubscriptionBindings { get; }
        public int TypeCount { get; }
        public int ContractCount { get; }
        public int FieldCount { get; }

        public FoxRunSchemaManifestInfo(
            int manifestVersion,
            string packageName,
            string generatorName,
            int generatorMajorVersion,
            string globalManifestHash,
            string foxRunManifestHash,
            IReadOnlyList<FoxRunSchemaTypeInfo> types,
            string subscriptionManifestHash = "",
            IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo>
                subscriptionBindings = null)
        {
            ManifestVersion = manifestVersion;
            PackageName = packageName ?? string.Empty;
            GeneratorName = generatorName ?? string.Empty;
            GeneratorMajorVersion = generatorMajorVersion;
            GlobalManifestHash = globalManifestHash ?? string.Empty;
            FoxRunManifestHash = foxRunManifestHash ?? string.Empty;
            Types = new List<FoxRunSchemaTypeInfo>(
                    types ?? Array.Empty<FoxRunSchemaTypeInfo>())
                .AsReadOnly();
            SubscriptionManifestHash =
                subscriptionManifestHash ?? string.Empty;
            SubscriptionBindings = new List<
                    FoxRunSchemaSubscriptionBindingInfo>(
                    subscriptionBindings
                    ?? Array.Empty<
                        FoxRunSchemaSubscriptionBindingInfo>())
                .AsReadOnly();
            TypeCount = Types.Count;
            ContractCount = Types
                .Where(type => type != null)
                .Sum(type => type.Contracts.Count);
            FieldCount = Types
                .Where(type => type != null)
                .SelectMany(type => type.Contracts)
                .Where(contract => contract != null)
                .Sum(contract => contract.Fields.Count);
        }
    }

    public sealed class FoxRunSchemaSubscriptionBindingInfo
    {
        public FoxRunSchemaSubscriptionBindingInfo(
            string declaringType,
            string memberName,
            string topic,
            string flow,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId,
            string reliability,
            string durability,
            string history,
            int depth,
            bool supportsWebSocket,
            bool isStream = false)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Flow = flow ?? string.Empty;
            PublishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    publishTransportIds
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId;
            Reliability = reliability ?? "inherit";
            Durability = durability ?? "inherit";
            History = history ?? "inherit";
            Depth = depth;
            SupportsWebSocket = supportsWebSocket;
            IsStream = isStream;
        }

        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string Flow { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
        public string Reliability { get; }
        public string Durability { get; }
        public string History { get; }
        public int Depth { get; }
        public bool SupportsWebSocket { get; }
        public bool IsStream { get; }
    }
}
