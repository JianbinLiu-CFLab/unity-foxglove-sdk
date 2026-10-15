// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Generated
// Purpose: Manager-local FoxRun transport Provider for the ROS 2 sidecar.

using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity2Foxglove.Ros2Bridge
{

#if UNITY_5_3_OR_NEWER
    public sealed partial class Ros2BridgeTransportProvider
    {
        private sealed class GeneratedSourceRegistration :
            IDisposable
        {
            private IFoxRunBridgeGeneratedSubscribeSource _source;
            private readonly ulong _generation;
            private readonly List<
                IFoxRunTransportSubscriptionLease> _leases;
            private readonly List<int> _publishTopicIndexes;

            private GeneratedSourceRegistration(
                IFoxRunBridgeGeneratedSubscribeSource source,
                ulong generation,
                List<IFoxRunTransportSubscriptionLease> leases,
                List<int> publishTopicIndexes)
            {
                _source = source;
                _generation = generation;
                _leases = leases;
                _publishTopicIndexes = publishTopicIndexes;
            }

            internal static bool TryCreate(
                Session session,
                IFoxRunBridgeGeneratedSubscribeSource source,
                out GeneratedSourceRegistration registration,
                out string reason)
            {
                registration = null;
                reason = string.Empty;
                if (session == null || source == null)
                {
                    reason = "The generated Bridge subscription source is unavailable.";
                    return false;
                }
                var leases = new List<
                    IFoxRunTransportSubscriptionLease>();
                var publishTopicIndexes = new List<int>();
                try
                {
                    var bindingCount =
                        source.FoxRunBridge_SubscribeBindingCount;
                    if (bindingCount <= 0)
                    {
                        reason =
                            "The generated Bridge subscription source has no bindings.";
                        return false;
                    }
                    if (checked((ulong)bindingCount)
                        > Protocol.U2R2ProtocolLimits.Default.MaxContracts)
                    {
                        reason =
                            "The generated Bridge subscription source exceeds the contract bound.";
                        return false;
                    }
                    for (var bindingIndex = 0;
                         bindingIndex
                         < bindingCount;
                         bindingIndex++)
                    {
                        if (!source.FoxRunBridge_TryGetSubscribeBinding(
                                bindingIndex,
                                out var binding,
                                out reason))
                        {
                            return false;
                        }
                        if (!IsValidBinding(binding, out reason))
                            return false;
                        var capturedBindingIndex = binding.BindingIndex;
                        var route = new FoxRunTransportSubscribeRoute(
                            binding.StableMemberId,
                            binding.Topic,
                            binding.CanonicalRosType,
                            binding.MaxPayloadBytes,
                            binding.DeliveryPolicy,
                            (payload, receiveTimeNs, sequence) =>
                            {
                                if (!source.FoxRunBridge_TryDecodeAndApply(
                                        capturedBindingIndex,
                                        payload,
                                        ProviderId,
                                        session.Generation,
                                        markRemoteOwned: true,
                                        out var decodeReason))
                                {
                                    throw new InvalidDataException(
                                        Bound(decodeReason));
                                }
                            },
                            binding.MessageEncoding);
                        var result = session.Subscribe(in route);
                        if (result.State
                            != FoxRunTransportRouteResultState.Accepted
                            || result.Lease == null)
                        {
                            reason = result.Reason;
                            return false;
                        }
                        leases.Add(result.Lease);
                        if (binding.PublishTopicIndex >= 0)
                        {
                            publishTopicIndexes.Add(
                                binding.PublishTopicIndex);
                        }
                    }

                    registration = new GeneratedSourceRegistration(
                        source,
                        session.Generation,
                        leases,
                        publishTopicIndexes);
                    leases = null;
                    reason = string.Empty;
                    return true;
                }
                finally
                {
                    if (leases != null)
                    {
                        var cleanupError = Ros2BridgeCleanup.RunAll(
                            leases.Count,
                            index => leases[index]?.Dispose(),
                            reverse: true);
                        if (cleanupError != null)
                            reason = Bound(cleanupError.Message);
                    }
                }
            }

            public void Dispose()
            {
                var source = _source;
                _source = null;
                var first = Ros2BridgeCleanup.RunAll(
                    _leases.Count,
                    index => _leases[index]?.Dispose(),
                    reverse: true);
                _leases.Clear();
                if (source != null)
                {
                    var ownershipError = Ros2BridgeCleanup.RunAll(
                        _publishTopicIndexes.Count,
                        index => source.FoxRunBridge_ReleaseRemoteOwnership(
                            _publishTopicIndexes[index],
                            ProviderId,
                            _generation));
                    first ??= ownershipError;
                }
                _publishTopicIndexes.Clear();
                if (first != null)
                    throw first;
            }

            private static bool IsValidBinding(
                FoxRunBridgeGeneratedSubscribeBinding binding,
                out string reason)
            {
                if (binding.BindingIndex < 0
                    || string.IsNullOrWhiteSpace(binding.StableMemberId)
                    || string.IsNullOrWhiteSpace(binding.Topic)
                    || string.IsNullOrWhiteSpace(binding.CanonicalRosType)
                    || !string.Equals(
                        binding.MessageEncoding,
                        "cdr",
                        StringComparison.Ordinal)
                    || binding.MaxPayloadBytes <= 0
                    || binding.MaxPayloadBytes
                    > Ros2BridgeFrameWriter.MaxPayloadBytes
                    || !IsSha256(binding.SchemaSha256))
                {
                    reason =
                        "The generated Bridge subscription binding is incomplete or invalid.";
                    return false;
                }
                reason = string.Empty;
                return true;
            }

            private static bool IsSha256(string value)
            {
                if (value == null || value.Length != 64)
                    return false;
                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    if (character < '0'
                        || character > '9'
                        && (character < 'a' || character > 'f'))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private sealed class CompositeMcapDecoderFactory :
            IStableMcapMessageDecoderFactory
        {
            private readonly System.Collections.Generic.IReadOnlyList<
                IMcapMessageDecoderFactory> _factories;

            internal CompositeMcapDecoderFactory(
                System.Collections.Generic.IReadOnlyList<
                    IMcapMessageDecoderFactory> factories)
            {
                _factories = factories
                             ?? throw new ArgumentNullException(nameof(factories));
            }

            public string StableDecoderId => ProviderId + "/mcap-cdr-v1";

            public IMcapMessageDecoder TryCreate(
                McapSchema schema,
                McapChannel channel)
            {
                for (var i = 0; i < _factories.Count; i++)
                {
                    var decoder = _factories[i]?.TryCreate(schema, channel);
                    if (decoder != null)
                        return decoder;
                }

                return null;
            }
        }
    }
#endif
}
