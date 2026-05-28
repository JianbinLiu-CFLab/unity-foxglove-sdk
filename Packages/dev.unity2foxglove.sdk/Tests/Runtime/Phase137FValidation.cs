// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137F runtime orchestration decoupling guard.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137FValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137F Tests ---");
            _passed = 0;

            VerifySessionFactoryExists();
            VerifyReplayOrchestratorExists();
            VerifyTickCoordinatorExists();
            VerifyFoxgloveRuntimeDelegates();
            VerifyNoCircularDependency();

            Console.WriteLine("Phase 137F: " + _passed + " checks passed.\n");
        }

        private static void VerifySessionFactoryExists()
        {
            var path = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/SessionFactory.cs";
            Check(File.Exists(path), "137F-1: SessionFactory.cs exists");
            var content = File.ReadAllText(path);
            Check(content.Contains("internal static class SessionFactory"),
                "137F-2: SessionFactory is internal static class");
            Check(content.Contains("FoxgloveSession Create("),
                "137F-3: SessionFactory.Create method exists");
        }

        private static void VerifyReplayOrchestratorExists()
        {
            var path = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayOrchestrator.cs";
            Check(File.Exists(path), "137F-4: ReplayOrchestrator.cs exists");
            var content = File.ReadAllText(path);
            Check(content.Contains("internal class ReplayOrchestrator"),
                "137F-5: ReplayOrchestrator is internal class");
            Check(content.Contains("void Attach("), "137F-6: Attach method exists");
            Check(content.Contains("void Detach("), "137F-7: Detach method exists");
        }

        private static void VerifyTickCoordinatorExists()
        {
            var path = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs";
            Check(File.Exists(path), "137F-8: TickCoordinator.cs exists");
            var content = File.ReadAllText(path);
            Check(content.Contains("internal class TickCoordinator"),
                "137F-9: TickCoordinator is internal class");
            Check(content.Contains("void Tick("), "137F-10: Tick method exists");
        }

        private static void VerifyFoxgloveRuntimeDelegates()
        {
            var path = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs";
            var content = File.ReadAllText(path);
            Check(content.Contains("_replayOrchestrator"), "137F-11: FoxgloveRuntime uses ReplayOrchestrator");
            Check(content.Contains("_tickCoordinator"), "137F-12: FoxgloveRuntime uses TickCoordinator");
            Check(content.Contains("SessionFactory.Create("), "137F-13: Start() delegates to SessionFactory");
        }

        private static void VerifyNoCircularDependency()
        {
            var factory = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/SessionFactory.cs");
            var orchestrator = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayOrchestrator.cs");
            var coordinator = File.ReadAllText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs");

            // SessionFactory depends on nothing new
            Check(!factory.Contains("ReplayOrchestrator") && !factory.Contains("TickCoordinator"),
                "137F-14: SessionFactory has no circular dependency");

            // ReplayOrchestrator depends on nothing new
            Check(!orchestrator.Contains("TickCoordinator") && !orchestrator.Contains("SessionFactory"),
                "137F-15: ReplayOrchestrator has no circular dependency");

            // TickCoordinator depends on nothing new
            Check(!coordinator.Contains("ReplayOrchestrator") && !coordinator.Contains("SessionFactory"),
                "137F-16: TickCoordinator has no circular dependency");
        }

        private static void Check(bool condition, string label)
        {
            if (condition) { Console.WriteLine("[PASS] " + label); _passed++; }
            else Console.WriteLine("[FAIL] " + label);
        }
    }
}
