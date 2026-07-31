// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 156 validation for the optional FoxRun R2FU Provider boundary.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase156Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 156 Tests ---");
            _passCount = 0;

            VerifyOptionalPackagePresent();
            VerifyPackageDependencyDirection();
            VerifyProviderRequiresRealManagerAttachment();
            VerifyProviderUsesTypedBindingsAndFailsClosed();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 156: " + _passCount + " checks passed.\n");
        }

        private static void VerifyOptionalPackagePresent()
        {
            Check(Directory.Exists(RepoPath("Packages/dev.unity2foxglove.ros2forunity")),
                "156-0: optional R2FU adapter package is present for Phase156 validation");
        }

        private static void VerifyPackageDependencyDirection()
        {
            var coreAsmdef = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef");
            var optionalAsmdef = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2Foxglove.Ros2ForUnity.asmdef");

            Check(!coreAsmdef.Contains("Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal),
                "156-1: core SDK asmdef remains free of optional R2FU package references");

            Check(optionalAsmdef.Contains("\"Unity.FoxgloveSDK\"", StringComparison.Ordinal),
                "156-2: optional R2FU package may depend on the core SDK Provider interface");
        }

        private static void VerifyProviderRequiresRealManagerAttachment()
        {
            var provider = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs");

            Check(provider.Contains("GetComponent<FoxgloveManager>()", StringComparison.Ordinal)
                  && provider.Contains("The R2FU Provider must share a GameObject with one FoxgloveManager.", StringComparison.Ordinal)
                  && !provider.Contains("Unity2FoxgloveRos2ContextFactory.Create()", StringComparison.Ordinal),
                "156-3: the R2FU Provider requires a real same-GameObject Manager instead of the unavailable facade factory");

            Check(provider.Contains("_manager.RegisterFoxRunTransportProvider(this)", StringComparison.Ordinal)
                  && provider.Contains("manager.UnregisterFoxRunTransportProvider(this)", StringComparison.Ordinal)
                  && provider.Contains("private void OnDisable() => Detach();", StringComparison.Ordinal),
                "156-4: the optional Provider attaches and detaches through the neutral Manager registry");
        }

        private static void VerifyProviderUsesTypedBindingsAndFailsClosed()
        {
            var provider = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs");
            var binding = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherBinding.cs");

            Check(provider.Contains("IFoxRunTransportProvider", StringComparison.Ordinal)
                  && provider.Contains("FoxRunRos2CustomPublisherHub", StringComparison.Ordinal)
                  && binding.Contains("IFoxRunRos2NativePublisherBackend", StringComparison.Ordinal)
                  && binding.Contains("_backend.TryPublish(token, mapped)", StringComparison.Ordinal)
                  && !binding.Contains("byte[] payload", StringComparison.Ordinal),
                "156-5: the R2FU Provider publishes through generated typed bindings instead of serialized core-SDK bytes");

            Check(provider.Contains("R2FU routes are emitted as generated typed ROS2 bindings, not untyped byte payloads.", StringComparison.Ordinal)
                  && provider.Contains("R2FU subscriptions are emitted as generated typed ROS2 bindings.", StringComparison.Ordinal)
                  && provider.Contains("FoxRunTransportPublishResult.Rejected", StringComparison.Ordinal)
                  && provider.Contains("FoxRunTransportSubscribeResult.Rejected", StringComparison.Ordinal),
                "156-6: the neutral Provider session fails closed instead of accepting untyped publish or subscribe routes");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase156"),
                "156-7: validation registry exposes the R2FU Provider boundary flag");
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
