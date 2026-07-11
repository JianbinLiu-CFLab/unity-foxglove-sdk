// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Builds deterministic Protobuf descriptor sets for FoxRun manifest contracts.

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunProtobufContractBuilder
    {
        private const string PackageName = "unity2foxglove.foxrun";

        public static FoxRunProtobufContract Build(FoxRunProtobufContractInput contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (!string.Equals(contract.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))
                throw new ArgumentException("FoxRun Protobuf contracts must use protobuf encoding.", nameof(contract));

            var messageName = ToMessageName(contract.SchemaName, contract.DeclaringType);
            var message = new DescriptorProto { Name = messageName };
            var file = new FileDescriptorProto
            {
                Name = "foxrun/" + messageName + ".proto",
                Package = PackageName,
                Syntax = "proto3"
            };
            file.MessageType.Add(message);
            var usedNumbers = new Dictionary<int, string>();
            var namedTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            var usedTypeNames = new HashSet<string>(StringComparer.Ordinal) { messageName };

            foreach (var field in contract.Fields.OrderBy(field => field.MemberName, StringComparer.Ordinal))
            {
                var number = FoxRunProtobufFieldNumber.Resolve(
                    BuildFieldIdentity(contract, field),
                    field.ProtobufFieldNumber);
                if (usedNumbers.TryGetValue(number, out var existingMember))
                {
                    throw new InvalidOperationException(
                        "FoxRun Protobuf field-number collision between '" + existingMember + "' and '"
                        + field.MemberName + "'. Set ProtobufFieldNumber explicitly on the new member.");
                }

                usedNumbers.Add(number, field.MemberName);
                var descriptorField = new FieldDescriptorProto
                {
                    Name = ToFieldName(field.JsonName, field.MemberName),
                    JsonName = field.JsonName ?? string.Empty,
                    Number = number,
                    Label = field.IsArray
                        ? FieldDescriptorProto.Types.Label.Repeated
                        : FieldDescriptorProto.Types.Label.Optional
                };
                ApplyType(descriptorField, field.CanonicalType, field.TypeShape, file, namedTypes, usedTypeNames);
                message.Field.Add(descriptorField);
            }

            var descriptorSet = new FileDescriptorSet();
            descriptorSet.File.Add(file);
            return new FoxRunProtobufContract(
                PackageName + "." + messageName,
                descriptorSet.ToByteArray());
        }

        private static string BuildFieldIdentity(FoxRunProtobufContractInput contract, FoxRunProtobufFieldInput field)
        {
            return (contract.DeclaringType ?? string.Empty) + "|"
                   + (contract.Topic ?? string.Empty) + "|"
                   + (contract.SchemaName ?? string.Empty) + "|"
                   + (field.MemberName ?? string.Empty);
        }

        private static void ApplyType(
            FieldDescriptorProto field,
            string canonicalType,
            FoxRunProtobufTypeShape typeShape,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            ISet<string> usedTypeNames)
        {
            if (typeShape != null)
            {
                ApplyTypeShape(field, typeShape, file, namedTypes, usedTypeNames);
                return;
            }

            switch (canonicalType ?? string.Empty)
            {
                case "float32": field.Type = FieldDescriptorProto.Types.Type.Float; return;
                case "float64": field.Type = FieldDescriptorProto.Types.Type.Double; return;
                case "bool": field.Type = FieldDescriptorProto.Types.Type.Bool; return;
                case "uint8":
                case "uint16":
                case "uint32": field.Type = FieldDescriptorProto.Types.Type.Uint32; return;
                case "int8":
                case "int16":
                case "int32": field.Type = FieldDescriptorProto.Types.Type.Int32; return;
                case "uint64": field.Type = FieldDescriptorProto.Types.Type.Uint64; return;
                case "int64": field.Type = FieldDescriptorProto.Types.Type.Int64; return;
                case "string": field.Type = FieldDescriptorProto.Types.Type.String; return;
                case "unity.vector2.float32":
                    ApplyVectorType(field, file, namedTypes, usedTypeNames, "unity.vector2.float32", "Unity_Vector2", new[] { "x", "y" });
                    return;
                case "unity.vector3.float32":
                    ApplyVectorType(field, file, namedTypes, usedTypeNames, "unity.vector3.float32", "Unity_Vector3", new[] { "x", "y", "z" });
                    return;
                case "unity.quaternion.float32":
                    ApplyVectorType(field, file, namedTypes, usedTypeNames, "unity.quaternion.float32", "Unity_Quaternion", new[] { "x", "y", "z", "w" });
                    return;
                case "unity.color.float32":
                    ApplyVectorType(field, file, namedTypes, usedTypeNames, "unity.color.float32", "Unity_Color", new[] { "r", "g", "b", "a" });
                    return;
                default:
                    throw new InvalidOperationException(
                        "FoxRun Protobuf contract does not support canonical field type '" + canonicalType + "'.");
            }
        }

        private static void ApplyVectorType(
            FieldDescriptorProto field,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            ISet<string> usedTypeNames,
            string typeKey,
            string nestedName,
            IReadOnlyList<string> componentNames)
        {
            field.Type = FieldDescriptorProto.Types.Type.Message;
            field.TypeName = "." + PackageName + "." + EnsureTypeName(typeKey, nestedName, namedTypes, usedTypeNames);
            if (namedTypes.ContainsKey(typeKey + "#defined"))
                return;

            namedTypes[typeKey + "#defined"] = string.Empty;
            var nested = new DescriptorProto { Name = namedTypes[typeKey] };
            for (var index = 0; index < componentNames.Count; index++)
            {
                nested.Field.Add(new FieldDescriptorProto
                {
                    Name = componentNames[index],
                    JsonName = componentNames[index],
                    Number = index + 1,
                    Label = FieldDescriptorProto.Types.Label.Optional,
                    Type = FieldDescriptorProto.Types.Type.Float
                });
            }

            file.MessageType.Add(nested);
        }

        private static void ApplyTypeShape(
            FieldDescriptorProto field,
            FoxRunProtobufTypeShape shape,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            ISet<string> usedTypeNames)
        {
            switch (shape.Kind)
            {
                case FoxRunProtobufTypeShapeKind.Canonical:
                    ApplyType(field, shape.CanonicalType, null, file, namedTypes, usedTypeNames);
                    return;
                case FoxRunProtobufTypeShapeKind.Object:
                    field.Type = FieldDescriptorProto.Types.Type.Message;
                    field.TypeName = "." + PackageName + "." + EnsureObjectDescriptor(shape, file, namedTypes, usedTypeNames);
                    return;
                case FoxRunProtobufTypeShapeKind.Enum:
                    field.Type = FieldDescriptorProto.Types.Type.Enum;
                    field.TypeName = "." + PackageName + "." + EnsureEnumDescriptor(shape, file, namedTypes, usedTypeNames);
                    return;
                default:
                    throw new InvalidOperationException("FoxRun Protobuf type shape kind is not supported: " + shape.Kind + ".");
            }
        }

        private static string EnsureObjectDescriptor(
            FoxRunProtobufTypeShape shape,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            ISet<string> usedTypeNames)
        {
            var typeKey = "object|" + shape.TypeName;
            var name = EnsureTypeName(typeKey, shape.TypeName, namedTypes, usedTypeNames);
            if (namedTypes.ContainsKey(typeKey + "#defined"))
                return name;

            namedTypes[typeKey + "#defined"] = string.Empty;
            var message = new DescriptorProto { Name = name };
            file.MessageType.Add(message);
            var usedNumbers = new Dictionary<int, string>();
            foreach (var nestedField in shape.Fields.OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                var number = FoxRunProtobufFieldNumber.Resolve(
                    shape.TypeName + "|" + nestedField.MemberName,
                    nestedField.ProtobufFieldNumber);
                if (usedNumbers.TryGetValue(number, out var existingMember))
                {
                    throw new InvalidOperationException(
                        "FoxRun Protobuf field-number collision between '" + existingMember + "' and '"
                        + nestedField.MemberName + "' in DTO '" + shape.TypeName
                        + "'. Set ProtobufFieldNumber explicitly on the new DTO member.");
                }

                usedNumbers.Add(number, nestedField.MemberName);
                var descriptorField = new FieldDescriptorProto
                {
                    Name = ToFieldName(nestedField.JsonName, nestedField.MemberName),
                    JsonName = nestedField.JsonName ?? string.Empty,
                    Number = number,
                    Label = nestedField.Repeated
                        ? FieldDescriptorProto.Types.Label.Repeated
                        : FieldDescriptorProto.Types.Label.Optional
                };
                ApplyTypeShape(descriptorField, nestedField.TypeShape, file, namedTypes, usedTypeNames);
                message.Field.Add(descriptorField);
            }

            return name;
        }

        private static string EnsureEnumDescriptor(
            FoxRunProtobufTypeShape shape,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            ISet<string> usedTypeNames)
        {
            var typeKey = "enum|" + shape.TypeName;
            var name = EnsureTypeName(typeKey, shape.TypeName, namedTypes, usedTypeNames);
            if (namedTypes.ContainsKey(typeKey + "#defined"))
                return name;

            namedTypes[typeKey + "#defined"] = string.Empty;
            if (shape.EnumValues.Count == 0)
                throw new InvalidOperationException("FoxRun Protobuf enum '" + shape.TypeName + "' has no values.");

            var descriptor = new EnumDescriptorProto { Name = name };
            foreach (var value in shape.EnumValues.OrderBy(candidate => candidate.Number).ThenBy(candidate => candidate.Name, StringComparer.Ordinal))
            {
                descriptor.Value.Add(new EnumValueDescriptorProto
                {
                    Name = ToIdentifier(value.Name, "UNSPECIFIED", upperFirst: true),
                    Number = value.Number
                });
            }

            file.EnumType.Add(descriptor);
            return name;
        }

        private static string EnsureTypeName(
            string typeKey,
            string requestedName,
            IDictionary<string, string> namedTypes,
            ISet<string> usedTypeNames)
        {
            if (namedTypes.TryGetValue(typeKey, out var existing))
                return existing;

            var baseName = ToIdentifier((requestedName ?? string.Empty).Replace('.', '_'), "FoxRunType", upperFirst: true);
            var name = baseName;
            var suffix = 2;
            while (!usedTypeNames.Add(name))
                name = baseName + "_" + suffix++.ToString();
            namedTypes[typeKey] = name;
            return name;
        }

        private static string ToMessageName(string schemaName, string declaringType)
        {
            var source = string.IsNullOrWhiteSpace(schemaName) ? declaringType : schemaName;
            var separator = source == null ? -1 : source.LastIndexOf('.');
            return ToIdentifier(separator >= 0 ? source.Substring(separator + 1) : source, "FoxRunMessage", upperFirst: true);
        }

        private static string ToFieldName(string jsonName, string memberName)
        {
            var source = string.IsNullOrWhiteSpace(jsonName) ? memberName : jsonName;
            return ToIdentifier(source, "field", upperFirst: false);
        }

        private static string ToIdentifier(string value, string fallback, bool upperFirst)
        {
            var source = value ?? string.Empty;
            var characters = new List<char>(source.Length + 1);
            foreach (var character in source)
            {
                if (char.IsLetterOrDigit(character) || character == '_')
                    characters.Add(character);
                else
                    characters.Add('_');
            }

            if (characters.Count == 0)
                characters.AddRange(fallback);
            if (!char.IsLetter(characters[0]) && characters[0] != '_')
                characters.Insert(0, '_');
            if (upperFirst && char.IsLower(characters[0]))
                characters[0] = char.ToUpperInvariant(characters[0]);
            return new string(characters.ToArray());
        }
    }

    public sealed class FoxRunProtobufContract
    {
        public FoxRunProtobufContract(string messageFullName, byte[] fileDescriptorSet)
        {
            MessageFullName = messageFullName ?? string.Empty;
            FileDescriptorSet = fileDescriptorSet == null ? Array.Empty<byte>() : (byte[])fileDescriptorSet.Clone();
        }

        public string MessageFullName { get; }
        public byte[] FileDescriptorSet { get; }
    }

    public sealed class FoxRunProtobufContractInput
    {
        public FoxRunProtobufContractInput(
            string declaringType,
            string topic,
            string schemaName,
            IReadOnlyList<FoxRunProtobufFieldInput> fields)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Fields = new List<FoxRunProtobufFieldInput>(fields ?? Array.Empty<FoxRunProtobufFieldInput>()).AsReadOnly();
        }

        public string DeclaringType { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public IReadOnlyList<FoxRunProtobufFieldInput> Fields { get; }
        public string Encoding => FoxRunGenerationDescriptorConstants.ProtobufEncoding;
    }

    public sealed class FoxRunProtobufFieldInput
    {
        public FoxRunProtobufFieldInput(
            string jsonName,
            string memberName,
            string canonicalType,
            bool isArray,
            int protobufFieldNumber = 0,
            FoxRunProtobufTypeShape typeShape = null)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            CanonicalType = canonicalType ?? string.Empty;
            IsArray = isArray;
            ProtobufFieldNumber = protobufFieldNumber;
            TypeShape = typeShape;
        }

        public string JsonName { get; }
        public string MemberName { get; }
        public string CanonicalType { get; }
        public bool IsArray { get; }
        public int ProtobufFieldNumber { get; }
        public FoxRunProtobufTypeShape TypeShape { get; }
    }
}
