// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-73 source-shape regression coverage for Jazzy runtime wrapper optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_73Validation.
    /// </summary>
    public static class Phase140_73Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-73: Jazzy Runtime Wrapper Package Optimization ===");
            _passed = 0;

            VerifyFrameNameCachingPreservesMutableFrameId();
            VerifyTransformMatrixIsCached();
            VerifySupportedRosVersionsAreCached();
            VerifyUnsafeSnapshotOptimizationsRemainRejected();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-73: {_passed} checks passed.");
        }

        private static void VerifyFrameNameCachingPreservesMutableFrameId()
        {
            var sensor = Read("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs");
            var frameName = Slice(sensor, "public override string frameName()", "    /// <summary>\r\n    /// Visualises");
            Check(sensor.Contains("private string cachedFrameName;", StringComparison.Ordinal)
                  && sensor.Contains("private string cachedFrameNameOwner;", StringComparison.Ordinal)
                  && sensor.Contains("private string cachedFrameNameFrameId;", StringComparison.Ordinal),
                "140-73A-1: Sensor caches computed frame names without assuming frameID is immutable");
            Check(frameName.Contains("cachedFrameNameOwner != ownerAgentName", StringComparison.Ordinal)
                  && frameName.Contains("cachedFrameNameFrameId != frameID", StringComparison.Ordinal)
                  && frameName.Contains("cachedFrameName = ownerAgentName + \"/\" + frameID;", StringComparison.Ordinal)
                  && frameName.Contains("return cachedFrameName;", StringComparison.Ordinal),
                "140-73A-2: Sensor.frameName recomputes when owner or public frameID changes");
        }

        private static void VerifyTransformMatrixIsCached()
        {
            var transformations = Read("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Transformations.cs");
            var method = Slice(transformations, "public static Matrix4x4 Unity2RosMatrix4x4()", "}");
            Check(transformations.Contains("private static readonly Matrix4x4 Unity2RosMatrix", StringComparison.Ordinal)
                  && method.Contains("return Unity2RosMatrix;", StringComparison.Ordinal)
                  && !method.Contains("new Matrix4x4", StringComparison.Ordinal)
                  && !method.Contains(".transpose", StringComparison.Ordinal),
                "140-73B-1: Unity2RosMatrix4x4 returns a cached value-type matrix");
        }

        private static void VerifySupportedRosVersionsAreCached()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var checkSupport = Slice(source, "private void CheckROSSupport(string ros2Codename)", "    private void CheckRmwImplementation()");
            Check(source.Contains("private static readonly string[] SupportedRosVersions", StringComparison.Ordinal)
                  && source.Contains("private static readonly string SupportedRosVersionsString", StringComparison.Ordinal),
                "140-73C-1: supported ROS version constants are cached");
            Check(!checkSupport.Contains("new List<string>()", StringComparison.Ordinal)
                  && checkSupport.Contains("SupportedRosVersionsString", StringComparison.Ordinal)
                  && checkSupport.Contains("Array.IndexOf(SupportedRosVersions, ros2Codename)", StringComparison.Ordinal),
                "140-73C-2: CheckROSSupport avoids per-call List and joined-string allocation");
        }

        private static void VerifyUnsafeSnapshotOptimizationsRemainRejected()
        {
            var component = Read("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var core = Read("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs");
            Check(component.Contains("Ros2cs.SpinOnce(ros2csNodes", StringComparison.Ordinal)
                  && core.Contains("Ros2cs.SpinOnce(ros2csNodes", StringComparison.Ordinal)
                  && !component.Contains("nodesSnapshot", StringComparison.Ordinal)
                  && !core.Contains("nodesSnapshot", StringComparison.Ordinal),
                "140-73D-1: executor spin remains serialized with graph mutation; rejected snapshot optimization is absent");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_73Validation.cs", StringComparison.Ordinal),
                "140-73E-1: test project compiles Phase140_73Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-73\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_73Validation.Validate", StringComparison.Ordinal),
                "140-73E-2: validation registry exposes --phase140-73");
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
