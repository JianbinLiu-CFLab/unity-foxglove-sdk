// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-4 publisher base lifecycle review fixes.

using System;
using System.IO;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for publisher base lifecycle defects found in Phase 140-4.
    /// </summary>
    public static class Phase140_4Validation
    {
        private const string PublisherBasePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs";

        private static int _passed;

        /// <summary>Runs all Phase 140-4 publisher base lifecycle review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-4: Publisher base lifecycle review fixes ===");
            _passed = 0;

            PublisherRateSchedulerStaleStateRequiresReset();
            PublisherOnEnableResetsRateStateAndWarningKeys();

            Console.WriteLine($"Phase 140-4: {_passed} checks passed.");
        }

        private static void PublisherRateSchedulerStaleStateRequiresReset()
        {
            var staleState = new FixedRatePublishState
            {
                HasSchedule = true,
                LastRateHz = 10f,
                NextDueSec = 100.1d
            };

            Check(!FixedRatePublishScheduler.ShouldPublish(
                    0d,
                    10f,
                    ref staleState,
                    nonPositivePublishesEveryFrame: true),
                "140-4A-1: stale fixed-rate state suppresses first publish after time resets");

            staleState = default;
            Check(FixedRatePublishScheduler.ShouldPublish(
                    0d,
                    10f,
                    ref staleState,
                    nonPositivePublishesEveryFrame: true),
                "140-4A-2: resetting fixed-rate state restores first-publish behavior");
        }

        private static void PublisherOnEnableResetsRateStateAndWarningKeys()
        {
            var source = ReadRepoText(PublisherBasePath);
            var onEnable = ExtractMethodBody(source, "OnEnable");

            Check(onEnable.Contains("_publishRateState = default;", StringComparison.Ordinal),
                "140-4B-1: publisher OnEnable resets fixed-rate scheduler state");
            Check(onEnable.Contains("_lastEncodingFallbackWarningKey = 0;", StringComparison.Ordinal)
                  && onEnable.Contains("_lastEncodingMismatchWarningKey = 0;", StringComparison.Ordinal)
                  && onEnable.Contains("_lastPublishTopicWarningKey = null;", StringComparison.Ordinal)
                  && onEnable.Contains("_lastOrdinaryTransportWarningKey = null;", StringComparison.Ordinal),
                "140-4B-2: publisher OnEnable resets de-duplicated warning keys");
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var match = Regex.Match(
                source,
                @"protected\s+virtual\s+void\s+" + methodName + @"\s*\(\)\s*\{(?<body>.*?)\n\s*\}",
                RegexOptions.Singleline);
            if (!match.Success)
                throw new InvalidOperationException("Could not find method body: " + methodName);

            return match.Groups["body"].Value;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))
                    || Directory.Exists(Path.Combine(dir, "Packages")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
