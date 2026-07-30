// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceDtoValidation
// Purpose: Reflection member selection helpers shared by FoxService DTO validation and schema previews.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceDtoReflectionMembers
    {
        public static IEnumerable<MemberInfo> SerializableMembers(Type type)
        {
            var seenJsonNames = new HashSet<string>(StringComparer.Ordinal);
            var seenPropertySlots = new HashSet<MethodInfo>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
                var members = current.GetMembers(flags);
                Array.Sort(members, CompareMemberOrder);
                foreach (var member in members)
                {
                    if (!(member is FieldInfo) && !(member is PropertyInfo))
                        continue;
                    if (member is PropertyInfo property
                        && property.GetMethod != null
                        && !seenPropertySlots.Add(
                            property.GetMethod.GetBaseDefinition()))
                    {
                        continue;
                    }

                    var ownsJsonName = !IsIgnored(member)
                                       && (!(member is PropertyInfo candidate)
                                           || (candidate.GetIndexParameters().Length == 0
                                               && candidate.GetMethod != null
                                               && candidate.GetMethod.IsPublic));
                    if (!ownsJsonName)
                    {
                        yield return member;
                        continue;
                    }

                    if (!seenJsonNames.Add(JsonPropertyName(member)))
                        continue;
                    yield return member;
                }
            }
        }

        public static string JsonPropertyName(MemberInfo member)
        {
            foreach (var attribute in member.GetCustomAttributes(false))
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
        {
            foreach (var attribute in member.GetCustomAttributes(false))
            {
                var typeName = attribute.GetType().FullName ?? string.Empty;
                if (typeName == "Newtonsoft.Json.JsonIgnoreAttribute"
                    || typeName == "System.Text.Json.Serialization.JsonIgnoreAttribute"
                    || typeName == "System.NonSerializedAttribute")
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareMemberOrder(MemberInfo left, MemberInfo right)
            => MemberOrder(left).CompareTo(MemberOrder(right));

        private static int MemberOrder(MemberInfo member)
        {
            try
            {
                var token = member.MetadataToken;
                return token > 0 ? token : int.MaxValue;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException)
            {
                return int.MaxValue;
            }
        }
    }
}
