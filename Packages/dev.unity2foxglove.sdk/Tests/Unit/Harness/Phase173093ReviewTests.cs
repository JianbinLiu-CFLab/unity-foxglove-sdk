// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-093 review regression guards.

using System;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-093")]
    [Trait("Domain", "Review")]
    public sealed class Phase173093ReviewTests
    {
        [Fact]
        public void OpenH264ArgumentsKeepWindowsCommandLineEscapingCoverage()
        {
            var tests = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Sensors/OpenH264EncoderSidecarTests.cs");
            var options = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/OpenH264EncoderOptions.cs");

            Assert.Contains("OpenH264ArgumentsUseWindowsCommandLineEscaping", tests, StringComparison.Ordinal);
            Assert.Contains(@"C:\OpenH264 Runtime\", tests, StringComparison.Ordinal);
            Assert.Contains("backslashes", options, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("builder.Append('\"');", options, StringComparison.Ordinal);
            Assert.Contains("builder.Append('\\\\', backslashes * 2);", options, StringComparison.Ordinal);
        }

        [Fact]
        public void PointCloudSmokeSourceReusesFrameBuffersInUpdatePath()
        {
            var source = TestSources.Text("Unity2Foxglove/Assets/Scripts/PointCloud/PointCloudSmokeSource.cs");
            var buildFrame = TestSources.Slice(source, "private PointCloudFrame BuildFrame", "    }\n}");

            Assert.Contains("private readonly PointCloudFrame[] _frameBuffers", source, StringComparison.Ordinal);
            Assert.Contains("frame.Points.Clear();", buildFrame, StringComparison.Ordinal);
            Assert.Contains("frame.Points.Capacity", buildFrame, StringComparison.Ordinal);
            Assert.DoesNotContain("new PointCloudFrame", buildFrame, StringComparison.Ordinal);
            Assert.DoesNotContain("new List<PointCloudPoint>", buildFrame, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerDiagnosticsCachesTransportClientLabels()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");
            var drawTransport = TestSources.Slice(source, "private void DrawTransportHealth", "private TransportClientLabelCache GetTransportClientLabel");

            Assert.Contains("_transportClientLabelCache", source, StringComparison.Ordinal);
            Assert.Contains("private TransportClientLabelCache GetTransportClientLabel", source, StringComparison.Ordinal);
            Assert.Contains("GetTransportClientLabel(c)", drawTransport, StringComparison.Ordinal);
            Assert.DoesNotContain("$\"#{c.ClientId}", drawTransport, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayChannelBehaviorOnlySwallowsJsonParseFailures()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayChannelBehavior.cs");

            Assert.Contains("catch (JsonException)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("catch\r\n", source, StringComparison.Ordinal);
            Assert.DoesNotContain("catch\n", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayTraceResetsStaticStateOnSubsystemRegistration()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/FoxgloveReplayTrace.cs");
            var reset = TestSources.Slice(source, "private static void ResetForSubsystemRegistration", "        internal static void ResetBudget");

            Assert.Contains("RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)", source, StringComparison.Ordinal);
            Assert.Contains("#if UNITY_5_3_OR_NEWER", source, StringComparison.Ordinal);
            Assert.Contains("Volatile.Write(ref _enabled, 0);", reset, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _ordinal, 0);", reset, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref _lines, 0);", reset, StringComparison.Ordinal);
        }

        [Fact]
        public void OpenH264InstallLocationRejectsProtectedSystemRoots()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264InstallLocation.cs");

            Assert.Contains("Environment.SpecialFolder.Windows", source, StringComparison.Ordinal);
            Assert.Contains("Environment.SpecialFolder.System", source, StringComparison.Ordinal);
            Assert.Contains("Environment.SpecialFolder.SystemX86", source, StringComparison.Ordinal);
            Assert.Contains("Environment.SpecialFolder.CommonApplicationData", source, StringComparison.Ordinal);
            Assert.Contains("EditorApplication.applicationPath", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FoxRunDescriptorReaderReportsMalformedJsonShapes()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxRunGenerationDescriptorJsonReader.cs");

            Assert.Contains("typeToken as JObject", source, StringComparison.Ordinal);
            Assert.Contains("memberToken as JObject", source, StringComparison.Ordinal);
            Assert.Contains("FoxRun generation descriptor 'types' entries must be JSON objects.", source, StringComparison.Ordinal);
            Assert.Contains("FoxRun generation descriptor 'members' entries must be JSON objects.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("(JObject)typeToken", source, StringComparison.Ordinal);
        }

        [Fact]
        public void McapInspectSmokeScriptPrintsDotnetStdoutOnSuccess()
        {
            var source = TestSources.Text("Scripts/smoke/mcap/ros2_cdr_mcap_inspect.py");
            var runDotnet = TestSources.Slice(source, "def run_dotnet", "def main");

            Assert.Contains("if result.stdout:", runDotnet, StringComparison.Ordinal);
            Assert.Contains("print(result.stdout, file=sys.stderr if result.returncode != EXIT_SUCCESS else sys.stdout, end=\"\")", runDotnet, StringComparison.Ordinal);
            Assert.Contains("file=sys.stderr", runDotnet, StringComparison.Ordinal);
        }

        [Fact]
        public void ValidationNamingGuardsUseSharedRepoRootLocator()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationNamingGuardsValidation.cs");
            var helper = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/TestRepoRootLocator.cs");
            var project = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Assert.Contains("TestRepoRootLocator.FindRepoRoot()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Phase16Validation.FindRepoRoot()", source, StringComparison.Ordinal);
            Assert.Contains("Unity2Foxglove", helper, StringComparison.Ordinal);
            Assert.Contains("Packages\", \"dev.unity2foxglove.sdk\", \"package.json\"", helper, StringComparison.Ordinal);
            Assert.Contains("TestRepoRootLocator.cs", project, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2ForUnityRuntimeSelectorDocumentsMultipleRuntimePackageGuard()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");

            Assert.Contains("Multiple ROS2 For Unity runtime packages are resolved in the Unity manifest", source, StringComparison.Ordinal);
            Assert.Contains("activePackages.Length > 1", source, StringComparison.Ordinal);
            Assert.Contains("BindActiveRuntimeForPlayMode", source, StringComparison.Ordinal);
        }
    }
}
