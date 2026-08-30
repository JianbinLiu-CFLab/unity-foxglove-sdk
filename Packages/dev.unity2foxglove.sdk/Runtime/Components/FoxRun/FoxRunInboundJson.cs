// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Bounded, non-polymorphic JSON decoding for generated FoxRun inputs.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public static class FoxRunInboundJson
    {
        private const int MaxTypeHintScanDepth = 32;

        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true);

        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        };

        private static readonly JsonSerializerSettings GeneratedObjectSettings =
            new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                Formatting = Formatting.None,
                MaxDepth = MaxTypeHintScanDepth,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error,
                ReferenceLoopHandling = ReferenceLoopHandling.Error,
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii,
                TypeNameHandling = TypeNameHandling.None,
                Converters = { new NonFiniteFloatJsonConverter() }
            };

        /// <remarks>
        /// This parser is intended for low-frequency FoxRun control inputs. It decodes UTF-8
        /// into a managed string and builds a JToken tree once per TryRead call.
        /// </remarks>
        private static bool TryToken(
            byte[] payload,
            string field,
            out JToken token,
            out string error,
            bool rejectUnknownRootProperties = false)
        {
            token = null;
            error = string.Empty;
            if (payload == null || payload.Length == 0)
            {
                error = "FoxRun inbound payload is empty.";
                return false;
            }

            try
            {
                var json = StrictUtf8.GetString(payload);
                var root = ParseToken(json);
                if (ContainsForbiddenTypeHint(root, 0, out var typeHintError))
                {
                    error = typeHintError;
                    return false;
                }

                if (!(root is JObject obj))
                {
                    error = "FoxRun inbound payload is missing field '" + field + "'.";
                    return false;
                }

                if (!obj.TryGetValue(field, StringComparison.Ordinal, out token))
                {
                    error = "FoxRun inbound payload is missing field '" + field + "'.";
                    return false;
                }

                if (rejectUnknownRootProperties)
                {
                    foreach (var property in obj.Properties())
                    {
                        if (!string.Equals(property.Name, field, StringComparison.Ordinal))
                        {
                            error = "FoxRun inbound payload contains unknown field '"
                                    + property.Name + "'.";
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                var detail = ex is DecoderFallbackException
                    ? "Payload is not valid UTF-8."
                    : ex.Message;
                if (ex is JsonReaderException
                    && detail.IndexOf("MaxDepth", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detail = "JSON nesting exceeds the explicit depth limit.";
                }
                error = "FoxRun inbound JSON is invalid: " + detail;
                return false;
            }
        }

        private static JToken ParseToken(string json)
        {
            using (var textReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(textReader)
                   {
                       DateParseHandling = DateParseHandling.None,
                       MaxDepth = MaxTypeHintScanDepth
                   })
            {
                var root = JToken.ReadFrom(jsonReader, LoadSettings);
                if (jsonReader.Read())
                    throw new JsonReaderException(
                        "FoxRun inbound JSON contains more than one root value.");
                return root;
            }
        }

        private static bool ContainsForbiddenTypeHint(JToken token, int depth, out string error)
        {
            error = string.Empty;
            if (depth > MaxTypeHintScanDepth)
            {
                error = "FoxRun inbound payload nesting exceeds the maximum supported depth.";
                return true;
            }

            if (token is JObject obj)
            {
                foreach (var property in obj.Properties())
                {
                    if (string.Equals(property.Name, "$type", StringComparison.Ordinal))
                    {
                        error = "FoxRun inbound payload contains a forbidden $type hint.";
                        return true;
                    }
                    if (ContainsForbiddenTypeHint(property.Value, depth + 1, out error))
                        return true;
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array)
                    if (ContainsForbiddenTypeHint(item, depth + 1, out error))
                        return true;
            }
            return false;
        }

        private static bool TryScalar<T>(byte[] payload, string field, JTokenType expected, out T value, out string error)
        {
            value = default;
            if (!TryToken(
                    payload,
                    field,
                    out var token,
                    out error,
                    rejectUnknownRootProperties: true))
                return false;
            if (token.Type != expected && !(expected == JTokenType.Float && token.Type == JTokenType.Integer))
            {
                error = "FoxRun inbound field '" + field + "' has the wrong JSON type.";
                return false;
            }
            try
            {
                value = token.Value<T>();
                if (!IsFinite(value))
                {
                    value = default;
                    error = "FoxRun inbound field '" + field + "' must be finite.";
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)
            {
                error = "FoxRun inbound field '" + field + "' cannot be converted: " + ex.Message;
                return false;
            }
        }

        public static bool TryRead(byte[] payload, string field, out string value, out string error) =>
            TryScalar(payload, field, JTokenType.String, out value, out error);
        public static bool TryRead(byte[] payload, string field, out bool value, out string error) =>
            TryScalar(payload, field, JTokenType.Boolean, out value, out error);
        public static bool TryRead(byte[] payload, string field, out byte value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out sbyte value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out short value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out ushort value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out int value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out uint value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out long value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out ulong value, out string error) =>
            TryScalar(payload, field, JTokenType.Integer, out value, out error);
        public static bool TryRead(byte[] payload, string field, out float value, out string error) =>
            TryScalar(payload, field, JTokenType.Float, out value, out error);
        public static bool TryRead(byte[] payload, string field, out double value, out string error) =>
            TryScalar(payload, field, JTokenType.Float, out value, out error);
        public static bool TryRead(byte[] payload, string field, out decimal value, out string error) =>
            TryScalar(payload, field, JTokenType.Float, out value, out error);
        public static bool TryRead(byte[] payload, string field, out char value, out string error)
        {
            value = default;
            if (!TryScalar(payload, field, JTokenType.String, out string text, out error))
                return false;
            if (text != null && text.Length == 1)
            {
                value = text[0];
                return true;
            }

            error = "FoxRun inbound field '" + field + "' must be a single character.";
            return false;
        }

        public static bool TryRead(byte[] payload, string field, out Vector2 value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && RejectUnknownProperties(obj, out error, "x", "y")
                && TryNumber(obj, "x", out value.x, out error)
                && TryNumber(obj, "y", out value.y, out error);
        }

        public static bool TryRead(byte[] payload, string field, out Vector3 value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && RejectUnknownProperties(obj, out error, "x", "y", "z")
                && TryNumber(obj, "x", out value.x, out error)
                && TryNumber(obj, "y", out value.y, out error)
                && TryNumber(obj, "z", out value.z, out error);
        }

        public static bool TryRead(byte[] payload, string field, out Quaternion value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && RejectUnknownProperties(obj, out error, "x", "y", "z", "w")
                && TryNumber(obj, "x", out value.x, out error)
                && TryNumber(obj, "y", out value.y, out error)
                && TryNumber(obj, "z", out value.z, out error)
                && TryNumber(obj, "w", out value.w, out error);
        }

        public static bool TryRead(byte[] payload, string field, out Color value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && RejectUnknownProperties(obj, out error, "r", "g", "b", "a")
                && TryNumber(obj, "r", out value.r, out error)
                && TryNumber(obj, "g", out value.g, out error)
                && TryNumber(obj, "b", out value.b, out error)
                && TryNumber(obj, "a", out value.a, out error);
        }

        /// <summary>
        /// Decodes a generator-validated DTO shape without enabling polymorphic
        /// type metadata. The source generator emits this call only for a
        /// statically inspected object or enum graph.
        /// </summary>
        public static bool TryReadObject<T>(
            byte[] payload,
            string field,
            out T value,
            out string error)
        {
            value = default;
            if (!TryToken(payload, field, out var token, out error, rejectUnknownRootProperties: true))
                return false;

            try
            {
                var serializer = JsonSerializer.Create(GeneratedObjectSettings);
                using (var reader = token.CreateReader())
                    value = serializer.Deserialize<T>(reader);
                error = string.Empty;
                return true;
            }
            catch (Exception ex) when (
                ex is JsonException
                || ex is FormatException
                || ex is OverflowException
                || ex is InvalidCastException)
            {
                error = "FoxRun inbound field '" + field
                        + "' cannot be converted to its generated DTO shape: "
                        + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Appends one generator-validated DTO value as deterministic JSON.
        /// Type metadata stays disabled and cyclic graphs fail closed.
        /// </summary>
        public static void AppendObject(StringBuilder json, object value)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            json.Append(JsonConvert.SerializeObject(
                value,
                Formatting.None,
                GeneratedObjectSettings));
        }

        private static bool TryObject(byte[] payload, string field, out JObject obj, out string error)
        {
            obj = null;
            if (!TryToken(
                    payload,
                    field,
                    out var token,
                    out error,
                    rejectUnknownRootProperties: true))
                return false;
            obj = token as JObject;
            if (obj != null)
                return true;
            error = "FoxRun inbound field '" + field + "' must be a JSON object.";
            return false;
        }

        private static bool RejectUnknownProperties(
            JObject obj,
            out string error,
            params string[] allowedNames)
        {
            error = string.Empty;
            foreach (var property in obj.Properties())
            {
                var allowed = false;
                for (var i = 0; i < allowedNames.Length; i++)
                {
                    if (string.Equals(property.Name, allowedNames[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    error = "FoxRun inbound vector payload contains unknown component '"
                            + property.Name + "'.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryNumber(JObject obj, string name, out float value, out string error)
        {
            value = 0f;
            error = string.Empty;
            if (!obj.TryGetValue(name, StringComparison.Ordinal, out var token)
                || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            {
                error = "FoxRun inbound vector field is missing numeric component '" + name + "'.";
                return false;
            }
            try
            {
                value = token.Value<float>();
                if (!IsFinite(value))
                {
                    value = 0f;
                    error = "FoxRun inbound vector component '" + name + "' must be finite.";
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)
            {
                error = "FoxRun inbound vector component '" + name + "' cannot be converted: " + ex.Message;
                return false;
            }
        }

        private static bool IsFinite<T>(T value)
        {
            if (typeof(T) == typeof(float))
            {
                var numeric = (float)(object)value;
                return !float.IsNaN(numeric) && !float.IsInfinity(numeric);
            }

            if (typeof(T) == typeof(double))
            {
                var numeric = (double)(object)value;
                return !double.IsNaN(numeric) && !double.IsInfinity(numeric);
            }

            return true;
        }

        private sealed class NonFiniteFloatJsonConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                var targetType = Nullable.GetUnderlyingType(objectType) ?? objectType;
                return targetType == typeof(float) || targetType == typeof(double);
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                if (value is float single)
                {
                    if (!IsFinite(single))
                        writer.WriteNull();
                    else
                        writer.WriteValue(single);
                    return;
                }

                if (value is double number)
                {
                    if (!IsFinite(number))
                        writer.WriteNull();
                    else
                        writer.WriteValue(number);
                    return;
                }

                throw new JsonSerializationException(
                    "FoxRun generated JSON converter received an unsupported floating type.");
            }

            public override object ReadJson(
                JsonReader reader,
                Type objectType,
                object existingValue,
                JsonSerializer serializer)
            {
                var nullableType = Nullable.GetUnderlyingType(objectType);
                var targetType = nullableType ?? objectType;
                if (reader.TokenType == JsonToken.Null)
                {
                    if (nullableType != null)
                        return null;
                    return targetType == typeof(float) ? (object)0f : 0d;
                }

                if (reader.TokenType != JsonToken.Float && reader.TokenType != JsonToken.Integer)
                {
                    throw new JsonSerializationException(
                        "FoxRun generated JSON floating fields require a finite JSON number or null.");
                }

                try
                {
                    if (targetType == typeof(float))
                    {
                        var single = Convert.ToSingle(reader.Value, CultureInfo.InvariantCulture);
                        if (!IsFinite(single))
                            throw new JsonSerializationException(
                                "FoxRun generated JSON floating fields must be finite.");
                        return single;
                    }

                    var number = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                    if (!IsFinite(number))
                        throw new JsonSerializationException(
                            "FoxRun generated JSON floating fields must be finite.");
                    return number;
                }
                catch (Exception ex) when (
                    ex is FormatException || ex is OverflowException || ex is InvalidCastException)
                {
                    throw new JsonSerializationException(
                        "FoxRun generated JSON floating fields must be finite.",
                        ex);
                }
            }
        }
    }
}
