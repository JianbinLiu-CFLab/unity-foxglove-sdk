// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 41 FoxRun event-driven publish policy validation.

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase41Validation.
    /// </summary>
    public static class Phase41Validation
    {
        private static int _passCount;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 41 Tests ---");
            _passCount = 0;

            TestFixedRateAlwaysPublishes();
            TestChangeFirstSample();
            TestChangeSkipsUnchanged();
            TestChangePublishesAfterChanged();
            TestChangeOrIntervalHeartbeat();
            TestHeartbeatDisabled();
            TestEpsilonSuppressesSmallChange();
            TestEpsilonAllowsLargeChange();
            TestRepeatedFloatNaNNoSpam();
            TestRepeatedDoubleNaNNoSpam();
            TestEmitterOutputPolicyMetadata();
            TestBackwardCompatConstructor();
            TestEnumValuesAreDistinct();
            TestEmitterIncludesGenericComparerUsing();
            TestEmitterPreservesChangeMode();
            TestEmitterUsesUniqueLastValueFields();
            TestEmitterUsesNaNSafeFloatComparison();
            TestEmitterUsesConfiguredEpsilonInGeneratedSource();

            Console.WriteLine("Phase 41: All checks passed.");
        }

        // 鈹€鈹€ Policy 鈹€鈹€

        private static void TestFixedRateAlwaysPublishes()
        {
            var r = FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.FixedRate, 10, true, false, 8, 0);
            Check(r, "41A-1: FixedRate always publishes even when unchanged");
        }

        private static void TestChangeFirstSample()
        {
            var r = FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.Change, 10, false, false, 0, 0);
            Check(r, "41A-2: Change publishes first sample");
        }

        private static void TestChangeSkipsUnchanged()
        {
            var r = FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.Change, 10, true, false, 8, 0);
            Check(!r, "41A-3: Change skips unchanged value");
        }

        private static void TestChangePublishesAfterChanged()
        {
            var r = FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.Change, 10, true, true, 8, 0);
            Check(r, "41A-4: Change publishes after value changed");
        }

        private static void TestChangeOrIntervalHeartbeat()
        {
            var r = FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.ChangeOrInterval, 15, true, false, 10, 5);
            Check(r, "41A-5: heartbeat fires after interval expiry (15 >= 10 + 5)");
        }

        private static void TestHeartbeatDisabled()
        {
            var r = FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.ChangeOrInterval, 12, true, false, 10, 0);
            Check(!r, "41A-6: zero force interval does not trigger heartbeat");
        }

        private static void TestEpsilonSuppressesSmallChange()
        {
            var changed = FoxRunChangeHelper.FloatChanged(1.05f, 1.0f, 0.1f);
            var shouldPublish = FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change,
                nowSec: 10,
                hasPreviousValue: true,
                valueChanged: changed,
                lastPublishSec: 8,
                forceIntervalSec: 0);
            Check(!changed && !shouldPublish, "41A-7: epsilon 0.1 suppresses diff 0.05 through publish policy");
        }

        private static void TestEpsilonAllowsLargeChange()
        {
            var changed = FoxRunChangeHelper.FloatChanged(1.2f, 1.0f, 0.1f);
            var shouldPublish = FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change,
                nowSec: 10,
                hasPreviousValue: true,
                valueChanged: changed,
                lastPublishSec: 8,
                forceIntervalSec: 0);
            Check(changed && shouldPublish, "41A-8: epsilon 0.1 allows diff 0.2 through publish policy");
        }

        private static void TestRepeatedFloatNaNNoSpam()
        {
            var changed = FoxRunChangeHelper.FloatChanged(float.NaN, float.NaN, 0f);
            Check(!changed, "41A-9: repeated float NaN does not spam publishes");
        }

        private static void TestRepeatedDoubleNaNNoSpam()
        {
            var changed = FoxRunChangeHelper.DoubleChanged(double.NaN, double.NaN, 0d);
            Check(!changed, "41A-10: repeated double NaN does not spam publishes");
        }

        // 鈹€鈹€ Emitter / Backward Compat 鈹€鈹€

        private static void TestEmitterOutputPolicyMetadata()
        {
            var tm = new FoxgloveSourceEmitter.TopicMember("_val", "float", "/debug/x", 10f, "",
                (int)FoxRunPolicy.Change, 0.01f, 2f);
            Check(tm.Policy == (int)FoxRunPolicy.Change, "41A-11a: emitter topic member carries Change mode");
            Check(tm.ChangeEpsilon == 0.01f, "41A-11b: emitter topic member carries epsilon");
            Check(tm.ForceIntervalSeconds == 2f, "41A-11c: emitter topic member carries force interval");
        }

        private static void TestBackwardCompatConstructor()
        {
            var tm = new FoxgloveSourceEmitter.TopicMember("_val", "float", "/debug/y", 10f, "");
            Check(tm.Policy == (int)FoxRunPolicy.FixedRate, "41A-12a: convenience constructor defaults to FixedRate");
            Check(tm.ChangeEpsilon == 0f, "41A-12b: convenience constructor defaults epsilon to 0");
            Check(tm.ForceIntervalSeconds == 0f, "41A-12c: convenience constructor defaults force interval to 0");
        }

        private static void TestEnumValuesAreDistinct()
        {
            Check(FoxRunPolicy.FixedRate != FoxRunPolicy.Change, "41A-13a: FixedRate != Change");
            Check(FoxRunPolicy.Change != FoxRunPolicy.ChangeOrInterval, "41A-13b: Change != ChangeOrInterval");
            Check((int)FoxRunPolicy.FixedRate == 1, "41A-13c: FixedRate integer value = 1");
            Check((int)FoxRunPolicy.Change == 2, "41A-13d: Change integer value = 2");
            Check((int)FoxRunPolicy.ChangeOrInterval == 3, "41A-13e: ChangeOrInterval integer value = 3");
            Check((int)FoxRunPolicy.Trigger == 4, "41A-13f: Trigger integer value = 4");
        }

        private static void TestEmitterIncludesGenericComparerUsing()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "PolicyStringSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Name", "string", "/debug/name", 10f, "",
                    (int)FoxRunPolicy.Change, 0f, 0f)
            });
            Check(source.Contains("using System.Collections.Generic;"),
                "41C-1: generated policy source imports EqualityComparer namespace");
        }

        private static void TestEmitterPreservesChangeMode()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "PolicyModeSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Value", "float", "/debug/value", 10f, "",
                    (int)FoxRunPolicy.Change, 0f, 5f)
            });
            Check(source.Contains("FoxRunPolicy.Change, nowSec"),
                "41C-2: generated source preserves Change instead of forcing heartbeat mode");
        }

        private static void TestEmitterUsesUniqueLastValueFields()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "PolicyMultiTopicSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Value", "float", "/debug/a", 10f, "",
                    (int)FoxRunPolicy.Change, 0f, 0f),
                new FoxgloveSourceEmitter.TopicMember("Value", "float", "/debug/b", 10f, "",
                    (int)FoxRunPolicy.Change, 0f, 0f)
            });
            Check(source.Contains("private float __last_0_0;") && source.Contains("private float __last_1_0;"),
                "41C-3: generated last-value fields are unique per topic/member");
            Check(!source.Contains("__last_Value"),
                "41C-3b: generated last-value fields do not collide on member name");
        }

        private static void TestEmitterUsesNaNSafeFloatComparison()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "PolicyNaNSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Value", "float", "/debug/value", 10f, "",
                    (int)FoxRunPolicy.Change, 0.01f, 0f)
            });
            Check(source.Contains("FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal),
                "41C-4: generated float comparison uses FoxRunChangeHelper (NaN-safe by contract)");
        }

        private static void TestEmitterUsesConfiguredEpsilonInGeneratedSource()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "PolicyEpsilonSource", new[]
            {
                new FoxgloveSourceEmitter.TopicMember("Value", "float", "/debug/value", 10f, "",
                    (int)FoxRunPolicy.Change, 0.1f, 0f)
            });

            Check(source.Contains("FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal)
                  && source.Contains("this.Value", StringComparison.Ordinal)
                  && source.Contains("__last_0_0", StringComparison.Ordinal)
                  && source.Contains("0.100000001f", StringComparison.Ordinal),
                "41C-5: generated source passes configured epsilon to float comparer");
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                _passCount++;
                Console.WriteLine($"[PASS] {label}");
                return;
            }
            throw new Exception($"[FAIL] {label}");
        }
    }
}
