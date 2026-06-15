// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceDtoValidation
// Purpose: Reflection member selection helpers shared by FoxService DTO validation and schema previews.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceDtoReflectionMembers
    {
        public static IEnumerable<MemberInfo> SerializableMembers(Type type)
        {
            var seenJsonNames = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
                foreach (var member in current.GetMembers(flags).OrderBy(MemberOrder))
                {
                    if (!(member is FieldInfo) && !(member is PropertyInfo))
                        continue;

                    if (seenJsonNames.Add(JsonPropertyName(member)))
                        yield return member;
                }
            }
        }

        public static string JsonPropertyName(MemberInfo member)
        {
            foreach (var attribute in member.GetCustomAttributes(true))
            {
                var attributeType = attribute.GetType();
                if (!string.Equals(attributeType.FullName, "Newtonsoft.Json.JsonPropertyAttribute", StringComparison.Ordinal))
                    continue;

                var propertyName = attributeType.GetProperty("PropertyName", BindingFlags.Instance | BindingFlags.Public);
                if (propertyName != null && propertyName.GetValue(attribute) is string value && !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return member.Name;
        }

        public static bool IsIgnored(MemberInfo member)
            => member.GetCustomAttributes(true).Any(attribute =>
            {
                var typeName = attribute.GetType().FullName ?? string.Empty;
                return typeName == "Newtonsoft.Json.JsonIgnoreAttribute"
                       || typeName == "System.Text.Json.Serialization.JsonIgnoreAttribute"
                       || typeName == "System.NonSerializedAttribute";
            });

        private static int MemberOrder(MemberInfo member)
        {
            try
            {
                return member.MetadataToken;
            }
            catch (InvalidOperationException)
            {
                return int.MaxValue;
            }
        }
    }
}
