// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-087 Unity review regression checks.

using System;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "173-087")]
    [Trait("Domain", "UnityReview")]
    public sealed class Phase173087ReviewTests
    {
        [Fact]
        public void ManagedWebSocketRedactUrlUsesCachedRegex()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/ManagedWebSocketOptions.cs");

            Assert.Equal(
                "wss://127.0.0.1:8765?token=REDACTED&foo=bar",
                ManagedWebSocketOptions.RedactUrl("wss://127.0.0.1:8765?token=secret&foo=bar"));
            Assert.Contains("private static readonly Regex TokenRedactRegex", source, StringComparison.Ordinal);
            Assert.Contains("return TokenRedactRegex.Replace(url, \"$1REDACTED\");", source, StringComparison.Ordinal);
            Assert.DoesNotContain("return Regex.Replace(", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2TimeSourcesThrottleUnavailableWarningsAcrossRuntimePackages()
        {
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs");
            AssertRos2WarningThrottle("Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs");
        }

        [Fact]
        public void SamplesDocumentHasSingleNumberFiveHeading()
        {
            var doc = TestSources.Text("Packages/dev.unity2foxglove.sdk/Documentation~/en/03_Samples_and_Demo_Project.md");

            Assert.DoesNotContain("## 5. Repository Demo Project", doc, StringComparison.Ordinal);
            Assert.Contains("## 5. ROS2 Bridge Sample", doc, StringComparison.Ordinal);
            Assert.Contains("## 6. Repository Demo Project", doc, StringComparison.Ordinal);
        }

        [Fact]
        public void ReplayPendingQueueExposesTestOnlyDebugHeadIndexWithoutReflection()
        {
            var queueSource = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayPendingQueue.cs");
            var testSource = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Replay/McapReplayHelperExtractionTests.cs");

            Assert.Contains("internal int DebugHeadIndex => _headIndex;", queueSource, StringComparison.Ordinal);
            Assert.DoesNotContain("BindingFlags", testSource, StringComparison.Ordinal);
            Assert.DoesNotContain("GetField(", testSource, StringComparison.Ordinal);
            Assert.Contains("queue.DebugHeadIndex", testSource, StringComparison.Ordinal);
        }

        [Fact]
        public void McapDirectMessageTestsDisposeReaderAndUseValueAssertions()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/Mcap/McapDirectMessageRecordsTests.cs");

            Assert.DoesNotContain("\n            var reader = new McapReader(ms);", source, StringComparison.Ordinal);
            Assert.Contains("using var reader = new McapReader(ms);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Assert.True(summary.Statistics.MessageCount ==", source, StringComparison.Ordinal);
            Assert.Contains("Assert.Equal(20UL, summary.Statistics.MessageCount);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void EditorCompileSymbolEntryPointUsesSafeWrapper()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");
            var method = TestSources.ExtractMethod(source, "public static void ReconcileCompileSymbolForEditor()");

            Assert.Contains("ReconcileCompileSymbolSafely();", method, StringComparison.Ordinal);
            Assert.DoesNotContain("ReconcileCompileSymbol();", method, StringComparison.Ordinal);
        }

        [Fact]
        public void TopicMetadataEmitterDocumentsProcessLifetimeSha256()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");

            Assert.Contains("Process-lifetime generator helper", source, StringComparison.Ordinal);
            Assert.Contains("SHA256.HashData", source, StringComparison.Ordinal);
        }

        private static void AssertRos2WarningThrottle(string path)
        {
            var source = TestSources.Text(path);

            Assert.Contains("private int rosUnavailableWarningLogged = 0;", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 1)", source, StringComparison.Ordinal);
            Assert.Contains("Volatile.Read(ref rosUnavailableWarningLogged)", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 0)", source, StringComparison.Ordinal);
        }
    }
}
