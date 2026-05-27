// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 72 validation for stable fixed-rate live publish cadence.

using System;
using System.IO;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the shared fixed-rate scheduler and its ordinary publisher
    /// plus FoxRun integration points.
    /// </summary>
    public static class Phase72Validation
    {
        private static int _passed;
        private static Type _stateType;
        private static MethodInfo _shouldPublishMethod;

        /// <summary>Runs all Phase 72 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 72: Live Publish Cadence Stability ===");
            _passed = 0;

            VerifySchedulerSurfaceAndSimulation();
            VerifyPublisherIntegration();
            VerifyFoxRunIntegration();

            Console.WriteLine($"Phase 72: {_passed} checks passed.");
        }

        private static void VerifySchedulerSurfaceAndSimulation()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Utilities/FixedRatePublishScheduler.cs");

            Check(source.Contains("public struct FixedRatePublishState"),
                "72A-1: FixedRatePublishState exists as a small value type");
            Check(source.Contains("public double NextDueSec"),
                "72A-2: scheduler state stores the next due timestamp");
            Check(source.Contains("public float LastRateHz"),
                "72A-3: scheduler state stores the previous rate for reset detection");
            Check(source.Contains("public bool HasSchedule"),
                "72A-4: scheduler state tracks first-use initialization");
            Check(source.Contains("public static class FixedRatePublishScheduler"),
                "72A-5: FixedRatePublishScheduler helper exists");
            Check(source.Contains("public static bool ShouldPublish"),
                "72A-6: scheduler exposes ShouldPublish");

            var assembly = typeof(Phase72Validation).Assembly;
            _stateType = assembly.GetType("Unity.FoxgloveSDK.Util.FixedRatePublishState");
            var schedulerType = assembly.GetType("Unity.FoxgloveSDK.Util.FixedRatePublishScheduler");
            Check(_stateType != null && schedulerType != null,
                "72A-7: scheduler types are compiled");
            if (_stateType == null || schedulerType == null)
                throw new Exception("[FAIL] 72A-7: scheduler types are compiled");

            _shouldPublishMethod = schedulerType.GetMethod(
                "ShouldPublish",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(double), typeof(float), _stateType.MakeByRefType(), typeof(bool) },
                modifiers: null);
            Check(_shouldPublishMethod != null && _shouldPublishMethod.ReturnType == typeof(bool),
                "72A-8: ShouldPublish signature is stable");
            if (_shouldPublishMethod == null || _shouldPublishMethod.ReturnType != typeof(bool))
                throw new Exception("[FAIL] 72A-8: ShouldPublish signature is stable");

            Check(SimulatePublishes(20f, 60d, 10d, true) == 200,
                "72A-9: 20 Hz at exact 60 fps produces exactly 200 publishes over 10 seconds");
            Check(Between(SimulatePublishes(20f, 50d, 10d, true), 195, 205),
                "72A-10: 20 Hz at 50 fps remains near target instead of drifting low");
            Check(Between(SimulatePublishes(20f, 21d, 10d, true), 195, 212),
                "72A-11: 20 Hz at 21 fps avoids the old every-other-frame collapse");
            Check(Between(SimulatePublishes(20f, 15d, 10d, true), 145, 155),
                "72A-12: target rates above frame rate are capped by available frames");

            var state = Activator.CreateInstance(_stateType);
            Check(InvokeShouldPublish(0d, 20f, ref state, true),
                "72A-13: first eligible frame publishes immediately");
            Check(InvokeShouldPublish(1d, 20f, ref state, true),
                "72A-14: a long stall publishes one catch-up frame");
            Check(!InvokeShouldPublish(1.001d, 20f, ref state, true),
                "72A-15: a long stall does not burst multiple immediate frames");

            state = Activator.CreateInstance(_stateType);
            Check(InvokeShouldPublish(0d, 0f, ref state, true),
                "72A-16: ordinary publishers can publish every frame for non-positive rates");
            Check(!InvokeShouldPublish(0d, 0f, ref state, false),
                "72A-17: FoxRun callers can reject raw non-positive rates");

            state = Activator.CreateInstance(_stateType);
            Check(InvokeShouldPublish(0d, 10f, ref state, true),
                "72A-18: scheduler initializes on first positive-rate call");
            Check(!InvokeShouldPublish(0.01d, 10f, ref state, true),
                "72A-19: scheduler suppresses frames before the next due timestamp");
            Check(InvokeShouldPublish(0.01d, 20f, ref state, true),
                "72A-20: rate changes reset cadence immediately");
        }

        private static void VerifyPublisherIntegration()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var shouldPublish = Slice(source, "protected bool ShouldPublishNow()", "protected static string SanitizeFrameId");

            Check(source.Contains("using Unity.FoxgloveSDK.Util;"),
                "72B-1: ordinary publishers import the scheduler utility namespace");
            Check(source.Contains("FixedRatePublishState _publishRateState"),
                "72B-2: ordinary publishers store scheduler state");
            Check(!source.Contains("_lastPublishTime"),
                "72B-3: ordinary publishers no longer use remainder-losing last-publish timestamps");
            Check(shouldPublish.Contains("FixedRatePublishScheduler.ShouldPublish"),
                "72B-4: ShouldPublishNow routes cadence through the shared scheduler");
            Check(shouldPublish.Contains("Time.unscaledTimeAsDouble"),
                "72B-5: ordinary publisher cadence keeps the existing unscaled time basis");
            Check(shouldPublish.Contains("EffectivePublishRateHz"),
                "72B-6: Phase 71 effective rate policy remains the source of truth");
            Check(shouldPublish.Contains("nonPositivePublishesEveryFrame: true"),
                "72B-7: ordinary publishers preserve non-positive no-throttle semantics");
        }

        private static void VerifyFoxRunIntegration()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = Slice(source, "private void Update()", "private void Scan()");
            var addSource = Slice(source, "private void AddSource", "private bool TriggerSource");
            var triggerSource = Slice(source, "private bool TriggerSource", "/// <summary>Clears all timers");

            Check(source.Contains("using Unity.FoxgloveSDK.Util;"),
                "72C-1: FoxRun hub imports the scheduler utility namespace");
            Check(source.Contains("Dictionary<IFoxgloveLogSource, FixedRatePublishState[]>"),
                "72C-2: FoxRun hub stores per-topic scheduler state");
            Check(update.Contains("Time.realtimeSinceStartupAsDouble"),
                "72C-3: FoxRun cadence keeps its existing realtime basis");
            Check(!update.Contains("var dt = Time.deltaTime") && !update.Contains("t[i] -= dt"),
                "72C-4: FoxRun no longer uses frame countdown timers for publish cadence");
            var oldCountdownFallback = "t[i] = info.RateHz > 0 ? 1f / " + "info.RateHz : 1f";
            Check(!update.Contains(oldCountdownFallback),
                "72C-5: FoxRun no longer resets countdown timers from elapsed frames");
            Check(update.Contains("var rateHz = info.RateHz"),
                "72C-6: FoxRun passes raw non-positive rates through so they disable scheduled publish");
            Check(update.Contains("FixedRatePublishScheduler.ShouldPublish"),
                "72C-7: FoxRun routes cadence through the shared scheduler");
            Check(update.Contains("nonPositivePublishesEveryFrame: false"),
                "72C-8: FoxRun keeps explicit non-positive rates disabled instead of every-frame publishers");
            Check(addSource.Contains("new FixedRatePublishState[count]"),
                "72C-9: FoxRun AddSource initializes scheduler state arrays");
            Check(!triggerSource.Contains("_timers"),
                "72C-10: triggered FoxRun publications bypass normal cadence state");
        }

        private static int SimulatePublishes(float rateHz, double frameHz, double seconds, bool nonPositivePublishesEveryFrame)
        {
            var state = Activator.CreateInstance(_stateType);
            var publishes = 0;
            var frames = (int)Math.Ceiling(frameHz * seconds);

            for (var frame = 0; frame < frames; frame++)
            {
                var nowSec = frame / frameHz;
                if (nowSec >= seconds)
                    break;

                if (InvokeShouldPublish(nowSec, rateHz, ref state, nonPositivePublishesEveryFrame))
                    publishes++;
            }

            return publishes;
        }

        private static bool InvokeShouldPublish(double nowSec, float rateHz, ref object state, bool nonPositivePublishesEveryFrame)
        {
            if (_shouldPublishMethod == null)
                throw new InvalidOperationException("Phase72 scheduler method was not resolved.");

            var args = new object[] { nowSec, rateHz, state, nonPositivePublishesEveryFrame };
            var result = (bool)_shouldPublishMethod.Invoke(null, args);
            state = args[2];
            return result;
        }

        private static bool Between(int value, int minInclusive, int maxInclusive)
        {
            return value >= minInclusive && value <= maxInclusive;
        }

        private static string Slice(string text, string start, string end)
        {
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;

            var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            return endIndex < 0
                ? text.Substring(startIndex)
                : text.Substring(startIndex, endIndex - startIndex);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}
