// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-20 validation for ROS2 Bridge and R2FU boundary review fixes.

using System;
using System.IO;
using Unity.FoxgloveSDK.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_20Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-20: SDK ROS2 Bridge and Middleware Boundary ===");
            _passed = 0;

            Ros2TopicValidationRejectsDigitLeadingTokens();
            Ros2BridgeTransportSourceShapeIsHardened();
            RuntimeSelectionManifestSourceShapeIsHardened();
            RuntimeSelectorInspectorDefersResolveHelpBox();
            RuntimePlayModeGuardDocumentsReloadResidualRisk();
            Ros2ValidationHelperReportsMissingGitClearly();
            Ros2BridgeFrameExposesNonAllocatingPayloadView();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-20: {_passed} checks passed.");
        }

        private static void Ros2TopicValidationRejectsDigitLeadingTokens()
        {
            Check(!Ros2BridgeTopicProfile.IsValidRos2TopicName("/1sensor/data"),
                "163-20A-1: ROS2 Bridge rejects digit-leading first topic tokens");
            Check(!Ros2BridgeTopicProfile.IsValidRos2TopicName("/sensor/2data"),
                "163-20A-2: ROS2 Bridge rejects digit-leading nested topic tokens");
            Check(Ros2BridgeTopicProfile.IsValidRos2TopicName("/_sensor/data_2"),
                "163-20A-3: ROS2 Bridge accepts underscore-leading and digit-containing tokens");
            Check(Ros2BridgeTopicProfile.IsValidRos2TopicName("/sensor/a2"),
                "163-20A-4: ROS2 Bridge accepts digit characters after the token start");
        }

        private static void Ros2BridgeTransportSourceShapeIsHardened()
        {
            var tcp = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeTcpClient.cs");
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeRuntime.cs");

            Check(tcp.Contains("client = null;", StringComparison.Ordinal)
                  && tcp.Contains("client?.Dispose();", StringComparison.Ordinal)
                  && tcp.IndexOf("task.Wait(timeoutMs)", StringComparison.Ordinal) < tcp.IndexOf("client = null;", StringComparison.Ordinal),
                "163-20B-1: TCP bridge connect disposes the client on every pre-assignment fault path");
            Check(runtime.Contains("AutoResetEvent", StringComparison.Ordinal)
                  && runtime.Contains("WaitOne(_reconnectIntervalMs)", StringComparison.Ordinal)
                  && runtime.Contains("WaitOne(50)", StringComparison.Ordinal)
                  && !runtime.Contains("ManualResetEventSlim", StringComparison.Ordinal),
                "163-20B-2: ROS2 Bridge worker uses auto-reset signaling instead of Wait/Reset races");
            Check(runtime.Contains("constructor timeout for worker connect attempts", StringComparison.Ordinal)
                  && runtime.Contains("constructor timeout for the actual transport send", StringComparison.Ordinal),
                "163-20B-3: queued runtime documents its IRos2BridgeSink timeout semantics");
        }

        private static void RuntimeSelectionManifestSourceShapeIsHardened()
        {
            var selection = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Check(selection.Contains("WriteManifestAtomically", StringComparison.Ordinal)
                  && selection.Contains("File.Replace(tempPath, manifestPath, null)", StringComparison.Ordinal),
                "163-20C-1: R2FU runtime selection writes manifest.json through an atomic replace path");
            Check(selection.Contains("ReadManifestJson(manifest", StringComparison.Ordinal)
                  && selection.Contains("dependencies.Properties()", StringComparison.Ordinal)
                  && selection.Contains("property.Remove();", StringComparison.Ordinal)
                  && selection.Contains("new JProperty(packageName", StringComparison.Ordinal),
                "163-20C-2: R2FU runtime removal and insertion use parsed JSON instead of line surgery");
            Check(selection.Contains("BuildRuntimePackageReference", StringComparison.Ordinal)
                  && selection.Contains("GetRelativePath(projectPackagesDirectory, runtimePackageDirectory)", StringComparison.Ordinal)
                  && !selection.Contains("\"file:../../Packages/\" + packageName", StringComparison.Ordinal),
                "163-20C-3: R2FU runtime dependency paths are derived from repository layout");
            Check(selection.Contains("if (status.SelectedRuntime == null)\n                return;", StringComparison.Ordinal)
                  && selection.Contains("Environment.SetEnvironmentVariable(\"RMW_IMPLEMENTATION\"", StringComparison.Ordinal),
                "163-20C-4: R2FU communication environment is not mutated when no runtime is selected");
        }

        private static void RuntimeSelectorInspectorDefersResolveHelpBox()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");
            var switchMethod = ExtractMethod(inspector, "SwitchAndResolve");

            Check(inspector.Contains("DrawPendingResolveMessage", StringComparison.Ordinal)
                  && switchMethod.IndexOf("_pendingResolveMessage =", StringComparison.Ordinal)
                     < switchMethod.IndexOf("SwitchActiveRuntimePackage", StringComparison.Ordinal),
                "163-20D-1: R2FU selector stores durable resolve feedback before package resolution");
            Check(!switchMethod.Contains("EditorGUILayout.HelpBox", StringComparison.Ordinal),
                "163-20D-2: R2FU selector does not emit HelpBox controls inside the change handler");
        }

        private static void RuntimePlayModeGuardDocumentsReloadResidualRisk()
        {
            var guard = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");

            Check(guard.Contains("assembly reload is continuing before Play Mode fully exited", StringComparison.Ordinal)
                  && guard.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal),
                "163-20E-1: R2FU play-mode guard reports asynchronous reload residual risk");
            Check(guard.Contains("EditorApplication.LockReloadAssemblies()", StringComparison.Ordinal)
                  && guard.Contains("RequestNativeRuntimeShutdownBeforeReload", StringComparison.Ordinal)
                  && guard.Contains("HasR2fuNativeOutputDemand()", StringComparison.Ordinal)
                  && guard.Contains("Ros2UnityComponentSuffix", StringComparison.Ordinal)
                  && !guard.Contains("\"ROS2.ROS2UnityComponent\"", StringComparison.Ordinal)
                  && guard.Contains("ShutdownShared", StringComparison.Ordinal),
                "163-20E-2: R2FU play-mode guard locks editor reloads and requests native shutdown before unsafe reload");
        }

        private static void Ros2ValidationHelperReportsMissingGitClearly()
        {
            var helper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseRos2ForUnityValidationHelpers.cs");

            Check(helper.Contains("catch (FileNotFoundException ex)", StringComparison.Ordinal)
                  && helper.Contains("git is not in PATH", StringComparison.Ordinal),
                "163-20F-1: ROS2 For Unity validation helper reports missing git clearly");
        }

        private static void Ros2BridgeFrameExposesNonAllocatingPayloadView()
        {
            var frame = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Ros2Bridge/Ros2BridgeFrame.cs");

            Check(frame.Contains("public ReadOnlyMemory<byte> PayloadMemory", StringComparison.Ordinal),
                "163-20G-1: ROS2 Bridge frame exposes a non-allocating public payload view");
            Check(frame.Contains("Use PayloadMemory for a non-allocating read-only view", StringComparison.Ordinal),
                "163-20G-2: obsolete Payload guidance points external callers to the public replacement");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_20Validation.cs", StringComparison.Ordinal),
                "163-20H-1: runtime test project compiles Phase163_20Validation");
            Check(registry.Contains("--phase163-20", StringComparison.Ordinal)
                  && registry.Contains("Phase163_20Validation.Validate", StringComparison.Ordinal),
                "163-20H-2: validation registry exposes --phase163-20");
        }

        private static string ExtractMethod(string source, string methodName)
        {
            var signature = source.IndexOf("private static", StringComparison.Ordinal);
            while (signature >= 0)
            {
                var nameIndex = source.IndexOf(methodName, signature, StringComparison.Ordinal);
                if (nameIndex >= 0)
                {
                    var openParen = source.IndexOf('(', nameIndex);
                    var nextBrace = source.IndexOf('{', signature);
                    if (openParen >= 0 && nextBrace >= 0 && openParen < nextBrace)
                        break;
                }

                signature = source.IndexOf("private static", signature + 1, StringComparison.Ordinal);
            }

            if (signature < 0)
                signature = source.IndexOf("public static", StringComparison.Ordinal);
            if (signature < 0)
                return string.Empty;

            var bodyStart = source.IndexOf('{', signature);
            if (bodyStart < 0)
                return string.Empty;

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }

            return source.Substring(bodyStart);
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
