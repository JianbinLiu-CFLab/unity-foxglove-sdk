// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-5 replay adapter and FoxRun hub hardening.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_5Validation
    {
        private const string ReplayAdapterPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs";
        private const string FoxRunHubPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs";
        private const string DebugOverlayPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveDebugOverlay.cs";
        private const string SchemaInfoRegistryPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs";
        private const string SchemaMcapMetadataPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaMcapMetadata.cs";
        private const string SchemaContractInfoPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaContractInfo.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-5: Replay object adapter and FoxRun hub hardening ===");
            _passed = 0;

            VerifyReplayAdapterNullSafeMappings();
            VerifyReplayAdapterRuntimeGuards();
            VerifyFoxRunHubIsolatesSourceFailures();
            VerifyFoxRunHubRegistrationAndScanGuards();
            VerifySchemaRegistryAndMetadataGuards();
            VerifyContractPolicyNormalization();

            Console.WriteLine($"Phase 134-5: {_passed} checks passed.");
        }

        private static void VerifyReplayAdapterNullSafeMappings()
        {
            var source = ReadRepoText(ReplayAdapterPath);
            Check(source.Contains("FrameMapping[] _frameOverrides = Array.Empty<FrameMapping>()", StringComparison.Ordinal)
                  && source.Contains("EntityMapping[] _entityOverrides = Array.Empty<EntityMapping>()", StringComparison.Ordinal),
                "134-5A-1: replay adapter initializes optional mapping arrays to empty arrays");
            Check(source.Contains("private void EnsureMappingArrays()", StringComparison.Ordinal)
                  && source.Contains("if (_frameOverrides == null)", StringComparison.Ordinal)
                  && source.Contains("if (_entityOverrides == null)", StringComparison.Ordinal),
                "134-5A-2: replay adapter repairs null serialized mapping arrays");
            Check(source.Contains("private void OnValidate()", StringComparison.Ordinal)
                  && source.Contains("EnsureMappingArrays();", StringComparison.Ordinal),
                "134-5A-3: replay adapter normalizes mapping arrays during Inspector validation");

            var start = Slice(source, "private void Start()", "/// <summary>Subscribes");
            Check(start.Contains("EnsureMappingArrays();", StringComparison.Ordinal)
                  && start.IndexOf("EnsureMappingArrays();", StringComparison.Ordinal)
                  < start.IndexOf("foreach (var fm in _frameOverrides)", StringComparison.Ordinal)
                  && start.IndexOf("EnsureMappingArrays();", StringComparison.Ordinal)
                  < start.IndexOf("foreach (var em in _entityOverrides)", StringComparison.Ordinal),
                "134-5A-4: replay adapter repairs mapping arrays before Startup iteration");
        }

        private static void VerifyReplayAdapterRuntimeGuards()
        {
            var source = ReadRepoText(ReplayAdapterPath);
            Check(source.Contains("MaxReplayJsonPayloadBytes = 4 * 1024 * 1024", StringComparison.Ordinal)
                  && source.Contains("payload.Length > MaxReplayJsonPayloadBytes", StringComparison.Ordinal)
                  && source.Contains("Encoding.UTF8.GetString(payload)", StringComparison.Ordinal)
                  && source.IndexOf("payload.Length > MaxReplayJsonPayloadBytes", StringComparison.Ordinal)
                  < source.IndexOf("Encoding.UTF8.GetString(payload)", StringComparison.Ordinal),
                "134-5A-5: replay adapter bounds JSON payloads before UTF-8 string allocation");
            Check(source.Contains("catch (Exception ex) when (IsRecoverableReplayException(ex))", StringComparison.Ordinal)
                  && source.Contains("!(ex is OutOfMemoryException)", StringComparison.Ordinal)
                  && source.Contains("!(ex is AccessViolationException)", StringComparison.Ordinal),
                "134-5A-6: replay adapter does not swallow fatal-like exceptions");

            var reset = Slice(source, "private void ResetPoseOwnershipSession()", "private void FlushDeferredScenePoses");
            Check(reset.Contains("_warnedFrames.Clear();", StringComparison.Ordinal)
                  && reset.Contains("_warnedEntities.Clear();", StringComparison.Ordinal)
                  && reset.Contains("_warnedTopics.Clear();", StringComparison.Ordinal)
                  && !source.Contains("_warnedAuto", StringComparison.Ordinal),
                "134-5A-7: replay adapter warning de-dup sets reset with each replay session");
        }

        private static void VerifyFoxRunHubIsolatesSourceFailures()
        {
            var source = ReadRepoText(FoxRunHubPath);
            Check(source.Contains("private readonly HashSet<string> _warnedSourceFailures = new();", StringComparison.Ordinal),
                "134-5B-1: FoxRun hub tracks de-duplicated source failure warnings");
            Check(source.Contains("TryPublishScheduledTopic(kv.Key, i, ref t[i], nowNs, nowSec)", StringComparison.Ordinal),
                "134-5B-2: FoxRun scheduled updates route through per-topic isolation");
            Check(source.Contains("private bool TryPublishScheduledTopic", StringComparison.Ordinal)
                  && source.Contains("catch (Exception ex)", StringComparison.Ordinal)
                  && source.Contains("LogSourceFailure(source, topicIndex, \"scheduled publish\", ex)", StringComparison.Ordinal)
                  && source.Contains("return false;", StringComparison.Ordinal),
                "134-5B-3: FoxRun scheduled source exceptions are contained and reported");
            Check(source.Contains("private bool TryPublishTriggeredTopic", StringComparison.Ordinal)
                  && source.Contains("LogSourceFailure(source, topicIndex, \"trigger publish\", ex)", StringComparison.Ordinal)
                  && source.Contains("return TryPublishTriggeredTopic(source, topicIndex, _mgr.NowNs", StringComparison.Ordinal),
                "134-5B-4: FoxRun trigger publishes return false instead of throwing on generated source failure");
            Check(source.Contains("[FoxRun] {operation} failed", StringComparison.Ordinal)
                  && source.Contains("_warnedSourceFailures.Add(key)", StringComparison.Ordinal),
                "134-5B-5: FoxRun source failure warnings identify operation/source/topic and suppress repeats");

            var update = Slice(source, "private void Update()", "private bool TryPublishScheduledTopic");
            Check(!update.Contains("FoxgloveLog_Publish", StringComparison.Ordinal)
                  && !update.Contains("FoxgloveLog_ShouldPublish", StringComparison.Ordinal)
                  && !update.Contains("FoxgloveLog_MarkPublished", StringComparison.Ordinal),
                "134-5B-6: FoxRun Update no longer calls generated source methods directly");
        }

        private static void VerifyFoxRunHubRegistrationAndScanGuards()
        {
            var source = ReadRepoText(FoxRunHubPath);
            Check(source.Contains("PendingRegistrations", StringComparison.Ordinal)
                  && source.Contains("PendingRegistrations.Add(source)", StringComparison.Ordinal)
                  && source.Contains("DrainPendingRegistrations();", StringComparison.Ordinal),
                "134-5B-7: FoxRun hub preserves early source registrations until singleton creation");
            Check(source.Contains("[SerializeField] private bool _enableFallbackSceneScan = true;", StringComparison.Ordinal)
                  && source.Contains("if (_enableFallbackSceneScan)", StringComparison.Ordinal)
                  && source.Contains("Scan();", StringComparison.Ordinal),
                "134-5B-8: FoxRun fallback scene scan can be disabled when self-registration is reliable");
            Check(source.Contains("catch (Exception ex) when (IsRecoverableSourceException(ex))", StringComparison.Ordinal)
                  && source.Contains("!(ex is OutOfMemoryException)", StringComparison.Ordinal),
                "134-5B-9: FoxRun source isolation preserves fatal-like exceptions");

            var overlay = ReadRepoText(DebugOverlayPath);
            var publish = Slice(overlay, "public static bool Publish(", "public static bool PublishValue(");
            Check(publish.IndexOf("manager.SuppressLivePublishersForReplay", StringComparison.Ordinal)
                  < publish.IndexOf("FoxgloveDebugOverlayEnvelope.TryCreate", StringComparison.Ordinal),
                "134-5B-10: debug overlay checks manager state before building envelopes");
            Check(overlay.Contains("catch (Exception ex) when (IsRecoverablePublishException(ex))", StringComparison.Ordinal)
                  && overlay.Contains("!(ex is OutOfMemoryException)", StringComparison.Ordinal),
                "134-5B-11: debug overlay does not swallow fatal-like exceptions");
        }

        private static void VerifySchemaRegistryAndMetadataGuards()
        {
            var registry = ReadRepoText(SchemaInfoRegistryPath);
            Check(registry.Contains("private static readonly object Sync = new();", StringComparison.Ordinal)
                  && registry.Contains("lock (Sync)", StringComparison.Ordinal),
                "134-5C-1: FoxRun schema info registry serializes static reads and writes");
            Check(registry.Contains("internal static void ClearForTests()", StringComparison.Ordinal)
                  && !registry.Contains("public static void ClearForTests()", StringComparison.Ordinal),
                "134-5C-2: schema registry test clear hook is no longer public runtime API");

            var first = CreateManifest("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            var same = CreateManifest(first.GlobalManifestHash);
            var conflict = CreateManifest("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
            FoxRunSchemaInfoRegistry.ClearForTests();
            Parallel.Invoke(
                () => FoxRunSchemaInfoRegistry.RegisterGenerated(first),
                () => FoxRunSchemaInfoRegistry.RegisterGenerated(same),
                () => FoxRunSchemaInfoRegistry.RegisterGenerated(conflict),
                () => { var _ = FoxRunSchemaInfoRegistry.Current; });
            Check(FoxRunSchemaInfoRegistry.HasGeneratedSchemaInfo
                  && FoxRunSchemaInfoRegistry.Current != null
                  && FoxRunSchemaInfoRegistry.HasConflict,
                "134-5C-3: schema registry remains readable under concurrent registration");
            FoxRunSchemaInfoRegistry.ClearForTests();

            var metadata = ReadRepoText(SchemaMcapMetadataPath);
            Check(metadata.Contains("IsRecoverableJsonParseException", StringComparison.Ordinal)
                  && metadata.Contains("ex is FormatException", StringComparison.Ordinal)
                  && metadata.Contains("ex is OverflowException", StringComparison.Ordinal)
                  && metadata.Contains("ex is InvalidCastException", StringComparison.Ordinal),
                "134-5C-4: FoxRun schema metadata parse failures cover non-fatal conversion errors");
        }

        private static void VerifyContractPolicyNormalization()
        {
            var source = ReadRepoText(SchemaContractInfoPath);
            Check(source.Contains("NormalizeNonNegative(changeEpsilon)", StringComparison.Ordinal)
                  && source.Contains("NormalizeNonNegative(forceIntervalSeconds)", StringComparison.Ordinal),
                "134-5D-1: FoxRun contract policy metadata normalizes non-negative values");

            var contract = new FoxRunSchemaContractInfo(
                "Type",
                "/topic",
                "schema",
                "json",
                "contract",
                "binding",
                "policy",
                "FixedRate",
                1f,
                -0.5f,
                float.NaN,
                Array.Empty<FoxRunSchemaFieldInfo>());
            Check(contract.ChangeEpsilon == 0f && contract.ForceIntervalSeconds == 0f,
                "134-5D-2: FoxRun contract policy constructor clamps negative and NaN values");
        }

        private static FoxRunSchemaManifestInfo CreateManifest(string hash)
        {
            return new FoxRunSchemaManifestInfo(
                1,
                "dev.unity2foxglove.sdk",
                "Unity2Foxglove.FoxRun",
                1,
                hash,
                hash,
                new List<FoxRunSchemaTypeInfo>
                {
                    new FoxRunSchemaTypeInfo(
                        "Fixture",
                        new[]
                        {
                            new FoxRunSchemaContractInfo(
                                "Fixture",
                                "/fixture",
                                "Fixture",
                                "json",
                                hash,
                                hash,
                                hash,
                                "FixedRate",
                                1f,
                                0f,
                                0f,
                                Array.Empty<FoxRunSchemaFieldInfo>())
                        })
                });
        }

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            return end < 0 ? source.Substring(start) : source.Substring(start, end - start);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))
                    || Directory.Exists(Path.Combine(dir, "Packages")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
