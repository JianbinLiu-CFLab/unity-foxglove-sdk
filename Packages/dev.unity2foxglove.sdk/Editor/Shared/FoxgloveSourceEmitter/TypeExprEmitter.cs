// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Emits change-detection and value expressions for Unity value types
    /// (<c>float</c>, <c>double</c>, <c>Vector3</c>, <c>Vector2</c>,
    /// <c>Quaternion</c>, <c>Color</c>) used in generated FoxRun partial
    /// classes.
    /// </summary>
    internal static class TypeExprEmitter
    {
        /// <summary>
        /// Emits a C# change-detection expression comparing current and last values.
        /// </summary>
        public static string ChangeExpr(string member, string type, string lastVar, float epsilon)
        {
            var t = type.StartsWith("UnityEngine.") ? type.Substring(12) : type;
            var eps = FloatLiteral(epsilon < 0 ? 0 : epsilon);
            var access = MemberAccess(member);
            switch (t)
            {
                case "float":
                case "Single":
                case "System.Single":
                    return $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}, {lastVar}, {eps})";
                case "double":
                case "Double":
                case "System.Double":
                    return $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.DoubleChanged({access}, {lastVar}, {eps})";
                case "Vector3":
                    return $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.x, {lastVar}.x, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.y, {lastVar}.y, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.z, {lastVar}.z, {eps})";
                case "Vector2":
                    return $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.x, {lastVar}.x, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.y, {lastVar}.y, {eps})";
                case "Quaternion":
                    return $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.x, {lastVar}.x, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.y, {lastVar}.y, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.z, {lastVar}.z, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.w, {lastVar}.w, {eps})";
                case "Color":
                    return $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.r, {lastVar}.r, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.g, {lastVar}.g, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.b, {lastVar}.b, {eps}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({access}.a, {lastVar}.a, {eps})";
                default:
                    return $"!EqualityComparer<{type}>.Default.Equals({access}, {lastVar})";
            }
        }

        /// <summary>
        /// Returns a C# payload value expression string for a Unity type
        /// (<c>Vector3</c>, <c>Vector2</c>, <c>Quaternion</c>, <c>Color</c>),
        /// or the raw member access for all other types.
        /// </summary>
        public static string ValueExpr(string name, string type)
        {
            var t = type;
            if (t.StartsWith("UnityEngine.")) t = t.Substring(12);
            var access = MemberAccess(name);
            switch (t)
            {
                case "Vector3": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y, [\"z\"] = {access}.z }}";
                case "Vector2": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y }}";
                case "Quaternion": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y, [\"z\"] = {access}.z, [\"w\"] = {access}.w }}";
                case "Color": return $"new Dictionary<string, object> {{ [\"r\"] = {access}.r, [\"g\"] = {access}.g, [\"b\"] = {access}.b, [\"a\"] = {access}.a }}";
                default: return access;
            }
        }

        /// <summary>
        /// Formats a float value as a C# float literal with invariant culture
        /// and <c>f</c> suffix.
        /// </summary>
        internal static string FloatLiteral(float value)
        {
            return value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "f";
        }

        /// <summary>
        /// Returns a <c>this.MemberName</c> access expression with keyword
        /// escaping applied.
        /// </summary>
        internal static string MemberAccess(string memberName)
        {
            return "this." + IdentifierUtils.EscapeIdentifier(memberName);
        }
    }
}
