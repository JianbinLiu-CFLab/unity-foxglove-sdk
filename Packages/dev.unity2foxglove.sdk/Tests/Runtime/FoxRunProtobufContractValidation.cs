// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards Phase175A FoxRun contract metadata without changing live transport behavior.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase175AValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 175A Tests ---");
            _passCount = 0;

            VerifyContractModelAndDescriptorBuilder();
            VerifySchemaInfoDescriptorEvidence();
            VerifyLiveTransportRemainsJsonOnly();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 175A: " + _passCount + " checks passed.\n");
        }

        private static void VerifyContractModelAndDescriptorBuilder()
        {
            var attributes = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunAttribute.cs");
            var model = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var builder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunProtobufContractBuilder.cs");

            Check(attributes.Contains("FoxRunEncoding Encoding", StringComparison.Ordinal)
                  && attributes.Contains("ProtobufFieldNumber", StringComparison.Ordinal)
                  && model.Contains("DeclaredEncodingToText", StringComparison.Ordinal),
                "175A-1: FoxRun source policy and field-number overrides flow into the shared model");
            Check(builder.Contains("FileDescriptorSet", StringComparison.Ordinal)
                  && builder.Contains("FoxRunProtobufFieldNumber.Resolve", StringComparison.Ordinal)
                  && builder.Contains("Type.Message", StringComparison.Ordinal),
                "175A-2: deterministic Protobuf descriptors use stable tags and nested message fields");
        }

        private static void VerifySchemaInfoDescriptorEvidence()
        {
            var contract = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaContractInfo.cs");
            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");

            Check(contract.Contains("ProtobufDescriptorSet", StringComparison.Ordinal)
                  && writer.Contains("Convert.FromBase64String", StringComparison.Ordinal)
                  && writer.Contains("FoxRunProtobufContractBuilder", StringComparison.Ordinal),
                "175A-3: generated schema info carries Protobuf descriptor evidence for the next transport wave");
        }

        private static void VerifyLiveTransportRemainsJsonOnly()
        {
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs");
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var publishEmitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var inputEmitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");

            Check(session.Contains("Action<uint, uint, string, string, byte[]> _messageCallback", StringComparison.Ordinal)
                  && hub.Contains("_router.Dispatch", StringComparison.Ordinal)
                  && publishEmitter.Contains("PublishFoxRunJsonBytes", StringComparison.Ordinal)
                  && inputEmitter.Contains("FoxRunInboundJson.TryRead", StringComparison.Ordinal),
                "175A-4: session callback, input routing, and generated publish/apply paths remain on the existing JSON transport");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase175a"),
                "175A-5: validation registry exposes the typed Protobuf contract-model flag");
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
