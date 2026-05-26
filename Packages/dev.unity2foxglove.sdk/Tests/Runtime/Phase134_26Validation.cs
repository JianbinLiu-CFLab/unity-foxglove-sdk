// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-26 regression coverage for R2FU adapter sample queue bounds.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_26Validation
    {
        private const string SmokePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityStringSmoke.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-26: R2FU Adapter Samples I ===");
            _passed = 0;

            VerifyDirectModeQueueBound();

            Console.WriteLine($"Phase 134-26: {_passed} checks passed.");
        }

        private static void VerifyDirectModeQueueBound()
        {
            var source = ReadRepoText(SmokePath);
            Check(source.Contains("private const int MaxDirectReceived = 32;", StringComparison.Ordinal),
                "134-26A-1: Phase110 direct diagnostic receive queue declares a fixed bound");
            Check(source.Contains("while (_directReceived.Count >= MaxDirectReceived)", StringComparison.Ordinal)
                  && source.Contains("_directReceived.Dequeue();", StringComparison.Ordinal)
                  && source.Contains("_directReceived.Enqueue(message.Data)", StringComparison.Ordinal),
                "134-26A-2: Phase110 direct diagnostic receive queue drops oldest messages before enqueue");
            Check(source.IndexOf("while (_directReceived.Count >= MaxDirectReceived)", StringComparison.Ordinal)
                  < source.IndexOf("_directReceived.Enqueue(message.Data)", StringComparison.Ordinal),
                "134-26A-3: Phase110 direct diagnostic receive queue bounds before enqueueing new data");
        }

        private static string ReadRepoText(string relativePath)
        {
            var fullPath = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Missing repository file: " + relativePath, fullPath);

            return File.ReadAllText(fullPath);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
