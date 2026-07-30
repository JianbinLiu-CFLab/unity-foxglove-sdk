// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks Provider-local portable QoS preservation across U2R2.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunRos2BridgeQosMapperTests
    {
        [Fact]
        public void U2R2CarriesEveryPortableAxisIndependently()
        {
            var qos = new FoxRunResolvedQos(
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepLast,
                37);

            var wire = WriteHeader(qos).GetProperty("qos");

            Assert.Equal("sensor_data", wire.GetProperty("profile").GetString());
            Assert.Equal("reliable", wire.GetProperty("reliability").GetString());
            Assert.Equal("transient_local", wire.GetProperty("durability").GetString());
            Assert.Equal("keep_last", wire.GetProperty("history").GetString());
            Assert.Equal(37, wire.GetProperty("depth").GetInt32());
        }

        [Fact]
        public void U2R2PreservesSystemDefaultWithoutProfileDowngrade()
        {
            var header = WriteHeader(FoxRunResolvedQos.SystemDefault);
            var wire = header.GetProperty("qos");

            Assert.Equal("system_default", header.GetProperty("profileName").GetString());
            Assert.Equal("system_default", wire.GetProperty("profile").GetString());
            Assert.Equal("system_default", wire.GetProperty("reliability").GetString());
            Assert.Equal("system_default", wire.GetProperty("durability").GetString());
            Assert.Equal("system_default", wire.GetProperty("history").GetString());
            Assert.Equal(0, wire.GetProperty("depth").GetInt32());
        }

        [Fact]
        public void U2R2PreservesKeepAllWithoutSynthesizingDepth()
        {
            var qos = new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepAll,
                0);

            var wire = WriteHeader(qos).GetProperty("qos");

            Assert.Equal("keep_all", wire.GetProperty("history").GetString());
            Assert.Equal(0, wire.GetProperty("depth").GetInt32());
        }

        [Fact]
        public void U2R2RejectsDefaultResolvedQos()
            => Assert.Throws<ArgumentException>(() => WriteHeader(default));

        [Fact]
        public void U2R2RejectsUnknownProfile()
        {
            var mapper = typeof(Ros2BridgeFrameWriter).GetMethod(
                "ProfileWireValue",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(mapper);

            var invocation = Assert.Throws<TargetInvocationException>(
                () => mapper.Invoke(
                    null,
                    new object[] { (FoxRunQosProfile)99 }));
            Assert.IsType<ArgumentOutOfRangeException>(
                invocation.InnerException);
        }

        [Fact]
        public void BridgeProviderOwnsNeutralLifecycleAndNoLegacyEndpointSwitch()
        {
            var provider = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");

            Assert.Contains("IFoxRunTransportProvider", provider);
            Assert.Contains("IFoxRunOrdinaryPayloadMapper", provider);
            Assert.Contains("TryCaptureSession(", provider);
            Assert.DoesNotContain("_ros2BridgeEnabled", provider);
            Assert.DoesNotContain("FoxRunEndpoint", provider);
        }

        [Fact]
        public void EndingBridgeSessionAlwaysReleasesProviderOwnership()
        {
            var provider = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeTransportProvider.cs");
            var method = CSharpSyntaxTree.ParseText(
                    provider,
                    new CSharpParseOptions(
                        preprocessorSymbols: new[] { "UNITY_5_3_OR_NEWER" }))
                .GetCompilationUnitRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(candidate =>
                    candidate.Identifier.ValueText == "Dispose"
                    && candidate.ToFullString().Contains("ReleaseSession"));

            var terminalTry = Assert.Single(
                method.Body?.Statements.OfType<TryStatementSyntax>()
                ?? Enumerable.Empty<TryStatementSyntax>());
            Assert.Contains(
                "owner?.ReleaseSession(Generation)",
                terminalTry.Finally?.ToFullString() ?? string.Empty);
        }

        private static JsonElement WriteHeader(FoxRunResolvedQos qos)
        {
            var frame = new Ros2BridgeFrame(
                "/phase186/qos",
                "foxglove_msgs/msg/FrameTransform",
                Ros2BridgeFrame.CdrEncoding,
                1234UL,
                7UL,
                new byte[] { 0, 1, 0, 0 },
                qos);
            var bytes = Ros2BridgeFrameWriter.Write(frame);
            var headerLength = ReadUInt32LittleEndian(bytes, 8);
            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(
                    bytes,
                    16,
                    checked((int)headerLength)));
            return document.RootElement.Clone();
        }

        private static uint ReadUInt32LittleEndian(
            byte[] bytes,
            int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(
                        Path.Combine(
                            directory.FullName,
                            "Packages",
                            "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }
    }
}
