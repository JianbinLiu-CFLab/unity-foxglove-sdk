// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137F runtime orchestration decoupling guard.

using System;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

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
            VerifyStartNreSafety();
            VerifyOrchestratorAttachRollback();

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

            Check(!factory.Contains("ReplayOrchestrator") && !factory.Contains("TickCoordinator"),
                "137F-14: SessionFactory has no circular dependency");
            Check(!orchestrator.Contains("TickCoordinator") && !orchestrator.Contains("SessionFactory"),
                "137F-15: ReplayOrchestrator has no circular dependency");
            Check(!coordinator.Contains("ReplayOrchestrator") && !coordinator.Contains("SessionFactory"),
                "137F-16: TickCoordinator has no circular dependency");
        }

        /// <summary>
        /// BUG 1 regression: when transport throws during Start(),
        /// the catch block must not NRE on session?.Dispose().
        /// </summary>
        private static void VerifyStartNreSafety()
        {
            var transport = new ThrowOnStartTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            Exception caught = null;
            try
            {
                runtime.Start("phase137f-nre", "127.0.0.1", 0);
            }
            catch (Exception ex)
            {
                caught = ex;
            }
            Check(caught != null, "137F-17: failed Start() throws exception");
            Check(caught.Message.Contains("ThrowOnStart"),
                "137F-18: failed Start() preserves original exception");
            Check(transport.WasStarted, "137F-19: transport.Start was reached before throw");
            runtime.Dispose();
        }

        /// <summary>
        /// BUG 2 regression: Attach writes fields before subscribing and
        /// rolls back on failure (Detach called inside catch).
        /// </summary>
        private static void VerifyOrchestratorAttachRollback()
        {
            var orchestratorPath =
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayOrchestrator.cs";
            var content = File.ReadAllText(orchestratorPath);

            // Verify field writes happen before event subscriptions
            var forwardsIndex = content.IndexOf("_replayForwarder = replayForwarder;", StringComparison.Ordinal);
            var subscribeIndex = content.IndexOf("replay.OnReplayMessage += replayForwarder;", StringComparison.Ordinal);
            Check(forwardsIndex >= 0 && subscribeIndex >= 0 && forwardsIndex < subscribeIndex,
                "137F-20: field writes before event subscription in Attach");

            // Verify try/catch with Detach rollback
            var catchIndex = content.IndexOf("catch", subscribeIndex, StringComparison.Ordinal);
            var detachRollback = content.IndexOf("Detach(replay);", catchIndex, StringComparison.Ordinal);
            Check(catchIndex >= 0 && detachRollback >= 0,
                "137F-21: Attach rolls back with Detach on subscription failure");

            // Verify RegisterChannels before event wiring
            var channelsIndex = content.IndexOf("RegisterChannels(session);", StringComparison.Ordinal);
            Check(channelsIndex >= 0 && channelsIndex < forwardsIndex,
                "137F-22: RegisterChannels before event wiring in Attach");
        }

        private static void Check(bool condition, string label)
        {
            if (condition) { Console.WriteLine("[PASS] " + label); _passed++; }
            else Console.WriteLine("[FAIL] " + label);
        }

        private sealed class ThrowOnStartTransport : IFoxgloveTransport
        {
            public bool WasStarted;
            public bool IsRunning { get; private set; }

            public void Start(string host, int port)
            {
                WasStarted = true;
                throw new InvalidOperationException("ThrowOnStart");
            }

            public void Stop() { }
            public void Dispose() { }

            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }
            public void SendText(uint clientId, string text) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void BroadcastText(string text) { }
            public void BroadcastBinary(byte[] data) { }
        }
    }
}
