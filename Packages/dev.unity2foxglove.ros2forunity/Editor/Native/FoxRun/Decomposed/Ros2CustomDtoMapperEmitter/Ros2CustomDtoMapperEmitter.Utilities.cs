// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Native/FoxRun
// Purpose: Phase192 partial source section for Ros2CustomDtoMapperEmitter.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class Ros2CustomDtoMapperEmitter
    {
        internal static string EnvelopeType(IFoxRunR2fuEmitterMember member)
            => CustomMessageNamespace + EnvelopeIdentity(member) + "Envelope";

        internal static string PayloadType(FoxRunRos2CustomDtoShape shape)
            => CustomMessageNamespace + RequirePayloadIdentity(shape);

        internal static string EnvelopeIdentity(IFoxRunR2fuEmitterMember member)
            => RequirePayloadIdentity(member.Ros2CustomDtoShape);

        internal static string CanonicalEnvelopeType(IFoxRunR2fuEmitterMember member)
            => RosPackageName + "/msg/" + EnvelopeIdentity(member) + "Envelope";

        internal static string CanonicalPayloadType(IFoxRunR2fuEmitterMember member)
            => RosPackageName + "/msg/" + RequirePayloadIdentity(member.Ros2CustomDtoShape);

        private static string RequirePayloadIdentity(FoxRunRos2CustomDtoShape shape)
        {
            if (shape == null || string.IsNullOrWhiteSpace(shape.PayloadIdentity))
                throw new InvalidOperationException("Custom ROS2 DTO shape has no payload identity.");
            return shape.PayloadIdentity;
        }

        private static string ModeLiteral(int mode)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunFlow)" + mode.ToString(CultureInfo.InvariantCulture);

        private static string ProviderLiteral(string provider)
        {
            if (string.Equals(
                    provider,
                    FoxRunR2fuGenerationConstants.ProviderId,
                    StringComparison.Ordinal))
                return NativeNamespace
                       + "FoxRunRos2RouteEndpoint.R2fu";
            if (string.Equals(
                    provider,
                    FoxRunR2fuGenerationConstants
                        .WebSocketProviderId,
                    StringComparison.Ordinal))
                return NativeNamespace
                       + "FoxRunRos2RouteEndpoint.WebSocket";
            return "("
                   + NativeNamespace
                   + "FoxRunRos2RouteEndpoint)0";
        }

        private static string EncodingLiteral(string encoding)
        {
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf";
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.JsonEncoding, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON";
            return "(global::Unity.FoxgloveSDK.Components.FoxRunEncoding)0";
        }

        private static string PresenceExpression(string source, FoxRunRos2CustomDtoMemberShape member)
        {
            if (TryUnwrapNullable(member.FullyQualifiedTypeName, out _))
                return source + ".HasValue";
            return source + " != null";
        }

        private static bool TryUnwrapNullable(string typeName, out string elementType)
        {
            const string prefix = "System.Nullable<";
            if (!string.IsNullOrWhiteSpace(typeName)
                && typeName.StartsWith(prefix, StringComparison.Ordinal)
                && typeName.EndsWith(">", StringComparison.Ordinal))
            {
                elementType = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);
                return true;
            }
            elementType = string.Empty;
            return false;
        }

        private static string RosProperty(string rosFieldName)
        {
            if (string.IsNullOrEmpty(rosFieldName))
                throw new InvalidOperationException("Custom ROS2 field name must not be empty.");
            return char.ToUpperInvariant(rosFieldName[0]) + rosFieldName.Substring(1);
        }

        private static string GlobalTypeName(string typeName)
        {
            var type = IdentifierUtils.EscapeTypeName(CSharpType(typeName));
            return type.StartsWith("global::", StringComparison.Ordinal) || IsKeywordType(type)
                ? type
                : "global::" + type;
        }

        private static string CSharpType(string typeName)
        {
            switch (typeName)
            {
                case "System.Boolean": return "bool";
                case "System.Byte": return "byte";
                case "System.SByte": return "sbyte";
                case "System.Int16": return "short";
                case "System.UInt16": return "ushort";
                case "System.Int32": return "int";
                case "System.UInt32": return "uint";
                case "System.Int64": return "long";
                case "System.UInt64": return "ulong";
                case "System.Single": return "float";
                case "System.Double": return "double";
                case "System.String": return "string";
                default: return string.IsNullOrWhiteSpace(typeName) ? "object" : typeName;
            }
        }

        private static bool IsKeywordType(string typeName)
        {
            switch (typeName)
            {
                case "bool": case "byte": case "sbyte": case "short": case "ushort":
                case "int": case "uint": case "long": case "ulong": case "float":
                case "double": case "string": case "object": return true;
                default: return false;
            }
        }

        private static string RosPrimitiveType(string rosType)
        {
            switch (rosType)
            {
                case "bool": return "bool";
                case "int8": return "sbyte";
                case "uint8": return "byte";
                case "int16": return "short";
                case "uint16": return "ushort";
                case "int32": return "int";
                case "uint32": return "uint";
                case "int64": return "long";
                case "uint64": return "ulong";
                case "float32": return "float";
                case "float64": return "double";
                default: return CSharpType(rosType);
            }
        }

        private static string ElementSizeLiteral(string elementTypeName)
        {
            switch (CSharpType(elementTypeName))
            {
                case "bool": case "byte": case "sbyte": return "1L";
                case "short": case "ushort": return "2L";
                case "int": case "uint": case "float": return "4L";
                case "long": case "ulong": case "double": return "8L";
                default: return "8L";
            }
        }

        private static string RosSequenceElementType(
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry)
        {
            if (member.NestedShape != null)
                return registry.Get(member.NestedShape).RosType;
            return RosPrimitiveType(StripSequenceSuffix(member.RosType));
        }

        private static bool IsStringSequence(FoxRunRos2CustomDtoMemberShape member)
            => string.Equals(
                StripSequenceSuffix(member.RosType),
                "string",
                StringComparison.Ordinal);

        private static bool IsEnumTypeName(string typeName)
        {
            var value = CSharpType(typeName);
            return !IsKeywordType(value)
                   && !string.Equals(value, "System.String", StringComparison.Ordinal)
                   && !string.Equals(value, "string", StringComparison.Ordinal);
        }

        private static string StripSequenceSuffix(string rosType)
        {
            var value = rosType ?? string.Empty;
            var bracket = value.IndexOf('[');
            return bracket < 0 ? value : value.Substring(0, bracket);
        }

        private sealed class ShapeRegistry
        {
            private readonly int _memberIndex;
            private readonly Dictionary<string, ShapeEntry> _byIdentity = new Dictionary<string, ShapeEntry>(StringComparer.Ordinal);
            private readonly List<ShapeEntry> _entries = new List<ShapeEntry>();

            public ShapeRegistry(int memberIndex)
            {
                _memberIndex = memberIndex;
            }

            public int Count => _entries.Count;
            public ShapeEntry this[int index] => _entries[index];

            public ShapeEntry Get(FoxRunRos2CustomDtoShape shape)
            {
                if (shape == null || string.IsNullOrWhiteSpace(shape.CanonicalIdentity))
                    throw new InvalidOperationException("Custom ROS2 DTO shape has no canonical identity.");
                if (_byIdentity.TryGetValue(shape.CanonicalIdentity, out var existing))
                    return existing;

                var suffix = _entries.Count == 0 ? string.Empty : "Nested_" + _entries.Count.ToString(CultureInfo.InvariantCulture);
                var entry = new ShapeEntry(this, shape, _memberIndex, suffix);
                _byIdentity.Add(shape.CanonicalIdentity, entry);
                _entries.Add(entry);
                foreach (var member in shape.Members)
                {
                    if (member.NestedShape != null)
                        Get(member.NestedShape);
                }
                return entry;
            }
        }

        private sealed class ShapeEntry
        {
            public ShapeEntry(ShapeRegistry registry, FoxRunRos2CustomDtoShape shape, int memberIndex, string suffix)
            {
                Registry = registry;
                Shape = shape;
                RosType = PayloadType(shape);
                DtoType = GlobalTypeName(shape.FullyQualifiedTypeName);
                var index = memberIndex.ToString(CultureInfo.InvariantCulture);
                var nestedPrefix = string.IsNullOrEmpty(suffix) ? string.Empty : suffix;
                DtoToRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "MapDtoToPayload_" + index;
                CopyRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "CopyPayload_" + index;
                RosToDtoMethod = "__FoxRunRos2Custom" + nestedPrefix + "MapPayloadToDto_" + index;
                DisposeRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "DisposePayload_" + index;
                EqualsRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "EqualsPayload_" + index;
            }

            public ShapeRegistry Registry { get; }
            public FoxRunRos2CustomDtoShape Shape { get; }
            public string RosType { get; }
            public string DtoType { get; }
            public string DtoToRosMethod { get; }
            public string CopyRosMethod { get; }
            public string RosToDtoMethod { get; }
            public string DisposeRosMethod { get; }
            public string EqualsRosMethod { get; }
        }

        private static string PolicyLiteral(int policy)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunPolicy)"
               + policy.ToString(CultureInfo.InvariantCulture);

        private static string TriggerFieldSuffix(IFoxRunR2fuEmitterMember member)
            => IdentifierUtils.SanitizeIdentifier(member.MemberName.TrimStart('_'))
               + "_"
               + TopicMetadataEmitter.Sha256Hex(
                       (member.MemberName ?? string.Empty) + "|" + (member.Topic ?? string.Empty))
                   .Substring(0, 8);
    }
}
