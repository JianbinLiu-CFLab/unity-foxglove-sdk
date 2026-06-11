// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-82 source-shape regression coverage for Foxglove extension hot-path optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_82Validation.
    /// </summary>
    public static class Phase140_82Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-82: Foxglove Extension and Local Tools Optimization ===");
            _passed = 0;

            VerifyPanelDomIsBuiltOutsideRenderLoop();
            VerifyRenderLoopUsesHoistedRateConstant();
            VerifyCursorDedupUsesNumericFields();
            VerifyReplayTimeFormattingIsCached();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-82: {_passed} checks passed.");
        }

        private static void VerifyPanelDomIsBuiltOutsideRenderLoop()
        {
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var buildPanelDom = Slice(source, "function buildPanelDom", "export function initPanel");
            var renderLoop = Slice(source, "context.onRender = (renderState, done) =>", "  return () =>");

            Check(buildPanelDom.Contains("root.innerHTML", StringComparison.Ordinal)
                  && buildPanelDom.Contains("querySelector", StringComparison.Ordinal)
                  && buildPanelDom.Contains("endpointInput.value = state.endpoint", StringComparison.Ordinal)
                  && renderLoop.Contains("panel.replayTime.textContent", StringComparison.Ordinal)
                  && renderLoop.Contains("panel.unityStatus.textContent", StringComparison.Ordinal)
                  && !renderLoop.Contains("innerHTML", StringComparison.Ordinal)
                  && !renderLoop.Contains("querySelector", StringComparison.Ordinal)
                  && !renderLoop.Contains("addEventListener", StringComparison.Ordinal)
                  && !renderLoop.Contains("replaceChildren", StringComparison.Ordinal)
                  && !renderLoop.Contains("escapeHtml", StringComparison.Ordinal),
                "140-82A-1: panel DOM template and listeners stay outside the render hot path");
        }

        private static void VerifyRenderLoopUsesHoistedRateConstant()
        {
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var renderLoop = Slice(source, "context.onRender = (renderState, done) =>", "  return () =>");

            Check(source.Contains("const MIN_INTERVAL_MS = 1000 / DEFAULT_MAX_HZ;", StringComparison.Ordinal)
                  && renderLoop.Contains("MIN_INTERVAL_MS", StringComparison.Ordinal)
                  && !renderLoop.Contains("1000 / DEFAULT_MAX_HZ", StringComparison.Ordinal),
                "140-82B-1: cursor send cadence uses a hoisted interval constant");
        }

        private static void VerifyCursorDedupUsesNumericFields()
        {
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var renderLoop = Slice(source, "context.onRender = (renderState, done) =>", "  return () =>");
            var shouldSend = Slice(source, "export function shouldSendCursor", "function buildPanelDom");

            Check(source.Contains("lastCursorSec", StringComparison.Ordinal)
                  && source.Contains("lastCursorNsec", StringComparison.Ordinal)
                  && shouldSend.Contains("lastSec: number", StringComparison.Ordinal)
                  && shouldSend.Contains("lastNsec: number", StringComparison.Ordinal)
                  && shouldSend.Contains("currentTime.sec !== lastSec || currentTime.nsec !== lastNsec", StringComparison.Ordinal)
                  && !renderLoop.Contains("cursorKey", StringComparison.Ordinal),
                "140-82C-1: cursor deduplication compares numeric time fields instead of allocating keys");
        }

        private static void VerifyReplayTimeFormattingIsCached()
        {
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var formatter = Slice(source, "function formatReplayTimeUtc", "async function sendCursor");
            var renderLoop = Slice(source, "context.onRender = (renderState, done) =>", "  return () =>");

            Check(source.Contains("type ReplayTimeDisplayCache", StringComparison.Ordinal)
                  && source.Contains("const replayTimeCache", StringComparison.Ordinal)
                  && formatter.Contains("cache.lastSec", StringComparison.Ordinal)
                  && formatter.Contains("cache.text", StringComparison.Ordinal)
                  && formatter.Contains("iso.slice(0, 10)", StringComparison.Ordinal)
                  && formatter.Contains("iso.slice(11, iso.length - 1)", StringComparison.Ordinal)
                  && !formatter.Contains(".replace(\"T\"", StringComparison.Ordinal)
                  && !formatter.Contains(".replace(\"Z\"", StringComparison.Ordinal)
                  && renderLoop.Contains("formatReplayTimeUtc(currentTime, replayTimeCache)", StringComparison.Ordinal),
                "140-82D-1: replay time display caches repeated values and avoids replace-chain formatting");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_82Validation.cs", StringComparison.Ordinal),
                "140-82E-1: test project compiles Phase140_82Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-82\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_82Validation.Validate", StringComparison.Ordinal),
                "140-82E-2: validation registry exposes --phase140-82");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
