// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Builds host-neutral native ROS2 copy shapes from reflection metadata.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunReflectionRos2MessageShapeBuilder
    {
        private const string Ros2MessageMetadataName = "ROS2.Message";
        private const string Ros2MessageInternalsMetadataName = "ROS2.Internal.MessageInternals";
        private const string Ros2ExtendedDisposableMetadataName = "ROS2.IExtendedDisposable";
        private const string IsExternalInitMetadataName = "System.Runtime.CompilerServices.IsExternalInit";
        // The tracked Humble/Jazzy/Lyrical packages define the public
        // ROS2.Message contract in ros2cs_common; ros2cs_core consumes it.
        private const string Ros2MessageAssemblyName = "ros2cs_common";

        public static FoxRunRos2MessageShape Build(Type type)
        {
            var contract = type?.GetInterfaces().FirstOrDefault(IsRos2MessageContract);
            return BuildMessage(
                type,
                contract,
                MetadataTypeName(type),
                new HashSet<string>(StringComparer.Ordinal));
        }

        private static FoxRunRos2MessageShape BuildMessage(
            Type type,
            Type contract,
            string path,
            ISet<string> stack)
        {
            var typeName = MetadataTypeName(type);
            var diagnostics = new List<string>();
            var implements = ImplementsContract(type, contract);
            var canonical = implements ? CanonicalRosType(type, path, diagnostics) : string.Empty;
            if (!implements)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    "FOXRUN038", path,
                    "Type must implement ROS2.Message from ros2cs_common by metadata identity."));
            }

            var hasConstructor = type != null
                && !type.IsAbstract
                && !type.IsInterface
                && type.GetConstructor(Type.EmptyTypes) != null;
            if (implements && !hasConstructor)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    "FOXRUN039", path,
                    "Native ROS2 message type requires a public parameterless constructor."));
            }

            var members = new List<FoxRunRos2MessageMemberShape>();
            if (implements)
            {
                if (!stack.Add(typeName))
                {
                    diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                        "FOXRUN042", path,
                        "Native ROS2 message graph contains a recursive reference."));
                }
                else
                {
                    BuildMembers(type, contract, path, stack, members, diagnostics);
                    stack.Remove(typeName);
                }
            }

            var identity = diagnostics.Count == 0 ? BuildIdentity(canonical, members) : string.Empty;
            return new FoxRunRos2MessageShape(
                "global::" + typeName,
                canonical,
                hasConstructor,
                implements,
                identity,
                members,
                diagnostics);
        }

        private static void BuildMembers(
            Type type,
            Type contract,
            string rootPath,
            ISet<string> stack,
            ICollection<FoxRunRos2MessageMemberShape> members,
            ICollection<string> diagnostics)
        {
            foreach (var candidate in PublicInstanceMembers(type).OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                var path = rootPath + "." + candidate.Name;
                if (TrySequence(candidate.Type, out var elementType, out var representation, out var unsupportedRank))
                {
                    if (unsupportedRank)
                    {
                        AddUnsupported(path, "Only one-dimensional native ROS2 sequences are supported.", diagnostics);
                        continue;
                    }

                    var fixedArray = representation == FoxRunRos2SequenceRepresentation.Array
                        && candidate.CanRead
                        && !candidate.CanWrite;
                    if (!candidate.CanRead || (!candidate.CanWrite && !fixedArray))
                    {
                        AddNotWritable(path, diagnostics);
                        continue;
                    }

                    var nestedIdentity = string.Empty;
                    FoxRunRos2MessageShape nestedShape = null;
                    if (!IsSupportedLeaf(elementType) && ImplementsContract(elementType, contract))
                    {
                        var nested = BuildMessage(elementType, contract, path + "[]", stack);
                        foreach (var diagnostic in nested.Diagnostics)
                            diagnostics.Add(diagnostic);
                        nestedIdentity = nested.CopyShapeIdentity;
                        nestedShape = nested;
                    }
                    else if (!IsSupportedLeaf(elementType))
                    {
                        AddUnsupported(path, "Sequence element type '" + MetadataTypeName(elementType) + "' cannot be deep-copied safely.", diagnostics);
                        continue;
                    }

                    members.Add(new FoxRunRos2MessageMemberShape(
                        candidate.Name,
                        FoxRunRos2MessageMemberKind.Sequence,
                        MetadataTypeName(candidate.Type),
                        MetadataTypeName(elementType),
                        nestedIdentity,
                        candidate.CanRead,
                        candidate.CanWrite,
                        fixedArray ? FoxRunRos2SequenceRepresentation.FixedArray : representation,
                        nestedShape: nestedShape));
                    continue;
                }

                if (!candidate.CanRead || !candidate.CanWrite)
                {
                    AddNotWritable(path, diagnostics);
                    continue;
                }

                var leafKind = LeafKind(candidate.Type);
                if (leafKind != null)
                {
                    members.Add(new FoxRunRos2MessageMemberShape(
                        candidate.Name,
                        leafKind.Value,
                        MetadataTypeName(candidate.Type),
                        "",
                        "",
                        candidate.CanRead,
                        candidate.CanWrite));
                    continue;
                }

                if (ImplementsContract(candidate.Type, contract))
                {
                    var nested = BuildMessage(candidate.Type, contract, path, stack);
                    foreach (var diagnostic in nested.Diagnostics)
                        diagnostics.Add(diagnostic);
                    members.Add(new FoxRunRos2MessageMemberShape(
                        candidate.Name,
                        FoxRunRos2MessageMemberKind.NestedMessage,
                        MetadataTypeName(candidate.Type),
                        "",
                        nested.CopyShapeIdentity,
                        candidate.CanRead,
                        candidate.CanWrite,
                        nestedShape: nested));
                    continue;
                }

                AddUnsupported(path, "Unsupported native ROS2 message member type '" + MetadataTypeName(candidate.Type) + "'.", diagnostics);
            }
        }

        private static IEnumerable<ReflectedMember> PublicInstanceMembers(Type type)
        {
            var infrastructureMethods = Ros2csInfrastructureImplementationMethods(type);
            // Generated ros2cs payload properties are declared on the concrete
            // message. Members that actually implement ros2cs lifecycle/type-
            // support interfaces are not wire data and must never enter a copy.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags))
                yield return new ReflectedMember(field.Name, field.FieldType, true, !field.IsInitOnly);
            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length == 0
                    && !PropertyImplementsInfrastructureContract(property, infrastructureMethods))
                    yield return new ReflectedMember(
                        property.Name,
                        property.PropertyType,
                        property.GetMethod?.IsPublic == true,
                        IsWritableSetter(property.SetMethod));
            }
        }

        private static ISet<MethodInfo> Ros2csInfrastructureImplementationMethods(Type type)
        {
            var methods = new HashSet<MethodInfo>();
            foreach (var contract in type.GetInterfaces().Where(IsRos2csInfrastructureContract))
            {
                var map = type.GetInterfaceMap(contract);
                foreach (var target in map.TargetMethods)
                    methods.Add(target);
            }
            return methods;
        }

        private static bool IsRos2csInfrastructureContract(Type candidate)
            => candidate != null
               && string.Equals(candidate.Assembly.GetName().Name, Ros2MessageAssemblyName, StringComparison.Ordinal)
               && (string.Equals(candidate.FullName, Ros2MessageInternalsMetadataName, StringComparison.Ordinal)
                   || string.Equals(candidate.FullName, Ros2ExtendedDisposableMetadataName, StringComparison.Ordinal));

        private static bool PropertyImplementsInfrastructureContract(
            PropertyInfo property,
            ISet<MethodInfo> infrastructureMethods)
            => (property.GetMethod != null && infrastructureMethods.Contains(property.GetMethod))
               || (property.SetMethod != null && infrastructureMethods.Contains(property.SetMethod));

        private static bool IsWritableSetter(MethodInfo setter)
            => setter?.IsPublic == true
               && !setter.ReturnParameter
                   .GetRequiredCustomModifiers()
                   .Any(modifier => string.Equals(
                       modifier.FullName,
                       IsExternalInitMetadataName,
                       StringComparison.Ordinal));

        private static bool TrySequence(
            Type type,
            out Type elementType,
            out FoxRunRos2SequenceRepresentation representation,
            out bool unsupportedRank)
        {
            elementType = null;
            representation = FoxRunRos2SequenceRepresentation.None;
            unsupportedRank = false;
            if (type?.IsArray == true)
            {
                elementType = type.GetElementType();
                representation = FoxRunRos2SequenceRepresentation.Array;
                unsupportedRank = type.GetArrayRank() != 1;
                return true;
            }

            if (type?.IsGenericType == true
                && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                representation = FoxRunRos2SequenceRepresentation.List;
                return true;
            }

            return false;
        }

        private static FoxRunRos2MessageMemberKind? LeafKind(Type type)
        {
            if (type == typeof(string))
                return FoxRunRos2MessageMemberKind.String;
            if (type?.IsEnum == true)
                return FoxRunRos2MessageMemberKind.Enum;
            return IsScalar(type)
                ? FoxRunRos2MessageMemberKind.Scalar
                : (FoxRunRos2MessageMemberKind?)null;
        }

        private static bool IsSupportedLeaf(Type type) => LeafKind(type) != null;

        private static bool IsScalar(Type type)
            => type == typeof(bool)
               || type == typeof(byte)
               || type == typeof(sbyte)
               || type == typeof(short)
               || type == typeof(ushort)
               || type == typeof(int)
               || type == typeof(uint)
               || type == typeof(long)
               || type == typeof(ulong)
               || type == typeof(float)
               || type == typeof(double)
               || type == typeof(char);

        private static bool IsRos2MessageContract(Type candidate)
            => candidate != null
               && string.Equals(candidate.FullName, Ros2MessageMetadataName, StringComparison.Ordinal)
               && string.Equals(candidate.Assembly.GetName().Name, Ros2MessageAssemblyName, StringComparison.Ordinal);

        private static bool ImplementsContract(Type type, Type contract)
            => type != null && contract != null && contract.IsAssignableFrom(type);

        private static string CanonicalRosType(Type type, string path, ICollection<string> diagnostics)
        {
            var ns = type?.Namespace ?? string.Empty;
            const string suffix = ".msg";
            if (!ns.EndsWith(suffix, StringComparison.Ordinal)
                || ns.Length == suffix.Length
                || ns.Substring(0, ns.Length - suffix.Length).IndexOf('.') >= 0)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    "FOXRUN040", path,
                    "Native ROS2 message type must be declared directly in a <package>.msg namespace; srv/action types are unsupported."));
                return string.Empty;
            }

            return ns.Substring(0, ns.Length - suffix.Length) + "/msg/" + type.Name;
        }

        private static void AddNotWritable(string path, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                "FOXRUN028", path,
                "Native ROS2 message members must be both readable and writable, except getter-only fixed arrays."));

        private static void AddUnsupported(string path, string message, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode("FOXRUN042", path, message));

        private static string BuildIdentity(string canonical, IEnumerable<FoxRunRos2MessageMemberShape> members)
            => canonical + "|" + string.Join(";", members.Select(member =>
                member.Name + ":" + member.Kind + ":" + member.FullyQualifiedTypeName + ":"
                + member.SequenceRepresentation + ":" + member.SequenceElementTypeName + ":"
                + member.NestedShapeIdentity + ":" + member.CanRead + ":" + member.CanWrite + ":" + member.FixedSize));

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
