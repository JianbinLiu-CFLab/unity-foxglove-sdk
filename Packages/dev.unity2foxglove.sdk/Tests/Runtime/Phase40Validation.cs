// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 40 camera backpressure policy validation.

using System;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase40Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 40 Tests ---");
            _passCount = 0;

            TestDisabledPolicyAlwaysAllows();
            TestEnabledUnchangedDropsAllows();
            TestDropIncreaseEntersCooldown();
            TestCooldownBlocksCapture();
            TestCooldownExpiresAllowsCapture();
            TestRepeatedDropExtendsCooldown();
            TestZeroCooldownSkipsOnceOnly();
            TestBudgetZeroIsUnlimited();
            TestBudgetAcceptsUnderOrEqual();
            TestBudgetRejectsOverLimit();

            Console.WriteLine("Phase 40: All checks passed.");
        }

        // ── Policy ──

        private static void TestDisabledPolicyAlwaysAllows()
        {
            var r = CameraBackpressurePolicy.Evaluate(false, 10, 5, 0, 10, 0);
            Check(r.AllowCapture, "40A-1: disabled policy allows capture despite drop increase");
            Check(!r.PressureObserved, "40A-1b: disabled policy reports no pressure");
        }

        private static void TestEnabledUnchangedDropsAllows()
        {
            var r = CameraBackpressurePolicy.Evaluate(true, 10, 5, 5, 5, 0);
            Check(r.AllowCapture, "40A-2: unchanged drops allows capture");
            Check(!r.PressureObserved, "40A-2b: no pressure observed");
        }

        private static void TestDropIncreaseEntersCooldown()
        {
            var r = CameraBackpressurePolicy.Evaluate(true, 10, 3, 0, 1, 0);
            Check(!r.AllowCapture, "40A-3: drop increase blocks capture");
            Check(r.SkippedByCooldown, "40A-3b: skipped by cooldown");
            Check(r.PressureObserved, "40A-3c: pressure observed");
            Check(r.NextCooldownUntilSec == 13, "40A-3d: cooldown until 10 + 3 = 13");
            Check(r.NextDropCount == 1, "40A-3e: next drop count is current");
        }

        private static void TestCooldownBlocksCapture()
        {
            var r = CameraBackpressurePolicy.Evaluate(true, 11, 5, 10, 10, 15);
            Check(!r.AllowCapture, "40A-4: cooldown window blocks capture");
            Check(r.SkippedByCooldown, "40A-4b: skipped by cooldown");
        }

        private static void TestCooldownExpiresAllowsCapture()
        {
            var r = CameraBackpressurePolicy.Evaluate(true, 16, 5, 10, 10, 15);
            Check(r.AllowCapture, "40A-5: cooldown expired allows capture");
            Check(!r.SkippedByCooldown, "40A-5b: not skipped");
        }

        private static void TestRepeatedDropExtendsCooldown()
        {
            var r1 = CameraBackpressurePolicy.Evaluate(true, 10, 5, 0, 1, 0);
            var r2 = CameraBackpressurePolicy.Evaluate(true, 12, 5, r1.NextDropCount, 2, r1.NextCooldownUntilSec);
            Check(!r2.AllowCapture, "40A-6: repeated drop extends cooldown");
            Check(r2.NextCooldownUntilSec == 17, "40A-6b: extended to 12 + 5 = 17");
            Check(r2.NextDropCount == 2, "40A-6c: next drop count updated");
        }

        private static void TestZeroCooldownSkipsOnceOnly()
        {
            var r = CameraBackpressurePolicy.Evaluate(true, 10, 0, 0, 1, 0);
            Check(r.AllowCapture, "40A-7: zero cooldown skips one sample only (allow capture)");
            Check(r.PressureObserved, "40A-7b: pressure observed but capture allowed");

            var r2 = CameraBackpressurePolicy.Evaluate(true, 10.1, 0, r.NextDropCount, 1, r.NextCooldownUntilSec);
            Check(r2.AllowCapture, "40A-7c: subsequent evaluation also allows capture");
        }

        // ── Payload Budget ──

        private static void TestBudgetZeroIsUnlimited()
        {
            Check(!CameraBackpressurePolicy.ExceedsBudget(new byte[9999], 0), "40A-8: budget=0 accepts any size");
            Check(!CameraBackpressurePolicy.ExceedsBudget(null, 0), "40A-8b: budget=0 accepts null");
        }

        private static void TestBudgetAcceptsUnderOrEqual()
        {
            Check(!CameraBackpressurePolicy.ExceedsBudget(new byte[100], 200), "40A-9: under budget accepted");
            Check(!CameraBackpressurePolicy.ExceedsBudget(new byte[200], 200), "40A-9b: equal to budget accepted");
        }

        private static void TestBudgetRejectsOverLimit()
        {
            Check(CameraBackpressurePolicy.ExceedsBudget(new byte[201], 200), "40A-10: over budget rejected");
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
