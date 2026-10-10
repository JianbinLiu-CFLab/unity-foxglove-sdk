// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Strict, canonical U2R2 envelope and v2 header codec.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public static class U2R2LegacyV1Codec
    {
        public static U2R2LegacyV1Message ParseFirstFrame(byte[] frameBytes)
        {
            var frame = U2R2ProtocolCodec.DecodeFrame(frameBytes);
            var operation = RequiredLegacyString(frame.Header, "op");
            switch (operation)
            {
                case "health_ping":
                case "prepare_publisher":
                    var version = RequiredLegacyInteger(frame.Header, "protocolVersion");
                    if (version != 1)
                        throw InvalidLegacy("A legacy control frame requires protocolVersion 1.");
                    var requestId = RequiredLegacyString(frame.Header, "requestId");
                    if (requestId.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw InvalidLegacy("A legacy requestId cannot contain line breaks.");
                    return new U2R2LegacyV1Message(
                        operation,
                        requestId,
                        operation == "prepare_publisher");
                case "publish":
                    return new U2R2LegacyV1Message(operation, string.Empty, true);
                default:
                    throw InvalidLegacy(
                        "The first legacy U2R2 frame must be health_ping, prepare_publisher, or publish.");
            }
        }

        private static string RequiredLegacyString(JObject header, string name)
        {
            var token = header[name];
            if (token == null || token.Type != JTokenType.String)
                throw InvalidLegacy("The legacy U2R2 " + name + " must be a string.");
            var value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
                throw InvalidLegacy("The legacy U2R2 " + name + " must be nonempty.");
            return value;
        }

        private static long RequiredLegacyInteger(JObject header, string name)
        {
            var token = header[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw InvalidLegacy("The legacy U2R2 " + name + " must be an integer.");
            try
            {
                return token.Value<long>();
            }
            catch (Exception exception) when (
                exception is OverflowException
                || exception is InvalidCastException
                || exception is FormatException)
            {
                throw InvalidLegacy("The legacy U2R2 " + name + " must be an integer.", exception);
            }
        }

        private static U2R2ProtocolException InvalidLegacy(
            string message,
            Exception innerException = null)
            => new U2R2ProtocolException("invalid_frame", message, true, innerException);
    }
}
