// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-12 validation for schema catalog and identity review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_12Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-12: Schema Catalogs and Schema Identity ===");
            _passed = 0;

            Ros2CatalogGeneratorTemplateStaysInSync();
            Ros2CatalogSourceFileCountMatchesEntries();
            FoxRunSchemaGuardDocumentsPolicyScopedBlocking();
            ProtobufRegistryDocumentsNamespaceBoundary();
            SchemaEvidenceSettingsSaveModeIndependently();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-12: {_passed} checks passed.");
        }

        private static void Ros2CatalogGeneratorTemplateStaysInSync()
        {
            var generator = ReadRepoText("Scripts/schema/generate_ros2_msg_schema_catalog.py");

            Check(generator.Contains("Ros2StandardMsgSchemaCatalog.TryGet(schemaName, out entry)", StringComparison.Ordinal),
                "163-12A-1: ROS2 msg catalog generator keeps standard schema fallback");
            Check(generator.Contains("throw new ArgumentNullException(nameof(registry));", StringComparison.Ordinal),
                "163-12A-2: ROS2 msg catalog generator preserves null registry throw");
            Check(generator.Contains("Ros2StandardMsgSchemaCatalog.RegisterSchemas(registry);", StringComparison.Ordinal),
                "163-12A-3: ROS2 msg catalog generator registers standard schemas");
        }

        private static void Ros2CatalogSourceFileCountMatchesEntries()
        {
            Check(FoxgloveRos2MsgSchemaCatalog.SourceFileCount == FoxgloveRos2MsgSchemaCatalog.Entries.Count,
                "163-12B-1: ROS2 msg catalog SourceFileCount matches exposed entries");
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet("sensor_msgs/msg/PointCloud2", out _),
                "163-12B-2: ROS2 msg catalog fallback resolves standard sensor_msgs PointCloud2");
        }

        private static void FoxRunSchemaGuardDocumentsPolicyScopedBlocking()
        {
            var recorded = new FoxRunSchemaMcapMetadataRecord
            {
                GlobalManifestHash = "recorded-hash"
            };
            var current = new FoxRunSchemaManifestInfo(
                manifestVersion: 1,
                packageName: "phase163-12",
                generatorName: "phase163-12",
                generatorMajorVersion: 1,
                globalManifestHash: "current-hash",
                foxRunManifestHash: "foxrun-hash",
                types: Array.Empty<FoxRunSchemaTypeInfo>());

            var result = FoxRunSchemaMcapMetadata.Evaluate(recorded, current);
            Check(result.State == FoxRunReplaySchemaGuardState.Mismatch && result.IsBlocking,
                "163-12C-1: schema guard marks mismatches as strict-mode blocking-capable");

            var guardSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaMcapMetadata.cs");
            var replaySource = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            Check(guardSource.Contains("not an unconditional replay-stop signal for warn mode", StringComparison.Ordinal)
                  && replaySource.Contains("schemaGuard.IsBlocking && identityMode == SchemaIdentityMode.Strict", StringComparison.Ordinal),
                "163-12C-2: schema guard documents policy-scoped blocking and replay enforces strict mode");
        }

        private static void ProtobufRegistryDocumentsNamespaceBoundary()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/ProtobufSchemaRegistry.cs");
            Check(source.Contains("intentionally lives beside the generated", StringComparison.Ordinal)
                  && source.Contains("ISchemaRegistry", StringComparison.Ordinal),
                "163-12D-1: protobuf schema registry documents generated/schema abstraction boundary");
        }

        private static void SchemaEvidenceSettingsSaveModeIndependently()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            Check(source.Contains("var previousMode = DefaultIdentityMode;", StringComparison.Ordinal)
                  && source.Contains("mode != previousMode", StringComparison.Ordinal)
                  && source.Contains("shouldSave = true;", StringComparison.Ordinal),
                "163-12E-1: schema evidence settings save identity mode independently");
            Check(source.Contains("TryNormalizeAssetsRootCached(root, out var normalized, out var error)", StringComparison.Ordinal)
                  && source.Contains("private static bool TryNormalizeAssetsRootCached", StringComparison.Ordinal)
                  && source.Contains("EditorGUILayout.HelpBox(error, MessageType.Error);", StringComparison.Ordinal),
                "163-12E-2: schema evidence settings still reject invalid evidence roots");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_12Validation.cs", StringComparison.Ordinal),
                "163-12F-1: runtime test project compiles Phase163_12Validation");
            Check(registry.Contains("--phase163-12", StringComparison.Ordinal)
                  && registry.Contains("Phase163_12Validation.Validate", StringComparison.Ordinal),
                "163-12F-2: validation registry exposes --phase163-12");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
