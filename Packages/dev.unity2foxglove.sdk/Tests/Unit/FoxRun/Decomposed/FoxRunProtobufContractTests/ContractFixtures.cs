// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks deterministic FoxRun Protobuf contract metadata before live transport support.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Google.Protobuf.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunProtobufContractTests
    {
        private static string ExtractAggregateSchemaName(string source)
        {
            var match = Regex.Match(
                source ?? string.Empty,
                @"\[FoxRunMessage\([^\]]*SchemaName\s*=\s*""(?<schema>[^""]+)""",
                RegexOptions.Singleline);
            Assert.True(match.Success, "Expected a FoxRunMessage SchemaName declaration.");
            return match.Groups["schema"].Value;
        }

        private static FoxRunProtobufContractInput CreateContract()
        {
            var fields = new List<FoxRunProtobufFieldInput>
            {
                new FoxRunProtobufFieldInput("count", "_count", "int32", false, protobufFieldNumber: 17),
                new FoxRunProtobufFieldInput("position", "_position", "unity.vector3.float32", false),
                new FoxRunProtobufFieldInput("samples", "_samples", "float32", true)
            };
            return new FoxRunProtobufContractInput(
                "Demo.WireState",
                "/phase175/wire_state",
                "Demo.WireState",
                fields);
        }

        private static FoxRunManifestMember MessagePackLogicalIdentityMember(
            string memberName,
            string jsonName,
            string schemaName)
            => new FoxRunManifestMember(
                "Demo",
                "LogicalOwner",
                memberName,
                "field",
                "System.Int32",
                true,
                false,
                string.Empty,
                "/phase185/logical-identity",
                10f,
                schemaName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                jsonFieldName: jsonName,
                encoding: (int)FoxRunEncoding.MessagePack,
                typeShape: FoxRunTypeShape.Canonical("int32"));

        private static FoxRunCanonicalManifest BuildProtobufManifest(FoxRunTypeShape shape)
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo", "WireState", "_telemetry", "field", "Demo.Telemetry", false, false, "",
                    "/phase175/wire_state", 10f, "Demo.WireState", (int)FoxRunPolicy.FixedRate, 0f,
                    encoding: (int)FoxRunEncoding.Protobuf,
                    typeShape: shape)
            });
        }

        private static FoxRunCanonicalManifest BuildInheritedTaggedManifest(
            FoxRunTypeShape shape,
            int nestedFieldNumber)
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "TaggedSource",
                    "_payload",
                    "field",
                    "Demo.TaggedPayload",
                    false,
                    false,
                    "",
                    "/phase185/tag-isolation",
                    10f,
                    "Demo.TaggedEnvelope",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    encoding: 0,
                    typeShape: shape,
                    protobufMetadata: new FoxRunProtobufMetadata(
                        17,
                        new FoxRunProtobufTypeMetadata(
                            "Demo.TaggedPayload",
                            new[]
                            {
                                new FoxRunProtobufFieldMetadata(
                                    "Value",
                                    "value",
                                    nestedFieldNumber)
                            })))
            });
        }

        private static string BuildDescriptor(FoxRunManifestContract contract)
        {
            var input = new FoxRunProtobufContractInput(
                contract.DeclaringType,
                contract.Topic,
                contract.SchemaName,
                contract.Fields.Select(field => new FoxRunProtobufFieldInput(
                    field.JsonName,
                    field.MemberName,
                    field.Type,
                    field.Array,
                    typeShape: field.TypeShape,
                    protobufMetadata: field.ProtobufMetadata)).ToArray());
            return Convert.ToBase64String(
                FoxRunProtobufContractBuilder.Build(input).FileDescriptorSet);
        }

        private sealed class ReflectionTelemetry
        {
            public string Label { get; set; }
            public ReflectionPose Pose { get; set; }
            public float[] Samples { get; set; }
        }

        private sealed class ReflectionPose
        {
            public UnityEngine.Vector3 Position { get; set; }
        }
    }
}
