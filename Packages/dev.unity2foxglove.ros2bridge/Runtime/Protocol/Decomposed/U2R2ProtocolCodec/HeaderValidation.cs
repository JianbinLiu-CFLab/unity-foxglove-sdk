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
    public static partial class U2R2ProtocolCodec
    {
        private static IReadOnlyCollection<U2R2Capability> ReadCapabilities(
            JObject header,
            U2R2Operation operation)
        {
            if (operation != U2R2Operation.Hello && operation != U2R2Operation.HelloAck)
            {
                if (header["capabilities"] != null)
                    throw InvalidFrame("Capabilities are only valid during U2R2 hello.");
                return Array.Empty<U2R2Capability>();
            }

            if (!(header["capabilities"] is JArray values) || values.Count == 0)
                throw InvalidFrame("U2R2 hello requires capabilities.");

            var result = new HashSet<U2R2Capability>();
            foreach (var value in values)
            {
                if (value.Type != JTokenType.String)
                    throw InvalidFrame("A U2R2 capability must be a string.");
                U2R2Capability capability;
                switch (value.Value<string>())
                {
                    case "publish":
                        capability = U2R2Capability.Publish;
                        break;
                    case "subscribe":
                        capability = U2R2Capability.Subscribe;
                        break;
                    default:
                        throw InvalidFrame("The U2R2 capability is unknown.");
                }
                if (!result.Add(capability))
                    throw InvalidFrame("The U2R2 capability list contains duplicates.");
            }
            return result.ToArray();
        }

        private static void ValidatePayload(U2R2Operation operation, int length)
        {
            var carriesPayload =
                operation == U2R2Operation.Publish || operation == U2R2Operation.Message;
            if (carriesPayload && length == 0)
                throw InvalidFrame("The U2R2 data operation requires a payload.");
            if (!carriesPayload && length != 0)
                throw InvalidFrame("This U2R2 operation cannot carry a payload.");
        }

        private static void ValidateXcdr1LittleEndianPayload(
            ReadOnlyMemory<byte> payload)
        {
            if (payload.Length < 4)
            {
                throw InvalidFrame(
                    "An inbound xcdr1-le message requires a four-byte encapsulation header.");
            }
            var bytes = payload.Span;
            if (bytes[0] != 0
                || bytes[1] != 1
                || bytes[2] != 0
                || bytes[3] != 0)
            {
                throw InvalidFrame(
                    "An inbound xcdr1-le message has an invalid encapsulation header.");
            }
        }

        private static void ReadResponseStatus(
            JObject header,
            U2R2Operation operation,
            bool isResponse,
            out string status,
            out string errorCode,
            out string errorMessage,
            out bool terminal)
        {
            status = string.Empty;
            errorCode = string.Empty;
            errorMessage = string.Empty;
            terminal = false;
            if (!isResponse)
            {
                if (header["status"] != null
                    || header["errorCode"] != null
                    || header["message"] != null
                    || header["terminal"] != null)
                {
                    throw InvalidFrame(
                        "Only U2R2 responses may contain response metadata.");
                }
                return;
            }

            status = RequiredString(header, "status");
            if (!string.Equals(status, "ok", StringComparison.Ordinal)
                && !string.Equals(status, "error", StringComparison.Ordinal))
            {
                throw InvalidFrame("The U2R2 response status is invalid.");
            }
            if (string.Equals(status, "ok", StringComparison.Ordinal))
            {
                if (operation == U2R2Operation.Busy
                    || operation == U2R2Operation.Fault)
                {
                    throw InvalidFrame(
                        "Busy and fault responses must have error status.");
                }
                if (header["errorCode"] != null
                    || header["message"] != null
                    || header["terminal"] != null)
                {
                    throw InvalidFrame(
                        "A successful U2R2 response cannot contain error metadata.");
                }
                return;
            }

            errorCode = RequiredString(header, "errorCode");
            errorMessage = RequiredString(header, "message");
            terminal = RequiredBoolean(header, "terminal");
            if (!StableErrors.TryGetValue(errorCode, out var rule))
                throw InvalidFrame("The U2R2 response errorCode is unknown.");
            if (terminal != rule.Terminal)
            {
                throw InvalidFrame(
                    "The U2R2 response terminal classification does not match its errorCode.");
            }
            if (!rule.ResponseOperations.Contains(operation))
                throw InvalidFrame("The U2R2 errorCode is invalid for this response operation.");
        }

        private static void ValidateHeaderValueDomain(
            JToken token,
            ulong containerDepth,
            ulong maxJsonDepth)
        {
            if (token == null)
                throw InvalidFrame("The U2R2 JSON header is invalid.");

            switch (token.Type)
            {
                case JTokenType.Object:
                    if (containerDepth > maxJsonDepth)
                        throw InvalidFrame("The U2R2 JSON header exceeds its depth limit.");
                    foreach (var property in ((JObject)token).Properties())
                    {
                        ValidateUtf8Text(property.Name, "property name");
                        ValidateHeaderValueDomain(
                            property.Value,
                            checked(containerDepth + 1),
                            maxJsonDepth);
                    }
                    return;
                case JTokenType.Array:
                    if (containerDepth > maxJsonDepth)
                        throw InvalidFrame("The U2R2 JSON header exceeds its depth limit.");
                    foreach (var value in (JArray)token)
                        ValidateHeaderValueDomain(
                            value,
                            checked(containerDepth + 1),
                            maxJsonDepth);
                    return;
                case JTokenType.Integer:
                    ReadUnsigned(token, "JSON number");
                    return;
                case JTokenType.String:
                    ValidateUtf8Text(token.Value<string>(), "string value");
                    return;
                case JTokenType.Boolean:
                case JTokenType.Null:
                    return;
                default:
                    throw InvalidFrame(
                        "U2R2 JSON values must be strings, Booleans, null, "
                        + "containers, or unsigned 64-bit integers.");
            }
        }

        private static void ValidateUtf8Text(string value, string role)
        {
            try
            {
                StrictUtf8.GetByteCount(value ?? string.Empty);
            }
            catch (EncoderFallbackException exception)
            {
                throw InvalidFrame(
                    "The U2R2 header contains a " + role + " that is not valid UTF-8.",
                    exception);
            }
        }

        private static string RequiredString(JObject header, string name)
        {
            var value = OptionalString(header, name);
            if (IsEmptyOrAsciiWhitespace(value))
                throw InvalidFrame("The U2R2 " + name + " must be a nonempty string.");
            return value;
        }

        private static string OptionalString(JObject header, string name)
        {
            var token = header[name];
            if (token == null)
                return string.Empty;
            if (token.Type != JTokenType.String)
                throw InvalidFrame("The U2R2 " + name + " must be a string.");
            var value = token.Value<string>();
            if (value.Length > 0 && IsEmptyOrAsciiWhitespace(value))
                throw InvalidFrame("The U2R2 " + name + " cannot be whitespace.");
            return value;
        }

        private static bool RequiredBoolean(JObject header, string name)
        {
            var token = header[name];
            if (token == null)
                throw InvalidFrame("The U2R2 " + name + " is required.");
            if (token.Type != JTokenType.Boolean)
                throw InvalidFrame("The U2R2 " + name + " must be a Boolean.");
            return token.Value<bool>();
        }

        private static ulong RequiredUnsigned(
            JObject header,
            string name,
            bool allowZero)
        {
            if (header[name] == null)
                throw InvalidFrame("The U2R2 " + name + " is required.");
            var value = ReadUnsigned(header[name], name);
            if (!allowZero && value == 0)
            {
                var errorCode = string.Equals(name, "requestId", StringComparison.Ordinal)
                    ? "invalid_request_id"
                    : "invalid_frame";
                throw new U2R2ProtocolException(
                    errorCode,
                    "The U2R2 " + name + " must be nonzero.");
            }
            return value;
        }

        private static ulong OptionalUnsigned(
            JObject header,
            string name,
            bool allowZero)
        {
            if (header[name] == null)
                return 0;
            return RequiredUnsigned(header, name, allowZero);
        }

        private static ulong ReadUnsigned(JToken token, string name)
        {
            if (token.Type != JTokenType.Integer)
                throw InvalidFrame("The U2R2 " + name + " must be an unsigned integer.");
            try
            {
                var rawValue = ((JValue)token).Value;
                if (rawValue is BigInteger bigInteger)
                {
                    if (bigInteger < BigInteger.Zero
                        || bigInteger > new BigInteger(ulong.MaxValue))
                    {
                        throw new OverflowException();
                    }
                    return (ulong)bigInteger;
                }
                return Convert.ToUInt64(rawValue, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (
                exception is OverflowException
                || exception is InvalidCastException
                || exception is FormatException)
            {
                throw InvalidFrame("The U2R2 " + name + " must be an unsigned integer.", exception);
            }
        }

        private static U2R2ProtocolException InvalidFrame(
            string message,
            Exception innerException = null)
            => new U2R2ProtocolException("invalid_frame", message, true, innerException);

        private static bool IsEmptyOrAsciiWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;
            for (var index = 0; index < value.Length; index++)
            {
                switch (value[index])
                {
                    case ' ':
                    case '\t':
                    case '\r':
                    case '\n':
                        continue;
                    default:
                        return false;
                }
            }
            return true;
        }

    }
}
