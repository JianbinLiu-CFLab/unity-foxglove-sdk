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
        public static void ValidateResponseCorrelation(
            U2R2ResponseExpectation expected,
            U2R2Message response)
        {
            if (expected == null)
                throw new ArgumentNullException(nameof(expected));
            if (response == null || !response.IsResponse)
                throw InvalidFrame("The correlated U2R2 frame is not a response.");
            if (!expected.AllowedResponseOperations.Contains(response.Operation))
            {
                throw new U2R2ProtocolException(
                    "response_mismatch",
                    "The U2R2 response operation is not valid for its request.");
            }

            var isSuccess =
                response.Operation == expected.SuccessResponseOperation
                && string.Equals(response.Status, "ok", StringComparison.Ordinal);
            bool identityMatches;
            if (expected.AssignsSessionIdentity)
            {
                identityMatches = isSuccess
                    ? response.SessionId.Length > 0
                      && response.ConnectionGeneration != 0
                    : response.SessionId.Length == 0
                      && response.ConnectionGeneration == 0;
            }
            else
            {
                identityMatches = string.Equals(
                                      response.SessionId,
                                      expected.SessionId,
                                      StringComparison.Ordinal)
                                  && response.ConnectionGeneration
                                  == expected.ConnectionGeneration;
            }
            var identifiersMatch = isSuccess
                ? response.ContractId == expected.ContractId
                  && response.MessageId == expected.MessageId
                : response.ContractId == 0 && response.MessageId == 0;
            if (response.RequestId != expected.RequestId
                || !identityMatches
                || !identifiersMatch)
            {
                throw new U2R2ProtocolException(
                    "response_mismatch",
                    "The U2R2 response does not match its exact request context.");
            }
        }

        internal static JObject ParseStrictV1Object(
            byte[] bytes,
            int offset,
            int count,
            string context)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            if (offset < 0 || count < 0 || offset > bytes.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));
            context = string.IsNullOrWhiteSpace(context)
                ? "U2R2 v1"
                : context;

            string json;
            try
            {
                json = StrictUtf8.GetString(bytes, offset, count);
            }
            catch (DecoderFallbackException exception)
            {
                throw new FormatException(
                    context + " JSON UTF-8 is invalid.",
                    exception);
            }

            try
            {
                return ParseStrictObject(
                    json,
                    U2R2ProtocolLimits.Default.MaxJsonDepth);
            }
            catch (U2R2ProtocolException exception)
            {
                throw new FormatException(
                    context + " JSON is invalid: " + exception.Message,
                    exception);
            }
        }

        private static JObject ParseStrictObject(
            string json,
            ulong maxJsonDepth)
        {
            try
            {
                Rfc8259JsonValidator.Validate(json, maxJsonDepth);
                using var textReader = new StringReader(json);
                using var reader = new JsonTextReader(textReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = maxJsonDepth > int.MaxValue
                        ? int.MaxValue
                        : checked((int)maxJsonDepth),
                    SupportMultipleContent = true,
                };
                if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                    throw InvalidFrame("The U2R2 JSON header must be an object.");

                var header = JObject.Load(
                    reader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    });
                if (reader.Read())
                    throw InvalidFrame("The U2R2 JSON header has trailing content.");
                return header;
            }
            catch (U2R2ProtocolException)
            {
                throw;
            }
            catch (JsonException exception)
                when (exception.Message.IndexOf(
                          "already exists",
                          StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw InvalidFrame(
                    "The U2R2 JSON header contains a duplicate property.",
                    exception);
            }
            catch (JsonException exception)
            {
                throw InvalidFrame("The U2R2 JSON header is invalid.", exception);
            }
        }

        private static JToken Canonicalize(JToken token)
        {
            if (token is JObject valueObject)
            {
                var result = new JObject();
                foreach (var property in valueObject.Properties()
                             .OrderBy(property => property.Name, Utf8ByteOrdinalComparer))
                {
                    result.Add(property.Name, Canonicalize(property.Value));
                }
                return result;
            }
            if (token is JArray valueArray)
                return new JArray(valueArray.Select(Canonicalize));
            return token.DeepClone();
        }

        private static string SerializeCanonicalHeader(JToken header)
        {
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new JsonTextWriter(text)
                   {
                       Formatting = Formatting.None,
                       StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
                       Culture = CultureInfo.InvariantCulture,
                   })
            {
                header.WriteTo(writer);
            }
            return text.ToString();
        }

        private static int CompareUtf8ByteOrdinal(string left, string right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            byte[] leftBytes;
            byte[] rightBytes;
            try
            {
                leftBytes = StrictUtf8.GetBytes(left);
                rightBytes = StrictUtf8.GetBytes(right);
            }
            catch (EncoderFallbackException exception)
            {
                throw InvalidFrame(
                    "The U2R2 header contains a property name that is not valid UTF-8.",
                    exception);
            }

            var commonLength = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < commonLength; index++)
            {
                var comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0)
                    return comparison;
            }
            return leftBytes.Length.CompareTo(rightBytes.Length);
        }

    }
}
