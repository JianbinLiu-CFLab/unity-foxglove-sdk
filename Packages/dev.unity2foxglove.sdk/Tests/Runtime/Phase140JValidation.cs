// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140J replay enable-failure diagnostics and cursor gate validation.

using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates Phase 140J replay startup diagnostics and external cursor unavailable state.
    /// </summary>
    public static class Phase140JValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140J validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140J: Replay enable-failure diagnostics and cursor gate ===");
            _passed = 0;

            ReplayControllerRecordsRecordingConflictFailure();
            ReplayCursorStateExplainsUnavailableReasons();
            SetupReplaySurfacesNonSchemaFailure();
            ManagerWarnsOnceForReplayUnavailableCursor();
            ValidationRegistryExposesPhase140J();

            Console.WriteLine($"Phase 140J: {_passed} checks passed.");
        }

        private static void ReplayControllerRecordsRecordingConflictFailure()
        {
            var logger = new CaptureLogger();
            using var controller = new ReplayController(
                logger,
                new RecordingState(isEnabled: true, coordinateMode: string.Empty),
                clock: null);

            controller.Enable("ignored.mcap", SchemaIdentityMode.Off);

            Check(!controller.IsEnabled,
                "140J-1A: replay remains disabled when recording is enabled");
            Check(controller.LastEnableFailureMessage.Contains("Recording and Replay cannot both be enabled", StringComparison.Ordinal),
                "140J-1B: replay records the recording/replay mutual-exclusion failure reason");
            Check(logger.LastWarning.Contains(controller.LastEnableFailureMessage, StringComparison.Ordinal),
                "140J-1C: warning and recorded replay failure reason stay aligned");
        }

        private static void ReplayCursorStateExplainsUnavailableReasons()
        {
            var snapshot = new PlaybackClock.PlaybackStateSnapshot
            {
                Status = 1,
                CurrentTimeNs = 12,
                Speed = 1f,
                DidSeek = false,
                RequestId = "phase140j"
            };

            var replayOff = JObject.Parse(ReplayCursorState.FromPlayback(
                replayEnabled: false,
                playbackEnabled: true,
                snapshot,
                startNs: 10,
                endNs: 20).ToJson());
            Check((bool)replayOff["available"] == false
                  && ((string)replayOff["message"]).Contains("Replay is not loaded", StringComparison.Ordinal),
                "140J-2A: cursor GET explains replay-off unavailable state");

            var playbackOff = JObject.Parse(ReplayCursorState.FromPlayback(
                replayEnabled: true,
                playbackEnabled: false,
                snapshot,
                startNs: 10,
                endNs: 20).ToJson());
            Check((bool)playbackOff["available"] == false
                  && ((string)playbackOff["message"]).Contains("Playback control is not enabled", StringComparison.Ordinal),
                "140J-2B: cursor GET explains playback-off unavailable state");

            var invalidRange = JObject.Parse(ReplayCursorState.FromPlayback(
                replayEnabled: true,
                playbackEnabled: true,
                snapshot,
                startNs: 20,
                endNs: 10).ToJson());
            Check((bool)invalidRange["available"] == false
                  && ((string)invalidRange["message"]).Contains("range is invalid", StringComparison.Ordinal),
                "140J-2C: cursor GET explains invalid range unavailable state");

            var available = JObject.Parse(ReplayCursorState.FromPlayback(
                replayEnabled: true,
                playbackEnabled: true,
                snapshot,
                startNs: 10,
                endNs: 20).ToJson());
            Check((bool)available["available"]
                  && (bool)available["replayEnabled"]
                  && (bool)available["playbackEnabled"]
                  && available["time"]?["sec"] != null
                  && available["message"]?.ToString() == "Replay cursor state available.",
                "140J-2D: cursor GET remains backward compatible on available state");
        }

        private static void SetupReplaySurfacesNonSchemaFailure()
        {
            var setup = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs");
            var setupReplay = ExtractMethod(setup, "private bool SetupReplay()");

            Check(setup.Contains("CreateReplayFallbackWarning", StringComparison.Ordinal)
                  && setup.Contains("ReplayStartFailureMessage", StringComparison.Ordinal)
                  && setup.Contains("restoring live publishers", StringComparison.Ordinal),
                "140J-3A: SetupReplay builds a user-facing non-schema replay fallback warning");
            Check(ContainsAfter(setupReplay, "Debug.LogWarning(CreateReplayFallbackWarning", "RestoreLivePublishers();"),
                "140J-3B: SetupReplay logs the non-schema replay failure before restoring live publishers");
            Check(setup.Contains("ReplayStartBlockedBySchemaMismatch", StringComparison.Ordinal)
                  && setup.Contains("Debug.LogError(\"[Foxglove] Replay startup aborted", StringComparison.Ordinal),
                "140J-3C: schema-block replay failure path remains distinct and fatal to startup");
        }

        private static void ManagerWarnsOnceForReplayUnavailableCursor()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(manager.Contains("_replayCursorEndpointLoggedUnavailable", StringComparison.Ordinal),
                "140J-4A: manager tracks one-shot replay-unavailable cursor warnings");
            Check(server.Contains("ExternalReplayCursorEnqueueResult.ReplayUnavailable", StringComparison.Ordinal)
                  && server.Contains("Foxglove timeline sync is on but external cursor control is unavailable", StringComparison.Ordinal)
                  && server.Contains("_replayCursorEndpointLoggedUnavailable = true", StringComparison.Ordinal),
                "140J-4B: first replay-unavailable cursor rejection logs a clear warning");
            Check(MethodContains(server, "private void StartReplayCursorEndpointIfNeeded()", "_replayCursorEndpointLoggedUnavailable = false;")
                  && MethodContains(server, "private void StopReplayCursorEndpoint()", "_replayCursorEndpointLoggedUnavailable = false;"),
                "140J-4C: replay-unavailable cursor warning resets when the endpoint starts or stops");
            Check(server.Contains("Replay cursor endpoint ready", StringComparison.Ordinal)
                  && server.Contains("GetExternalReplayCursorState", StringComparison.Ordinal),
                "140J-4D: cursor endpoint remains advertised as the diagnostic state channel");
        }

        private static void ValidationRegistryExposesPhase140J()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase140j\", \"Phase 140J\", Phase140JValidation.Validate", StringComparison.Ordinal),
                "140J-5A: validation registry exposes --phase140j");
        }

        private static bool Ordered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static bool ContainsAfter(string source, string anchor, string expected)
        {
            var anchorIndex = source.IndexOf(anchor, StringComparison.Ordinal);
            return anchorIndex >= 0
                   && source.IndexOf(expected, anchorIndex + anchor.Length, StringComparison.Ordinal) > anchorIndex;
        }

        private static bool MethodContains(string source, string signature, string expected)
        {
            var method = ExtractMethod(source, signature);
            return method.Contains(expected, StringComparison.Ordinal);
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return source.Substring(start);
        }

        private static string Read(string path)
            => File.ReadAllText(path);

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class RecordingState : IRecordingStateReader
        {
            public RecordingState(bool isEnabled, string coordinateMode)
            {
                IsEnabled = isEnabled;
                CoordinateMode = coordinateMode;
            }

            public bool IsEnabled { get; }
            public string CoordinateMode { get; }
        }

        private sealed class CaptureLogger : IFoxgloveLogger
        {
            public string LastWarning { get; private set; } = string.Empty;
            public string LastError { get; private set; } = string.Empty;

            public void LogWarning(string message)
            {
                LastWarning = message ?? string.Empty;
            }

            public void LogError(string message)
            {
                LastError = message ?? string.Empty;
            }
        }
    }
}
