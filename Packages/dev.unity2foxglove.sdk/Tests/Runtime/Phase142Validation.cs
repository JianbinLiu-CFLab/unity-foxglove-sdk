// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 142 FoxRun type safety hardening validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase142Validation.
    /// </summary>
    public static class Phase142Validation
    {
        private static int _passCount;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 142 Tests ---");
            _passCount = 0;

            VerifyChangeHelperParity();
            VerifyNoInlineHelpers();
            VerifySharedHelperReference();
            VerifyDefaultFallbackPreserved();
            VerifyFOXRUN006NativeContainerMessage();
            VerifyFOXRUN006GenericMessageUnchanged();
            VerifyFOXRUN006IdSeverityUnchanged();
            VerifyConditionDiagnosticIdsReservedBy141A();
            VerifyInventoryBackwardCompatible();
            VerifyChangeHelperCompiles();

            Console.WriteLine("Phase 142: " + _passCount + " checks passed.\n");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }

        private static void VerifyChangeHelperParity()
        {
            // NaN / NaN pair (no change)
            Check(!FoxRunChangeHelper.FloatChanged(float.NaN, float.NaN, 0f),
                "142-1: NaN/NaN float pair reports no change");
            Check(!FoxRunChangeHelper.DoubleChanged(double.NaN, double.NaN, 0f),
                "142-2: NaN/NaN double pair reports no change");

            // NaN / finite pair (changed)
            Check(FoxRunChangeHelper.FloatChanged(float.NaN, 1f, 0f),
                "142-3: NaN/finite float pair reports changed");
            Check(FoxRunChangeHelper.DoubleChanged(double.NaN, 1.0, 0f),
                "142-4: NaN/finite double pair reports changed");

            // Identical finite values (no change)
            Check(!FoxRunChangeHelper.FloatChanged(5f, 5f, 0f),
                "142-5: identical float values report no change");
            Check(!FoxRunChangeHelper.DoubleChanged(5.0, 5.0, 0f),
                "142-6: identical double values report no change");

            // Within epsilon (no change)
            Check(!FoxRunChangeHelper.FloatChanged(5f, 5.0001f, 0.001f),
                "142-7: float values within epsilon report no change");
            Check(!FoxRunChangeHelper.DoubleChanged(5.0, 5.0001, 0.001),
                "142-8: double values within epsilon report no change");

            // Just outside epsilon (changed)
            Check(FoxRunChangeHelper.FloatChanged(5f, 5.002f, 0.001f),
                "142-9: float values outside epsilon report changed");
            Check(FoxRunChangeHelper.DoubleChanged(5.0, 5.002, 0.001),
                "142-10: double values outside epsilon report changed");

            // +0.0 vs -0.0 (no change for epsilon=0)
            Check(!FoxRunChangeHelper.FloatChanged(0f, -0f, 0f),
                "142-11: +0.0 vs -0.0 float reports no change");
            Check(!FoxRunChangeHelper.DoubleChanged(0.0, -0.0, 0f),
                "142-12: +0.0 vs -0.0 double reports no change");

            // epsilon = 0, exact comparison
            Check(FoxRunChangeHelper.FloatChanged(1f, 1.0001f, 0f),
                "142-13: epsilon=0 float detects any difference");
            Check(FoxRunChangeHelper.DoubleChanged(1.0, 1.0001, 0f),
                "142-14: epsilon=0 double detects any difference");

            // Max/Min value safety
            Check(!FoxRunChangeHelper.FloatChanged(float.MaxValue, float.MaxValue, 0f),
                "142-15: float.MaxValue identical reports no change");
            Check(FoxRunChangeHelper.FloatChanged(float.MaxValue, float.MinValue, 0f),
                "142-16: float.MaxValue vs float.MinValue reports changed");
        }

        // policy=1 ->Change policy triggers ChangeExpr code path
        private static FoxgloveSourceEmitter.TopicMember Change(string name, string type, string topic)
        {
            FoxRunTypeShape typeShape = null;
            switch (type)
            {
                case "UnityEngine.Vector2":
                    typeShape = FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector2));
                    break;
                case "UnityEngine.Vector3":
                    typeShape = FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Vector3));
                    break;
                case "UnityEngine.Quaternion":
                    typeShape = FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Quaternion));
                    break;
                case "UnityEngine.Color":
                    typeShape = FoxRunReflectionTypeShapeBuilder.Build(typeof(UnityEngine.Color));
                    break;
            }

            return new FoxgloveSourceEmitter.TopicMember(
                name,
                type,
                topic,
                10f,
                "",
                policy: (int)FoxRunPolicy.Change,
                tolerance: 0.001f,
                typeShape: typeShape);
        }

        private static void VerifyNoInlineHelpers()
        {
            var members = new List<FoxgloveSourceEmitter.TopicMember> { Change("_val", "System.Single", "/test") };
            var output = FoxgloveSourceEmitter.EmitClass("Test", "NoInline", members);

            Check(!output.Contains("__foxrun_float_changed", StringComparison.Ordinal),
                "142-17: generated code contains no __foxrun_float_changed inline helper");
            Check(!output.Contains("__foxrun_double_changed", StringComparison.Ordinal),
                "142-18: generated code contains no __foxrun_double_changed inline helper");
        }

        private static void VerifySharedHelperReference()
        {
            var floatOutput = FoxgloveSourceEmitter.EmitClass("Test", "FloatRef",
                new List<FoxgloveSourceEmitter.TopicMember> { Change("_val", "System.Single", "/test/f") });
            Check(floatOutput.Contains("global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal),
                "142-19: float member emits FoxRunChangeHelper.FloatChanged");

            var doubleOutput = FoxgloveSourceEmitter.EmitClass("Test", "DoubleRef",
                new List<FoxgloveSourceEmitter.TopicMember> { Change("_val", "System.Double", "/test/d") });
            Check(doubleOutput.Contains("global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.DoubleChanged", StringComparison.Ordinal),
                "142-20: double member emits FoxRunChangeHelper.DoubleChanged");

            var vec3Output = FoxgloveSourceEmitter.EmitClass("Test", "Vec3Ref",
                new List<FoxgloveSourceEmitter.TopicMember> { Change("_pos", "UnityEngine.Vector3", "/test/v3") });
            Check(vec3Output.Contains("global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal),
                "142-21: Vector3 member emits FoxRunChangeHelper.FloatChanged");

            var vec2Output = FoxgloveSourceEmitter.EmitClass("Test", "Vec2Ref",
                new List<FoxgloveSourceEmitter.TopicMember> { Change("_pos", "UnityEngine.Vector2", "/test/v2") });
            Check(vec2Output.Contains("global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal),
                "142-22: Vector2 member emits FoxRunChangeHelper.FloatChanged");

            var quatOutput = FoxgloveSourceEmitter.EmitClass("Test", "QuatRef",
                new List<FoxgloveSourceEmitter.TopicMember> { Change("_rot", "UnityEngine.Quaternion", "/test/q") });
            Check(quatOutput.Contains("global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal),
                "142-23: Quaternion member emits FoxRunChangeHelper.FloatChanged");

            var colorOutput = FoxgloveSourceEmitter.EmitClass("Test", "ColorRef",
                new List<FoxgloveSourceEmitter.TopicMember> { Change("_col", "UnityEngine.Color", "/test/c") });
            Check(colorOutput.Contains("global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged", StringComparison.Ordinal),
                "142-24: Color member emits FoxRunChangeHelper.FloatChanged");
        }

        private static void VerifyDefaultFallbackPreserved()
        {
            var changeExprDefault = FoxgloveSourceEmitter.ChangeExpr("_val", "SomeCustomType", "__last", 0f);
            Check(changeExprDefault.Contains("EqualityComparer<SomeCustomType>", StringComparison.Ordinal),
                "142-25: default ChangeExpr fallback uses EqualityComparer");
        }

        private static void VerifyFOXRUN006NativeContainerMessage()
        {
            // CanonicalType must be non-empty and NOT a known canonical type to trigger FOXRUN006.
            const string nonCanonical = "unity.container.native";

            // Fully-qualified NativeArray
            var d1 = GetFirstFOXRUN006(MakeMember("_data", "Unity.Collections.NativeArray<float>", nonCanonical, false));
            Check(d1 != null, "142-26: NativeArray<float> produces FOXRUN006");
            if (d1 != null)
                Check(d1.Message.Contains("Unity native container", StringComparison.Ordinal),
                    "142-27: FOXRUN006 native-container message uses 'Unity native container' text");

            // Unqualified NativeList
            var d2 = GetFirstFOXRUN006(MakeMember("_list", "NativeList<int>", nonCanonical, false));
            Check(d2 != null, "142-28: NativeList<int> produces FOXRUN006");
            if (d2 != null)
                Check(d2.Message.Contains("Unity native container", StringComparison.Ordinal),
                    "142-29: unqualified NativeList produces native-container FOXRUN006");

            // Unqualified NativeHashMap
            var d3 = GetFirstFOXRUN006(MakeMember("_map", "NativeHashMap<int,float>", nonCanonical, false));
            Check(d3 != null, "142-30: NativeHashMap produces FOXRUN006");
            if (d3 != null)
                Check(d3.Message.Contains("Unity native container", StringComparison.Ordinal),
                    "142-31: NativeHashMap produces native-container FOXRUN006");
        }

        private static FoxRunGenerationDiagnostic GetFirstFOXRUN006(FoxRunGenerationMember member)
        {
            return FoxRunGenerationModelValidator.Validate(MakeModel(member))
                .FirstOrDefault(d => d.Id == "FOXRUN006");
        }

        private static void VerifyFOXRUN006GenericMessageUnchanged()
        {
            var d = GetFirstFOXRUN006(MakeMember("_x", "SomeRandom.UnsupportedType", "unknown", false));
            Check(d != null, "142-32: unsupported type produces FOXRUN006");
            if (d != null)
            {
                Check(d.Message.Contains("is not a canonical built-in contract type", StringComparison.Ordinal),
                    "142-33: generic unsupported type keeps original FOXRUN006 message");
                Check(!d.Message.Contains("Unity native container", StringComparison.Ordinal),
                    "142-34: generic unsupported type does NOT use native-container message");
            }
        }

        private static void VerifyFOXRUN006IdSeverityUnchanged()
        {
            var d1 = GetFirstFOXRUN006(MakeMember("_n", "NativeArray<int>", "unity.container.native", false));
            Check(d1 != null && d1.Severity == "Error", "142-35: FOXRUN006 severity is Error for native container");

            var d2 = GetFirstFOXRUN006(MakeMember("_x", "Unknown", "unknown", false));
            Check(d2 != null && d2.Severity == "Error", "142-36: FOXRUN006 severity is Error for generic unsupported");
        }

        private static void VerifyConditionDiagnosticIdsReservedBy141A()
        {
            var generatorSource = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();
            Check(generatorSource.Contains("\"FOXRUN015\"", StringComparison.Ordinal),
                "142-37: FOXRUN015 condition diagnostic descriptor is present");
            Check(generatorSource.Contains("\"FOXRUN016\"", StringComparison.Ordinal),
                "142-38: FOXRUN016 condition diagnostic descriptor is present");
        }

        private static void VerifyInventoryBackwardCompatible()
        {
            var generatorSource = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();

            var expected = new Dictionary<string, (string title, string severity)>
            {
                { "FOXRUN001", ("Class not partial", "Error") },
                { "FOXRUN002", ("Topic schema conflict", "Warning") },
                { "FOXRUN003", ("Field name collision", "Warning") },
                { "FOXRUN004", ("Multi-variable field declaration", "Error") },
                { "FOXRUN005", ("Mixed same-topic Policy policy", "Warning") },
                { "FOXRUN006", ("Unsupported FoxRun type", "Error") },
                { "FOXRUN007", ("Generic FoxRun type", "Warning") },
                { "FOXRUN008", ("FoxRun topic must be absolute", "Error") },
                { "FOXRUN009", ("FoxRun scheduled publishing disabled", "Warning") },
                { "FOXRUN010", ("Binary FoxRun values unsupported", "Warning") },
                { "FOXRUN011", ("FoxRun declaring class name required", "Error") },
                { "FOXRUN012", ("FoxRun member name required", "Error") },
                { "FOXRUN013", ("FoxRun policy out of range", "Error") },
                { "FOXRUN014", ("FoxRun member kind invalid", "Error") },
                { "FOXRUN015", ("FoxRun condition member missing", "Error") },
                { "FOXRUN016", ("FoxRun condition member must be bool", "Error") },
                { "FOXRUN017", ("Mixed same-topic conditional gates", "Error") },
            };

            foreach (var kv in expected)
            {
                var id = kv.Key;
                var (title, severity) = kv.Value;
                Check(generatorSource.Contains("\"" + id + "\"", StringComparison.Ordinal),
                    "142-39: " + id + " descriptor ID still present");
                Check(generatorSource.Contains("\"" + title + "\"", StringComparison.Ordinal),
                    "142-40: " + id + " title unchanged");
                Check(generatorSource.Contains("DiagnosticSeverity." + severity, StringComparison.Ordinal),
                    "142-41: " + id + " severity category (" + severity + ") still exists in source generator");
            }
        }

        private static void VerifyChangeHelperCompiles()
        {
            var helperPath = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/FoxRunChangeHelper.cs");
            Check(File.Exists(helperPath), "142-42: FoxRunChangeHelper.cs exists");
            var content = File.ReadAllText(helperPath);
            Check(content.Contains("class FoxRunChangeHelper", StringComparison.Ordinal),
                "142-43: FoxRunChangeHelper class is present in the file");
            Check(content.Contains("FloatChanged", StringComparison.Ordinal)
                  && content.Contains("DoubleChanged", StringComparison.Ordinal),
                "142-44: both FloatChanged and DoubleChanged methods are present");
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase142 validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static FoxRunGenerationModel MakeModel(params FoxRunGenerationMember[] members)
        {
            var type = new FoxRunGenerationType("TestNs", "TestClass", members.ToList());
            return new FoxRunGenerationModel(new[] { type });
        }

        private static FoxRunGenerationMember MakeMember(
            string memberName, string rawType, string canonicalType, bool isArray)
        {
            return new FoxRunGenerationMember(
                "TestNs", "TestClass", memberName, "field",
                rawType, rawType, canonicalType,
                true, isArray, isArray ? "float" : "",
                "/test/" + memberName, 10f, "",
                (int)FoxRunPolicy.FixedRate, 0f, "Reflection", 0, "");
        }
    }
}
