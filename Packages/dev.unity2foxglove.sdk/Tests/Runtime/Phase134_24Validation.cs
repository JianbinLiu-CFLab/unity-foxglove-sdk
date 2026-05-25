// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-24 validation for Unity demo runtime script hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_24Validation
    {
        private const string ManualContextPath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs";

        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyPhase109ContextUsesRequestedNodeName();

            Console.WriteLine($"Phase134_24Validation: PASS ({_passed} checks)");
        }

        private static void VerifyPhase109ContextUsesRequestedNodeName()
        {
            var source = ReadRepoFile(ManualContextPath);

            Check(source.Contains("var normalizedName = NormalizeName(nodeName);", StringComparison.Ordinal),
                "134-24-A1: Phase109 manual facade normalizes requested node name once");
            Check(source.Contains("_ros2Unity.CreateNode(normalizedName)", StringComparison.Ordinal),
                "134-24-A2: Phase109 manual facade passes normalized name to native ROS2 node creation");
            Check(source.Contains("new Phase109Ros2ForUnityNode(_ros2Unity, ros2Node, normalizedName)", StringComparison.Ordinal),
                "134-24-A3: Phase109 wrapper and native node share the same normalized name");
            Check(!source.Contains("_ros2Unity.CreateNode(\"unity2foxglove_phase109\")", StringComparison.Ordinal),
                "134-24-A4: Phase109 manual facade no longer hard-codes the native node name");
        }

        private static string ReadRepoFile(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }
    }
}
