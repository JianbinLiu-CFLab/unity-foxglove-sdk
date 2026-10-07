// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Schemas
// Purpose: Regression coverage for Module 8 schema serialization P3 fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{


public sealed partial class SchemaSerializationP3Tests
    {
        [Fact]
        public void V3RecordingRoundTripsThroughReplaySchemaGuard()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-sdk-v3-roundtrip-" + Guid.NewGuid().ToString("N") + ".mcap");
            var registry = new DefaultSchemaRegistry();
            try
            {
                ComponentMessagePackCodecRegistry.RegisterGenerated(
                    new ComponentMessagePackGeneratedManifest(
                        "module8",
                        "v1",
                        new[]
                        {
                            new ComponentMessagePackGeneratedEntry(
                                typeof(ComponentIdentityMessage),
                                "module8.Component",
                                "shape.v1",
                                true,
                                true,
                                string.Empty)
                        }));

                using (var stream = new MemoryStream())
                {
                    using (var recorder = new McapRecorder(stream, leaveOpen: true))
                    {
                        recorder.AddChannel(1, "/module8/component", "msgpack", "", "", "");
                        recorder.WriteMessage(
                            1,
                            1UL,
                            new byte[] { 0x91, 0x01 },
                            "msgpack",
                            "module8.Component",
                            "shape.v1");
                        var components = recorder.GetRecordedComponentContractSnapshot();
                        Assert.Single(components);
                        Assert.True(SdkWireSchemaIdentity.TryCompute(
                            registry,
                            Array.Empty<McapSchema>(),
                            components,
                            out var hash));
                        Assert.True(SdkWireSchemaMcapMetadata.TryCreateJson(
                            hash,
                            components,
                            out var metadataJson));
                        recorder.WriteMetadata(SdkWireSchemaMcapMetadata.MetadataName, metadataJson);
                        recorder.Close();
                    }
                    File.WriteAllBytes(path, stream.ToArray());
                }

                using var engine = new McapReplayEngine();
                engine.Load(path);
                var result = ReplaySchemaGuard.EvaluateWithMode(
                    engine,
                    SchemaIdentityMode.Strict,
                    registry);

                Assert.Equal(FoxRunReplaySchemaGuardState.Match, result.State);
                Assert.False(result.IsBlocking);
            }
            finally
            {
                ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        [Fact]
        public void MissingCurrentComponentShapeSetsReplayMismatchFlag()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-sdk-missing-component-shape-" + Guid.NewGuid().ToString("N") + ".mcap");
            var registry = new DefaultSchemaRegistry();
            var components = new[]
            {
                new SdkWireSchemaComponentIdentity
                {
                    Topic = "/module8/component",
                    Encoding = "msgpack",
                    LogicalSchema = "module8.Component",
                    ShapeIdentity = "shape.v1"
                }
            };
            try
            {
                ComponentMessagePackCodecRegistry.RegisterGenerated(
                    new ComponentMessagePackGeneratedManifest(
                        "module8",
                        "v1",
                        new[]
                        {
                            new ComponentMessagePackGeneratedEntry(
                                typeof(ComponentIdentityMessage),
                                "module8.Component",
                                "shape.v1",
                                true,
                                true,
                                string.Empty)
                        }));
                Assert.True(SdkWireSchemaIdentity.TryCompute(
                    registry,
                    Array.Empty<McapSchema>(),
                    components,
                    out var hash));
                Assert.True(SdkWireSchemaMcapMetadata.TryCreateJson(hash, components, out var metadataJson));

                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/module8/component", "msgpack", "", "", "");
                    recorder.WriteMessage(1, 1UL, new byte[] { 0x91, 0x01 }, "msgpack", "module8.Component", "shape.v1");
                    recorder.WriteMetadata(SdkWireSchemaMcapMetadata.MetadataName, metadataJson);
                    recorder.Close();
                }

                ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
                using (var diagnosticEngine = new McapReplayEngine())
                {
                    diagnosticEngine.Load(path);
                    var diagnostic = ReplaySchemaGuard.EvaluateWithMode(
                        diagnosticEngine,
                        SchemaIdentityMode.Strict,
                        registry);
                    Assert.Equal(FoxRunReplaySchemaGuardState.MissingCurrent, diagnostic.State);
                    Assert.Contains("module8.Component", diagnostic.Message);
                    Assert.Contains("shape.v1", diagnostic.Message);
                }
                using var controller = new ReplayController(new ConsoleLogger(), null, null, registry);
                controller.Enable(path, SchemaIdentityMode.Strict);

                Assert.False(controller.IsEnabled);
                Assert.True(controller.LastEnableHadSchemaMismatch);
                Assert.True(controller.LastEnableBlockedBySchemaMismatch);
            }
            finally
            {
                ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        [Fact]
        public void RawMessagePackWritesDoNotClaimTypedComponentContract()
        {
            using var transport = new MatrixTransport();
            using var session = new FoxgloveSession("module8-channel-ownership", transport);
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, leaveOpen: true);
            session.SetRecorder(recorder);

            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 1,
                Topic = "/same/topic",
                Encoding = "msgpack"
            });
            session.RegisterChannel(new AdvertiseChannel
            {
                Id = 2,
                Topic = "/same/topic",
                Encoding = "msgpack",
                ComponentLogicalSchemaName = "module8.Component",
                ComponentShapeIdentity = "shape.v1"
            });

            session.Publish(1, new byte[] { 0x91, 0x01 }, 1UL);
            Assert.Empty(recorder.GetRecordedComponentContractSnapshot());

            session.Publish(2, new byte[] { 0x91, 0x02 }, 2UL);
            session.Publish(1, new byte[] { 0x91, 0x03 }, 3UL);

            var contract = Assert.Single(recorder.GetRecordedComponentContractSnapshot());
            Assert.Equal("/same/topic", contract.Topic);
            Assert.Equal("module8.Component", contract.LogicalSchema);
            Assert.Equal("shape.v1", contract.ShapeIdentity);
        }

        [Fact]
        public void SdkWireSchemaIdentityChangesWhenRegisteredSchemaContentChanges()
        {
            var first = new DefaultSchemaRegistry();
            first.Register(new SchemaEntry
            {
                Name = "module8.Identity",
                Encoding = "jsonschema",
                Content = "{\"type\":\"integer\"}"
            });
            var second = new DefaultSchemaRegistry();
            second.Register(new SchemaEntry
            {
                Name = "module8.Identity",
                Encoding = "jsonschema",
                Content = "{\"type\":\"string\"}"
            });

            Assert.True(SdkWireSchemaIdentity.TryCompute(first, out var firstHash));
            Assert.True(SdkWireSchemaIdentity.TryCompute(second, out var secondHash));
            Assert.NotEqual(firstHash, secondHash);
            Assert.Equal(64, firstHash.Length);
            Assert.Equal(64, secondHash.Length);
        }

        [Fact]
        public void SdkWireSchemaIdentityDoesNotSelfFillMissingRecordedTextSchema()
        {
            var registry = new DefaultSchemaRegistry();
            var recorded = new[]
            {
                new McapSchema
                {
                    Name = "module8.Missing",
                    Encoding = "jsonschema",
                    Data = Encoding.UTF8.GetBytes("{\"type\":\"integer\"}")
                }
            };

            Assert.False(SdkWireSchemaIdentity.TryCompute(
                registry,
                recorded,
                out var hash,
                out var failureReason));
            Assert.Equal(string.Empty, hash);
            Assert.Contains("module8.Missing", failureReason);
            Assert.Contains("jsonschema", failureReason);
        }

        [Fact]
        public void MissingSchemaDiagnosticIsBounded()
        {
            var registry = new DefaultSchemaRegistry();
            var recorded = new[]
            {
                new McapSchema
                {
                    Name = new string('x', 4096),
                    Encoding = "jsonschema",
                    Data = Array.Empty<byte>()
                }
            };

            Assert.False(SdkWireSchemaIdentity.TryCompute(
                registry,
                recorded,
                out _,
                out var failureReason));
            Assert.Contains("...", failureReason);
            Assert.True(failureReason.Length < 256, failureReason.Length.ToString());
        }

        [Fact]
        public void SdkWireSchemaIdentityDoesNotFallbackMissingRecordedProtobufSchema()
        {
            var registry = new DefaultSchemaRegistry();
            var recorded = new[]
            {
                new McapSchema
                {
                    Name = "module8.MissingProtobuf",
                    Encoding = "protobuf",
                    Data = new byte[] { 0x0a, 0x01, 0x01 }
                }
            };

            Assert.False(SdkWireSchemaIdentity.TryCompute(
                registry,
                recorded,
                out var hash,
                out var failureReason));
            Assert.Equal(string.Empty, hash);
            Assert.Contains("module8.MissingProtobuf", failureReason);
            Assert.Contains("protobuf", failureReason);
        }

        [Fact]
        public void SdkWireSchemaIdentityIncludesRecordedComponentShape()
        {
            var components = new[]
            {
                new SdkWireSchemaComponentIdentity
                {
                    Topic = "/module8/component",
                    Encoding = "msgpack",
                    LogicalSchema = "module8.Component",
                    ShapeIdentity = "shape.v1"
                }
            };
            var registry = new DefaultSchemaRegistry();
            try
            {
                ComponentMessagePackCodecRegistry.RegisterGenerated(
                    new ComponentMessagePackGeneratedManifest(
                        "module8",
                        "v1",
                        new[]
                        {
                            new ComponentMessagePackGeneratedEntry(
                                typeof(ComponentIdentityMessage),
                                "module8.Component",
                                "shape.v1",
                                true,
                                true,
                                string.Empty)
                        }));
                Assert.True(SdkWireSchemaIdentity.TryCompute(
                    registry,
                    Array.Empty<McapSchema>(),
                    components,
                    out var firstHash));

                ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
                ComponentMessagePackCodecRegistry.RegisterGenerated(
                    new ComponentMessagePackGeneratedManifest(
                        "module8",
                        "v2",
                        new[]
                        {
                            new ComponentMessagePackGeneratedEntry(
                                typeof(ComponentIdentityMessage),
                                "module8.Component",
                                "shape.v2",
                                true,
                                true,
                                string.Empty)
                        }));
                var currentComponents = new[]
                {
                    new SdkWireSchemaComponentIdentity
                    {
                        Topic = "/module8/component",
                        Encoding = "msgpack",
                        LogicalSchema = "module8.Component",
                        ShapeIdentity = "shape.v2"
                    }
                };
                Assert.True(SdkWireSchemaIdentity.TryCompute(
                    registry,
                    Array.Empty<McapSchema>(),
                    currentComponents,
                    out var secondHash));
                Assert.NotEqual(firstHash, secondHash);
                Assert.False(SdkWireSchemaIdentity.TryCompute(
                    registry,
                    Array.Empty<McapSchema>(),
                    new[]
                    {
                        new SdkWireSchemaComponentIdentity
                        {
                            Topic = "/module8/component",
                            Encoding = "msgpack",
                            LogicalSchema = "module8.Component",
                            ShapeIdentity = "shape.v1"
                        }
                    },
                    out var rejectedHash));
                Assert.Equal(string.Empty, rejectedHash);

                Assert.True(SdkWireSchemaMcapMetadata.TryCreateJson(firstHash, components, out var json));
                Assert.True(SdkWireSchemaMcapMetadata.TryParseJson(
                    json,
                    out var parsedHash,
                    out var version,
                    out var parsedComponents,
                    out var error), error);
                Assert.Equal(firstHash, parsedHash);
                Assert.Equal(3, version);
                Assert.Single(parsedComponents);
            }
            finally
            {
                ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            }
        }

    }
}
