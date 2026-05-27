// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-32 regression coverage for optional R2FU adapter test harness isolation.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_32Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-32: Runtime Test Harness ===");
            _passed = 0;

            VerifyOptionalAdapterCompileGate();
            VerifyAdapterFacadeValidationsUseReflection();
            VerifyValidationWiring();
            VerifyProgramRegistryDispatchIsAuthoritative();
            VerifyRuntimeTestHarnessCleanup();
            VerifyDescriptorReaderParity();
            VerifyRegistryAndValidationStateBounds();
            VerifyProtoSampleFactoryContracts();
            VerifyStandaloneMcapInspectorOwnership();

            Console.WriteLine($"Phase 134-32: {_passed} checks passed.");
        }

        private static void VerifyOptionalAdapterCompileGate()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(project.Contains("<IncludeRos2ForUnityAdapter Condition=\"'$(IncludeRos2ForUnityAdapter)' == ''\">false</IncludeRos2ForUnityAdapter>", StringComparison.Ordinal),
                "134-32A-1: test project defaults optional R2FU adapter compilation to false");
            Check(project.Contains("../../../dev.unity2foxglove.ros2forunity/Runtime/**/*.cs", StringComparison.Ordinal)
                  && project.Contains("Condition=\"'$(IncludeRos2ForUnityAdapter)' == 'true' and Exists('../../../dev.unity2foxglove.ros2forunity/Runtime')\"", StringComparison.Ordinal),
                "134-32A-2: optional R2FU adapter sources compile only behind explicit property and path check");
            Check(!project.Contains("<Compile Include=\"../../../dev.unity2foxglove.ros2forunity/Runtime/**/*.cs\" LinkBase=\"Ros2ForUnityRuntime\" />", StringComparison.Ordinal),
                "134-32A-3: test project no longer unconditionally compiles optional R2FU adapter sources");
        }

        private static void VerifyAdapterFacadeValidationsUseReflection()
        {
            var phase108 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase108Validation.cs");
            var phase13421 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_21Validation.cs");

            Check(!phase108.Contains("using Unity2Foxglove.Ros2ForUnity;", StringComparison.Ordinal)
                  && !phase13421.Contains("using Unity2Foxglove.Ros2ForUnity;", StringComparison.Ordinal),
                "134-32B-1: facade behavior validations do not directly import optional R2FU namespace");
            Check(phase108.Contains("FindType(\"Unity2Foxglove.Ros2ForUnity.Unity2FoxgloveRos2ContextFactory\")", StringComparison.Ordinal)
                  && phase13421.Contains("FactoryTypeName = \"Unity2Foxglove.Ros2ForUnity.Unity2FoxgloveRos2ContextFactory\"", StringComparison.Ordinal),
                "134-32B-2: facade behavior validations discover optional runtime types by reflection");
            Check(!phase108.Contains("Unity2FoxgloveRos2ContextFactory.Create()", StringComparison.Ordinal)
                  && !phase13421.Contains("Unity2FoxgloveRos2ContextFactory.Create()", StringComparison.Ordinal),
                "134-32B-3: facade behavior validations avoid compile-time factory calls");
            Check(phase108.Contains("skipped unless IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal)
                  && phase13421.Contains("skipped unless IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal),
                "134-32B-4: facade behavior validations document adapter-enabled runtime gate");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("<Compile Include=\"Phase134_32Validation.cs\" />", StringComparison.Ordinal),
                "134-32C-1: Phase134_32Validation is compiled by the runtime test project");
            Check(registry.Contains("Ci(\"--phase134-32\", \"Phase 134-32\", Phase134_32Validation.Validate)", StringComparison.Ordinal),
                "134-32C-2: Phase134_32Validation is wired into the validation registry");
        }

        private static void VerifyProgramRegistryDispatchIsAuthoritative()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");

            Check(!program.Contains("RunPhase50Only", StringComparison.Ordinal)
                  && !program.Contains("RunPhase136Only", StringComparison.Ordinal)
                  && !program.Contains("RunPhase82NativeSmoke", StringComparison.Ordinal),
                "134-32D-1: legacy registered-validation wrapper methods are removed from Program");
            Check(!program.Contains("argList.Contains(\"--phase50\")", StringComparison.Ordinal)
                  && !program.Contains("argList.Contains(\"--phase136\")", StringComparison.Ordinal)
                  && !program.Contains("argList.Contains(\"--phase13\")", StringComparison.Ordinal),
                "134-32D-2: Program no longer has duplicate registered flag branches");
            Check(program.Contains("--phase98-sample-send-all", StringComparison.Ordinal)
                  && program.Contains("--phase97-health", StringComparison.Ordinal)
                  && program.Contains("--phase44-all-schemas-mcap", StringComparison.Ordinal),
                "134-32D-3: Program still keeps non-registry operational subcommands");
            Check(program.Contains("Console.Error.WriteLine($\"\\n[FAIL] {validation.Name}: {ex.Message}\")", StringComparison.Ordinal),
                "134-32D-4: registry validation failures are written to stderr");
        }

        private static void VerifyRuntimeTestHarnessCleanup()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");

            Check(program.Contains("finally", StringComparison.Ordinal)
                  && program.Contains("heartbeat?.Dispose();", StringComparison.Ordinal)
                  && program.Contains("sceneTimer?.Dispose();", StringComparison.Ordinal)
                  && program.Contains("runtime.Dispose();", StringComparison.Ordinal),
                "134-32E-1: manual server mode disposes timers and runtime in a finally block");
        }

        private static void VerifyDescriptorReaderParity()
        {
            var reader = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunGenerationDescriptorJsonReader.cs");

            Check(reader.Contains("isValueType: BoolValue(member, \"isValueType\")", StringComparison.Ordinal),
                "134-32F-1: descriptor JSON reader preserves isValueType");
            Check(reader.Contains("FoxRunPublishMode.OnChange", StringComparison.Ordinal)
                  && !reader.Contains("case \"OnChange\": return 1;", StringComparison.Ordinal),
                "134-32F-2: descriptor JSON reader maps publish modes through the runtime enum");
        }

        private static void VerifyRegistryAndValidationStateBounds()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var category = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationCategory.cs");
            var skeleton = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/SkeletonValidation.cs");

            Check(registry.Contains("Duplicate validation flag registered", StringComparison.Ordinal)
                  && registry.Contains("StringComparer.Ordinal", StringComparison.Ordinal),
                "134-32G-1: validation registry rejects duplicate flags deterministically");
            Check(!category.Contains("OptionalTooling", StringComparison.Ordinal),
                "134-32G-2: unused OptionalTooling validation category is removed");
            Check(skeleton.Contains("internal static class SkeletonValidation", StringComparison.Ordinal)
                  && skeleton.Contains("_passCount = 0;", StringComparison.Ordinal),
                "134-32G-3: skeleton validation is internal and resets pass state per run");
        }

        private static void VerifyProtoSampleFactoryContracts()
        {
            var factory = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveProtoSampleFactory.cs");

            Check(factory.Contains("Map payloads are optional", StringComparison.Ordinal)
                  && factory.Contains("if (field.IsMap)", StringComparison.Ordinal),
                "134-32H-1: protobuf sample factory documents intentional map-field skips");
            Check(factory.Contains("Enum.GetUnderlyingType(propertyType) != typeof(int)", StringComparison.Ordinal)
                  && factory.Contains("non-int32 enum backing type", StringComparison.Ordinal),
                "134-32H-2: protobuf sample factory guards enum int32 assumptions");
        }

        private static void VerifyStandaloneMcapInspectorOwnership()
        {
            var oldPath = Path.Combine(RepoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/McapInspector.cs".Replace('/', Path.DirectorySeparatorChar));
            var inspector = ReadRepoText("Scripts/mcap/McapInspector.cs");

            Check(!File.Exists(oldPath),
                "134-32I-1: standalone McapInspector no longer lives in runtime test source folder");
            Check(inspector.Contains("Standalone diagnostic tool", StringComparison.Ordinal)
                  && inspector.Contains("intentionally not compiled into the runtime test harness", StringComparison.Ordinal),
                "134-32I-2: standalone McapInspector ownership is documented");
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
