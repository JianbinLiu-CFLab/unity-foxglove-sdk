// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunDeclarationModelTests
    {
        [Fact]
        [Trait("Phase", "184-H")]
        public void Phase184TransportClientMarkerStateRejectsTornAndUnstablePairs()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");
            var probe = new Phase184TransportClientMarkerStateProbe(source, 8);

            Assert.True(probe.KindType.IsEnum);
            Assert.True(probe.DecisionType.IsValueType);
            Assert.All(
                probe.DecisionType.GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                property => Assert.False(property.CanWrite));

            Assert.Equal("None", probe.Observe(0, 0).Kind);
            Assert.Equal(("Normal", 0, 0L), probe.Observe(0, 0));

            probe.Reset();
            Assert.Equal("None", probe.Observe(-1, 0).Kind);
            Assert.Equal("None", probe.Observe(0, -1).Kind);
            Assert.Equal("None", probe.Observe(0, 0).Kind);
            Assert.Equal(("Normal", 0, 0L), probe.Observe(0, 0));

            probe.Reset();
            Assert.Equal("None", probe.Observe(1, 0).Kind);
            Assert.Equal("None", probe.Observe(0, 1).Kind);
            Assert.Equal("None", probe.Observe(1, 1).Kind);
            Assert.Equal(("Normal", 1, 1L), probe.Observe(1, 1));

            probe.Reset();
            Assert.Equal("None", probe.Observe(2, 1).Kind);
            Assert.Equal("None", probe.Observe(2, 2).Kind);
            Assert.Equal(("Normal", 2, 2L), probe.Observe(2, 2));

            probe.Reset();
            Assert.Equal("None", probe.Observe(3, 3).Kind);
            Assert.Equal("None", probe.Observe(4, 4).Kind);
            Assert.Equal("None", probe.Observe(3, 3).Kind);
            Assert.Equal(("Normal", 3, 3L), probe.Observe(3, 3));
            Assert.Equal("None", probe.Observe(3, 3).Kind);
            Assert.Equal("None", probe.Observe(3, 3).Kind);

            probe.Reset();
            Assert.Equal("None", probe.Observe(5, 5).Kind);
            probe.ResetPending();
            Assert.Equal("None", probe.Observe(5, 5).Kind);
            Assert.Equal(("Normal", 5, 5L), probe.Observe(5, 5));

            probe.Reset();
            for (var pair = 0; pair < 8; pair++)
            {
                Assert.Equal("None", probe.Observe(pair, pair).Kind);
                Assert.Equal(
                    ("Normal", pair, (long)pair),
                    probe.Observe(pair, pair));
            }

            Assert.Equal("None", probe.Observe(8, 8).Kind);
            Assert.Equal(("Overflow", 8, 8L), probe.Observe(8, 8));
            Assert.True(probe.IsOverflowed);
            Assert.Equal("None", probe.Observe(9, 9).Kind);
            Assert.Equal("None", probe.Observe(9, 9).Kind);

            probe.Reset();
            Assert.False(probe.IsOverflowed);
            Assert.Equal("None", probe.Observe(0, 0).Kind);
            Assert.Equal(("Normal", 0, 0L), probe.Observe(0, 0));
        }



        [Fact]
        [Trait("Phase", "181-F")]
        public void Phase181OriginProbeBindsNullablePayloadToCurrentRunToken()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase181FoxRunCustomRos2InterfaceAcceptance.cs");

            Assert.Contains(
                "CreateState(\n"
                + "                \"unity-bidirectional\",\n"
                + "                RunTokenProbeCount(_runToken),\n"
                + "                true)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "state.Count == RunTokenProbeCount(_runToken)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "private static int RunTokenProbeCount(string token)",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchExitQuiescesFoxRunSourcesBeforePlayModeShutdown()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var scheduleExit =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void SchedulePlayModeExit()");

            Assert.Contains(
                "PHASE184G_BATCH_SOURCES_QUIESCED",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "route.isActiveAndEnabled",
                source,
                StringComparison.Ordinal);
            var exitScheduleIndex = scheduleExit.IndexOf(
                "EditorApplication.delayCall += ExitPlayModeNow;",
                StringComparison.Ordinal);
            var quiesceIndex = scheduleExit.IndexOf(
                "QuiesceAcceptanceSources();",
                StringComparison.Ordinal);
            Assert.True(exitScheduleIndex >= 0);
            Assert.True(quiesceIndex >= 0);
            Assert.True(exitScheduleIndex < quiesceIndex);
            Assert.Contains(
                "catch (Exception exception)",
                scheduleExit,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchProbeRestoresDeadlinesAndKeepsExitIdempotentAcrossReload()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var attach =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void AttachHandlers()");
            var open =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void OpenSceneAndEnterPlayMode()");
            var retry =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void RetryCanceledPlayEntry()");
            var requestEditorExit =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void RequestEditorExit(int exitCode, string outcome)");
            var queueRetry =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void QueuePlayEntryRetry(string reason)");
            var workerResults =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static bool AllRequiredWorkerResultsReady()");

            Assert.Contains("RestoreRunState();", attach, StringComparison.Ordinal);
            Assert.Contains(
                "PersistTime(\"started-at\", value);",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "SessionState.SetBool(SessionKey(\"terminal-pass-observed\")",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "SessionState.GetBool(SessionKey(\"exit-requested\"), false)",
                open,
                StringComparison.Ordinal);
            Assert.Contains("StartupDeadlineExpired()", open, StringComparison.Ordinal);
            Assert.Contains("StartupDeadlineExpired()", retry, StringComparison.Ordinal);
            Assert.Contains("_editorExitQueued", requestEditorExit, StringComparison.Ordinal);
            Assert.Contains(
                "SessionKey(\"exit-code\")",
                requestEditorExit,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SessionState.SetBool(SessionKey(\"play-entry-retry-queued\"), true);",
                queueRetry,
                StringComparison.Ordinal);
            Assert.Contains(
                "SchedulePlayEntryAttempt();",
                queueRetry,
                StringComparison.Ordinal);
            Assert.Contains(
                "_requiredWorkerResultPaths.Length == 0",
                workerResults,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchProbeRequiresTokenBoundariesOnBothSides()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var matcher =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static bool HasExactRunToken(string condition)");

            Assert.Contains(
                "index > 0 && !char.IsWhiteSpace(condition[index - 1])",
                matcher,
                StringComparison.Ordinal);
            Assert.Contains(
                "end == condition.Length || char.IsWhiteSpace(condition[end])",
                matcher,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void BatchNativeAcceptanceRetriesPlayCanceledBeforeEditModeTransition()
        {
            foreach (var path in new[]
                     {
                         "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                         + "Phase181BatchModeCustomRos2InteropProbe.cs",
                         "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                         + "Phase184BatchModeProfileProbe.cs",
                     })
            {
                var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(path);
                Assert.Contains(
                    "SessionState.SetBool(SessionKey(\"play-entry-pending\"), true);",
                    source,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "RetryCanceledPlayEntry",
                    source,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "SessionState.GetBool(SessionKey(\"play-entry-pending\"), false)",
                    source,
                    StringComparison.Ordinal);
            }
        }



    }
}
