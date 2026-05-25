// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-5 replay adapter and FoxRun hub hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_5Validation
    {
        private const string ReplayAdapterPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs";
        private const string FoxRunHubPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-5: Replay object adapter and FoxRun hub hardening ===");
            _passed = 0;

            VerifyReplayAdapterNullSafeMappings();
            VerifyFoxRunHubIsolatesSourceFailures();

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
