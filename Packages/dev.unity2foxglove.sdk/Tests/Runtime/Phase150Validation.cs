// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 150 validation for SDK-style channel facade API boundaries.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase150Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 150 Tests ---");
            _passCount = 0;

            VerifyChannelFacadePublicApiShape();
            VerifyManagerFactoriesAndGenerationGuardShape();
            VerifyRawChannelPublishesExactBytes();
            VerifyProtobufCatalogClrTypeLookupShape();
            VerifyTestSurfaceKeepsUnityFacingProtoExtensionsOutOfDotnetRunner();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 150: " + _passCount + " checks passed.\n");
        }

        private static void VerifyChannelFacadePublicApiShape()
        {
            var json = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/Channels/FoxgloveJsonChannel.cs");
            var raw = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/Channels/FoxgloveRawChannel.cs");
            var proto = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Channels/FoxgloveProtoChannel.cs");

            Check(json.Contains("public sealed class FoxgloveJsonChannel", StringComparison.Ordinal)
                  && json.Contains("public void Log(object message)", StringComparison.Ordinal)
                  && json.Contains("public void Log(object message, ulong timestampNs)", StringComparison.Ordinal)
                  && json.Contains("PublishJsonChannel(_generation", StringComparison.Ordinal),
                "150-1: JSON channel exposes SDK-style Log overloads");

            Check(raw.Contains("public sealed class FoxgloveRawChannel", StringComparison.Ordinal)
                  && raw.Contains("public void Log(byte[] payload)", StringComparison.Ordinal)
                  && raw.Contains("public void Log(byte[] payload, ulong timestampNs)", StringComparison.Ordinal)
                  && raw.Contains("PublishRawChannel(_generation", StringComparison.Ordinal)
                  && !raw.Contains("ReadOnlyMemory", StringComparison.Ordinal),
                "150-2: raw channel exposes byte-array Log overloads without ReadOnlyMemory");

            Check(proto.Contains("public sealed class FoxgloveProtoChannel<T>", StringComparison.Ordinal)
                  && proto.Contains("where T : class, Google.Protobuf.IMessage", StringComparison.Ordinal)
                  && proto.Contains("message.ToByteArray()", StringComparison.Ordinal)
                  && proto.Contains("public static class FoxgloveProtoChannelExtensions", StringComparison.Ordinal)
                  && proto.Contains("CreateProtoChannel<T>(this FoxgloveManager manager", StringComparison.Ordinal)
                  && proto.Contains("throw new ArgumentNullException(nameof(message))", StringComparison.Ordinal),
                "150-3: protobuf channel extension serializes IMessage values and rejects null messages");
        }

        private static void VerifyManagerFactoriesAndGenerationGuardShape()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Channels.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(manager.Contains("public FoxgloveJsonChannel CreateJsonChannel", StringComparison.Ordinal)
                  && manager.Contains("public FoxgloveRawChannel CreateRawChannel", StringComparison.Ordinal),
                "150-4: manager exposes JSON and raw channel factories without depending on the proto assembly");

            Check(manager.Contains("_channelSessionGeneration", StringComparison.Ordinal)
                  && manager.Contains("ValidateChannelSessionGeneration", StringComparison.Ordinal)
                  && manager.Contains("ulong generation, uint channelId", StringComparison.Ordinal)
                  && manager.Contains("InvalidOperationException", StringComparison.Ordinal),
                "150-5: manager publish helpers validate captured channel session generation");

            Check(server.Contains("AdvanceChannelSessionGeneration", StringComparison.Ordinal)
                  && server.Contains("_channelCache.Clear()", StringComparison.Ordinal)
                  && server.Contains("_nextChannelId = FirstAutoChannelId", StringComparison.Ordinal),
                "150-6: StopServer invalidates channel generation before channel ids are recycled");
        }

        private static void VerifyRawChannelPublishesExactBytes()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Channels.cs");
            var rawHelper = PhaseValidationSourceHelpers.SourceMethod(manager, "PublishRawChannel");

            Check(rawHelper.Contains("_runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), timestampNs)", StringComparison.Ordinal)
                  && !rawHelper.Contains("PublishJson", StringComparison.Ordinal)
                  && !rawHelper.Contains("PublishProto", StringComparison.Ordinal),
                "150-7: raw channel helper publishes caller bytes by channel id without serialization");
        }

        private static void VerifyProtobufCatalogClrTypeLookupShape()
        {
            var catalog = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/FoxgloveProtoSchemaCatalog.cs");

            Check(catalog.Contains("TryGetByClrType(Type clrType", StringComparison.Ordinal)
                  && catalog.Contains("EntriesByClrType", StringComparison.Ordinal)
                  && catalog.Contains("BuildEntriesByClrType", StringComparison.Ordinal),
                "150-8: protobuf catalog supports explicit CLR type lookup");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase150"),
                "150-10: validation registry exposes the SDK-style channel API flag");
        }

        private static void VerifyTestSurfaceKeepsUnityFacingProtoExtensionsOutOfDotnetRunner()
        {
            var runtimeProject = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var testSurface = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props");

            Check(runtimeProject.Contains("Runtime/Schemas/Proto/**/Channels/**/*.cs", StringComparison.Ordinal)
                  && testSurface.Contains("Runtime/Schemas/Proto/**/Channels/**/*.cs", StringComparison.Ordinal),
                "150-9: .NET test surface excludes Unity-facing proto channel extensions");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
