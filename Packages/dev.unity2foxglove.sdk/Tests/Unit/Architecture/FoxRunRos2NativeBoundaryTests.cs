// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Architecture
// Purpose: Pins the public generated/native registration seam and ROS-free emitter boundary.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "179-B")]
    [Trait("Domain", "Architecture")]
    public sealed class FoxRunRos2NativeBoundaryTests
    {
        [Fact]
        public void OptionalNativeGeneratedCodeSeamIsPublicExactAndCompileTimeClosed()
        {
            var root = FindRepoRoot();
            var nativeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun");
            var sourceContract = File.ReadAllText(Path.Combine(nativeRoot, "IFoxRunRos2SubscriptionSource.cs"));
            var registrar = File.ReadAllText(Path.Combine(nativeRoot, "IFoxRunRos2SubscriptionRegistrar.cs"));
            var contract = File.ReadAllText(Path.Combine(nativeRoot, "FoxRunRos2GeneratedContract.cs"));

            Assert.Contains("public interface IFoxRunRos2SubscriptionSource", sourceContract, StringComparison.Ordinal);
            Assert.Contains("public interface IFoxRunRos2SubscriptionRegistrar", registrar, StringComparison.Ordinal);
            Assert.Contains("where T : ROS2.Message, new()", registrar, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CopyContext", registrar, StringComparison.Ordinal);
            Assert.Contains("public sealed class FoxRunRos2GeneratedContract", contract, StringComparison.Ordinal);
            Assert.Contains("FoxRunMode mode", contract, StringComparison.Ordinal);
            Assert.Contains("FoxRunSubscriptionProvider subscriptionProvider", contract, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2QosPreset qosPreset", contract, StringComparison.Ordinal);
            Assert.Contains("HasCompleteMetadata", contract, StringComparison.Ordinal);
            Assert.DoesNotContain("reflection", registrar, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dynamic", registrar, StringComparison.Ordinal);
        }

        [Fact]
        public void NativeInboundHostDoesNotRequireWebSocketInputMetadata()
        {
            var root = FindRepoRoot();
            var host = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                "FoxRunRos2SubscriptionHub.cs"));

            Assert.Contains("IFoxRunRos2SubscriptionSource", host, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxgloveInputSource", host, StringComparison.Ordinal);
            Assert.DoesNotContain("FoxgloveInputTopicInfo", host, StringComparison.Ordinal);
        }

        [Fact]
        public void SharedEmitterRemainsRosAssemblyFreeAndHasNoRuntimeGenericConstruction()
        {
            var root = FindRepoRoot();
            var emitter = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Shared",
                "FoxgloveSourceEmitter",
                "Ros2InputDispatchEmitter.cs"));

            Assert.DoesNotContain("using ROS2", emitter, StringComparison.Ordinal);
            Assert.DoesNotContain("MakeGenericMethod", emitter, StringComparison.Ordinal);
            Assert.DoesNotContain("Activator", emitter, StringComparison.Ordinal);
            Assert.DoesNotContain("dynamic", emitter, StringComparison.Ordinal);
            Assert.Contains("registrar.Register<", emitter, StringComparison.Ordinal);
        }

        [Fact]
        public void CustomTypesupportCatalogHasOnlyTheDocumentedOptionalFacadeSeam()
        {
            var root = FindRepoRoot();
            var nativeRoot = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun");
            var contract = File.ReadAllText(Path.Combine(nativeRoot, "IFoxRunRos2CustomTypesupportCatalog.cs"));
            var readiness = File.ReadAllText(Path.Combine(nativeRoot, "FoxRunRos2CustomTypesupportReadiness.cs"));
            var registry = File.ReadAllText(Path.Combine(nativeRoot, "FoxRunRos2CustomTypesupportCatalogRegistry.cs"));
            var candidateBuilder = File.ReadAllText(Path.Combine(
                root,
                "Scripts",
                "ros2forunity",
                "interfaces",
                "build_foxrun_custom_typesupport_addon.py"));

            Assert.Contains("public interface IFoxRunRos2CustomTypesupportCatalog", contract, StringComparison.Ordinal);
            Assert.Contains("public static void Register(IFoxRunRos2CustomTypesupportCatalog catalog)", registry, StringComparison.Ordinal);
            Assert.Contains("internal enum FoxRunRos2CustomTypesupportReadinessCode", readiness, StringComparison.Ordinal);
            Assert.DoesNotContain("using ROS2", contract, StringComparison.Ordinal);
            Assert.DoesNotContain("ROS2.", registry, StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2cs.Init", registry, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateNode", registry, StringComparison.Ordinal);
            Assert.DoesNotContain("LoadLibrary", registry, StringComparison.Ordinal);
            Assert.DoesNotContain("AppDomain", registry, StringComparison.Ordinal);
            Assert.Contains("FoxRunRos2CustomTypesupportCatalogRegistry.Register", candidateBuilder, StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2cs.Init", candidateBuilder, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateNode", candidateBuilder, StringComparison.Ordinal);

            var coreRuntimeAsmdef = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Unity.FoxgloveSDK.asmdef"));
            Assert.DoesNotContain("CustomTypesupport", coreRuntimeAsmdef, StringComparison.Ordinal);
            Assert.DoesNotContain("ROS2", coreRuntimeAsmdef, StringComparison.Ordinal);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
