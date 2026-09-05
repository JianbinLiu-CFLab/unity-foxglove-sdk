// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Builds deterministic Protobuf descriptor sets for FoxRun manifest contracts.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunProtobufContractBuilder
    {
        private const string PackageName = "unity2foxglove.foxrun";

        /// <summary>
        /// Resolves the fully-qualified root message name stored in a Foxglove
        /// protobuf channel. The name must exist in the descriptor set, so an
        /// omitted logical schema receives a deterministic topic-qualified name.
        /// </summary>
        public static string ResolveMessageFullName(string schemaName, string declaringType, string topic)
        {
            var messageName = ToMessageName(schemaName, declaringType, topic);
            return PackageName + "." + messageName;
        }

        public static FoxRunProtobufContract Build(FoxRunProtobufContractInput contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (!string.Equals(contract.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))
                throw new ArgumentException("FoxRun Protobuf contracts must use protobuf encoding.", nameof(contract));

            var messageFullName = ResolveMessageFullName(
                contract.SchemaName,
                contract.DeclaringType,
                contract.Topic);
            var messageName = messageFullName.Substring(PackageName.Length + 1);
            var message = new DescriptorProto { Name = messageName };
            var file = new FileDescriptorProto
            {
                Name = "foxrun/" + messageName + ".proto",
                Package = PackageName,
                Syntax = "proto3"
            };
            file.MessageType.Add(message);
            var usedNumbers = new Dictionary<int, string>();
            var usedFieldNames = new Dictionary<string, string>(StringComparer.Ordinal);
            var namedTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            // Enum values are package-level siblings of their enum type in
            // Protobuf. Use one symbol table for generated types and values so
            // invalid duplicate package symbols fail before descriptor output.
            var usedSymbols = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [messageName] = "root message '" + messageName + "'"
            };
            var declaredEnumValueNames = new HashSet<string>(StringComparer.Ordinal);
            var visitedShapes = new HashSet<FoxRunTypeShape>();
            foreach (var field in contract.Fields)
            {
                CollectDeclaredEnumValueNames(
                    field?.TypeShape,
                    declaredEnumValueNames,
                    visitedShapes);
            }

            foreach (var field in contract.Fields.OrderBy(field => field.MemberName, StringComparer.Ordinal))
            {
                var number = FoxRunProtobufFieldNumber.Resolve(
                    BuildFieldIdentity(
                        contract.DeclaringType,
                        contract.Topic,
                        contract.SchemaName,
                        field.MemberName),
                    field.ProtobufFieldNumber);
                if (usedNumbers.TryGetValue(number, out var existingMember))
                {
                    throw new InvalidOperationException(
                        "FoxRun Protobuf field-number collision between '" + existingMember + "' and '"
                        + field.MemberName + "'. Set ProtobufFieldNumber explicitly on the new member.");
                }

                usedNumbers.Add(number, field.MemberName);
                var descriptorName = ToFieldName(field.JsonName, field.MemberName);
                ReserveIdentifier(
                    usedFieldNames,
                    descriptorName,
                    field.JsonName,
                    field.MemberName,
                    "root field");
                var descriptorField = new FieldDescriptorProto
                {
                    Name = descriptorName,
                    JsonName = field.JsonName ?? string.Empty,
                    Number = number,
                    Label = field.IsArray
                        ? FieldDescriptorProto.Types.Label.Repeated
                        : FieldDescriptorProto.Types.Label.Optional
                };
                ApplyType(
                    descriptorField,
                    field.CanonicalType,
                    ProtobufValueShape(field.TypeShape, field.IsArray),
                    field.ProtobufMetadata?.TypeMetadata,
                    file,
                    namedTypes,
                    usedSymbols,
                    declaredEnumValueNames);
                message.Field.Add(descriptorField);
            }

            var descriptorSet = new FileDescriptorSet();
            descriptorSet.File.Add(file);
            return new FoxRunProtobufContract(
                messageFullName,
                descriptorSet.ToByteArray());
        }

        private static void CollectDeclaredEnumValueNames(
            FoxRunTypeShape shape,
            ISet<string> names,
            ISet<FoxRunTypeShape> visited)
        {
            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            if (shape == null || !visited.Add(shape))
                return;

            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Enum:
                    foreach (var value in shape.EnumValues)
                    {
                        if (value == null)
                            continue;
                        names.Add(ToIdentifier(
                            value.Name,
                            "UNSPECIFIED",
                            upperFirst: true));
                    }
                    return;

                case FoxRunTypeShapeKind.Object:
                    foreach (var field in shape.Fields)
                    {
                        if (field != null)
                            CollectDeclaredEnumValueNames(field.TypeShape, names, visited);
                    }
                    return;

                case FoxRunTypeShapeKind.Collection:
                    CollectDeclaredEnumValueNames(shape.ElementShape, names, visited);
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Builds the stable root-field identity shared by descriptors and
        /// generated Protobuf readers and writers.
        /// </summary>
        public static string BuildFieldIdentity(
            string declaringType,
            string topic,
            string schemaName,
            string memberName)
        {
            return (declaringType ?? string.Empty) + "|"
                   + (topic ?? string.Empty) + "|"
                   + ResolveMessageFullName(schemaName, declaringType, topic) + "|"
                   + (memberName ?? string.Empty);
        }

        private static void ApplyType(
            FieldDescriptorProto field,
            string canonicalType,
            FoxRunTypeShape typeShape,
            FoxRunProtobufTypeMetadata protobufMetadata,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames)
        {
            if (typeShape != null)
            {
                ApplyTypeShape(
                    field,
                    typeShape,
                    protobufMetadata,
                    file,
                    namedTypes,
                    usedSymbols,
                    declaredEnumValueNames);
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
                    ApplyVectorType(field, file, namedTypes, usedSymbols, declaredEnumValueNames, "unity.vector2.float32", "Unity_Vector2", new[] { "x", "y" });
                    return;
                case "unity.vector3.float32":
                    ApplyVectorType(field, file, namedTypes, usedSymbols, declaredEnumValueNames, "unity.vector3.float32", "Unity_Vector3", new[] { "x", "y", "z" });
                    return;
                case "unity.quaternion.float32":
                    ApplyVectorType(field, file, namedTypes, usedSymbols, declaredEnumValueNames, "unity.quaternion.float32", "Unity_Quaternion", new[] { "x", "y", "z", "w" });
                    return;
                case "unity.color.float32":
                    ApplyVectorType(field, file, namedTypes, usedSymbols, declaredEnumValueNames, "unity.color.float32", "Unity_Color", new[] { "r", "g", "b", "a" });
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
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames,
            string typeKey,
            string nestedName,
            IReadOnlyList<string> componentNames)
        {
            field.Type = FieldDescriptorProto.Types.Type.Message;
            field.TypeName = "." + PackageName + "." + EnsureTypeName(
                typeKey,
                nestedName,
                namedTypes,
                usedSymbols,
                declaredEnumValueNames);
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
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata protobufMetadata,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames)
        {
            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    ApplyType(field, shape.CanonicalType, null, null, file, namedTypes, usedSymbols, declaredEnumValueNames);
                    return;
                case FoxRunTypeShapeKind.Object:
                    field.Type = FieldDescriptorProto.Types.Type.Message;
                    field.TypeName = "." + PackageName + "." + EnsureObjectDescriptor(
                        shape,
                        protobufMetadata,
                        file,
                        namedTypes,
                        usedSymbols,
                        declaredEnumValueNames);
                    return;
                case FoxRunTypeShapeKind.Enum:
                    field.Type = FieldDescriptorProto.Types.Type.Enum;
                    field.TypeName = "." + PackageName + "." + EnsureEnumDescriptor(
                        shape,
                        file,
                        namedTypes,
                        usedSymbols,
                        declaredEnumValueNames);
                    return;
                case FoxRunTypeShapeKind.Collection:
                    throw new InvalidOperationException(
                        "FoxRun Protobuf collection metadata must be separated from its element type before descriptor emission.");
                default:
                    throw new InvalidOperationException("FoxRun Protobuf type shape kind is not supported: " + shape.Kind + ".");
            }
        }

        private static string EnsureObjectDescriptor(
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata protobufMetadata,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames)
        {
            var typeKey = "object|" + shape.TypeName;
            var name = EnsureTypeName(
                typeKey,
                shape.TypeName,
                namedTypes,
                usedSymbols,
                declaredEnumValueNames);
            var identityKey = typeKey + "#identity";
            var identity = FoxRunProtobufObjectShapeIdentity.Build(
                shape,
                protobufMetadata);
            if (namedTypes.ContainsKey(typeKey + "#defined"))
            {
                if (!namedTypes.TryGetValue(identityKey, out var existingIdentity)
                    || !string.Equals(existingIdentity, identity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "FoxRun Protobuf object type '" + shape.TypeName
                        + "' was reused with an inconsistent shape or Protobuf metadata contract.");
                }
                return name;
            }

            namedTypes[identityKey] = identity;
            namedTypes[typeKey + "#defined"] = string.Empty;
            var message = new DescriptorProto { Name = name };
            file.MessageType.Add(message);
            var usedNumbers = new Dictionary<int, string>();
            var usedFieldNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var nestedField in shape.Fields.OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                var fieldMetadata = protobufMetadata?.Find(
                    nestedField.MemberName,
                    nestedField.JsonName);
                var number = FoxRunProtobufFieldNumber.Resolve(
                    shape.TypeName + "|" + nestedField.MemberName,
                    fieldMetadata?.FieldNumber ?? 0);
                if (usedNumbers.TryGetValue(number, out var existingMember))
                {
                    throw new InvalidOperationException(
                        "FoxRun Protobuf field-number collision between '" + existingMember + "' and '"
                        + nestedField.MemberName + "' in DTO '" + shape.TypeName
                        + "'. Set ProtobufFieldNumber explicitly on the new DTO member.");
                }

                usedNumbers.Add(number, nestedField.MemberName);
                var descriptorName = ToFieldName(nestedField.JsonName, nestedField.MemberName);
                ReserveIdentifier(
                    usedFieldNames,
                    descriptorName,
                    nestedField.JsonName,
                    nestedField.MemberName,
                    "field in DTO '" + shape.TypeName + "'");
                var descriptorField = new FieldDescriptorProto
                {
                    Name = descriptorName,
                    JsonName = nestedField.JsonName ?? string.Empty,
                    Number = number,
                    Label = nestedField.Repeated
                        ? FieldDescriptorProto.Types.Label.Repeated
                        : FieldDescriptorProto.Types.Label.Optional
                };
                ApplyTypeShape(
                    descriptorField,
                    ProtobufValueShape(nestedField.TypeShape, nestedField.Repeated),
                    fieldMetadata?.TypeMetadata,
                    file,
                    namedTypes,
                    usedSymbols,
                    declaredEnumValueNames);
                message.Field.Add(descriptorField);
            }

            return name;
        }

        private static FoxRunTypeShape ProtobufValueShape(
            FoxRunTypeShape shape,
            bool repeated)
        {
            if (!repeated || shape == null)
                return shape;
            if (shape.Kind != FoxRunTypeShapeKind.Collection || shape.ElementShape == null)
            {
                throw new InvalidOperationException(
                    "FoxRun repeated Protobuf metadata requires a collection type shape with an element shape.");
            }
            return shape.ElementShape;
        }

        private static string EnsureEnumDescriptor(
            FoxRunTypeShape shape,
            FileDescriptorProto file,
            IDictionary<string, string> namedTypes,
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames)
        {
            var typeKey = "enum|" + shape.TypeName;
            var name = EnsureTypeName(
                typeKey,
                shape.TypeName,
                namedTypes,
                usedSymbols,
                declaredEnumValueNames);
            if (namedTypes.ContainsKey(typeKey + "#defined"))
                return name;

            namedTypes[typeKey + "#defined"] = string.Empty;
            if (shape.EnumValues.Count == 0)
                throw new InvalidOperationException("FoxRun Protobuf enum '" + shape.TypeName + "' has no values.");

            var descriptor = new EnumDescriptorProto { Name = name };
            var ordered = shape.EnumValues
                .OrderBy(candidate => candidate.Number)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ToList();
            var zero = ordered.FirstOrDefault(candidate => candidate.Number == 0);
            if (zero == null)
            {
                var syntheticName = SyntheticUnspecifiedName(
                    ordered,
                    usedSymbols,
                    declaredEnumValueNames);
                ReserveIdentifier(
                    usedSymbols,
                    syntheticName,
                    syntheticName,
                    syntheticName,
                    "value in enum '" + shape.TypeName + "'");
                descriptor.Value.Add(new EnumValueDescriptorProto
                {
                    Name = syntheticName,
                    Number = 0
                });
            }
            else
            {
                AppendEnumValue(descriptor, zero, usedSymbols, shape.TypeName);
            }

            foreach (var value in ordered)
            {
                if (ReferenceEquals(value, zero))
                    continue;
                AppendEnumValue(descriptor, value, usedSymbols, shape.TypeName);
            }

            file.EnumType.Add(descriptor);
            return name;
        }

        private static void AppendEnumValue(
            EnumDescriptorProto descriptor,
            FoxRunEnumValue value,
            IDictionary<string, string> usedSymbols,
            string enumTypeName)
        {
            var descriptorName = ToIdentifier(
                value.Name,
                "UNSPECIFIED",
                upperFirst: true);
            ReserveIdentifier(
                usedSymbols,
                descriptorName,
                value.Name,
                value.Name,
                "value in enum '" + enumTypeName + "'");
            descriptor.Value.Add(new EnumValueDescriptorProto
            {
                Name = descriptorName,
                Number = value.Number
            });
        }

        private static void ReserveIdentifier(
            IDictionary<string, string> usedNames,
            string descriptorName,
            string declaredName,
            string memberName,
            string scope)
        {
            var source = string.IsNullOrWhiteSpace(declaredName)
                ? memberName ?? string.Empty
                : declaredName;
            if (usedNames.TryGetValue(descriptorName, out var existing))
            {
                throw new InvalidOperationException(
                    "FoxRun Protobuf identifier collision in " + scope + ": '"
                    + existing + "' and '" + source + "' both normalize to '"
                    + descriptorName + "'.");
            }

            usedNames.Add(descriptorName, source);
        }

        private static string SyntheticUnspecifiedName(
            IReadOnlyList<FoxRunEnumValue> declaredValues,
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames)
        {
            var declaredNames = new HashSet<string>(
                declaredValues.Select(value =>
                    ToIdentifier(value.Name, "UNSPECIFIED", upperFirst: true)),
                StringComparer.Ordinal);
            var baseName = "UNSPECIFIED";
            var candidate = baseName;
            var suffix = 2;
            while (declaredNames.Contains(candidate)
                   || declaredEnumValueNames.Contains(candidate)
                   || usedSymbols.ContainsKey(candidate))
                candidate = baseName + "_" + suffix++.ToString(CultureInfo.InvariantCulture);
            return candidate;
        }

        private static string EnsureTypeName(
            string typeKey,
            string requestedName,
            IDictionary<string, string> namedTypes,
            IDictionary<string, string> usedSymbols,
            ISet<string> declaredEnumValueNames)
        {
            if (namedTypes.TryGetValue(typeKey, out var existing))
                return existing;

            var baseName = ToIdentifier((requestedName ?? string.Empty).Replace('.', '_'), "FoxRunType", upperFirst: true);
            var name = baseName;
            var suffix = 2;
            while (usedSymbols.ContainsKey(name)
                   || declaredEnumValueNames.Contains(name))
                name = baseName + "_" + suffix++.ToString(CultureInfo.InvariantCulture);
            usedSymbols.Add(name, "type '" + (requestedName ?? string.Empty) + "'");
            namedTypes[typeKey] = name;
            return name;
        }

        private static string ToMessageName(string schemaName, string declaringType, string topic)
        {
            if (!string.IsNullOrWhiteSpace(schemaName))
            {
                var source = schemaName.StartsWith(PackageName + ".", StringComparison.Ordinal)
                    ? schemaName.Substring(PackageName.Length + 1)
                    : schemaName;
                var separator = source.LastIndexOf('.');
                return ToIdentifier(
                    separator >= 0 ? source.Substring(separator + 1) : source,
                    "FoxRunMessage",
                    upperFirst: true);
            }

            var typeName = ToIdentifier(
                (declaringType ?? string.Empty).Replace('.', '_'),
                "FoxRunMessage",
                upperFirst: true);
            return typeName + "_" + ComputeStableTopicHash(topic).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static uint ComputeStableTopicHash(string topic)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var character in topic ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash;
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
                if (character > 0x7f)
                {
                    throw new InvalidOperationException(
                        "FoxRun Protobuf identifiers must use the ASCII identifier grammar; invalid value '"
                        + source + "'.");
                }

                if (IsAsciiLetter(character)
                    || IsAsciiDigit(character)
                    || character == '_')
                    characters.Add(character);
                else
                    characters.Add('_');
            }

            if (characters.Count == 0)
                characters.AddRange(fallback);
            if (!IsAsciiLetter(characters[0]) && characters[0] != '_')
                characters.Insert(0, '_');
            if (upperFirst && char.IsLower(characters[0]))
                characters[0] = char.ToUpperInvariant(characters[0]);
            return new string(characters.ToArray());
        }

        private static bool IsAsciiLetter(char value)
            => (value >= 'A' && value <= 'Z')
               || (value >= 'a' && value <= 'z');

        private static bool IsAsciiDigit(char value)
            => value >= '0' && value <= '9';
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
            FoxRunTypeShape typeShape = null,
            FoxRunProtobufMetadata protobufMetadata = null)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            CanonicalType = canonicalType ?? string.Empty;
            IsArray = isArray;
            TypeShape = typeShape;
            ProtobufMetadata = protobufMetadata
                               ?? FoxRunProtobufMetadata.FromTypeShape(
                                   typeShape,
                                   protobufFieldNumber);
        }

        public string JsonName { get; }
        public string MemberName { get; }
        public string CanonicalType { get; }
        public bool IsArray { get; }
        public int ProtobufFieldNumber => ProtobufMetadata?.FieldNumber ?? 0;
        public FoxRunTypeShape TypeShape { get; }
        public FoxRunProtobufMetadata ProtobufMetadata { get; }
    }
}
