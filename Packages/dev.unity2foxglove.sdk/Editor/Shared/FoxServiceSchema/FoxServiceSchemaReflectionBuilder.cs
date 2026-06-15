// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceSchema
// Purpose: Reflection-side schema preview builder for declarative FoxService descriptors.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceSchemaReflectionBuilder
    {
        public static FoxServiceSchemaModel Build(Type type, string side)
            => Build(
                type,
                side,
                0,
                new Dictionary<string, FoxServiceSchemaModel>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));

        private static FoxServiceSchemaModel Build(
            Type type,
            string side,
            int depth,
            IDictionary<string, FoxServiceSchemaModel> memo,
            ISet<string> stack)
        {
            if (type == null || type == typeof(void))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (depth > FoxServiceDtoRules.MaxDepth)
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type.IsArray && type.GetArrayRank() == 1)
                return FoxServiceSchemaModel.ArrayOf(Build(type.GetElementType(), side, depth + 1, memo, stack));

            if (TryGetJsonScalarType(type, out var scalar))
                return FoxServiceSchemaModel.Scalar(scalar);

            if (type.IsEnum)
                return FoxServiceSchemaModel.Scalar("integer");

            if (IsUnsupportedSchemaPreviewType(type))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (TryGetDictionaryValueType(type, out _, out var valueType))
                return FoxServiceSchemaModel.Dictionary(Build(valueType, side, depth + 1, memo, stack));

            if (TryGetListElementType(type, side, out var elementType))
                return FoxServiceSchemaModel.ArrayOf(Build(elementType, side, depth + 1, memo, stack));

            var typeKey = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
            if (memo.TryGetValue(typeKey, out var cached))
                return cached;
            if (!stack.Add(typeKey))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            var properties = new List<FoxServiceSchemaProperty>();
            var flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (var field in type.GetFields(flags).OrderBy(field => field.MetadataToken))
            {
                if (field.IsStatic || field.IsLiteral || IsIgnoredDtoMember(field))
                    continue;
                properties.Add(new FoxServiceSchemaProperty(JsonPropertyName(field), Build(field.FieldType, side, depth + 1, memo, stack)));
            }

            foreach (var property in type.GetProperties(flags).OrderBy(property => property.MetadataToken))
            {
                if (property.GetIndexParameters().Length != 0
                    || property.GetMethod == null
                    || !property.GetMethod.IsPublic
                    || IsIgnoredDtoMember(property))
                    continue;
                properties.Add(new FoxServiceSchemaProperty(JsonPropertyName(property), Build(property.PropertyType, side, depth + 1, memo, stack)));
            }

            var model = FoxServiceSchemaModel.Object(properties);
            stack.Remove(typeKey);
            memo[typeKey] = model;
            return model;
        }

        private static bool IsUnsupportedSchemaPreviewType(Type type)
        {
            var fullName = type.FullName ?? type.Name;
            return type == typeof(object)
                   || type.IsInterface
                   || typeof(Delegate).IsAssignableFrom(type)
                   || FoxServiceDtoTypeNames.IsTaskLike(fullName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(fullName)
                   || IsUnityEngineObject(type);
        }

        private static bool IsUnityEngineObject(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, "UnityEngine.Object", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string JsonPropertyName(MemberInfo member)
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

        private static bool TryGetJsonScalarType(Type type, out string jsonType)
        {
            jsonType = null;
            if (type == typeof(bool))
            {
                jsonType = "boolean";
                return true;
            }

            if (type == typeof(byte)
                || type == typeof(sbyte)
                || type == typeof(short)
                || type == typeof(ushort)
                || type == typeof(int)
                || type == typeof(uint)
                || type == typeof(long)
                || type == typeof(ulong))
            {
                jsonType = "integer";
                return true;
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                jsonType = "number";
                return true;
            }

            if (type == typeof(string)
                || type == typeof(char)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(Guid)
                || type == typeof(TimeSpan))
            {
                jsonType = "string";
                return true;
            }

            return false;
        }

        private static bool TryGetListElementType(Type type, string side, out Type elementType)
        {
            elementType = null;
            if (type == null || !type.IsGenericType)
                return false;

            var contract = GenericContractName(type.GetGenericTypeDefinition());
            if (!FoxServiceDtoTypeNames.IsListContract(contract, side))
                return false;

            elementType = type.GetGenericArguments()[0];
            return true;
        }

        private static bool TryGetDictionaryValueType(Type type, out Type keyType, out Type valueType)
        {
            keyType = null;
            valueType = null;
            if (type == null || !type.IsGenericType)
                return false;

            var contract = GenericDictionaryContractName(type.GetGenericTypeDefinition());
            if (!FoxServiceDtoTypeNames.IsDictionaryContract(contract))
                return false;

            var arguments = type.GetGenericArguments();
            keyType = arguments[0];
            valueType = arguments[1];
            return true;
        }

        private static string GenericContractName(Type definition)
        {
            if (definition == typeof(List<>)) return "System.Collections.Generic.List<T>";
            if (definition == typeof(IList<>)) return "System.Collections.Generic.IList<T>";
            if (definition == typeof(IReadOnlyList<>)) return "System.Collections.Generic.IReadOnlyList<T>";
            if (definition == typeof(HashSet<>)) return "System.Collections.Generic.HashSet<T>";
            if (definition == typeof(ICollection<>)) return "System.Collections.Generic.ICollection<T>";
            if (definition == typeof(IReadOnlyCollection<>)) return "System.Collections.Generic.IReadOnlyCollection<T>";
            if (definition == typeof(Queue<>)) return "System.Collections.Generic.Queue<T>";
            if (definition == typeof(Stack<>)) return "System.Collections.Generic.Stack<T>";
            if (definition == typeof(System.Collections.ObjectModel.Collection<>)) return "System.Collections.ObjectModel.Collection<T>";
            return (definition.FullName ?? definition.Name).Replace('+', '.');
        }

        private static string GenericDictionaryContractName(Type definition)
        {
            if (definition == typeof(Dictionary<,>)) return "System.Collections.Generic.Dictionary<TKey, TValue>";
            if (definition == typeof(IDictionary<,>)) return "System.Collections.Generic.IDictionary<TKey, TValue>";
            if (definition == typeof(IReadOnlyDictionary<,>)) return "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
            if (definition == typeof(SortedDictionary<,>)) return "System.Collections.Generic.SortedDictionary<TKey, TValue>";
            return (definition.FullName ?? definition.Name).Replace('+', '.');
        }

        private static bool IsIgnoredDtoMember(MemberInfo member)
            => member.GetCustomAttributes(true).Any(attribute =>
            {
                var typeName = attribute.GetType().FullName ?? string.Empty;
                return typeName == "Newtonsoft.Json.JsonIgnoreAttribute"
                       || typeName == "System.Text.Json.Serialization.JsonIgnoreAttribute"
                       || typeName == "System.NonSerializedAttribute";
            });
    }
}
