// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards Phase175C Manager wire-policy migration and Inspector contracts.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase175CValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 175C Tests ---");
            _passed = 0;

            VerifyPolicyResolverAndFrozenSessionState();
            VerifyGeneratedInheritedDualCodecDispatch();
            VerifyInspectorContract();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 175C: " + _passed + " checks passed.\n");
        }

        private static void VerifyPolicyResolverAndFrozenSessionState()
        {
            var resolver = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunWireEncodingResolver.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(resolver.Contains("ValidateManagerDefault", StringComparison.Ordinal)
                  && resolver.Contains("FoxRunWireEncoding.Protobuf", StringComparison.Ordinal),
                "175C-1: resolver accepts only concrete Manager defaults");
            Check(inbound.Contains("_defaultFoxRunWireEncoding = FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunWireEncoding == FoxRunWireEncoding.Inherit", StringComparison.Ordinal)
                  && inbound.Contains("ResolveFoxRunWireEncoding", StringComparison.Ordinal),
                "175C-2: Manager defaults inherited topics to Protobuf");
            var captureIndex = server.IndexOf("CaptureFoxRunWireEncodingForSession();", StringComparison.Ordinal);
            var schemaRegistrationIndex = server.IndexOf("FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas", StringComparison.Ordinal);
            Check(captureIndex >= 0
                  && schemaRegistrationIndex > captureIndex
                && inbound.Contains("public FoxRunWireEncoding ActiveFoxRunDefaultWireEncoding => _hasActiveFoxRunWireEncoding", StringComparison.Ordinal)
                && inbound.Contains("? _activeFoxRunDefaultWireEncoding", StringComparison.Ordinal)
                  && inbound.Contains("_activeFoxRunDefaultWireEncoding = DefaultFoxRunWireEncoding;", StringComparison.Ordinal)
                  && server.Contains("ClearFoxRunWireEncodingForSession();", StringComparison.Ordinal),
                "175C-3: Manager freezes the policy before registration and clears it with the session lifecycle");
        }

        private static void VerifyGeneratedInheritedDualCodecDispatch()
        {
            var input = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");

            Check(input.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal)
                  && input.Contains("Unsupported FoxRun inbound wire encoding", StringComparison.Ordinal),
                "175C-4: generated inbound dispatch preserves Inherit and supports both concrete encodings");
            Check(publish.Contains("mgr.ResolveFoxRunWireEncoding(FoxRunWireEncoding.Inherit)", StringComparison.Ordinal),
                "175C-5: generated publish dispatch resolves inherited encoding through Manager");
            Check(router.Contains("DeclaredWireEncoding", StringComparison.Ordinal)
                  && router.Contains("DefaultWireEncoding", StringComparison.Ordinal),
                "175C-6: input router separates declared and effective client contracts");
        }

        private static void VerifyInspectorContract()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.FoxRun.cs");
            var labels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEncodingEditorLabels.cs");
            var main = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");

            Check(main.Contains("DrawSection(\"FoxRun\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Publish Data\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"FoxRun\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"FoxRun\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"FoxServices\"", StringComparison.Ordinal),
                "175C-7: FoxRun Inspector section sits between MCAP and FoxServices");
            Check(labels.Contains("ManagerDefaultLabels = { \"Protobuf\", \"JSON\" }", StringComparison.Ordinal)
                  && labels.Contains("property.enumValueIndex == (int)FoxRunWireEncoding.Json ? 1 : 0", StringComparison.Ordinal)
                  && labels.Contains("property.enumValueIndex = selected == 0", StringComparison.Ordinal)
                  && labels.Contains("? (int)FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && labels.Contains(": (int)FoxRunWireEncoding.Json", StringComparison.Ordinal)
                  && !labels.Contains("MsgPack", StringComparison.Ordinal)
                  && !labels.Contains("ROS2", StringComparison.Ordinal),
                "175C-8: Manager dropdown offers only Protobuf and JSON and cannot persist Inherit");
            Check(inspector.Contains("Topic\", \"Direction | Declared | Effective | Schema", StringComparison.Ordinal)
                  && inspector.Contains("restarted or re-enabled", StringComparison.Ordinal),
                "175C-9: Inspector exposes effective topic summary and restart boundary");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase175c"
                                                           && item.Name == "Phase 175C: FoxRun Manager wire policy and migration"),
                "175C-10: validation registry exposes a descriptive Manager policy gate");
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
            _passed++;
        }
    }
}
