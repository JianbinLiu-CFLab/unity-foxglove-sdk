// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "170B")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunCodegenDecompositionTests
    {
        [Fact]
        public void FoxrunCodeGeneratorDelegatesScannerValidatorAndMemberData()
        {
            var codegen = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var memberData = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunMemberData.cs");
            var scanner = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var validator = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunServiceValidator.cs");

            Assert.Contains("public static partial class FoxrunCodeGenerator", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("public sealed class MemberData", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("private static FoxRunAndServiceScanResult ScanFoxRunMembersAndServices", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("private static List<FoxServiceSourceEmitter.ServiceMethod> ScanServiceType", codegen, StringComparison.Ordinal);
            Assert.DoesNotContain("private static void ValidateServiceMethod", codegen, StringComparison.Ordinal);

            Assert.Contains("public sealed class MemberData", memberData, StringComparison.Ordinal);
            Assert.Contains("ToManifestMember()", memberData, StringComparison.Ordinal);
            Assert.Contains("ToReflectionMember()", memberData, StringComparison.Ordinal);

            Assert.Contains("private static FoxRunAndServiceScanResult ScanFoxRunMembersAndServices", scanner, StringComparison.Ordinal);
            Assert.Contains("private static FoxRunScanResult ScanFoxRunMembers", scanner, StringComparison.Ordinal);
            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", scanner, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", scanner, StringComparison.Ordinal);

            Assert.Contains("private static List<FoxServiceSourceEmitter.ServiceMethod> ScanServiceType", validator, StringComparison.Ordinal);
            Assert.Contains("private static void ValidateServiceMethod", validator, StringComparison.Ordinal);
            Assert.Contains("FOXSERVICE001", validator, StringComparison.Ordinal);
            Assert.Contains("FOXSERVICE002", validator, StringComparison.Ordinal);
            Assert.Contains("FOXSERVICE005", validator, StringComparison.Ordinal);
        }
    }
}
