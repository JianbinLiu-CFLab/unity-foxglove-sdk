// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Adapts reflection-visible FoxRun DTOs into encoding-neutral shapes.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunReflectionTypeShapeBuilder
    {
        public static FoxRunTypeShape Build(Type type)
        {
            return Build(
                type,
                0,
                new Dictionary<string, FoxRunTypeShape>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
        }

        private static FoxRunTypeShape Build(
            Type type,
            int depth,
            IDictionary<string, FoxRunTypeShape> memo,
            ISet<string> stack)
        {
            if (type == null)
                throw new ArgumentException("A FoxRun field type is required.", nameof(type));
            if (depth > FoxServiceDtoRules.MaxDepth)
                throw Unsupported(
                    "FoxRun DTO nesting exceeds the supported depth.");

            var nullable = Nullable.GetUnderlyingType(type) != null;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsGenericParameter || type.ContainsGenericParameters)
            {
                throw Unsupported(
                    "FoxRun MessagePack open generic values are not supported.");
            }
            if (type.IsArray && type.GetArrayRank() != 1)
            {
                throw Unsupported(
                    "FoxRun MessagePack collections must be one-dimensional.");
            }
            if (TryGetRepeatedElementType(type, out var elementType))
            {
                var collectionKind = type.IsArray
                    ? elementType == typeof(byte)
                        ? FoxRunCollectionKind.Binary
                        : FoxRunCollectionKind.Array
                    : FoxRunCollectionKind.List;
                var elementShape = Build(elementType, depth + 1, memo, stack);
                if (elementShape.Kind == FoxRunTypeShapeKind.Collection)
                {
                    throw Unsupported(
                        "FoxRun MessagePack jagged or nested collections are not supported.");
                }
                return FoxRunTypeShape.Collection(
                    collectionKind,
                    elementShape,
                    nullable);
            }

            var typeName = FullTypeName(type);
            if (TryBuildUnityValueShape(typeName, nullable, out var unityValueShape))
                return unityValueShape;

            var canonicalType = FoxRunCanonicalTypeNormalizer.NormalizeTypeName(typeName);
            if (FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(canonicalType))
                return FoxRunTypeShape.Canonical(canonicalType, nullable);

            if (type.IsEnum)
                return BuildEnum(type, memo).WithNullable(nullable);

            if (IsUnsupported(type))
            {
                throw Unsupported(
                    "FoxRun DTO type '" + FullTypeName(type) + "' is not supported.");
            }

            if (memo.TryGetValue(typeName, out var cached))
            {
                EnsureCachedShapeFitsDepth(cached, depth);
                return cached.WithNullable(nullable);
            }
            if (!stack.Add(typeName))
            {
                throw Unsupported(
                    "FoxRun DTO graph contains a cycle at '" + typeName + "'.");
            }

            EnsureNoDuplicateDeclaredJsonNames(type);
            var fields = new List<FoxRunTypeField>();
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
                        IsPublicWritableProperty(property), depth, memo, stack);
                }
            }

            var result = FoxRunTypeShape.Object(
                typeName,
                fields,
                canConstruct: CanConstruct(type),
                isValueType: type.IsValueType);
            stack.Remove(typeName);
            memo[typeName] = result;
            return result.WithNullable(nullable);
        }

        private static void EnsureNoDuplicateDeclaredJsonNames(Type type)
        {
            var membersByJsonName =
                new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
            var lookupMembersByClrName =
                new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
            var ambiguousLookupMembers =
                new Dictionary<string, MemberInfo[]>(StringComparer.Ordinal);
            var serializableClrNames =
                new HashSet<string>(StringComparer.Ordinal);
            var propertySlots = new HashSet<MethodInfo>();
            for (var current = type;
                 current != null && current != typeof(object);
                 current = current.BaseType)
            {
                var members = current.GetMembers(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
                foreach (var member in members)
                {
                    if (!CanAffectClrMemberLookup(member))
                        continue;

                    if (member is PropertyInfo property)
                    {
                        var accessor =
                            property.GetGetMethod(nonPublic: true)
                            ?? property.GetSetMethod(nonPublic: true);
                        if (accessor != null
                        && !propertySlots.Add(
                                accessor.GetBaseDefinition()))
                        {
                            continue;
                        }
                    }

                    var isSerializable = IsSerializableMember(member);
                    if (lookupMembersByClrName.TryGetValue(
                            member.Name,
                            out var sameName)
                        && sameName.DeclaringType != member.DeclaringType
                        && isSerializable)
                    {
                        if (!ambiguousLookupMembers.ContainsKey(member.Name))
                        {
                            ambiguousLookupMembers.Add(
                                member.Name,
                                new[] { sameName, member });
                        }
                    }
                    else if (!lookupMembersByClrName.ContainsKey(member.Name))
                    {
                        lookupMembersByClrName.Add(member.Name, member);
                    }

                    if (!isSerializable)
                        continue;
                    serializableClrNames.Add(member.Name);

                    if (FoxServiceDtoReflectionMembers.IsIgnored(member))
                        continue;

                    var jsonName =
                        FoxServiceDtoReflectionMembers.JsonPropertyName(
                            member);
                    if (membersByJsonName.TryGetValue(
                            jsonName,
                            out var existing))
                    {
                        throw Unsupported(
                            "FoxRun DTO type '"
                            + FullTypeName(type)
                            + "' contains duplicate JSON field name '"
                            + jsonName
                            + "'.");
                    }
                    membersByJsonName.Add(jsonName, member);
                }
            }

            foreach (var name in serializableClrNames.OrderBy(
                         value => value,
                         StringComparer.Ordinal))
            {
                if (!ambiguousLookupMembers.TryGetValue(
                        name,
                        out var collision))
                {
                    continue;
                }

                throw Unsupported(
                    "FoxRun DTO type '"
                    + FullTypeName(type)
                    + "' contains inherited members with ambiguous CLR name '"
                    + name
                    + "' ('"
                    + collision[0].DeclaringType?.FullName
                    + "' and '"
                    + collision[1].DeclaringType?.FullName
                    + "').");
            }
        }

        private static bool CanAffectClrMemberLookup(MemberInfo member)
            => !member.IsDefined(
                   typeof(CompilerGeneratedAttribute),
                   inherit: false)
               && (member is FieldInfo
                   || member is PropertyInfo
                   || member is EventInfo
                   || member is Type
                   || (member is MethodInfo method && !method.IsSpecialName));

        private static bool IsSerializableMember(MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.IsPublic
                       && !field.IsStatic
                       && !field.IsLiteral;

            if (member is PropertyInfo property)
            {
                return property.GetIndexParameters().Length == 0
                       && property.GetMethod != null
                       && property.GetMethod.IsPublic;
            }

            return false;
        }

        private static void EnsureCachedShapeFitsDepth(
            FoxRunTypeShape shape,
            int depth)
        {
            if (FoxRunTypeShapeDepth.MaximumRelativeDepth(shape)
                > FoxServiceDtoRules.MaxDepth - depth)
            {
                throw Unsupported(
                    "FoxRun DTO nesting exceeds the supported depth.");
            }
        }

        private static void AddMember(
            ICollection<FoxRunTypeField> fields,
            string memberName,
            string jsonName,
            Type memberType,
            bool canAssign,
            int depth,
            IDictionary<string, FoxRunTypeShape> memo,
            ISet<string> stack)
        {
            var repeated = TryGetRepeatedElementType(memberType, out var elementType);
            var collectionKind = memberType != null && memberType.IsArray
                ? FoxRunCollectionKind.Array
                : repeated
                    ? FoxRunCollectionKind.List
                    : FoxRunCollectionKind.None;
            fields.Add(new FoxRunTypeField(
                jsonName,
                memberName,
                Build(memberType, depth + 1, memo, stack),
                repeated,
                repeatedCollectionKind: collectionKind,
                canAssign: canAssign,
                isNullable: Nullable.GetUnderlyingType(repeated ? elementType : memberType) != null));
        }

        private static FoxRunTypeShape BuildEnum(
            Type type,
            IDictionary<string, FoxRunTypeShape> memo)
        {
            var typeName = FullTypeName(type);
            if (memo.TryGetValue(typeName, out var cached))
                return cached;

            var values = Enum.GetNames(type)
                .Select(name => new FoxRunEnumValue(name, CheckedEnumValue(type, name)))
                .OrderBy(value => value.Number)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .ToList();

            var result = FoxRunTypeShape.Enum(typeName, values);
            memo[typeName] = result;
            return result;
        }

        private static int CheckedEnumValue(Type type, string name)
        {
            var value = Convert.ToDecimal(Enum.Parse(type, name));
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "FOXRUN616: FoxRun MessagePack enum value '" + FullTypeName(type) + "." + name
                    + "' is outside the signed Int32 range.");
            }
            return decimal.ToInt32(value);
        }

        private static InvalidOperationException Unsupported(string message)
            => new InvalidOperationException("FOXRUN616: " + message);

        private static bool IsUnsupported(Type type)
        {
            var typeName = FullTypeName(type);
            return FoxServiceDtoTypeNames.IsScalar(typeName)
                   || type == typeof(object)
                   || type.IsInterface
                   || type.IsAbstract
                   || typeof(Delegate).IsAssignableFrom(type)
                   || type.IsPointer
                   || type.IsByRef
                   || type.IsGenericType
                   || string.Equals(
                       typeName,
                       "System.ValueTuple",
                       StringComparison.Ordinal)
                   || FoxServiceDtoTypeNames.IsTaskLike(typeName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(typeName)
                   || IsUnityObject(type);
        }

        private static bool CanConstruct(Type type)
            => type.IsValueType
               || type.GetConstructor(
                   BindingFlags.Instance | BindingFlags.Public,
                   binder: null,
                   Type.EmptyTypes,
                   modifiers: null) != null;

        private static bool IsPublicWritableProperty(PropertyInfo property)
        {
            var setter = property?.SetMethod;
            if (setter == null || !setter.IsPublic)
                return false;

            return !setter.ReturnParameter
                .GetRequiredCustomModifiers()
                .Concat(setter.ReturnParameter.GetOptionalCustomModifiers())
                .Any(modifier => string.Equals(
                    modifier.FullName,
                    "System.Runtime.CompilerServices.IsExternalInit",
                    StringComparison.Ordinal));
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
            if (definition != typeof(List<>)
                && definition != typeof(IList<>)
                && definition != typeof(IReadOnlyList<>))
                return false;

            elementType = type.GetGenericArguments()[0];
            return true;
        }

        private static bool TryBuildUnityValueShape(
            string typeName,
            bool nullable,
            out FoxRunTypeShape shape)
        {
            string[] components;
            switch (typeName ?? string.Empty)
            {
                case "UnityEngine.Vector2":
                    components = new[] { "x", "y" };
                    break;
                case "UnityEngine.Vector3":
                    components = new[] { "x", "y", "z" };
                    break;
                case "UnityEngine.Quaternion":
                    components = new[] { "x", "y", "z", "w" };
                    break;
                case "UnityEngine.Color":
                    components = new[] { "r", "g", "b", "a" };
                    break;
                default:
                    shape = null;
                    return false;
            }

            shape = FoxRunTypeShape.Object(
                typeName,
                components
                    .Select(component => new FoxRunTypeField(
                        component,
                        component,
                        FoxRunTypeShape.Canonical("float32")))
                    .ToList(),
                nullable,
                canConstruct: true,
                isValueType: true);
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
