// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Builds deterministic custom ROS2 DTO shapes from reflection metadata.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunReflectionRos2CustomDtoShapeBuilder
    {
        public static FoxRunRos2CustomDtoShape Build(Type type)
            => BuildDto(type, MetadataTypeName(type), new HashSet<string>(StringComparer.Ordinal));

        private static FoxRunRos2CustomDtoShape BuildDto(Type type, string path, ISet<string> stack)
        {
            var typeName = MetadataTypeName(type);
            var diagnostics = new List<string>();
            var isConstructible = IsConcreteDto(type)
                && type.GetConstructor(Type.EmptyTypes) != null;

            if (!IsConcreteDto(type))
            {
                AddUnsupported(path, "Custom ROS2 DTO roots and nested values must be concrete, non-generic classes.", diagnostics);
                return Unsupported(typeName, isConstructible, diagnostics);
            }

            if (!isConstructible)
                AddNonConstructible(path, diagnostics);

            if (!stack.Add(typeName))
            {
                AddUnsupported(path, "Custom ROS2 DTO graph contains a cyclic type reference.", diagnostics);
                return Unsupported(typeName, isConstructible, diagnostics);
            }

            var members = new List<FoxRunRos2CustomDtoMemberShape>();
            var rosNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in PublicInstanceMembers(type).OrderBy(member => member.Name, StringComparer.Ordinal))
                BuildMember(candidate, path, stack, members, rosNames, diagnostics);
            stack.Remove(typeName);

            var canonical = diagnostics.Count == 0
                ? FoxRunRos2CustomIdentity.BuildCanonicalIdentity(typeName, members)
                : string.Empty;
            return new FoxRunRos2CustomDtoShape(
                typeName,
                canonical,
                diagnostics.Count == 0 ? FoxRunRos2CustomIdentity.BuildPayloadIdentity(typeName, canonical) : string.Empty,
                isConstructible,
                diagnostics.Count == 0,
                members,
                diagnostics);
        }

        private static FoxRunRos2CustomDtoShape Unsupported(string typeName, bool isConstructible, IReadOnlyList<string> diagnostics)
            => new FoxRunRos2CustomDtoShape(typeName, string.Empty, string.Empty, isConstructible, false,
                Array.Empty<FoxRunRos2CustomDtoMemberShape>(), diagnostics);

        private static void BuildMember(
            ReflectedMember candidate,
            string rootPath,
            ISet<string> stack,
            ICollection<FoxRunRos2CustomDtoMemberShape> members,
            ISet<string> rosNames,
            ICollection<string> diagnostics)
        {
            var path = rootPath + "." + candidate.Name;
            var rosName = FoxRunRos2CustomNamingPolicy.ToRosFieldName(candidate.Name);
            if (string.IsNullOrEmpty(rosName))
            {
                AddUnsupported(path, "Custom ROS2 DTO member names must contain at least one letter or digit.", diagnostics);
                return;
            }

            if (FoxRunRos2CustomNamingPolicy.IsReservedUserField(rosName))
            {
                AddUnsupported(path, "Custom ROS2 DTO member '" + rosName + "' uses the reserved foxrun_ prefix.", diagnostics);
                return;
            }

            if (!rosNames.Add(rosName))
            {
                AddUnsupported(path, "Custom ROS2 DTO member name collides after ROS snake_case conversion: '" + rosName + "'.", diagnostics);
                return;
            }

            if (!candidate.CanRead || !candidate.CanWrite)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    FoxRunRos2CustomDtoDiagnostic.NonWritableInboundMember,
                    path,
                    "Custom ROS2 DTO members must be readable and writable for native inbound application."));
                return;
            }

            var valueType = candidate.Type;
            var hasPresence = IsReferenceOrNullable(valueType);
            if (TryNullable(valueType, out var nullableElement))
            {
                valueType = nullableElement;
                hasPresence = true;
            }

            if (TrySequence(valueType, out var sequenceElement, out var sequenceRepresentation, out var sequenceError))
            {
                if (!string.IsNullOrEmpty(sequenceError))
                {
                    AddUnsupported(path, sequenceError, diagnostics);
                    return;
                }

                if (TryNullable(sequenceElement, out _))
                {
                    AddUnsupported(path, "Custom ROS2 DTO sequences cannot contain nullable elements.", diagnostics);
                    return;
                }

                var sequenceRosType = RosType(sequenceElement, path, stack, diagnostics, out var nestedShape);
                if (string.IsNullOrEmpty(sequenceRosType))
                    return;
                members.Add(new FoxRunRos2CustomDtoMemberShape(
                    candidate.Name, rosName, FoxRunRos2CustomDtoMemberKind.Sequence,
                    MetadataTypeName(candidate.Type), sequenceRosType + "[]", MetadataTypeName(sequenceElement),
                    nestedShape?.CanonicalIdentity ?? string.Empty, true, candidate.CanRead, candidate.CanWrite,
                    sequenceRepresentation, nestedShape));
                return;
            }

            var rosType = RosType(valueType, path, stack, diagnostics, out var nested);
            if (string.IsNullOrEmpty(rosType))
                return;
            var kind = valueType == typeof(string)
                ? FoxRunRos2CustomDtoMemberKind.String
                : valueType.IsEnum
                    ? FoxRunRos2CustomDtoMemberKind.Enum
                    : nested != null ? FoxRunRos2CustomDtoMemberKind.NestedDto : FoxRunRos2CustomDtoMemberKind.Scalar;
            members.Add(new FoxRunRos2CustomDtoMemberShape(
                candidate.Name, rosName, kind, MetadataTypeName(candidate.Type), rosType, string.Empty,
                nested?.CanonicalIdentity ?? string.Empty, hasPresence, candidate.CanRead, candidate.CanWrite,
                nestedShape: nested));
        }

        private static string RosType(
            Type type,
            string path,
            ISet<string> stack,
            ICollection<string> diagnostics,
            out FoxRunRos2CustomDtoShape nested)
        {
            nested = null;
            var scalar = ScalarRosType(type);
            if (!string.IsNullOrEmpty(scalar))
                return scalar;
            if (type?.IsEnum == true)
                return ScalarRosType(Enum.GetUnderlyingType(type));

            if (IsExplicitlyUnsupported(type))
            {
                AddUnsupported(path, "Custom ROS2 DTO member type '" + DisplayTypeName(type) + "' cannot be represented without loss.", diagnostics);
                return string.Empty;
            }

            if (!IsConcreteDto(type))
            {
                AddUnsupported(path, "Unsupported custom ROS2 DTO member type '" + DisplayTypeName(type) + "'.", diagnostics);
                return string.Empty;
            }

            nested = BuildDto(type, path, stack);
            foreach (var diagnostic in nested.Diagnostics)
                diagnostics.Add(diagnostic);
            return nested.IsSupported ? nested.PayloadIdentity : string.Empty;
        }

        private static IEnumerable<ReflectedMember> PublicInstanceMembers(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags).Where(field => !field.IsStatic && !field.IsLiteral))
                yield return new ReflectedMember(field.Name, field.FieldType, true, !field.IsInitOnly);
            foreach (var property in type.GetProperties(flags)
                         .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod?.IsStatic != true))
            {
                yield return new ReflectedMember(
                    property.Name,
                    property.PropertyType,
                    property.GetMethod?.IsPublic == true,
                    property.SetMethod?.IsPublic == true && !IsInitOnly(property.SetMethod));
            }
        }

        private static bool TryNullable(Type type, out Type element)
        {
            element = null;
            if (type?.IsGenericType != true || type.GetGenericTypeDefinition() != typeof(Nullable<>))
                return false;
            element = type.GetGenericArguments()[0];
            return true;
        }

        private static bool TrySequence(
            Type type,
            out Type element,
            out FoxRunRos2CustomDtoSequenceRepresentation representation,
            out string error)
        {
            element = null;
            representation = FoxRunRos2CustomDtoSequenceRepresentation.None;
            error = string.Empty;
            if (type?.IsArray == true)
            {
                if (type.GetArrayRank() != 1)
                {
                    error = "Only one-dimensional custom ROS2 DTO sequences are supported.";
                    return true;
                }

                element = type.GetElementType();
                if (element?.IsArray == true)
                    error = "Jagged custom ROS2 DTO arrays are unsupported.";
                representation = FoxRunRos2CustomDtoSequenceRepresentation.Array;
                return true;
            }

            if (type?.IsGenericType == true && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                element = type.GetGenericArguments()[0];
                if (element?.IsArray == true)
                    error = "Jagged custom ROS2 DTO arrays are unsupported.";
                representation = FoxRunRos2CustomDtoSequenceRepresentation.List;
                return true;
            }

            return false;
        }

        private static bool IsConcreteDto(Type type)
            => type != null
               && type.IsClass
               // CLR arrays report IsClass=true, but a transport sequence is
               // a DTO member shape, never a DTO root or nested DTO.
               && !type.IsArray
               && !type.IsAbstract
               && !type.IsInterface
               // A closed generic type is still a generic DTO declaration.  It
               // would make the generated interface identity depend on a
               // CLR-only construction detail and would disagree with the
               // Roslyn path, which rejects all generic named types.
               && !type.IsGenericType
               && !IsExplicitlyUnsupported(type);

        private static bool IsReferenceOrNullable(Type type)
            => type != null && (!type.IsValueType || Nullable.GetUnderlyingType(type) != null);

        private static bool IsExplicitlyUnsupported(Type type)
        {
            if (type == null || type == typeof(object) || type == typeof(char) || type == typeof(decimal))
                return true;
            if (type.IsPointer || typeof(Delegate).IsAssignableFrom(type) || typeof(Stream).IsAssignableFrom(type)
                || typeof(Task).IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                return true;
            if (IsUnityObject(type))
                return true;
            if (!type.IsGenericType)
                return false;
            var definition = type.GetGenericTypeDefinition();
            return definition == typeof(Dictionary<,>)
                   || definition == typeof(HashSet<>)
                   || definition == typeof(ISet<>)
                   || definition == typeof(IDictionary<,>);
        }

        private static bool IsUnityObject(Type type)
        {
            for (var candidate = type; candidate != null; candidate = candidate.BaseType)
            {
                if (string.Equals(candidate.FullName, "UnityEngine.Object", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string ScalarRosType(Type type)
        {
            if (type == typeof(bool)) return "bool";
            if (type == typeof(sbyte)) return "int8";
            if (type == typeof(byte)) return "uint8";
            if (type == typeof(short)) return "int16";
            if (type == typeof(ushort)) return "uint16";
            if (type == typeof(int)) return "int32";
            if (type == typeof(uint)) return "uint32";
            if (type == typeof(long)) return "int64";
            if (type == typeof(ulong)) return "uint64";
            if (type == typeof(float)) return "float32";
            if (type == typeof(double)) return "float64";
            if (type == typeof(string)) return "string";
            return string.Empty;
        }

        private static void AddUnsupported(string path, string message, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(FoxRunRos2CustomDtoDiagnostic.UnsupportedShape, path, message));

        private static void AddNonConstructible(string path, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                FoxRunRos2CustomDtoDiagnostic.NonConstructible,
                path,
                "Custom ROS2 DTO roots and nested values require a public parameterless constructor for native inbound application."));

        private static string DisplayTypeName(Type type)
            => type == typeof(decimal) ? "Decimal" : MetadataTypeName(type);

        private static bool IsInitOnly(MethodInfo setter)
            => setter?.ReturnParameter.GetRequiredCustomModifiers().Any(modifier =>
                string.Equals(modifier.FullName, "System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal)) == true;

        private static string MetadataTypeName(Type type)
        {
            if (type == null)
                return string.Empty;
            if (type.IsArray)
                return MetadataTypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var name = definition.FullName ?? definition.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);
                return name.Replace('+', '.') + "<" + string.Join(",", type.GetGenericArguments().Select(MetadataTypeName)) + ">";
            }
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        private sealed class ReflectedMember
        {
            public ReflectedMember(string name, Type type, bool canRead, bool canWrite)
            {
                Name = name;
                Type = type;
                CanRead = canRead;
                CanWrite = canWrite;
            }

            public string Name { get; }
            public Type Type { get; }
            public bool CanRead { get; }
            public bool CanWrite { get; }
        }
    }
}
