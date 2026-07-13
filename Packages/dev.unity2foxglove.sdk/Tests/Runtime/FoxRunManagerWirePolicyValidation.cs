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
            VerifyExplicitProtobufManualAcceptance();
            VerifyExplicitJsonManualAcceptance();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 175C: " + _passed + " checks passed.\n");
        }

        private static void VerifyPolicyResolverAndFrozenSessionState()
        {
            var resolver = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunWireEncodingResolver.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPublishing.cs");
            var migration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(resolver.Contains("ValidateManagerDefault", StringComparison.Ordinal)
                  && resolver.Contains("PublishAndSubscribe requires an explicit", StringComparison.Ordinal),
                "175C-1: resolver accepts only concrete directional defaults and rejects ambiguous bidirectional inheritance");
            Check(inbound.Contains("_defaultFoxRunWireEncoding = FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                  && publishing.Contains("_defaultFoxRunPublishEncoding", StringComparison.Ordinal)
                  && migration.Contains("ISerializationCallbackReceiver", StringComparison.Ordinal)
                  && migration.Contains("FoxRunWireEncodingPolicyMigration.Migrate", StringComparison.Ordinal),
                "175C-2: Manager retains the legacy policy source and migrates it into directional defaults in player-safe deserialization");
            var captureIndex = server.IndexOf("CaptureFoxRunWireEncodingForSession();", StringComparison.Ordinal);
            var schemaRegistrationIndex = server.IndexOf("FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas", StringComparison.Ordinal);
            Check(captureIndex >= 0
                  && schemaRegistrationIndex > captureIndex
                && inbound.Contains("ActiveFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                && publishing.Contains("ActiveFoxRunPublishEncoding", StringComparison.Ordinal)
                  && inbound.Contains("_activeFoxRunSubscriptionEncoding = DefaultFoxRunSubscriptionEncoding;", StringComparison.Ordinal)
                  && inbound.Contains("_activeFoxRunPublishEncoding = DefaultFoxRunPublishEncoding;", StringComparison.Ordinal)
                  && server.Contains("ClearFoxRunWireEncodingForSession();", StringComparison.Ordinal),
                "175C-3: Manager freezes both directional policies before registration and clears them with the session lifecycle");
        }

        private static void VerifyGeneratedInheritedDualCodecDispatch()
        {
            var input = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");

            Check(input.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal)
                  && input.Contains("Unsupported FoxRun inbound wire encoding", StringComparison.Ordinal),
                "175C-4: generated inbound dispatch preserves Inherit and supports both concrete encodings");
            Check(publish.Contains("mgr.ResolveFoxRunWireEncoding(FoxRunWireEncoding.Inherit, FoxRunMode.PublishOnly)", StringComparison.Ordinal),
                "175C-5: generated publish dispatch resolves inherited encoding through the publish policy");
            Check(router.Contains("DeclaredWireEncoding", StringComparison.Ordinal)
                  && router.Contains("DefaultSubscriptionWireEncoding", StringComparison.Ordinal),
                "175C-6: input router separates declared and effective subscription contracts");
        }

        private static void VerifyInspectorContract()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var labels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEncodingEditorLabels.cs");
            var main = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");

            Check(main.Contains("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Publish Data\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"FoxServices\"", StringComparison.Ordinal),
                "175C-7: Subscribe Data Inspector section sits between MCAP and FoxServices");
            Check(labels.Contains("ManagerDefaultLabels = { \"Protobuf\", \"JSON\" }", StringComparison.Ordinal)
                  && labels.Contains("property.enumValueIndex == (int)FoxRunWireEncoding.Json ? 1 : 0", StringComparison.Ordinal)
                  && labels.Contains("property.enumValueIndex = selected == 0", StringComparison.Ordinal)
                  && labels.Contains("? (int)FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && labels.Contains(": (int)FoxRunWireEncoding.Json", StringComparison.Ordinal)
                  && !labels.Contains("MsgPack", StringComparison.Ordinal)
                  && !labels.Contains("ROS2", StringComparison.Ordinal),
                "175C-8: Manager dropdown offers only Protobuf and JSON and cannot persist Inherit");
            Check(inspector.Contains("Default Subscription Encoding", StringComparison.Ordinal)
                  && inspector.Contains("Subscription Rate Limit Hz (per Topic)", StringComparison.Ordinal)
                  && inspector.Contains("restarted or re-enabled", StringComparison.Ordinal),
                "175C-9: Inspector exposes subscription controls and the restart boundary");
        }

        private static void VerifyExplicitProtobufManualAcceptance()
        {
            const string relativePath = "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase175ProtobufManualAcceptance.cs";
            var root = Phase16Validation.FindRepoRoot();
            var fullPath = root == null
                ? string.Empty
                : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

            Check(source.Contains("class Phase175ProtobufManualAcceptance", StringComparison.Ordinal)
                  && source.Contains("/phase175/protobuf/target-value", StringComparison.Ordinal)
                  && source.Contains("/phase175/protobuf/shared-state", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunMode.SubscribeOnly, Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 1", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunMode.PublishAndSubscribe, Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 1", StringComparison.Ordinal)
                  && source.Contains("#pragma warning disable FOXRUN026", StringComparison.Ordinal)
                  && source.Contains("remote-authoritative shared observation", StringComparison.Ordinal)
                  && source.Contains("#pragma warning restore FOXRUN026", StringComparison.Ordinal)
                  && !source.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal),
                "175C-10: manual acceptance pins Protobuf contracts and documents bidirectional authority");
        }

        private static void VerifyExplicitJsonManualAcceptance()
        {
            const string relativePath = "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase175JsonManualAcceptance.cs";
            var root = Phase16Validation.FindRepoRoot();
            var fullPath = root == null
                ? string.Empty
                : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

            Check(source.Contains("class Phase175JsonManualAcceptance", StringComparison.Ordinal)
                  && source.Contains("/phase175/json/legacy-state", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunMode.SubscribeOnly, Encoding = FoxRunWireEncoding.Json", StringComparison.Ordinal)
                  && source.Contains("requestedLegacyJsonState", StringComparison.Ordinal)
                  && source.Contains("Applied JSON legacy state", StringComparison.Ordinal)
                  && !source.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal),
                "175C-11: manual acceptance pins an explicit JSON legacy contract");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase175c"
                                                           && item.Name == "Phase 175C: FoxRun Manager wire policy and migration"),
                "175C-12: validation registry exposes a descriptive Manager policy gate");
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
