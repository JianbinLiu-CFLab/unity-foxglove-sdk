// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-24 regression coverage for ROS2 For Unity adapter package contracts.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_24Validation.
    /// </summary>
    public static class Phase140_24Validation
    {
        private const string BaseSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        private const string NativePackageSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY_JAZZY_WIN64_PACKAGE";

        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-24: ROS2 For Unity Adapter Package ===");
            _passed = 0;

            NativeAsmdefRequiresRuntimePackageSymbol();
            InstallerManagesBaseAndNativeSymbolsTogether();
            ReadmeKeepsExternalImportSeparateFromNativeBridge();
            FacadeContractsDocumentUnavailableNoOps();
            ComplianceHashesAreLowercase();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 140-24: {_passed} checks passed.");
        }

        private static void NativeAsmdefRequiresRuntimePackageSymbol()
        {
            var asmdef = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");

            Check(asmdef.Contains("\"" + BaseSymbol + "\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"" + NativePackageSymbol + "\"", StringComparison.Ordinal)
                  && asmdef.IndexOf("\"defineConstraints\"", StringComparison.Ordinal)
                     < asmdef.IndexOf("\"" + NativePackageSymbol + "\"", StringComparison.Ordinal),
                "140-24A-1: Native bridge asmdef requires both the public R2FU symbol and runtime-package symbol");
            Check(asmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64\"", StringComparison.Ordinal),
                "140-24A-2: Native bridge hard reference remains explicitly package-scoped");
        }

        private static void InstallerManagesBaseAndNativeSymbolsTogether()
        {
            var installer = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");

            Check(installer.Contains("BaseCompileSymbol", StringComparison.Ordinal)
                  && installer.Contains("NativeRuntimePackageCompileSymbol", StringComparison.Ordinal)
                  && installer.Contains(NativePackageSymbol, StringComparison.Ordinal),
                "140-24B-1: define installer declares separate base and native runtime-package symbols");
            Check(installer.Contains("EnsureSymbol(parts, BaseCompileSymbol)", StringComparison.Ordinal)
                  && installer.Contains("EnsureSymbol(parts, NativeRuntimePackageCompileSymbol)", StringComparison.Ordinal)
                  && installer.Contains("RemoveSymbol(parts, NativeRuntimePackageCompileSymbol)", StringComparison.Ordinal),
                "140-24B-2: define installer adds and removes both managed symbols as one runtime-package contract");
        }

        private static void ReadmeKeepsExternalImportSeparateFromNativeBridge()
        {
            var readme = Read("Packages/dev.unity2foxglove.ros2forunity/README.md");

            Check(!readme.Contains("set the symbol manually for external imports", StringComparison.Ordinal)
                  && !readme.Contains("For an external, non-package ROS2 For Unity import, add that symbol manually.", StringComparison.Ordinal),
                "140-24C-1: package README no longer tells external-import users to activate the Native bridge asmdef");
            Check(readme.Contains(NativePackageSymbol, StringComparison.Ordinal)
                  && readme.Contains("managed by the runtime-package detector", StringComparison.Ordinal)
                  && readme.Contains("external source-only adapter samples", StringComparison.Ordinal),
                "140-24C-2: README documents runtime-package Native bridge symbol separately from external samples");
        }

        private static void FacadeContractsDocumentUnavailableNoOps()
        {
            var contextInterface = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Context.cs");
            var unavailable = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2UnavailableContext.cs");

            Check(contextInterface.Contains("Null or whitespace node names must be normalized", StringComparison.Ordinal),
                "140-24D-1: ROS2 context interface documents null/blank node name normalization");
            Check(unavailable.Contains("Unavailable subscriptions preserve the topic but intentionally do not invoke callbacks", StringComparison.Ordinal),
                "140-24D-2: unavailable subscription no-op behavior is documented at the implementation boundary");
        }

        private static void ComplianceHashesAreLowercase()
        {
            var manifest = Read("Packages/dev.unity2foxglove.ros2forunity/Compliance/ros2-for-unity-adoption-manifest.json");
            var matches = Regex.Matches(manifest, "\"(?:artifactSha256|releaseAssetSha256)\"\\s*:\\s*\"([0-9A-Fa-f]{64})\"");
            Check(matches.Count >= 2, "140-24E-1: compliance manifest exposes expected SHA-256 fields");

            foreach (Match match in matches)
            {
                var value = match.Groups[1].Value;
                Check(value == value.ToLowerInvariant(),
                    "140-24E-2: compliance SHA-256 value is lowercase: " + value);
            }
        }

        private static void PhaseWiringIsPresent()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase140_24Validation.cs", StringComparison.Ordinal),
                "140-24F-1: test project compiles Phase140_24Validation");
            Check(registry.Contains("Ci(\"--phase140-24\", \"Phase 140-24\", Phase140_24Validation.Validate", StringComparison.Ordinal),
                "140-24F-2: validation registry exposes --phase140-24");
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
