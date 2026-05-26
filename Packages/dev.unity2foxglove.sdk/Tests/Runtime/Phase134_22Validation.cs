// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-22 validation for bundled Jazzy ROS2 For Unity wrapper hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_22Validation
    {
        private const string RuntimeRoot = "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts";

        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyExecutorTimeoutCleanup("ROS2UnityCore.cs", "ROS2UnityCore");
            VerifyExecutorTimeoutCleanup("ROS2UnityComponent.cs", "ROS2UnityComponent");
            VerifyPathDeduplication();

            Console.WriteLine($"Phase134_22Validation: PASS ({_passed} checks)");
        }

        private static void VerifyExecutorTimeoutCleanup(string fileName, string typeName)
        {
            var source = ReadRuntimeSource(fileName);
            Check(!source.Contains("if (!StopExecutor())", StringComparison.Ordinal),
                $"134-22-A1: {typeName} no longer returns immediately on executor stop timeout");
            Check(source.Contains("bool executorStopped = StopExecutor();", StringComparison.Ordinal)
                  && source.Contains("QuarantineNodesAfterExecutorTimeout", StringComparison.Ordinal),
                $"134-22-A2: {typeName} routes executor timeout through node quarantine cleanup");
            Check(source.Contains("TryDetachRuntimeState(executorStopped, out instance)", StringComparison.Ordinal)
                  && source.Contains("instance.DestroyROS2ForUnity();", StringComparison.Ordinal),
                $"134-22-A3: {typeName} keeps lifecycle owner release on the cleanup path");
            Check(source.Contains("could not acquire state lock after executor timeout", StringComparison.Ordinal)
                  && source.Contains("ROS2 lifecycle owner remains active", StringComparison.Ordinal),
                $"134-22-A4: {typeName} reports controlled lifecycle failure when cleanup cannot be made safe");
        }

        private static void VerifyPathDeduplication()
        {
            var source = ReadRuntimeSource("ROS2ForUnity.cs");
            Check(source.Contains("NormalizeEnvPathEntry", StringComparison.Ordinal)
                  && source.Contains("new HashSet<string>(comparer)", StringComparison.Ordinal),
                "134-22-B1: Windows plugin PATH setup normalizes and de-duplicates entries");
            Check(source.Contains("StringComparer.OrdinalIgnoreCase", StringComparison.Ordinal)
                  && source.Contains("currentPath.Split", StringComparison.Ordinal)
                  && source.Contains("seen.Add(NormalizeEnvPathEntry(entry))", StringComparison.Ordinal),
                "134-22-B2: Windows plugin PATH de-duplication handles existing entries case-insensitively");
            Check(!source.Contains("pluginPath + envPathSep + currentPath", StringComparison.Ordinal),
                "134-22-B3: Windows plugin PATH setup no longer blindly prepends duplicate entries");
        }

        private static string ReadRuntimeSource(string fileName)
        {
            var path = Path.Combine(RepoRoot, RuntimeRoot.Replace('/', Path.DirectorySeparatorChar), fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing bundled Jazzy runtime wrapper source: " + fileName, path);
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
