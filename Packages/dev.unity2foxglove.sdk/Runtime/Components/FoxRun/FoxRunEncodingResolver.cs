// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Converts concrete FoxRun Foxglove encodings to protocol spelling.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Pure helpers for concrete Foxglove encoding values.</summary>
    public static class FoxRunEncodingResolver
    {
        /// <summary>
        /// Resolves the internal zero omission sentinel against one concrete
        /// directional profile default.
        /// </summary>
        public static FoxRunEncoding Resolve(
            FoxRunEncoding declaredEncoding,
            FoxRunEncoding profileDefault)
        {
            if (declaredEncoding == 0)
                return ValidateProfileDefault(profileDefault);
            return ValidateProfileDefault(declaredEncoding);
        }

        /// <summary>
        /// Resolves the internal zero omission sentinel for one flow. A
        /// full-duplex caller must resolve its two directions independently.
        /// </summary>
        public static FoxRunEncoding Resolve(
            FoxRunEncoding declaredEncoding,
            FoxRunFlow mode,
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscribeDefault)
        {
            if (declaredEncoding != 0)
                return ValidateProfileDefault(declaredEncoding);

            switch (mode)
            {
                case FoxRunFlow.Publish:
                    return ValidateProfileDefault(publishDefault);
                case FoxRunFlow.Subscribe:
                    return ValidateProfileDefault(subscribeDefault);
                case FoxRunFlow.PublishAndSubscribe:
                    throw new System.ArgumentException(
                        "Full-duplex omitted Encoding must be resolved per direction.",
                        nameof(declaredEncoding));
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(mode));
            }
        }

        /// <summary>Validates a concrete directional profile default.</summary>
        public static FoxRunEncoding ValidateProfileDefault(FoxRunEncoding encoding)
        {
            if (encoding == FoxRunEncoding.Protobuf
                || encoding == FoxRunEncoding.JSON
                || encoding == FoxRunEncoding.MessagePack)
                return encoding;

            throw new ArgumentOutOfRangeException(
                nameof(encoding),
                "FoxRun profile encoding must be Protobuf, JSON, or MessagePack.");
        }

        /// <summary>Returns the Foxglove protocol spelling.</summary>
        public static string ToProtocolEncoding(FoxRunEncoding encoding)
        {
            switch (encoding)
            {
                case FoxRunEncoding.Protobuf: return "protobuf";
                case FoxRunEncoding.JSON: return "json";
                case FoxRunEncoding.MessagePack: return "msgpack";
                default: throw new ArgumentOutOfRangeException(nameof(encoding));
            }
        }

        /// <summary>Parses a concrete Foxglove protocol encoding.</summary>
        public static FoxRunEncoding FromProtocolEncoding(string encoding)
        {
            if (string.Equals(encoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return FoxRunEncoding.Protobuf;
            if (string.Equals(encoding, "json", StringComparison.OrdinalIgnoreCase))
                return FoxRunEncoding.JSON;
            if (string.Equals(encoding, "msgpack", StringComparison.OrdinalIgnoreCase))
                return FoxRunEncoding.MessagePack;

            throw new ArgumentException(
                "Unsupported FoxRun encoding: " + (encoding ?? string.Empty),
                nameof(encoding));
        }
    }
}
