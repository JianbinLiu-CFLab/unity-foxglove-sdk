// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 112B FoxRun debug overlay validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase112BValidation
    {
        private const string HelperPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveDebugOverlay.cs";
        private const string EnvelopePath = "Packages/dev.unity2foxglove.sdk/Runtime/Utilities/FoxgloveDebugOverlayEnvelope.cs";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 112B: FoxRun Debug Overlay Channel ===");
            _passed = 0;

            VerifyEnvelopeShape();
            VerifyInputRejection();
            VerifySourceBoundaries();
            VerifyPhase112ManifestFixtureUnchanged();
            VerifyDocs();

            Console.WriteLine($"Phase 112B: {_passed} checks passed.");
        }

        private static void VerifyEnvelopeShape()
        {
            var values = new Dictionary<string, object>
            {
                ["iteration"] = 12,
                ["reason"] = "fallback"
            };

            Check(FoxgloveDebugOverlayEnvelope.TryCreate(
                    "/debug/phase112b",
                    "PlannerController",
                    values,
                    "optional short label",
                    out var envelope),
                "112B-A1: envelope accepts valid debug topic, source, label, and values");

            var json = JObject.FromObject(envelope);
            Check((int)json["version"] == 1
                  && (string)json["kind"] == "debugOverlay"
                  && (string)json["source"] == "PlannerController"
                  && (string)json["label"] == "optional short label"
                  && (int)json["values"]["iteration"] == 12
                  && (string)json["values"]["reason"] == "fallback",
                "112B-A2: envelope has exact observable JSON shape");

            Check(FoxgloveDebugOverlayEnvelope.TryCreateValue(
                    "/debug/phase112b",
                    "PlannerController",
                    "frame",
                    42,
                    null,
                    out var singleValueEnvelope)
                  && (int)JObject.FromObject(singleValueEnvelope)["values"]["frame"] == 42,
                "112B-A3: PublishValue envelope maps one key/value pair");

            var noLabelJson = JObject.FromObject(singleValueEnvelope);
            Check(!noLabelJson.ContainsKey("label"),
                "112B-A4: null label is omitted from overlay JSON");
        }

        private static void VerifyInputRejection()
        {
            Check(FoxgloveDebugOverlayEnvelope.IsValidTopic("/debug/phase112b")
                  && !FoxgloveDebugOverlayEnvelope.IsValidTopic("")
                  && !FoxgloveDebugOverlayEnvelope.IsValidTopic("debug/phase112b")
                  && !FoxgloveDebugOverlayEnvelope.IsValidTopic("/robot/state")
                  && !FoxgloveDebugOverlayEnvelope.IsValidTopic("   "),
                "112B-B1: topics must be explicit /debug/ topics");

            Check(!FoxgloveDebugOverlayEnvelope.TryCreate(
                    "/debug/phase112b",
                    "",
                    new Dictionary<string, object> { ["ok"] = 1 },
                    null,
                    out _),
                "112B-B2: empty source is rejected");

            Check(!FoxgloveDebugOverlayEnvelope.TryCreate(
                    "/debug/phase112b",
                    "PlannerController",
                    null,
                    null,
                    out _)
                  && !FoxgloveDebugOverlayEnvelope.TryCreate(
                      "/debug/phase112b",
                      "PlannerController",
                      new Dictionary<string, object>(),
                      null,
                      out _)
                  && !FoxgloveDebugOverlayEnvelope.TryCreate(
                      "/debug/phase112b",
                      "PlannerController",
                      new Dictionary<string, object> { [""] = 1 },
                      null,
                      out _),
                "112B-B3: null values, empty values, and empty keys are rejected");

            Check(!FoxgloveDebugOverlayEnvelope.TryCreateValue(
                    "/debug/phase112b",
                    "PlannerController",
                    "bytes",
                    new byte[] { 1, 2, 3 },
                    null,
                    out _)
                  && !FoxgloveDebugOverlayEnvelope.TryCreateValue(
                      "/debug/phase112b",
                      "PlannerController",
                      "bytes",
                      new List<byte> { 1, 2, 3 },
                      null,
                      out _)
                  && !FoxgloveDebugOverlayEnvelope.TryCreateValue(
                      "/debug/phase112b",
                      "PlannerController",
                      "bytes",
                      new ReadOnlyMemory<byte>(new byte[] { 1 }),
                      null,
                      out _)
                  && !FoxgloveDebugOverlayEnvelope.TryCreateValue(
                      "/debug/phase112b",
                      "PlannerController",
                      "bytes",
                      new ArraySegment<byte>(new byte[] { 1 }),
                      null,
                      out _)
                  && !FoxgloveDebugOverlayEnvelope.TryCreateValue(
                      "/debug/phase112b",
                      "PlannerController",
                      "stream",
                      new MemoryStream(new byte[] { 1 }),
                      null,
                      out _),
                "112B-B4: binary and stream debug values are rejected");
        }

        private static void VerifySourceBoundaries()
        {
            Check(RepoFileExists(HelperPath) && RepoFileExists(EnvelopePath),
                "112B-C1: helper and envelope source files exist");

            var helper = ReadRepoText(HelperPath);
            Check(helper.Contains("PublishJson(topic, \"\", envelope, timestamp)", StringComparison.Ordinal)
                  && !helper.Contains("GetOrRegisterSchemaChannel", StringComparison.Ordinal),
                "112B-C2: helper publishes schemaless JSON with an empty schema name");

            foreach (var token in new[]
            {
                "FoxRunManifest",
                "ManifestHasher",
                "FoxRunSchemaInfo",
                "ReplayController",
                "McapReplayEngine",
                "protobuf",
                "ROS2",
                "R2FU"
            })
            {
                Check(!helper.Contains(token, StringComparison.OrdinalIgnoreCase),
                    "112B-C3: helper avoids out-of-scope token: " + token);
            }

            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunAttribute.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter.cs"
            })
            {
                Check(!ReadRepoText(path).Contains("FoxgloveDebugOverlay", StringComparison.Ordinal),
                    "112B-C4: debug overlay is not wired into existing FoxRun contract file: " + path);
            }

            var manifestSources = Directory.GetFiles(RepoPath("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest"), "*.cs");
            Check(manifestSources.All(path => !File.ReadAllText(path).Contains("FoxgloveDebugOverlay", StringComparison.Ordinal)),
                "112B-C5: manifest model/json/hash files do not know about debug overlay");
        }

        private static void VerifyPhase112ManifestFixtureUnchanged()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "RobotState",
                    "_batteryLevel",
                    "field",
                    "System.Single",
                    true,
                    false,
                    "",
                    "/phase112/battery",
                    10f,
                    "",
                    1,
                    0.001f,
                    0f)
            });

            Check(manifest.GlobalManifestHash == "54a93011d18c1ba9d53c955eb047c285096fb1a0a58376beb31c935ed3eff0e4",
                "112B-D1: Phase112 fixture manifest hash is unchanged");
        }

        private static void VerifyDocs()
        {
            var en = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md");
            var zh = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md");
            var research = ReadRepoText("docs/research-shared-emitter-architecture.md");

            foreach (var doc in new[] { en, zh, research })
            {
                Check(doc.Contains("debug overlay", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("non-contract", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("not included", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("replay", StringComparison.OrdinalIgnoreCase),
                    "112B-E1: docs describe debug overlay as non-contract and not a replay guard key");
            }
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase112B file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase112B validation.");
            return root;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
