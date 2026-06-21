// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 156 validation for the optional FoxRun ROS2 R2FU sink boundary.

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

            VerifyPackageDependencyDirection();
            VerifyBootstrapRequiresRealContextProvider();
            VerifySinkIsOutboundOnlyAndFailClosed();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 156: " + _passCount + " checks passed.\n");
        }

        private static void VerifyPackageDependencyDirection()
        {
            var coreAsmdef = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef");
            var optionalAsmdef = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2Foxglove.Ros2ForUnity.asmdef");

            Check(!coreAsmdef.Contains("Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal),
                "156-1: core SDK asmdef remains free of optional R2FU package references");

            Check(optionalAsmdef.Contains("\"Unity.FoxgloveSDK\"", StringComparison.Ordinal),
                "156-2: optional R2FU package may depend on the core SDK sink interface");
        }

        private static void VerifyBootstrapRequiresRealContextProvider()
        {
            var bootstrap = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Ros2TopicSinkBootstrap.cs");

            Check(bootstrap.Contains("Func<IUnity2FoxgloveRos2Context> createContext", StringComparison.Ordinal)
                  && bootstrap.Contains("_createContext = createContext ?? throw new ArgumentNullException", StringComparison.Ordinal)
                  && !bootstrap.Contains("Unity2FoxgloveRos2ContextFactory.Create()", StringComparison.Ordinal),
                "156-3: ROS2 sink bootstrap requires a real context provider instead of the unavailable facade factory");

            Check(bootstrap.Contains("FoxgloveLogHub.TryGetTopicSinkRouter(out _router)", StringComparison.Ordinal)
                  && bootstrap.Contains("_router.AddSink(_sink)", StringComparison.Ordinal)
                  && bootstrap.Contains("_router.RemoveSink(_sink)", StringComparison.Ordinal),
                "156-4: optional bootstrap attaches and detaches through the core sink router");
        }

        private static void VerifySinkIsOutboundOnlyAndFailClosed()
        {
            var sink = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Ros2R2FUTopicSink.cs");

            Check(sink.Contains("public sealed class Ros2R2FUTopicSink : IFoxTopicSink", StringComparison.Ordinal)
                  && sink.Contains("IRos2TopicPublisherFactory", StringComparison.Ordinal)
                  && sink.Contains("TryPublish(payload ?? Array.Empty<byte>(), timestampNs", StringComparison.Ordinal),
                "156-5: ROS2 R2FU sink consumes serialized FoxRun bytes through explicit publisher mappings");

            Check(sink.Contains("ReportOnce(contract.Topic, \"ROS2 runtime unavailable", StringComparison.Ordinal)
                  && sink.Contains("explicit ROS2 mapping for FoxRun topic", StringComparison.Ordinal)
                  && !sink.Contains("CreateSubscription", StringComparison.Ordinal),
                "156-6: ROS2 sink is outbound-only and fails closed for unavailable runtimes or unknown mappings");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase156"),
                "156-7: validation registry exposes the ROS2 R2FU topic sink flag");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
