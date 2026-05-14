// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Shared publisher encoding policy used by Inspector UI and runtime
// publish helpers. Kept UnityEngine-free so policy can be unit tested.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Global default publisher encoding configured on <see cref="FoxgloveManager"/>.
    /// </summary>
    public enum GlobalEncoding
    {
        Json,
        Protobuf
    }

    /// <summary>
    /// Per-publisher encoding override policy.
    /// </summary>
    public enum PublisherEncodingOverride
    {
        UseManager,
        Json,
        Protobuf
    }

    /// <summary>
    /// Resolved encoding that a publisher will actually emit.
    /// </summary>
    public enum PublisherEffectiveEncoding
    {
        Json,
        Protobuf,
        Unsupported
    }

    /// <summary>
    /// Resolved publisher encoding request and fallback state.
    /// </summary>
    public readonly struct PublisherEncodingResolution
    {
        /// <summary>
        /// Creates a resolved publisher encoding result.
        /// </summary>
        public PublisherEncodingResolution(
            PublisherEffectiveEncoding requested,
            PublisherEffectiveEncoding effective,
            bool fellBack)
        {
            Requested = requested;
            Effective = effective;
            FellBack = fellBack;
        }

        public PublisherEffectiveEncoding Requested { get; }
        public PublisherEffectiveEncoding Effective { get; }
        public bool FellBack { get; }
        public bool IsSupported => Effective != PublisherEffectiveEncoding.Unsupported;
        public string RequestedLabel => PublisherEncodingPolicy.ToProtocolEncoding(Requested);
        public string EffectiveLabel => PublisherEncodingPolicy.ToProtocolEncoding(Effective);
    }

    /// <summary>
    /// Resolves global manager defaults, optional publisher overrides, and
    /// publisher capabilities into one effective wire encoding.
    /// </summary>
    public static class PublisherEncodingPolicy
    {
        /// <summary>
        /// Resolves the effective wire encoding from manager defaults, publisher
        /// overrides, and the publisher's supported serialization formats.
        /// </summary>
        public static PublisherEncodingResolution Resolve(
            GlobalEncoding managerDefault,
            bool allowPublisherOverride,
            PublisherEncodingOverride publisherOverride,
            bool supportsJson,
            bool supportsProtobuf)
        {
            var requested = ResolveRequested(managerDefault, allowPublisherOverride, publisherOverride);
            if (Supports(requested, supportsJson, supportsProtobuf))
                return new PublisherEncodingResolution(requested, requested, fellBack: false);

            var fallback = supportsJson
                ? PublisherEffectiveEncoding.Json
                : supportsProtobuf
                    ? PublisherEffectiveEncoding.Protobuf
                    : PublisherEffectiveEncoding.Unsupported;

            return new PublisherEncodingResolution(requested, fallback, fellBack: true);
        }

        /// <summary>
        /// Converts a resolved encoding value to the Foxglove protocol label.
        /// </summary>
        public static string ToProtocolEncoding(PublisherEffectiveEncoding encoding)
        {
            switch (encoding)
            {
                case PublisherEffectiveEncoding.Json:
                    return "json";
                case PublisherEffectiveEncoding.Protobuf:
                    return "protobuf";
                default:
                    return "unsupported";
            }
        }

        private static PublisherEffectiveEncoding ResolveRequested(
            GlobalEncoding managerDefault,
            bool allowPublisherOverride,
            PublisherEncodingOverride publisherOverride)
        {
            if (allowPublisherOverride)
            {
                if (publisherOverride == PublisherEncodingOverride.Json)
                    return PublisherEffectiveEncoding.Json;
                if (publisherOverride == PublisherEncodingOverride.Protobuf)
                    return PublisherEffectiveEncoding.Protobuf;
            }

            return managerDefault == GlobalEncoding.Protobuf
                ? PublisherEffectiveEncoding.Protobuf
                : PublisherEffectiveEncoding.Json;
        }

        private static bool Supports(
            PublisherEffectiveEncoding requested,
            bool supportsJson,
            bool supportsProtobuf)
        {
            return requested == PublisherEffectiveEncoding.Json
                ? supportsJson
                : requested == PublisherEffectiveEncoding.Protobuf && supportsProtobuf;
        }
    }
}
