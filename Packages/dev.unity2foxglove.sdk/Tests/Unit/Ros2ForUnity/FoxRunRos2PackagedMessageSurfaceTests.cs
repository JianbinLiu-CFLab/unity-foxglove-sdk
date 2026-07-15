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
    public sealed class FoxRunRos2PackagedMessageSurfaceTests
    {
        private static readonly string[] ExpectedSurface =
        {
            "distro=humble",
            "distro=jazzy",
            "distro=lyrical",
            "subscription=CreateSubscription<T>(System.String,System.Action<T>,ROS2.QualityOfServiceProfile=null) where T:ROS2.Message,new()",
            "subscription-remove=RemoveSubscription(ROS2.ISubscriptionBase):System.Boolean",
            "message=std_msgs.msg.String;Data:System.String{get;set}",
            "message=geometry_msgs.msg.Vector3;X:System.Double{get;set};Y:System.Double{get;set};Z:System.Double{get;set}",
            "message=geometry_msgs.msg.Quaternion;W:System.Double{get;set};X:System.Double{get;set};Y:System.Double{get;set};Z:System.Double{get;set}",
            "message=std_msgs.msg.Header;Frame_id:System.String{get;set};Stamp:builtin_interfaces.msg.Time{get;set}",
            "message=builtin_interfaces.msg.Time;Nanosec:System.UInt32{get;set};Sec:System.Int32{get;set}",
            "message=geometry_msgs.msg.Twist;Angular:geometry_msgs.msg.Vector3{get;set};Linear:geometry_msgs.msg.Vector3{get;set}",
            "message=sensor_msgs.msg.Joy;Axes:System.Single[]{get;set};Buttons:System.Int32[]{get;set};Header:std_msgs.msg.Header{get;set}",
            "message=sensor_msgs.msg.Imu;Angular_velocity:geometry_msgs.msg.Vector3{get;set};Angular_velocity_covariance:System.Double[]{get};Header:std_msgs.msg.Header{get;set};Linear_acceleration:geometry_msgs.msg.Vector3{get;set};Linear_acceleration_covariance:System.Double[]{get};Orientation:geometry_msgs.msg.Quaternion{get;set};Orientation_covariance:System.Double[]{get}",
        };

        [Fact]
        public void PackagedRuntimesExposeTheRequiredCommonMetadataSurface()
        {
            Assert.Equal(ExpectedSurface, ReadActualSurface());
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void PackagedCoreExposesCompatibleQosPolicies(string distro)
        {
            using var stream = File.OpenRead(CoreAssemblyPath(distro));
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();

            AssertEnumMembers(reader, "ROS2.QosPresetProfile",
                "DEFAULT", "PARAMETERS", "PARAMETER_EVENTS", "SENSOR_DATA", "SERVICES_DEFAULT", "SYSTEM_DEFAULT");
            AssertEnumMembers(reader, "ROS2.HistoryPolicy",
                "QOS_POLICY_HISTORY_KEEP_ALL", "QOS_POLICY_HISTORY_KEEP_LAST", "QOS_POLICY_HISTORY_SYSTEM_DEFAULT");
            AssertEnumMembers(reader, "ROS2.ReliabilityPolicy",
                "QOS_POLICY_RELIABILITY_BEST_EFFORT", "QOS_POLICY_RELIABILITY_RELIABLE", "QOS_POLICY_RELIABILITY_SYSTEM_DEFAULT");
            AssertEnumMembers(reader, "ROS2.DurabilityPolicy",
                "QOS_POLICY_DURABILITY_SYSTEM_DEFAULT", "QOS_POLICY_DURABILITY_TRANSIENT_LOCAL", "QOS_POLICY_DURABILITY_VOLATILE");
            AssertEnumMembers(reader, "ROS2.LivelinessPolicy",
                "QOS_POLICY_LIVELINESS_AUTOMATIC", "QOS_POLICY_LIVELINESS_MANUAL_BY_TOPIC", "QOS_POLICY_LIVELINESS_SYSTEM_DEFAULT");

            var qos = FindType(reader, "ROS2.QualityOfServiceProfile");
            AssertPublicMethod(reader, qos, ".ctor", "System.Void", "ROS2.QosPresetProfile");
            AssertPublicMethod(reader, qos, "SetHistory", "System.Void", "ROS2.HistoryPolicy", "System.Int32");
            AssertPublicMethod(reader, qos, "SetReliability", "System.Void", "ROS2.ReliabilityPolicy");
            AssertPublicMethod(reader, qos, "SetDurability", "System.Void", "ROS2.DurabilityPolicy");
            AssertPublicMethod(reader, qos, "SetPolicies", "System.Void",
                "ROS2.HistoryPolicy", "System.Int32", "ROS2.ReliabilityPolicy", "ROS2.DurabilityPolicy");
            AssertPublicMethod(reader, qos, "SetDeadline", "System.Void", "System.TimeSpan");
            AssertPublicMethod(reader, qos, "SetLifespan", "System.Void", "System.TimeSpan");
            AssertPublicMethod(reader, qos, "SetLiveliness", "System.Void", "ROS2.LivelinessPolicy");
            AssertPublicMethod(reader, qos, "SetLivelinessLeaseDuration", "System.Void", "System.TimeSpan");
            AssertPublicMethod(reader, qos, "Dispose", "System.Void");
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void SubscriptionTriggerCallbackDisposesCallbackMessageInFinally(string distro)
        {
            using var stream = File.OpenRead(CoreAssemblyPath(distro));
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var subscription = reader.GetTypeDefinition(FindType(reader, "ROS2.Subscription`1"));
            var triggerHandle = subscription.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == "TriggerCallback");
            var trigger = reader.GetMethodDefinition(triggerHandle);
            var body = peReader.GetMethodBody(trigger.RelativeVirtualAddress);
            var finallyRegion = Assert.Single(body.ExceptionRegions.Where(region => region.Kind == ExceptionRegionKind.Finally));
            var il = body.GetILBytes();
            var tryCalls = ReadCalledMethods(reader, il, finallyRegion.TryOffset, finallyRegion.TryLength);
            var finallyCalls = ReadCalledMethods(reader, il, finallyRegion.HandlerOffset, finallyRegion.HandlerLength);

            Assert.Contains("ROS2.Internal.MessageInternals::ReadNativeMessage", tryCalls);
            Assert.Contains(tryCalls, method =>
                method.StartsWith("System.Action`1", StringComparison.Ordinal)
                && method.EndsWith("::Invoke", StringComparison.Ordinal));
            Assert.Contains("System.IDisposable::Dispose", finallyCalls);
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void AcceptedMessagesImplementOwnedDisposableContractAndHavePublicConstructors(string distro)
        {
            foreach (var message in MessageLocations(distro))
            {
                using var stream = File.OpenRead(message.AssemblyPath);
                using var peReader = new PEReader(stream);
                var reader = peReader.GetMetadataReader();
                var typeHandle = FindType(reader, message.TypeName);
                var type = reader.GetTypeDefinition(typeHandle);
                var interfaces = type.GetInterfaceImplementations()
                    .Select(handle => ResolveTypeName(reader, reader.GetInterfaceImplementation(handle).Interface))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                Assert.Contains("ROS2.Message", interfaces);
                Assert.Contains("ROS2.Internal.MessageInternals", interfaces);
                Assert.Contains("ROS2.IExtendedDisposable", interfaces);
                Assert.Contains("System.IDisposable", interfaces);
                Assert.Contains(type.GetMethods(), handle =>
                {
                    var method = reader.GetMethodDefinition(handle);
                    return reader.GetString(method.Name) == ".ctor"
                           && IsPublic(method.Attributes)
                           && method.DecodeSignature(new MetadataTypeNameProvider(), null).ParameterTypes.Length == 0;
                });

                var publicFields = type.GetFields().Where(handle =>
                    (reader.GetFieldDefinition(handle).Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public);
                Assert.Empty(publicFields);
            }
        }

        [Theory]
        [InlineData("humble")]
        [InlineData("jazzy")]
        [InlineData("lyrical")]
        public void GeneratedDisposeCascadesDirectNestedMessagesButNeedsExplicitSequenceOwnership(string distro)
        {
            foreach (var message in MessageLocations(distro))
            {
                using var stream = File.OpenRead(message.AssemblyPath);
                using var peReader = new PEReader(stream);
                var reader = peReader.GetMetadataReader();
                var type = reader.GetTypeDefinition(FindType(reader, message.TypeName));
                var dispose = type.GetMethods().Select(reader.GetMethodDefinition).Single(method =>
                    reader.GetString(method.Name) == "Dispose" && IsPublic(method.Attributes));
                var body = peReader.GetMethodBody(dispose.RelativeVirtualAddress);
                var calls = ReadCalledMethods(reader, body.GetILBytes(), 0, body.GetILBytes().Length);

                Assert.Contains(calls, method =>
                    method.EndsWith("::DisposeAllOwnedSequenceElements", StringComparison.Ordinal));
                if (message.TypeName is "geometry_msgs.msg.Twist"
                    or "sensor_msgs.msg.Joy"
                    or "sensor_msgs.msg.Imu"
                    or "std_msgs.msg.Header")
                    Assert.Contains(calls, method => method.EndsWith("::Dispose", StringComparison.Ordinal));
            }
        }

        private static string[] ReadActualSurface()
        {
            var distros = new[] { "humble", "jazzy", "lyrical" };
            var packageSurfaces = distros.Select(ReadPackageSurface).ToArray();
            var common = packageSurfaces[0];

            for (var i = 1; i < packageSurfaces.Length; i++)
            {
                if (!common.SequenceEqual(packageSurfaces[i], StringComparer.Ordinal))
                {
                    return new[]
                    {
                        "surface mismatch between " + distros[0] + " and " + distros[i],
                        string.Join("\n", common),
                        string.Join("\n", packageSurfaces[i]),
                    };
                }
            }

            return distros.Select(distro => "distro=" + distro).Concat(common).ToArray();
        }

        private static string[] ReadPackageSurface(string distro)
        {
            var runtimeRoot = RuntimeRoot(distro);
            var plugins = Path.Combine(runtimeRoot, "Plugins");

            return new[]
            {
                ReadSubscriptionSignature(Path.Combine(runtimeRoot, "Scripts", "ROS2Node.cs")),
                ReadRemoveSubscriptionSignature(Path.Combine(runtimeRoot, "Scripts", "ROS2Node.cs")),
                ReadMessageShape(Path.Combine(plugins, "std_msgs_assembly.dll"), "std_msgs.msg.String", "Data"),
                ReadMessageShape(Path.Combine(plugins, "geometry_msgs_assembly.dll"), "geometry_msgs.msg.Vector3", "X", "Y", "Z"),
                ReadMessageShape(Path.Combine(plugins, "geometry_msgs_assembly.dll"), "geometry_msgs.msg.Quaternion", "W", "X", "Y", "Z"),
                ReadMessageShape(Path.Combine(plugins, "std_msgs_assembly.dll"), "std_msgs.msg.Header", "Frame_id", "Stamp"),
                ReadMessageShape(Path.Combine(plugins, "builtin_interfaces_assembly.dll"), "builtin_interfaces.msg.Time", "Nanosec", "Sec"),
                ReadMessageShape(Path.Combine(plugins, "geometry_msgs_assembly.dll"), "geometry_msgs.msg.Twist", "Angular", "Linear"),
                ReadMessageShape(Path.Combine(plugins, "sensor_msgs_assembly.dll"), "sensor_msgs.msg.Joy", "Axes", "Buttons", "Header"),
                ReadMessageShape(
                    Path.Combine(plugins, "sensor_msgs_assembly.dll"),
                    "sensor_msgs.msg.Imu",
                    "Angular_velocity",
                    "Angular_velocity_covariance",
                    "Header",
                    "Linear_acceleration",
                    "Linear_acceleration_covariance",
                    "Orientation",
                    "Orientation_covariance"),
            };
        }

        private static string ReadSubscriptionSignature(string sourcePath)
        {
            var method = ReadMethod(sourcePath, "CreateSubscription", 3, genericArity: 1);
            Require(method.ReturnType.ToString() == "Subscription<T>", sourcePath + " has an unexpected subscription return type.");
            Require(method.ParameterList.Parameters[0].Type.ToString() == "string", sourcePath + " has an unexpected topic parameter.");
            Require(method.ParameterList.Parameters[1].Type.ToString() == "Action<T>", sourcePath + " has an unexpected callback parameter.");
            Require(method.ParameterList.Parameters[2].Type.ToString() == "QualityOfServiceProfile", sourcePath + " has an unexpected QoS parameter.");
            Require(method.ParameterList.Parameters[2].Default?.Value.ToString() == "null", sourcePath + " must default QoS to null.");

            var constraint = method.ConstraintClauses.Single();
            var constraintTypes = constraint.Constraints.Select(item => item.ToString()).ToArray();
            Require(constraintTypes.SequenceEqual(new[] { "Message", "new()" }), sourcePath + " has unexpected generic constraints.");

            return "subscription=CreateSubscription<T>(System.String,System.Action<T>,ROS2.QualityOfServiceProfile=null) where T:ROS2.Message,new()";
        }

        private static string ReadRemoveSubscriptionSignature(string sourcePath)
        {
            var method = ReadMethod(sourcePath, "RemoveSubscription", 1, genericArity: 0);
            Require(method.ReturnType.ToString() == "bool", sourcePath + " has an unexpected remove return type.");
            Require(method.ParameterList.Parameters[0].Type.ToString() == "ISubscriptionBase", sourcePath + " has an unexpected remove parameter.");
            return "subscription-remove=RemoveSubscription(ROS2.ISubscriptionBase):System.Boolean";
        }

        private static MethodDeclarationSyntax ReadMethod(string sourcePath, string name, int parameterCount, int genericArity)
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(sourcePath)).GetCompilationUnitRoot();
            return root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == name
                                  && method.ParameterList.Parameters.Count == parameterCount
                                  && (method.TypeParameterList?.Parameters.Count ?? 0) == genericArity);
        }

        private static string ReadMessageShape(string assemblyPath, string typeName, params string[] dataPropertyNames)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var reader = peReader.GetMetadataReader();
            var typeHandle = reader.TypeDefinitions.Single(handle => GetFullName(reader, handle) == typeName);
            var type = reader.GetTypeDefinition(typeHandle);
            var provider = new MetadataTypeNameProvider();
            var properties = new List<string>();
            var publicDataPropertyNames = type.GetProperties()
                .Where(handle =>
                {
                    var property = reader.GetPropertyDefinition(handle);
                    var name = reader.GetString(property.Name);
                    return name is not ("IsDisposed" or "TypeSupportHandle" or "Handle")
                           && IsPublic(reader, property.GetAccessors().Getter);
                })
                .Select(handle => reader.GetString(reader.GetPropertyDefinition(handle).Name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Require(publicDataPropertyNames.SequenceEqual(
                    dataPropertyNames.OrderBy(name => name, StringComparer.Ordinal),
                    StringComparer.Ordinal),
                typeName + " has unexpected or missing public data properties.");

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var name = reader.GetString(property.Name);
                if (!dataPropertyNames.Contains(name, StringComparer.Ordinal))
                    continue;

                var accessors = property.GetAccessors();
                Require(IsPublic(reader, accessors.Getter), typeName + "." + name + " must have a public getter.");
                var signature = property.DecodeSignature(provider, genericContext: null);
                var access = IsPublic(reader, accessors.Setter) ? "{get;set}" : "{get}";
                properties.Add(name + ":" + signature.ReturnType + access);
            }

            properties.Sort(StringComparer.Ordinal);
            Require(properties.Count == dataPropertyNames.Length, typeName + " is missing one or more required data properties.");
            return "message=" + typeName + ";" + string.Join(";", properties);
        }

        private static bool IsPublic(MetadataReader reader, MethodDefinitionHandle handle)
        {
            if (handle.IsNil)
                return false;

            var attributes = reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
            return attributes == MethodAttributes.Public;
        }

        private static bool IsPublic(MethodAttributes attributes)
        {
            return (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
        }

        private static void AssertEnumMembers(MetadataReader reader, string typeName, params string[] expected)
        {
            var type = reader.GetTypeDefinition(FindType(reader, typeName));
            var actual = type.GetFields()
                .Where(handle =>
                {
                    var attributes = reader.GetFieldDefinition(handle).Attributes;
                    return (attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public
                           && (attributes & FieldAttributes.Static) != 0;
                })
                .Select(handle => reader.GetString(reader.GetFieldDefinition(handle).Name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
        }

        private static void AssertPublicMethod(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            string methodName,
            string returnType,
            params string[] parameterTypes)
        {
            var provider = new MetadataTypeNameProvider();
            var matches = reader.GetTypeDefinition(typeHandle).GetMethods().Where(handle =>
            {
                var method = reader.GetMethodDefinition(handle);
                if (!IsPublic(method.Attributes) || reader.GetString(method.Name) != methodName)
                    return false;
                var signature = method.DecodeSignature(provider, null);
                return signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameterTypes);
            });
            Assert.Single(matches);
        }

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
