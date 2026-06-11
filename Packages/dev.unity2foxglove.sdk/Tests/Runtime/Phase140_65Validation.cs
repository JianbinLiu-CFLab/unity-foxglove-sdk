// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-65 regression coverage for virtual LiDAR sensor allocation optimizations.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_65Validation.
    /// </summary>
    public static class Phase140_65Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-65: Virtual LiDAR and Digital Twin Sensor Optimization ===");
            _passed = 0;

            VirtualLidarCachesScanBoundaryCallback();
            LidarModelRegistryAvoidsPerLookupLinqAllocations();
            LidarModelRegistryPreservesLookupBehavior();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-65: {_passed} checks passed.");
        }

        private static void VirtualLidarCachesScanBoundaryCallback()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/VirtualLidar.cs");
            var fixedUpdate = Slice(source, "private void FixedUpdate()", "private int BudgetColumnsPerTick()");

            Check(source.Contains("private Action _onScanBoundary", StringComparison.Ordinal)
                  && source.Contains("private Action OnScanBoundaryAction", StringComparison.Ordinal)
                  && source.Contains("private void OnScanBoundary()", StringComparison.Ordinal),
                "140-65A-1: VirtualLidar caches the scan-boundary Action");
            Check(fixedUpdate.Contains("OnScanBoundaryAction", StringComparison.Ordinal)
                  && !fixedUpdate.Contains("() =>", StringComparison.Ordinal)
                  && !fixedUpdate.Contains("new Action", StringComparison.Ordinal),
                "140-65A-2: FixedUpdate passes the cached scan-boundary callback instead of allocating a lambda");
            Check(source.Contains("StartNewScan(Time.fixedTimeAsDouble)", StringComparison.Ordinal),
                "140-65A-3: cached scan-boundary callback preserves fixed-time read at invocation");
        }

        private static void LidarModelRegistryAvoidsPerLookupLinqAllocations()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/LidarModelRegistry.cs");

            Check(source.Contains("private static readonly IReadOnlyList<LidarModelSpec> _allReadOnly = _all.AsReadOnly()", StringComparison.Ordinal)
                  && source.Contains("public static IReadOnlyList<LidarModelSpec> All => _allReadOnly", StringComparison.Ordinal),
                "140-65B-1: LidarModelRegistry caches the read-only All wrapper");
            Check(source.Contains("_byVendor.TryGetValue(v, out var specs)", StringComparison.Ordinal)
                  && source.Contains("_byModel.TryGetValue((v, model), out spec)", StringComparison.Ordinal),
                "140-65B-2: LidarModelRegistry uses cached lookups for vendor and model queries");
            Check(!source.Contains("using System.Linq", StringComparison.Ordinal)
                  && !source.Contains(".Where(", StringComparison.Ordinal)
                  && !source.Contains(".FirstOrDefault(", StringComparison.Ordinal),
                "140-65B-3: LidarModelRegistry hot lookups do not depend on LINQ closures");
        }

        private static void LidarModelRegistryPreservesLookupBehavior()
        {
            var all = LidarModelRegistry.All;
            var allAgain = LidarModelRegistry.All;
            Check(ReferenceEquals(all, allAgain) && all.Count > 0,
                "140-65C-1: All returns one cached non-empty read-only view");

            Check(LidarModelRegistry.TryGet(LidarVendor.Ouster, "OS-1-32", out var os132)
                  && os132 != null
                  && os132.Vendor == LidarVendor.Ouster
                  && os132.Model == "OS-1-32",
                "140-65C-2: TryGet still resolves built-in Ouster models");
            Check(!LidarModelRegistry.TryGet(LidarVendor.Ouster, "missing-model", out var missing)
                  && missing == null,
                "140-65C-3: TryGet still reports missing models without a spec");

            var ouster = LidarModelRegistry.ForVendor(LidarVendor.Ouster).ToList();
            Check(ouster.Count > 0 && ouster.All(s => s.Vendor == LidarVendor.Ouster),
                "140-65C-4: ForVendor still returns only models for the requested vendor");
            Check(ouster.Select(s => s.Model).SequenceEqual(all.Where(s => s.Vendor == LidarVendor.Ouster).Select(s => s.Model)),
                "140-65C-5: ForVendor preserves the registry display order");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_65Validation.cs", StringComparison.Ordinal),
                "140-65D-1: test project compiles Phase140_65Validation");
            Check(registry.Contains("Ci(\"--phase140-65\", \"Phase 140-65\", Phase140_65Validation.Validate", StringComparison.Ordinal),
                "140-65D-2: validation registry exposes --phase140-65");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] Missing start token: " + startToken);

            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;

            return source.Substring(start, end - start);
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
