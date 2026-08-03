// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Schemas
// Purpose: Rejects ambiguous or incomplete Protobuf descriptor registries.

using System.IO;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Unity.FoxgloveSDK.Schemas;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Schemas")]
    public sealed class ProtobufSchemaRegistryValidationTests
    {
        [Fact]
        public void ConstructorRejectsMissingDescriptorDependencies()
        {
            var descriptorSet = new FileDescriptorSet();
            var root = CreateFile("root.proto", "phase187", "Root");
            root.Dependency.Add("missing.proto");
            descriptorSet.File.Add(root);

            var exception = Assert.Throws<InvalidDataException>(() => CreateRegistry(descriptorSet));

            Assert.Contains("missing.proto", exception.Message);
            Assert.Contains("root.proto", exception.Message);
        }

        [Fact]
        public void ConstructorRejectsDuplicateDescriptorFileNames()
        {
            var descriptorSet = new FileDescriptorSet();
            descriptorSet.File.Add(CreateFile("duplicate.proto", "phase187", "First"));
            descriptorSet.File.Add(CreateFile("duplicate.proto", "phase187", "Second"));

            var exception = Assert.Throws<InvalidDataException>(() => CreateRegistry(descriptorSet));

            Assert.Contains("duplicate.proto", exception.Message);
        }

        [Fact]
        public void ConstructorRejectsDuplicateFullyQualifiedMessageNames()
        {
            var descriptorSet = new FileDescriptorSet();
            descriptorSet.File.Add(CreateFile("first.proto", "phase187", "Duplicate"));
            descriptorSet.File.Add(CreateFile("second.proto", "phase187", "Duplicate"));

            var exception = Assert.Throws<InvalidDataException>(() => CreateRegistry(descriptorSet));

            Assert.Contains("phase187.Duplicate", exception.Message);
            Assert.Contains("first.proto", exception.Message);
            Assert.Contains("second.proto", exception.Message);
        }

        private static ProtobufSchemaRegistry CreateRegistry(FileDescriptorSet descriptorSet)
            => new ProtobufSchemaRegistry(descriptorSet.ToByteArray(), new DefaultSchemaRegistry());

        private static FileDescriptorProto CreateFile(string fileName, string packageName, string messageName)
        {
            var file = new FileDescriptorProto
            {
                Name = fileName,
                Package = packageName,
                Syntax = "proto3"
            };
            file.MessageType.Add(new DescriptorProto { Name = messageName });
            return file;
        }
    }
}
