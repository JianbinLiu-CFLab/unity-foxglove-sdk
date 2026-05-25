// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-14 regression coverage for native Draco point-cloud input bounds.

using System;
using System.IO;
using Foxglove.Schemas.PointCloud;
using Unity.FoxgloveSDK.Schemas.PointCloud;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_14Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-14: PointCloud LaserScan Draco Path ===");
            _passed = 0;

            NativeDracoInputBudgetMatchesPackedDataBoundary();
            NativeDracoBudgetRejectsOversizedScratchBeforeAllocation();
            NativeDracoTryEncodeChecksBudgetBeforeScratchAllocation();

            Console.WriteLine($"Phase 134-14: {_passed} checks passed.");
        }

        private static void NativeDracoInputBudgetMatchesPackedDataBoundary()
        {
            Check(DracoPointCloudNativeEncoder.MaxInputBytes == PointCloudPackedDataBuilder.MaxPackedDataBytes,
                "134-14A-1: native Draco input budget matches packed point-cloud data budget");
            Check(DracoPointCloudNativeEncoder.XyzBytesPerPoint == 12,
                "134-14A-2: native Draco XYZ scratch budget uses 12 bytes per point");
            Check(DracoPointCloudNativeEncoder.MaxInputPoints
                  == PointCloudPackedDataBuilder.MaxPackedDataBytes / DracoPointCloudNativeEncoder.XyzBytesPerPoint,
                "134-14A-3: native Draco max point count derives from byte budget");
        }

        private static void NativeDracoBudgetRejectsOversizedScratchBeforeAllocation()
        {
            Check(DracoPointCloudNativeEncoder.ValidateInputBudget(1, out var smallError)
                  && string.IsNullOrEmpty(smallError),
                "134-14B-1: native Draco accepts small input budgets");

            var oversizedPointCount = DracoPointCloudNativeEncoder.MaxInputPoints + 1;
            Check(!DracoPointCloudNativeEncoder.ValidateInputBudget(oversizedPointCount, out var error)
                  && error.Contains(PointCloudPackedDataBuilder.MaxPackedDataBytes.ToString())
                  && error.Contains(((long)oversizedPointCount * DracoPointCloudNativeEncoder.XyzBytesPerPoint).ToString()),
                "134-14B-2: native Draco rejects oversized input budgets with byte details");
        }

        private static void NativeDracoTryEncodeChecksBudgetBeforeScratchAllocation()
        {
            var source = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/PointCloud/DracoPointCloudNativeEncoder.cs");
            var tryEncodeIndex = source.IndexOf("public static bool TryEncode", StringComparison.Ordinal);
            var validateIndex = source.IndexOf(
                "ValidateInputBudget(frame.Points.Count",
                tryEncodeIndex,
                StringComparison.Ordinal);
            var buildIndex = source.IndexOf("BuildXyzArray(frame)", tryEncodeIndex, StringComparison.Ordinal);

            Check(tryEncodeIndex >= 0 && validateIndex > tryEncodeIndex && buildIndex > validateIndex,
                "134-14C-1: TryEncode validates input budget before allocating XYZ scratch");
            Check(source.Contains("PointCloudPackedDataBuilder.MaxPackedDataBytes"),
                "134-14C-2: native Draco source reuses packed point-cloud byte boundary");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
