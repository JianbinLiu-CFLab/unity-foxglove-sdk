// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-2 review regression checks for FoxgloveManager lifecycle contracts.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_2Validation
    {
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-2: Runtime Manager Lifecycle Review ===");

            var root = Phase16Validation.FindRepoRoot();
            var manager = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var setup = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var diagnostics = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs");
            var publisher = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var runtime = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var registry = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(manager.Contains("[DisallowMultipleComponent]", StringComparison.Ordinal)
                  && manager.Contains("public partial class FoxgloveManager", StringComparison.Ordinal),
                "163-2A: FoxgloveManager prevents duplicate components on one GameObject");
            Check(manager.Contains("private System.Collections.Generic.List<string> _allowedBrowserOrigins = new();", StringComparison.Ordinal),
                "163-2B: hosted Foxglove Web origin is controlled only by the hosted-web toggle");
            Check(Slice(manager, "private void OnDisable()", "private void OnDestroy()")
                    .Contains("StopServer(restoreLivePublishers: true)", StringComparison.Ordinal),
                "163-2C: OnDisable restores replay-disabled live publishers");
            Check(Slice(manager, "private void OnDestroy()", "private void OnApplicationQuit()")
                    .Contains("StopServer(restoreLivePublishers: true)", StringComparison.Ordinal),
                "163-2D: OnDestroy also leaves publisher state clean");
            Check(server.Contains("_runtime.Stop();", StringComparison.Ordinal)
                  && server.Contains("_sharedSensorClock.Reset();", StringComparison.Ordinal)
                  && server.IndexOf("_runtime.Stop();", StringComparison.Ordinal) < server.IndexOf("_sharedSensorClock.Reset();", StringComparison.Ordinal),
                "163-2E: StopServer resets shared sensor clock after runtime stop");
            Check(setup.Contains("FindObjectsByType<FoxgloveManager>", StringComparison.Ordinal)
                  && setup.Contains("ShouldDisableLivePublisherForReplay", StringComparison.Ordinal)
                  && setup.Contains("publisher.ConfiguredManager", StringComparison.Ordinal)
                  && setup.Contains("publisher.gameObject.scene.handle == gameObject.scene.handle", StringComparison.Ordinal),
                "163-2F: replay live-publisher suppression is scoped to this manager");
            Check(publisher.Contains("ResolveManagerFromCandidates", StringComparison.Ordinal)
                  && publisher.Contains("Multiple FoxgloveManager instances found; assign Manager explicitly.", StringComparison.Ordinal)
                  && publisher.Contains("sameSceneCount == 1", StringComparison.Ordinal),
                "163-2G: publisher auto-resolution avoids arbitrary multi-manager binding");
            Check(runtime.Contains("private bool _stopped = true;", StringComparison.Ordinal)
                  && runtime.Contains("if (_stopped && _session == null)", StringComparison.Ordinal)
                  && runtime.Contains("_stopped = false;", StringComparison.Ordinal),
                "163-2H: FoxgloveRuntime.Stop is explicitly idempotent across lifecycle callbacks");
            Check(diagnostics.Contains("temporarily", StringComparison.Ordinal)
                  && diagnostics.Contains("Unity's global Log stack-trace mode", StringComparison.Ordinal),
                "163-2I: stack-trace suppression documents its global Unity logging window");
            Check(registry.Contains("Ci(\"--phase163-2\", \"Phase 163-2\", Phase163_2Validation.Validate", StringComparison.Ordinal),
                "163-2J: PhaseValidationRegistry wires --phase163-2");

            Console.WriteLine("Phase 163-2: 10 checks passed.");
            Console.WriteLine();
        }

        private static string Read(string root, string relativePath)
            => File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string Slice(string text, string startMarker, string endMarker)
        {
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
            return end < 0 ? text.Substring(start) : text.Substring(start, end - start);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("[FAIL] " + message);
            }

            Console.WriteLine("[PASS] " + message);
        }
    }
}
