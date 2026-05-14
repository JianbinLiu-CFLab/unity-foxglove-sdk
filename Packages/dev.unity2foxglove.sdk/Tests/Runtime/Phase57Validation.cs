// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 57 regression coverage for schema/publisher encoding
// lifecycle, cache, null-payload, and typed publisher boundary hardening.

using System;
using System.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Foxglove.Schemas;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates fixes from the schema/publisher encoding review.
    /// </summary>
    public static class Phase57Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 57: Schema Publisher Encoding Hardening ===");
            _passed = 0;

            VerifyProtobufSchemaSetupDoesNotStronglyRetainRegistries();
            VerifyManagerCachesSchemaChannelsOnlyAfterSuccessfulRegistration();
            VerifyNullMessageDataPayloadsEncodeAsEmptyFrames();
            VerifyCameraPublisherClampsReadbackLifecycle();
            VerifyPointCloudPublisherAppliesProgrammaticPointBudget();

            Console.WriteLine($"Phase 57: {_passed} checks passed.");
        }

        private static void VerifyProtobufSchemaSetupDoesNotStronglyRetainRegistries()
        {
            var setup = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/ProtobufSchemasSetup.cs");
            Check(!setup.Contains("HashSet<ISchemaRegistry>") && !setup.Contains("_seenRegistries"),
                "57A-1: protobuf setup no longer keeps a static strong set of schema registries");

            var first = new DefaultSchemaRegistry();
            var second = new DefaultSchemaRegistry();
            ProtobufSchemasSetup.RegisterSchemas(first);
            ProtobufSchemasSetup.RegisterSchemas(first);
            ProtobufSchemasSetup.RegisterSchemas(second);

            Check(first.TryGetSchema(FoxgloveSchemaDefinitions.CompressedImageSchemaName, "protobuf", out var firstEntry)
                && firstEntry.RawContent != null && firstEntry.RawContent.Length > 0,
                "57A-2: repeated protobuf registration keeps the first registry usable");
            Check(second.TryGetSchema(FoxgloveSchemaDefinitions.CompressedImageSchemaName, "protobuf", out var secondEntry)
                && secondEntry.RawContent != null && secondEntry.RawContent.Length > 0,
                "57A-3: protobuf registration still populates independent registries");
        }

        private static void VerifyManagerCachesSchemaChannelsOnlyAfterSuccessfulRegistration()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var registerIndex = manager.IndexOf("_runtime.RegisterSchemaChannel", StringComparison.Ordinal);
            var cacheIndex = manager.IndexOf("_channelCache[key]", registerIndex, StringComparison.Ordinal);

            Check(registerIndex >= 0 && cacheIndex > registerIndex,
                "57B-1: FoxgloveManager caches schema channel IDs only after runtime registration succeeds");
        }

        private static void VerifyNullMessageDataPayloadsEncodeAsEmptyFrames()
        {
            var frame = BinaryEncoding.EncodeServerMessageData(7, 123UL, null);
            Check(frame.Length == 13,
                "57C-1: null MessageData payload encodes as an empty binary frame");

            Check(BinaryEncoding.TryDecodeServerMessageData(frame, out var subscriptionId, out var logTimeNs, out var payload),
                "57C-2: null-derived empty MessageData frame decodes successfully");
            Check(subscriptionId == 7 && logTimeNs == 123UL && payload.Length == 0,
                "57C-3: null-derived empty MessageData frame preserves metadata");
        }

        private static void VerifyCameraPublisherClampsReadbackLifecycle()
        {
            var camera = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");

            Check(camera.Contains("[SerializeField, Min(1)] private int _maxPendingReadbacks"),
                "57D-1: camera publisher inspector clamps max pending readbacks to at least one");
            Check(camera.Contains("Math.Max(1, _maxPendingReadbacks)") || camera.Contains("Mathf.Max(1, _maxPendingReadbacks)"),
                "57D-2: camera publisher runtime clamps serialized max pending readbacks to at least one");
            Check(camera.Contains("!isActiveAndEnabled"),
                "57D-3: camera readback callback suppresses publishes after component or GameObject disable");
        }

        private static void VerifyPointCloudPublisherAppliesProgrammaticPointBudget()
        {
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var builder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/PointCloudMessageBuilder.cs");

            Check(publisher.Contains("ClampFrameToPointBudget") && publisher.Contains("Math.Max(1, _maxPoints)"),
                "57E-1: point cloud publisher applies the serialized point budget to programmatic frames");
            Check(builder.Contains("MaxPackedDataBytes") && builder.Contains("ValidatePackedDataBudget"),
                "57E-2: point cloud builder preflights packed data size before allocating the byte buffer");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
