// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Bounded, non-polymorphic JSON decoding for generated FoxRun inputs.

using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public static class FoxRunInboundJson
    {
        private const int MaxTypeHintScanDepth = 32;

        private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        };

        /// <remarks>
        /// This parser is intended for low-frequency FoxRun control inputs. It decodes UTF-8
        /// into a managed string and builds a JToken tree once per TryRead call.
        /// </remarks>
        private static bool TryToken(byte[] payload, string field, out JToken token, out string error)
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
                var json = Encoding.UTF8.GetString(payload);
                var root = JToken.Parse(json, LoadSettings);
                if (ContainsForbiddenTypeHint(root, 0, out var typeHintError))
                {
                    error = typeHintError;
                    return false;
                }

                if (!(root is JObject obj) || !obj.TryGetValue(field, StringComparison.Ordinal, out token))
                {
                    error = "FoxRun inbound payload is missing field '" + field + "'.";
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException)
            {
                error = "FoxRun inbound JSON is invalid: " + ex.Message;
                return false;
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
            if (!TryToken(payload, field, out var token, out error))
                return false;
            if (token.Type != expected && !(expected == JTokenType.Float && token.Type == JTokenType.Integer))
            {
                error = "FoxRun inbound field '" + field + "' has the wrong JSON type.";
                return false;
            }
            try
            {
                value = token.Value<T>();
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
                && TryNumber(obj, "x", out value.x, out error)
                && TryNumber(obj, "y", out value.y, out error);
        }

        public static bool TryRead(byte[] payload, string field, out Vector3 value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && TryNumber(obj, "x", out value.x, out error)
                && TryNumber(obj, "y", out value.y, out error)
                && TryNumber(obj, "z", out value.z, out error);
        }

        public static bool TryRead(byte[] payload, string field, out Quaternion value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && TryNumber(obj, "x", out value.x, out error)
                && TryNumber(obj, "y", out value.y, out error)
                && TryNumber(obj, "z", out value.z, out error)
                && TryNumber(obj, "w", out value.w, out error);
        }

        public static bool TryRead(byte[] payload, string field, out Color value, out string error)
        {
            value = default;
            return TryObject(payload, field, out var obj, out error)
                && TryNumber(obj, "r", out value.r, out error)
                && TryNumber(obj, "g", out value.g, out error)
                && TryNumber(obj, "b", out value.b, out error)
                && TryNumber(obj, "a", out value.a, out error);
        }

        private static bool TryObject(byte[] payload, string field, out JObject obj, out string error)
        {
            obj = null;
            if (!TryToken(payload, field, out var token, out error))
                return false;
            obj = token as JObject;
            if (obj != null)
                return true;
            error = "FoxRun inbound field '" + field + "' must be a JSON object.";
            return false;
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
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)
            {
                error = "FoxRun inbound vector component '" + name + "' cannot be converted: " + ex.Message;
                return false;
            }
        }
    }
}
