// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-5 replay object adapter review fixes.

using System;
using System.IO;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for replay object adapter defects found in Phase 140-5.
    /// </summary>
    public static class Phase140_5Validation
    {
        private const string AdapterPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs";

        private const string ReplayContextPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayMessageContext.cs";

        private static int _passed;

        /// <summary>Runs all Phase 140-5 replay adapter review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-5: Replay object adapter review fixes ===");
            _passed = 0;

            ReplaySessionIdentityUsesControllerSessionId();
            ReplayAdapterDoesNotReclassifyManagerNonPose();
            ReplayAdapterCachesLookupMissesAndEvictsStaleTransforms();
            ReplayAdapterReadsPartialJsonFieldsSafely();
            ReplayAdapterCachesReflectionAndPreservesParseStacks();
            ReplayAdapterReflectionCacheAvoidsHotPathStringKeys();
            ReplayAdapterReusesCoordinateConversionDecisionPerPose();
            ReplayAdapterDocumentsSinglePrimitiveEntityConstraint();

            Console.WriteLine($"Phase 140-5: {_passed} checks passed.");
        }

        private static void ReplaySessionIdentityUsesControllerSessionId()
        {
            var context = ReadRepoText(ReplayContextPath);
            var controller = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            var adapter = ReadRepoText(AdapterPath);

            Check(context.Contains("public readonly ulong ReplaySessionId;", StringComparison.Ordinal)
                  && context.Contains("ReplaySessionId = replaySessionId;", StringComparison.Ordinal),
                "140-5A-1: replay message and batch contexts carry a replay session id");
            Check(controller.Contains("_replaySessionId", StringComparison.Ordinal)
                  && controller.Contains("NextReplaySessionId", StringComparison.Ordinal)
                  && controller.Contains("replaySessionId:", StringComparison.Ordinal)
                  && controller.Contains("ReplaySessionId", StringComparison.Ordinal),
                "140-5A-2: replay controller publishes a unique session id into scene contexts");
            Check(adapter.Contains("_activeReplaySessionId", StringComparison.Ordinal)
                  && adapter.Contains("IsSameReplaySession(context)", StringComparison.Ordinal)
                  && adapter.Contains("context.ReplaySessionId", StringComparison.Ordinal),
                "140-5A-3: replay adapter resets state by session id instead of start time alone");
        }

        private static void ReplayAdapterDoesNotReclassifyManagerNonPose()
        {
            var source = ReadRepoText(AdapterPath);
            var resolveBehavior = ExtractMethodBody(source, "private ReplayChannelBehavior ResolveBehavior");

            Check(Ordered(resolveBehavior,
                    "behavior == ReplayChannelBehavior.NonPose",
                    "return behavior;")
                  && Ordered(resolveBehavior,
                    "return behavior;",
                    "ReplayChannelBehaviorClassifier.ClassifyJsonObject"),
                "140-5B-1: manager NonPose classification is final before JSON shape heuristics");
        }

        private static void ReplayAdapterCachesLookupMissesAndEvictsStaleTransforms()
        {
            var source = ReadRepoText(AdapterPath);
            var reset = ExtractMethodBody(source, "private void ResetPoseOwnershipSession");
            var resolveFrame = ExtractMethodBody(source, "private Transform ResolveFrame");
            var resolveEntity = ExtractMethodBody(source, "private Transform ResolveEntity");

            Check(source.Contains("_missedFrames", StringComparison.Ordinal)
                  && source.Contains("_missedEntities", StringComparison.Ordinal)
                  && reset.Contains("_missedFrames.Clear();", StringComparison.Ordinal)
                  && reset.Contains("_missedEntities.Clear();", StringComparison.Ordinal),
                "140-5C-1: replay adapter keeps per-session negative lookup caches");
            Check(resolveFrame.Contains("target != null", StringComparison.Ordinal)
                  && resolveFrame.Contains("_frameCache.Remove(childFrameId)", StringComparison.Ordinal)
                  && resolveEntity.Contains("target != null", StringComparison.Ordinal)
                  && resolveEntity.Contains("_entityCache.Remove(entityId)", StringComparison.Ordinal),
                "140-5C-2: replay adapter evicts destroyed cached frame/entity targets");
            Check(resolveFrame.Contains("!_missedFrames.Contains(childFrameId)", StringComparison.Ordinal)
                  && resolveFrame.Contains("_missedFrames.Add(childFrameId)", StringComparison.Ordinal)
                  && resolveEntity.Contains("!_missedEntities.Contains(entityId)", StringComparison.Ordinal)
                  && resolveEntity.Contains("_missedEntities.Add(entityId)", StringComparison.Ordinal),
                "140-5C-3: replay adapter avoids repeated GameObject.Find for known misses");
        }

        private static void ReplayAdapterReadsPartialJsonFieldsSafely()
        {
            var source = ReadRepoText(AdapterPath);

            Check(source.Contains("ReadJsonFloat(", StringComparison.Ordinal)
                  && !source.Contains("(float)translation[\"x\"]", StringComparison.Ordinal)
                  && !source.Contains("(float)rotation[\"w\"]", StringComparison.Ordinal)
                  && !source.Contains("(float)scaleObj[\"x\"]", StringComparison.Ordinal)
                  && !source.Contains("(float)color[\"r\"]", StringComparison.Ordinal),
                "140-5D-1: replay adapter uses null-safe JSON numeric reads for pose and visuals");
        }

        private static void ReplayAdapterCachesReflectionAndPreservesParseStacks()
        {
            var source = ReadRepoText(AdapterPath);

            Check(source.Contains("ProtobufParserCache", StringComparison.Ordinal)
                  && source.Contains("ResolveProtobufParser", StringComparison.Ordinal)
                  && source.Contains("ReplayPropertyCache.Resolve", StringComparison.Ordinal),
                "140-5E-1: replay adapter caches protobuf parser and property reflection lookups");
            Check(source.Contains("ExceptionDispatchInfo.Capture(ex.InnerException).Throw();", StringComparison.Ordinal)
                  && source.Contains("FormatReplayException(ex)", StringComparison.Ordinal),
                "140-5E-2: replay adapter preserves protobuf parse stacks and logs full replay exceptions");
        }

        private static void ReplayAdapterReflectionCacheAvoidsHotPathStringKeys()
        {
            var source = ReadRepoText(AdapterPath);

            Check(source.Contains("ReplayPropertyCache.Resolve", StringComparison.Ordinal)
                  && !source.Contains("type.FullName +", StringComparison.Ordinal),
                "140-5E-3: property reflection cache uses a non-allocating value key");
        }

        private static void ReplayAdapterReusesCoordinateConversionDecisionPerPose()
        {
            var source = ReadRepoText(AdapterPath);
            var applyPose = ExtractMethodBody(source, "private void ApplyPoseSample");

            Check(Regex.Matches(applyPose, @"\bShouldConvert\b").Count == 1
                  && applyPose.Contains("var shouldConvert = ShouldConvert;", StringComparison.Ordinal),
                "140-5E-4: pose application resolves coordinate conversion once");
        }

        private static void ReplayAdapterDocumentsSinglePrimitiveEntityConstraint()
        {
            var source = ReadRepoText(AdapterPath);

            Check(source.Contains("one target Transform per entity", StringComparison.Ordinal)
                  && source.Contains("first cube/model primitive", StringComparison.Ordinal),
                "140-5F-1: replay adapter documents the intentional single-primitive entity mapping");
        }

        private static string ExtractMethodBody(string source, string signaturePrefix)
        {
            var signatureIndex = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
            if (signatureIndex < 0)
                return string.Empty;
            var braceIndex = source.IndexOf('{', signatureIndex);
            if (braceIndex < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceIndex; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(braceIndex, i - braceIndex + 1);
                }
            }

            return string.Empty;
        }

        private static bool Ordered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
