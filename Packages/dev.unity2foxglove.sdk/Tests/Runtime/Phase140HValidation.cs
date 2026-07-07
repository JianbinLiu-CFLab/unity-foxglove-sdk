// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140H publish cadence diagnostics and fixed-time scheduler boundary validation.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Phase 140H publish cadence diagnostic boundary and drift-only fixed scheduler helper.
    /// </summary>
    public static class Phase140HValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140H validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140H: Publish cadence diagnostics and drift boundary ===");
            _passed = 0;

            ManagerOwnsPerTopicCadenceDiagnostics();
            PublishBoundaryRecordsJsonProtobufAndRos2();
            TransportRemainsTopicAgnostic();
            FixedSchedulerHelperIsDriftOnly();
            VirtualImuRemainsOutsideBaseSchedulerPath();
            ValidationRegistryExposesPhase140H();

            Console.WriteLine($"Phase 140H: {_passed} checks passed.");
        }

        private static void ManagerOwnsPerTopicCadenceDiagnostics()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Diagnostics.cs");
            var state = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/StatisticsRuntimeState.cs");
            var editorDiagnostics = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");
            Check(!manager.Contains("private sealed class PublishCadenceDiagnostics", StringComparison.Ordinal)
                  && !diagnostics.Contains("private sealed class PublishCadenceDiagnostics", StringComparison.Ordinal)
                  && state.Contains("internal sealed class PublishCadenceDiagnostics", StringComparison.Ordinal)
                  && state.Contains("internal readonly PublishCadenceDiagnostics PublishCadenceDiagnostics = new();", StringComparison.Ordinal),
                "140H-1A: publish cadence implementation lives in Manager diagnostics state");
            Check(diagnostics.Contains("_publishCadenceDiagnosticsEnabled", StringComparison.Ordinal)
                  && diagnostics.Contains("PublishCadenceDiagnosticsEnabled", StringComparison.Ordinal),
                "140H-1B: manager exposes opt-in publish cadence diagnostics");
            Check(state.Contains("topic={0} encoding={1} messages={2}", StringComparison.Ordinal)
                  && state.Contains("maxPerFrame={7} burstFrames={8}", StringComparison.Ordinal),
                "140H-1C: manager aggregates per-topic diagnostic summaries");
            Check(manager.Contains("FlushPublishCadenceDiagnosticsIfNeeded();", StringComparison.Ordinal)
                  && diagnostics.Contains("LogPublishCadenceSummary(summary)", StringComparison.Ordinal),
                "140H-1D: manager periodically surfaces aggregated cadence diagnostics");
            Check(editorDiagnostics.Contains("DrawPublishCadenceDiagnostics();", StringComparison.Ordinal)
                  && editorDiagnostics.Contains("FoxgloveManagerInspectorLayout.Subheader(\"Publish Cadence\")", StringComparison.Ordinal)
                  && editorDiagnostics.Contains("_publishCadenceDiagnosticsEnabled", StringComparison.Ordinal),
                "140H-1E: Inspector exposes cadence controls under Diagnostics");
            Check(diagnostics.Contains("Debug.LogFormat(LogType.Log, LogOption.NoStacktrace", StringComparison.Ordinal)
                  && !diagnostics.Contains("Application.SetStackTraceLogType", StringComparison.Ordinal),
                "140H-1F: periodic cadence logs suppress per-summary stack traces without mutating global log settings");
        }

        private static void PublishBoundaryRecordsJsonProtobufAndRos2()
        {
            var publishing = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            Check(MethodContains(publishing, "public void PublishJson", "_runtime.PublishJson(channelId, message, logTimeNs);")
                  && MethodContains(publishing, "public void PublishJson", "RecordPublishCadence(topic, JsonEncoding);"),
                "140H-2A: JSON publish path records cadence after successful publish");
            Check(MethodContains(publishing, "public void PublishProto", "_runtime.Publish(channelId, payload ?? System.Array.Empty<byte>(), logTimeNs);")
                  && MethodContains(publishing, "public void PublishProto", "RecordPublishCadence(topic, ProtobufEncoding);"),
                "140H-2B: protobuf publish path records cadence after successful publish");
            Check(MethodContains(publishing, "public void PublishRos2(string topic", "_runtime.PublishRos2Cdr(channelId, payload, logTimeNs);")
                  && MethodContains(publishing, "public void PublishRos2(string topic", "RecordPublishCadence(topic, CdrEncoding);"),
                "140H-2C: ROS2 publish path records cadence after successful publish");
            Check(!MethodContains(publishing, "public void PublishRos2BridgeCdr(string topic, string topicOverride", "RecordPublishCadence("),
                "140H-2D: ROS2 Bridge mirror path is not counted as WebSocket publish cadence");
        }

        private static void TransportRemainsTopicAgnostic()
        {
            var queue = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/WebSocket/WsSendQueue.cs");
            Check(queue.Contains("private readonly Queue<QueuedFrame> _controlFrames", StringComparison.Ordinal)
                  && queue.Contains("private readonly Queue<QueuedFrame> _dataFrames", StringComparison.Ordinal),
                "140H-3A: WebSocket send queue still preserves separate control/data lanes");
            Check(queue.Contains("public byte Opcode { get; }", StringComparison.Ordinal)
                  && queue.Contains("public byte[] Payload { get; }", StringComparison.Ordinal)
                  && queue.Contains("public FramePriority Priority { get; }", StringComparison.Ordinal)
                  && !queue.Contains("public string Topic", StringComparison.Ordinal)
                  && !queue.Contains("public uint ChannelId", StringComparison.Ordinal),
                "140H-3B: queued transport frames remain topic-agnostic");
            Check(!queue.Contains("PublishCadence", StringComparison.Ordinal)
                  && !queue.Contains("topic=", StringComparison.Ordinal),
                "140H-3C: per-topic cadence diagnostics are not implemented in transport");
        }

        private static void FixedSchedulerHelperIsDriftOnly()
        {
            var publisherBase = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            Check(MethodContains(publisherBase, "protected bool ShouldPublishNow()", "Time.unscaledTimeAsDouble"),
                "140H-4A: existing render-clock scheduler helper remains unchanged");
            Check(MethodContains(publisherBase, "protected bool ShouldPublishNowFixed()", "Time.fixedTimeAsDouble")
                  && MethodContains(publisherBase, "protected bool ShouldPublishNowFixed()", "FixedRatePublishScheduler.ShouldPublish(")
                  && MethodContains(publisherBase, "protected bool ShouldPublishNowFixed()", "ref _publishRateStateFixed"),
                "140H-4B: fixed-time scheduler helper uses independent fixed scheduler state");
            Check(publisherBase.Contains("private FixedRatePublishState _publishRateStateFixed;", StringComparison.Ordinal)
                  && MethodContains(publisherBase, "protected virtual void OnEnable()", "_publishRateStateFixed = default;"),
                "140H-4C: fixed-time scheduler state resets independently on enable");
            Check(publisherBase.Contains("This is drift-only for physics-clock publishers", StringComparison.Ordinal)
                  && publisherBase.Contains("WebSocket arrival cadence", StringComparison.Ordinal),
                "140H-4D: fixed-time helper documents drift-only semantics");
        }

        private static void VirtualImuRemainsOutsideBaseSchedulerPath()
        {
            var virtualImu = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            Check(virtualImu.Contains("public class VirtualImu : MonoBehaviour", StringComparison.Ordinal),
                "140H-5A: VirtualImu remains outside FoxglovePublisherBase scheduling");
            Check(!virtualImu.Contains("ShouldPublishNow()", StringComparison.Ordinal)
                  && !virtualImu.Contains("ShouldPublishNowFixed()", StringComparison.Ordinal),
                "140H-5B: VirtualImu is not accidentally moved onto base scheduler helpers");
            Check(virtualImu.Contains("ImuNativeFrameReady", StringComparison.Ordinal)
                  && virtualImu.Contains("_manager.PublishProto(_topic", StringComparison.Ordinal),
                "140H-5C: validation acknowledges IMU has both WebSocket and native frame lanes");
        }

        private static void ValidationRegistryExposesPhase140H()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase140h\", \"Phase 140H: publish cadence diagnostics and fixed-time scheduler boundary validation\", Phase140HValidation.Validate", StringComparison.Ordinal),
                "140H-6A: validation registry exposes --phase140h");
        }

        private static bool MethodContains(string source, string signature, string expected)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return false;

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return false;

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var method = source.Substring(start, i - start + 1);
                        return method.Contains(expected, StringComparison.Ordinal);
                    }
                }
            }

            return false;
        }

        private static string Read(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase140H validation.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
