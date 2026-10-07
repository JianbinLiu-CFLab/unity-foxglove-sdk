// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify the metadata-only and lifetime contract of tracked ROS2 For Unity runtimes.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity2Foxglove.Tests.Ros2ForUnity
{
    public sealed partial class FoxRunRos2PackagedMessageSurfaceTests
    {
        private static IReadOnlyList<string> ReadCalledMethods(
            MetadataReader reader,
            byte[] il,
            int start,
            int length)
        {
            var methods = new List<string>();
            var end = Math.Min(il.Length, start + length);
            var offset = start;
            while (offset < end)
            {
                var opcodeValue = (ushort)il[offset++];
                if (opcodeValue == 0xfe)
                {
                    Require(offset < end, "Truncated two-byte IL opcode.");
                    opcodeValue = (ushort)(0xfe00 | il[offset++]);
                }

                Require(IlOpCodes.TryGetValue(opcodeValue, out var opcode),
                    "Unknown IL opcode 0x" + opcodeValue.ToString("x4") + ".");
                var operandOffset = offset;
                var operandSize = GetOperandSize(opcode, il, operandOffset, end);
                Require(operandOffset + operandSize <= end,
                    "IL operand extends beyond the requested method region.");

                if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
                {
                    Require(operandSize == sizeof(int), "Call instruction must carry a metadata token.");
                    var token = BitConverter.ToInt32(il, operandOffset);
                    methods.Add(ResolveMethodIdentity(reader, MetadataTokens.EntityHandle(token)));
                }

                offset += operandSize;
            }
            return methods;
        }

        private static int GetOperandSize(OpCode opcode, byte[] il, int operandOffset, int end)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                case OperandType.InlineSwitch:
                    Require(operandOffset + sizeof(int) <= end, "Truncated IL switch operand.");
                    var count = BitConverter.ToInt32(il, operandOffset);
                    Require(count >= 0 && count <= (end - operandOffset - sizeof(int)) / sizeof(int),
                        "Invalid IL switch target count.");
                    return sizeof(int) + count * sizeof(int);
                default:
                    throw new InvalidDataException("Unsupported IL operand type " + opcode.OperandType + ".");
            }
        }

        private static string ResolveMethodIdentity(MetadataReader reader, EntityHandle handle)
        {
            if (handle.Kind == HandleKind.MethodSpecification)
                return ResolveMethodIdentity(reader, reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method);

            if (handle.Kind == HandleKind.MemberReference)
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                return ResolveTypeName(reader, member.Parent) + "::" + reader.GetString(member.Name);
            }

            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var methodHandle = (MethodDefinitionHandle)handle;
                foreach (var typeHandle in reader.TypeDefinitions)
                {
                    if (reader.GetTypeDefinition(typeHandle).GetMethods().Contains(methodHandle))
                    {
                        var method = reader.GetMethodDefinition(methodHandle);
                        return GetFullName(reader, typeHandle) + "::" + reader.GetString(method.Name);
                    }
                }
            }

            throw new InvalidDataException("Unsupported method metadata handle " + handle.Kind + ".");
        }

        private static TypeDefinitionHandle FindType(MetadataReader reader, string typeName)
        {
            return reader.TypeDefinitions.Single(handle => GetFullName(reader, handle) == typeName);
        }

        private static string ResolveTypeName(MetadataReader reader, EntityHandle handle)
        {
            var provider = new MetadataTypeNameProvider();
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => provider.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => provider.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => provider.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
                _ => handle.Kind.ToString(),
            };
        }

        private static string RuntimeRoot(string distro)
        {
            return Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64",
                "Runtime",
                "Ros2ForUnity");
        }

        private static string CoreAssemblyPath(string distro)
        {
            return Path.Combine(RuntimeRoot(distro), "Plugins", "ros2cs_core.dll");
        }

        private static IEnumerable<(string AssemblyPath, string TypeName)> MessageLocations(string distro)
        {
            var plugins = Path.Combine(RuntimeRoot(distro), "Plugins");
            yield return (Path.Combine(plugins, "std_msgs_assembly.dll"), "std_msgs.msg.String");
            yield return (Path.Combine(plugins, "geometry_msgs_assembly.dll"), "geometry_msgs.msg.Vector3");
            yield return (Path.Combine(plugins, "geometry_msgs_assembly.dll"), "geometry_msgs.msg.Quaternion");
            yield return (Path.Combine(plugins, "std_msgs_assembly.dll"), "std_msgs.msg.Header");
            yield return (Path.Combine(plugins, "builtin_interfaces_assembly.dll"), "builtin_interfaces.msg.Time");
            yield return (Path.Combine(plugins, "geometry_msgs_assembly.dll"), "geometry_msgs.msg.Twist");
            yield return (Path.Combine(plugins, "sensor_msgs_assembly.dll"), "sensor_msgs.msg.Joy");
            yield return (Path.Combine(plugins, "sensor_msgs_assembly.dll"), "sensor_msgs.msg.Imu");
        }

        private static string GetFullName(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(type.Namespace);
            var name = reader.GetString(type.Name);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string FindRepoRoot()
        {
            var overrideRoot = Environment.GetEnvironmentVariable("FOXGLOVE_REPO_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot) && IsRepoRoot(overrideRoot))
                return Path.GetFullPath(overrideRoot);

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (IsRepoRoot(directory.FullName))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory + ".");
        }

        private static bool IsRepoRoot(string path)
        {
            return File.Exists(Path.Combine(path, "README.md"))
                   && Directory.Exists(Path.Combine(path, "Packages"))
                   && Directory.Exists(Path.Combine(path, "Unity2Foxglove"));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidDataException(message);
        }

        private static readonly IReadOnlyDictionary<ushort, OpCode> IlOpCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null))
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));

        private sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string, object>
        {
            public string GetArrayType(string elementType, ArrayShape shape)
            {
                return elementType + "[" + new string(',', shape.Rank - 1) + "]";
            }

            public string GetByReferenceType(string elementType) => elementType + "&";

            public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";

            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            {
                return genericType + "<" + string.Join(",", typeArguments) + ">";
            }

            public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;

            public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;

            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

            public string GetPinnedType(string elementType) => elementType;

            public string GetPointerType(string elementType) => elementType + "*";

            public string GetPrimitiveType(PrimitiveTypeCode typeCode)
            {
                return typeCode switch
                {
                    PrimitiveTypeCode.Boolean => "System.Boolean",
                    PrimitiveTypeCode.Byte => "System.Byte",
                    PrimitiveTypeCode.Char => "System.Char",
                    PrimitiveTypeCode.Double => "System.Double",
                    PrimitiveTypeCode.Int16 => "System.Int16",
                    PrimitiveTypeCode.Int32 => "System.Int32",
                    PrimitiveTypeCode.Int64 => "System.Int64",
                    PrimitiveTypeCode.IntPtr => "System.IntPtr",
                    PrimitiveTypeCode.Object => "System.Object",
                    PrimitiveTypeCode.SByte => "System.SByte",
                    PrimitiveTypeCode.Single => "System.Single",
                    PrimitiveTypeCode.String => "System.String",
                    PrimitiveTypeCode.UInt16 => "System.UInt16",
                    PrimitiveTypeCode.UInt32 => "System.UInt32",
                    PrimitiveTypeCode.UInt64 => "System.UInt64",
                    PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                    PrimitiveTypeCode.Void => "System.Void",
                    _ => typeCode.ToString(),
                };
            }

            public string GetSZArrayType(string elementType) => elementType + "[]";

            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                return GetFullName(reader, handle);
            }

            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var type = reader.GetTypeReference(handle);
                var ns = reader.GetString(type.Namespace);
                var name = reader.GetString(type.Name);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }

            public string GetTypeFromSpecification(
                MetadataReader reader,
                object genericContext,
                TypeSpecificationHandle handle,
                byte rawTypeKind)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
            }
        }
    }
}
