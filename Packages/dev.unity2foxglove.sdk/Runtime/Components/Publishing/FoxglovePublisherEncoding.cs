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
        Json = 0,
        Protobuf = 1,
        Ros2 = 2,
        MsgPack = 3
    }

    /// <summary>
    /// Per-publisher encoding override policy.
    /// </summary>
    public enum PublisherEncodingOverride
    {
        UseManager = 0,
        Json = 1,
        Protobuf = 2,
        Ros2 = 3,
        MsgPack = 4
    }

    /// <summary>
    /// Resolved encoding that a publisher will actually emit.
    /// </summary>
    public enum PublisherEffectiveEncoding
    {
        Json = 0,
        Protobuf = 1,
        Unsupported = 2,
        Ros2 = 3,
        MsgPack = 4
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
        public string RequestedLabel => PublisherEncodingPolicy.ToDisplayEncoding(Requested);
        public string EffectiveLabel => PublisherEncodingPolicy.ToDisplayEncoding(Effective);
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
            => Resolve(
                managerDefault,
                allowPublisherOverride,
                publisherOverride,
                supportsJson,
                supportsProtobuf,
                supportsRos2: false,
                supportsMsgPack: false);

        /// <summary>
        /// Resolves the effective wire encoding from manager defaults, publisher
        /// overrides, and the publisher's supported serialization formats.
        /// </summary>
        public static PublisherEncodingResolution Resolve(
            GlobalEncoding managerDefault,
            bool allowPublisherOverride,
            PublisherEncodingOverride publisherOverride,
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2)
            => Resolve(
                managerDefault,
                allowPublisherOverride,
                publisherOverride,
                supportsJson,
                supportsProtobuf,
                supportsRos2,
                supportsMsgPack: false);

        /// <summary>
        /// Resolves the effective wire encoding from manager defaults, publisher
        /// overrides, and the publisher's supported serialization formats.
        /// </summary>
        public static PublisherEncodingResolution Resolve(
            GlobalEncoding managerDefault,
            bool allowPublisherOverride,
            PublisherEncodingOverride publisherOverride,
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2,
            bool supportsMsgPack)
        {
            var requested = ResolveRequested(managerDefault, allowPublisherOverride, publisherOverride);
            if (Supports(requested, supportsJson, supportsProtobuf, supportsRos2, supportsMsgPack))
                return new PublisherEncodingResolution(requested, requested, fellBack: false);

            var fallback = FirstSupported(supportsJson, supportsProtobuf, supportsRos2, supportsMsgPack);

            return new PublisherEncodingResolution(requested, fallback, fellBack: true);
        }

        /// <summary>
        /// Converts a resolved encoding value to the user-facing product label.
        /// </summary>
        public static string ToDisplayEncoding(PublisherEffectiveEncoding encoding)
        {
            switch (encoding)
            {
                case PublisherEffectiveEncoding.Json:
                    return "JSON";
                case PublisherEffectiveEncoding.Protobuf:
                    return "Protobuf";
                case PublisherEffectiveEncoding.Ros2:
                    return "ROS2";
                case PublisherEffectiveEncoding.MsgPack:
                    return "MsgPack";
                default:
                    return "unsupported";
            }
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
                case PublisherEffectiveEncoding.Ros2:
                    return "cdr";
                case PublisherEffectiveEncoding.MsgPack:
                    return "msgpack";
                default:
                    return "unsupported";
            }
        }

        /// <summary>
        /// Converts a resolved encoding value to the Foxglove schemaEncoding label.
        /// </summary>
        public static string ToSchemaEncoding(PublisherEffectiveEncoding encoding)
        {
            switch (encoding)
            {
                case PublisherEffectiveEncoding.Json:
                    return "jsonschema";
                case PublisherEffectiveEncoding.Protobuf:
                    return "protobuf";
                case PublisherEffectiveEncoding.Ros2:
                    return "ros2msg";
                case PublisherEffectiveEncoding.MsgPack:
                    return "";
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
                if (publisherOverride == PublisherEncodingOverride.Ros2)
                    return PublisherEffectiveEncoding.Ros2;
                if (publisherOverride == PublisherEncodingOverride.MsgPack)
                    return PublisherEffectiveEncoding.MsgPack;
            }

            switch (managerDefault)
            {
                case GlobalEncoding.Protobuf:
                    return PublisherEffectiveEncoding.Protobuf;
                case GlobalEncoding.Ros2:
                    return PublisherEffectiveEncoding.Ros2;
                case GlobalEncoding.MsgPack:
                    return PublisherEffectiveEncoding.MsgPack;
                case GlobalEncoding.Json:
                default:
                    return PublisherEffectiveEncoding.Json;
            }
        }

        private static bool Supports(
            PublisherEffectiveEncoding requested,
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2,
            bool supportsMsgPack)
        {
            switch (requested)
            {
                case PublisherEffectiveEncoding.Json:
                    return supportsJson;
                case PublisherEffectiveEncoding.Protobuf:
                    return supportsProtobuf;
                case PublisherEffectiveEncoding.Ros2:
                    return supportsRos2;
                case PublisherEffectiveEncoding.MsgPack:
                    return supportsMsgPack;
                default:
                    return false;
            }
        }

        private static PublisherEffectiveEncoding FirstSupported(
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2,
            bool supportsMsgPack)
        {
            if (supportsProtobuf) return PublisherEffectiveEncoding.Protobuf;
            // Fallback order intentionally keeps MsgPack before JSON for publishers
            // that opt into compact custom-client raw channels.
            if (supportsMsgPack) return PublisherEffectiveEncoding.MsgPack;
            if (supportsJson) return PublisherEffectiveEncoding.Json;
            if (supportsRos2) return PublisherEffectiveEncoding.Ros2;
            return PublisherEffectiveEncoding.Unsupported;
        }
    }
}
