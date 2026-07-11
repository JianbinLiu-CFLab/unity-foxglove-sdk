// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Adapts reflection-visible FoxRun DTOs into host-independent Protobuf shapes.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunProtobufReflectionTypeShapeBuilder
    {
        public static FoxRunProtobufTypeShape Build(Type type)
        {
            return Build(
                type,
                0,
                new Dictionary<string, FoxRunProtobufTypeShape>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }

        private static FoxRunProtobufTypeShape Build(
            Type type,
            int depth,
            IDictionary<string, FoxRunProtobufTypeShape> memo,
            ISet<string> stack)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == null)
                throw new ArgumentException("A FoxRun Protobuf field type is required.", nameof(type));
            if (depth > FoxServiceDtoRules.MaxDepth)
                throw new InvalidOperationException("FoxRun Protobuf DTO nesting exceeds the supported depth.");

            if (TryGetRepeatedElementType(type, out var elementType))
                return Build(elementType, depth + 1, memo, stack);

            var canonicalType = FoxRunCanonicalTypeNormalizer.NormalizeTypeName(FullTypeName(type));
            if (FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(canonicalType))
                return FoxRunProtobufTypeShape.Canonical(canonicalType);

            if (type.IsEnum)
                return BuildEnum(type, memo);

            if (IsUnsupported(type))
            {
                throw new InvalidOperationException(
                    "FoxRun Protobuf DTO type '" + FullTypeName(type) + "' is not supported.");
            }

            var typeName = FullTypeName(type);
            if (memo.TryGetValue(typeName, out var cached))
                return cached;
            if (!stack.Add(typeName))
            {
                throw new InvalidOperationException(
                    "FoxRun Protobuf DTO graph contains a cycle at '" + typeName + "'.");
            }

            var fields = new List<FoxRunProtobufTypeField>();
            foreach (var member in FoxServiceDtoReflectionMembers.SerializableMembers(type))
            {
                if (FoxServiceDtoReflectionMembers.IsIgnored(member))
                    continue;

                if (member is FieldInfo field)
                {
                    if (field.IsStatic || field.IsLiteral)
                        continue;
                    AddMember(fields, field.Name, FoxServiceDtoReflectionMembers.JsonPropertyName(field), field.FieldType, !field.IsInitOnly, depth, memo, stack);
                }
                else if (member is PropertyInfo property)
                {
                    if (property.GetIndexParameters().Length != 0 || property.GetMethod == null || !property.GetMethod.IsPublic)
                        continue;
                    AddMember(fields, property.Name, FoxServiceDtoReflectionMembers.JsonPropertyName(property), property.PropertyType,
                        property.SetMethod != null && property.SetMethod.IsPublic, depth, memo, stack);
                }
            }

            var result = FoxRunProtobufTypeShape.Object(typeName, fields);
            stack.Remove(typeName);
            memo[typeName] = result;
            return result;
        }

        private static void AddMember(
            ICollection<FoxRunProtobufTypeField> fields,
            string memberName,
            string jsonName,
            Type memberType,
            bool canAssign,
            int depth,
            IDictionary<string, FoxRunProtobufTypeShape> memo,
            ISet<string> stack)
        {
            var repeated = TryGetRepeatedElementType(memberType, out var elementType);
            var collectionKind = memberType != null && memberType.IsArray
                ? FoxRunProtobufRepeatedCollectionKind.Array
                : repeated
                    ? FoxRunProtobufRepeatedCollectionKind.List
                    : FoxRunProtobufRepeatedCollectionKind.None;
            var valueType = repeated ? elementType : memberType;
            fields.Add(new FoxRunProtobufTypeField(
                jsonName,
                memberName,
                Build(valueType, depth + 1, memo, stack),
                repeated,
                repeatedCollectionKind: collectionKind,
                canAssign: canAssign,
                isNullable: Nullable.GetUnderlyingType(valueType) != null));
        }

        private static FoxRunProtobufTypeShape BuildEnum(
            Type type,
            IDictionary<string, FoxRunProtobufTypeShape> memo)
        {
            var typeName = FullTypeName(type);
            if (memo.TryGetValue(typeName, out var cached))
                return cached;

            var values = Enum.GetNames(type)
                .Select(name => new FoxRunProtobufEnumValue(name, Convert.ToInt32(Enum.Parse(type, name))))
                .OrderBy(value => value.Number)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .ToList();
            if (!values.Any(value => value.Number == 0))
                values.Insert(0, new FoxRunProtobufEnumValue("UNSPECIFIED", 0));

            var result = FoxRunProtobufTypeShape.Enum(typeName, values);
            memo[typeName] = result;
            return result;
        }

        private static bool IsUnsupported(Type type)
        {
            var typeName = FullTypeName(type);
            return type == typeof(object)
                   || type.IsInterface
                   || typeof(Delegate).IsAssignableFrom(type)
                   || type.IsPointer
                   || type.IsByRef
                   || type.IsGenericType
                   || FoxServiceDtoTypeNames.IsTaskLike(typeName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(typeName)
                   || IsUnityObject(type);
        }

        private static bool IsUnityObject(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, "UnityEngine.Object", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool TryGetRepeatedElementType(Type type, out Type elementType)
        {
            elementType = null;
            if (type == null)
                return false;
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (!type.IsGenericType)
                return false;
            var definition = type.GetGenericTypeDefinition();
            var contract = FoxServiceDtoTypeNames.NormalizeGenericContractName(definition.FullName ?? definition.Name);
            if (!FoxServiceDtoTypeNames.IsListContract(contract, FoxServiceDtoRules.ResponseSide))
                return false;

            elementType = type.GetGenericArguments()[0];
            return true;
        }

        private static string FullTypeName(Type type)
        {
            return type == null
                ? string.Empty
                : (type.FullName ?? type.Name).Replace('+', '.');
        }
    }
}
