// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 53 FoxRun explicit trigger telemetry validation.

using System;
using System.IO;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase53Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 53: FoxRun Triggered Event Telemetry ===");
            _passed = 0;

            VerifyOnTriggerModeContract();
            VerifyEmitterGeneratesTriggerMethods();
            VerifyEmitterSkipsTimerOnlyTriggers();
            VerifyEmitterRoutesMemberTriggerTopics();
            VerifyMixedTopicWithTriggerIsTriggerOnly();
            VerifyTriggerApiSourceContract();

            Console.WriteLine($"Phase 53: {_passed} checks passed.");
        }

        private static void VerifyOnTriggerModeContract()
        {
            Check((int)FoxRunPublishMode.OnTrigger == 3,
                "53A-1: OnTrigger enum value is stable at 3");

            var shouldPublish = FoxRunPublishPolicy.ShouldPublish(
                FoxRunPublishMode.OnTrigger, 10, false, true, 0, 0);
            Check(!shouldPublish,
                "53A-2: OnTrigger never publishes from scheduled policy ticks");

            Check(FoxRunPublishPolicy.ShouldPublish(FoxRunPublishMode.FixedRate, 10, true, false, 8, 0),
                "53A-3a: FixedRate behavior remains unchanged");
            Check(!FoxRunPublishPolicy.ShouldPublish(FoxRunPublishMode.OnChange, 10, true, false, 8, 0),
                "53A-3b: OnChange unchanged values still skip");
            Check(FoxRunPublishPolicy.ShouldPublish(FoxRunPublishMode.OnChangeOrInterval, 15, true, false, 10, 5),
                "53A-3c: OnChangeOrInterval heartbeat still publishes");
        }

        private static void VerifyEmitterGeneratesTriggerMethods()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "TriggerSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("_state", "System.String", "/events/state", 10f, "",
                    (int)FoxRunPublishMode.OnTrigger, 0f, 0f)
            });

            Check(source.Contains("public bool FoxRun_Trigger_state()"),
                "53B-1: emitter includes member trigger method for OnTrigger field");
            Check(source.Contains("public bool FoxRun_TriggerAll()"),
                "53B-2: emitter includes TriggerAll when any trigger topic exists");
            Check(source.Contains("FoxgloveLogHub.Trigger(this, 0)"),
                "53B-3: generated trigger method calls FoxgloveLogHub.Trigger");
            Check(!source.Contains("System.Reflection") && !source.Contains("GetCustomAttributes"),
                "53B-4: generated trigger source contains no runtime reflection calls");
        }

        private static void VerifyEmitterSkipsTimerOnlyTriggers()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "TimerOnlySource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("_value", "System.Int32", "/debug/value", 10f, "",
                    (int)FoxRunPublishMode.OnChange, 0f, 0f)
            });

            Check(!source.Contains("FoxRun_Trigger_value") && !source.Contains("FoxRun_TriggerAll"),
                "53B-5: emitter does not generate trigger methods for timer-only members");
            Check(!source.Contains("FoxgloveLogHub.Trigger"),
                "53B-6: timer-only generated source does not route through trigger API");
        }

        private static void VerifyEmitterRoutesMemberTriggerTopics()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "MultiTriggerSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("_event", "System.Int32", "/events/a", 10f, "",
                    (int)FoxRunPublishMode.OnTrigger, 0f, 0f),
                new FoxgloveSourceEmitter.TopicMember("_event", "System.Int32", "/events/b", 10f, "",
                    (int)FoxRunPublishMode.OnTrigger, 0f, 0f),
                new FoxgloveSourceEmitter.TopicMember("_event", "System.Int32", "/debug/timer", 10f, "",
                    (int)FoxRunPublishMode.FixedRate, 0f, 0f)
            });

            var body = ExtractMethodBody(source, "FoxRun_Trigger_event");
            Check(body.Contains("FoxgloveLogHub.Trigger(this, 0)") && body.Contains("FoxgloveLogHub.Trigger(this, 1)"),
                "53B-7: same member trigger method calls every trigger topic index");
            Check(!body.Contains("FoxgloveLogHub.Trigger(this, 2)"),
                "53B-8: member trigger method excludes timer-only topic indexes");
        }

        private static void VerifyMixedTopicWithTriggerIsTriggerOnly()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "MixedTriggerSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("_event", "System.String", "/events/mixed", 10f, "",
                    (int)FoxRunPublishMode.OnTrigger, 0f, 0f),
                new FoxgloveSourceEmitter.TopicMember("_counter", "System.Int32", "/events/mixed", 10f, "",
                    (int)FoxRunPublishMode.OnChange, 0f, 0f)
            });

            Check(source.Contains("FoxRunPublishMode.OnTrigger"),
                "53C-1: mixed topic with any trigger member reports OnTrigger topic metadata");
            Check(source.Contains("case 0: return false;"),
                "53C-2: mixed trigger topic is skipped by scheduled policy path");
            Check(source.Contains("public bool FoxRun_Trigger_event()"),
                "53C-3: trigger member in mixed topic gets a member trigger method");
            Check(!source.Contains("FoxRun_Trigger_counter()"),
                "53C-4: timer-only member in mixed topic does not get a trigger method");
        }

        private static void VerifyTriggerApiSourceContract()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");

            Check(source.Contains("public static bool Trigger(IFoxgloveLogSource source, int topicIndex)"),
                "53D-1: FoxgloveLogHub exposes stable trigger API");
            Check(source.Contains("source == null") && source.Contains("return false"),
                "53D-2: trigger API treats null source as unavailable state");
            Check(source.Contains("SuppressLivePublishersForReplay") && source.Contains("return false"),
                "53D-3: trigger API respects replay live-publisher suppression");
            Check(source.Contains("topicIndex < 0") && source.Contains("FoxgloveLog_TopicCount"),
                "53D-4: trigger API validates topic index");
            Check(source.Contains("FoxgloveLog_Publish(topicIndex") && source.Contains("FoxgloveLog_MarkPublished(topicIndex"),
                "53D-5: trigger API publishes then updates policy state");
            Check(!source.Contains("StartServer") && !source.Contains("Start("),
                "53D-6: trigger API does not start or create manager lifecycle");
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var start = source.IndexOf(methodName, StringComparison.Ordinal);
            if (start < 0) return "";
            var brace = source.IndexOf('{', start);
            if (brace < 0) return "";
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(brace, i - brace + 1);
                }
            }
            return "";
        }

        private static string ReadRepoText(string relativePath)
        {
            var baseDir = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(baseDir, relativePath));
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);
                baseDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
            }

            var cwdCandidate = Path.GetFullPath(relativePath);
            if (File.Exists(cwdCandidate))
                return File.ReadAllText(cwdCandidate);

            throw new FileNotFoundException($"Could not find file '{relativePath}'.");
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }

            throw new Exception($"[FAIL] {label}");
        }
    }
}
