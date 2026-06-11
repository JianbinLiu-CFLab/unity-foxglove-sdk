// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-75 source-shape regression coverage for Unity demo runtime optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_75Validation.
    /// </summary>
    public static class Phase140_75Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-75: Unity Demo Maze and Runtime Scripts Optimization ===");
            _passed = 0;

            VerifyAssetsDemoScaleUsesParameterEvents();
            VerifyPackageSampleScaleUsesParameterEvents();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-75: {_passed} checks passed.");
        }

        private static void VerifyAssetsDemoScaleUsesParameterEvents()
        {
            var source = Read("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            VerifyScaleEventShape(source, "140-75A", "Assets FullDemo");
        }

        private static void VerifyPackageSampleScaleUsesParameterEvents()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");
            VerifyScaleEventShape(source, "140-75B", "Samples~ FullDemo");
        }

        private static void VerifyScaleEventShape(string source, string prefix, string label)
        {
            var initialize = Slice(source, "private bool TryInitializeDemo()", "    /// <summary>\r\n    /// Unsubscribes");
            var update = Slice(source, "private void Update()", "    private GameObject FindCube()");
            var parameterChanged = Slice(source, "private void OnParameterChanged", "    /// <summary>\r\n    /// When the scene cube color changes");

            Check(initialize.Contains("var initialScale = rt.Parameters.GetWireParameter(\"/cube/scale\")?.Value;", StringComparison.Ordinal)
                  && initialize.Contains("ApplyScaleFromParameter(initialScale);", StringComparison.Ordinal),
                $"{prefix}-1: {label} explicitly applies initial /cube/scale after registration");

            Check(parameterChanged.Contains("name == \"/cube/scale\"", StringComparison.Ordinal)
                  && parameterChanged.Contains("ApplyScaleFromParameter(scaleValue)", StringComparison.Ordinal)
                  && source.Contains("private void ApplyScaleFromParameter(JToken value)", StringComparison.Ordinal)
                  && source.Contains("private static bool TryReadScale(JToken value, out float clamped, out string reason)", StringComparison.Ordinal),
                $"{prefix}-2: {label} handles /cube/scale through parameter change events");

            Check(!update.Contains("GetWireParameter(\"/cube/scale\")", StringComparison.Ordinal)
                  && !update.Contains("ApplyScaleFromParameter", StringComparison.Ordinal),
                $"{prefix}-3: {label} Update no longer allocates a wire Parameter DTO for /cube/scale");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_75Validation.cs", StringComparison.Ordinal),
                "140-75C-1: test project compiles Phase140_75Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-75\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_75Validation.Validate", StringComparison.Ordinal),
                "140-75C-2: validation registry exposes --phase140-75");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
