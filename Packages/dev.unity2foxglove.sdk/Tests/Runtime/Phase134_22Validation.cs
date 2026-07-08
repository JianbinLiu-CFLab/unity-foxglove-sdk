// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-22 validation for bundled Jazzy ROS2 For Unity wrapper hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_22Validation.
    /// </summary>
    public static class Phase134_22Validation
    {
        private const string RuntimeRoot = "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts";

        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            _passed = 0;

            VerifyRuntimeAsmdefPlatformBoundary();
            VerifyMetadataAndFinalizerDiagnostics();
            VerifySensorInitialPublishingContract();
            VerifyTimeSourceHardening();
            VerifyLifecycleAndDisposeShape();
            VerifyExecutorTimeoutCleanup("ROS2UnityCore.cs", "ROS2UnityCore");
            VerifyExecutorTimeoutCleanup("ROS2UnityComponent.cs", "ROS2UnityComponent");
            VerifyPathDeduplication();

            Console.WriteLine($"Phase134_22Validation: PASS ({_passed} checks)");
        }

        private static void VerifyRuntimeAsmdefPlatformBoundary()
        {
            var source = ReadRuntimeAsmdef();
            Check(source.Contains("\"WindowsStandalone64\"", StringComparison.Ordinal)
                  && source.Contains("\"Editor\"", StringComparison.Ordinal)
                  && source.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && !source.Contains("\"includePlatforms\": []", StringComparison.Ordinal),
                "134-22-C1: Jazzy Win64 runtime asmdef is platform-limited with the stable runtime assembly name");
        }

        private static void VerifyMetadataAndFinalizerDiagnostics()
        {
            var source = ReadRuntimeSource("ROS2ForUnity.cs");
            Check(source.Contains("private const string ros2ForUnityAssetFolderName", StringComparison.Ordinal),
                "134-22-D1: runtime asset folder identity is immutable");
            Check(source.Contains("private destructor field is missing or null", StringComparison.Ordinal)
                  && source.Contains("GC.SuppressFinalize(destructor)", StringComparison.Ordinal),
                "134-22-D2: ros2cs finalizer suppression logs missing private-field drift");
            Check(source.Contains("DocumentElement == null", StringComparison.Ordinal)
                  && source.Contains("SelectSingleNode(valuePath)", StringComparison.Ordinal)
                  && source.Contains("missing required node", StringComparison.Ordinal),
                "134-22-D3: metadata XPath lookup reports missing XML nodes explicitly");
            Check(source.Contains("exception is XmlException", StringComparison.Ordinal)
                  && source.Contains("Could not load ROS2 For Unity metadata files", StringComparison.Ordinal),
                "134-22-D4: metadata load failures include malformed XML diagnostics");
            Check(source.Contains("string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();", StringComparison.Ordinal)
                  && source.Contains("bool standaloneBuild = IsStandalone();", StringComparison.Ordinal)
                  && source.Contains("string currentRos2Version = standaloneBuild", StringComparison.Ordinal)
                  && source.Contains("? GetMetadataValue(ros2csMetadata, \"/ros2cs/ros2\")", StringComparison.Ordinal)
                  && source.Contains(": GetROSVersion();", StringComparison.Ordinal)
                  && source.Contains("CheckROSSupport(currentRos2Version)", StringComparison.Ordinal)
                  && source.Contains("WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version)", StringComparison.Ordinal)
                  && source.Contains("CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)", StringComparison.Ordinal)
                  && !source.Contains("ROS2 version in standalone process environment does not match this runtime package", StringComparison.Ordinal),
                "134-22-D5: standalone runtime support checks use packaged metadata and ignores externally sourced ROS_DISTRO");
        }

        private static void VerifySensorInitialPublishingContract()
        {
            var source = ReadRuntimeSource("Sensor.cs");
            Check(!source.Contains("publishing = true;", StringComparison.Ordinal),
                "134-22-E1: Sensor preserves serialized initial publishing state");
        }

        private static void VerifyTimeSourceHardening()
        {
            var scalable = ReadRuntimeSource(Path.Combine("Time", "ROS2ScalableTimeSource.cs"));
            Check(scalable.Contains("RefreshUnityTimeCache", StringComparison.Ordinal)
                  && scalable.Contains("lastTimeScale", StringComparison.Ordinal)
                  && !scalable.Contains("initialTimeScale = Time.timeScale", StringComparison.Ordinal),
                "134-22-F1: scalable time source consumes cached Unity time off executor thread");

            var timeUtils = ReadRuntimeSource(Path.Combine("Time", "TimeUtils.cs"));
            Check(timeUtils.Contains("fractionalSeconds", StringComparison.Ordinal)
                  && !timeUtils.Contains("secondsIn * 1e9", StringComparison.Ordinal)
                  && timeUtils.Contains("Interface for acquiring time", StringComparison.Ordinal),
                "134-22-F2: TimeUtils avoids full-epoch double nanosecond multiplication and fixes XML typo");

            var iface = ReadRuntimeSource(Path.Combine("Time", "ITimeSource.cs"));
            Check(iface.Contains("Interface for acquiring time", StringComparison.Ordinal),
                "134-22-F3: ITimeSource XML summary typo is fixed");

            foreach (var runtimeRoot in RuntimeSourceRoots())
            {
                var unity = ReadRuntimeSource(runtimeRoot, Path.Combine("Time", "UnityTimeSource.cs"));
                Check(unity.Contains("must be constructed on the Unity main thread", StringComparison.Ordinal),
                    "134-22-F4: UnityTimeSource constructor reports off-main-thread construction clearly for " + Path.GetFileName(runtimeRoot));
            }
        }

        private static void VerifyLifecycleAndDisposeShape()
        {
            var component = ReadRuntimeSource("ROS2UnityComponent.cs");
            Check(component.Contains("[DisallowMultipleComponent]", StringComparison.Ordinal),
                "134-22-G1: ROS2UnityComponent enforces one component per GameObject");
            Check(component.Contains("needsConstruct", StringComparison.Ordinal)
                  && component.Contains("private volatile bool cachedOk = false", StringComparison.Ordinal)
                  && component.Contains("if (initialized && cachedOk && nodes != null && ros2forUnity != null)", StringComparison.Ordinal)
                  && component.Contains("needsConstruct = ros2forUnity == null;", StringComparison.Ordinal),
                "134-22-G2: ROS2UnityComponent Ok uses a cached steady-state fast path before lazy construction");
            Check(component.Contains("!disposed && ros2forUnity != null && nodes != null && executableActions != null && ros2csNodes != null", StringComparison.Ordinal),
                "134-22-G3: ROS2UnityComponent Tick has explicit disposed/null parity guard");
            Check(component.Contains("Thread threadToStart = null", StringComparison.Ordinal)
                  && component.Contains("threadToStart.Start();", StringComparison.Ordinal),
                "134-22-G4: ROS2UnityComponent starts executor thread after releasing state lock");

            var core = ReadRuntimeSource("ROS2UnityCore.cs");
            Check(core.Contains("Thread threadToStart;", StringComparison.Ordinal)
                  && core.Contains("threadToStart.Start();", StringComparison.Ordinal),
                "134-22-G5: ROS2UnityCore starts executor thread after releasing constructor lock");

            var node = ReadRuntimeSource("ROS2Node.cs");
            Check(node.Contains("Failed to remove ROS2 node", StringComparison.Ordinal)
                  && node.Contains("Debug.LogWarning", StringComparison.Ordinal),
                "134-22-G6: ROS2Node.Dispose reports native removal failures");
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

        private static string ReadRuntimeSource(string runtimeRoot, string fileName)
        {
            var path = Path.Combine(RepoRoot, runtimeRoot.Replace('/', Path.DirectorySeparatorChar), fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing bundled runtime wrapper source: " + fileName, path);
            return File.ReadAllText(path);
        }

        private static string[] RuntimeSourceRoots()
            => new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts",
                RuntimeRoot,
                "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts"
            };

        private static string ReadRuntimeAsmdef()
        {
            var path = Path.Combine(RepoRoot, RuntimeRoot.Replace('/', Path.DirectorySeparatorChar), "Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef");
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing bundled Jazzy runtime asmdef", path);
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
