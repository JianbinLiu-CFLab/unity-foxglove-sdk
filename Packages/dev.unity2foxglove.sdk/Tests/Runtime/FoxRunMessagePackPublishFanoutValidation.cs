// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural release gate for generated typed MessagePack output and fanout wiring.

using System;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunMessagePackPublishFanoutValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun MessagePack publish/fanout validation ---");
            _passed = 0;

            var emitter = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/MessagePackPublishDispatchEmitter.cs");
            Check(
                ContainsAll(
                    emitter,
                    "FoxgloveMsgPackWriter",
                    "WriteMapHeader",
                    "ToArray()"),
                "185B-1: generated output delegates to the maintained deterministic MessagePack writer");

            var publish = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            Check(
                ContainsAll(
                    publish,
                    "__foxRunLastMessagePack_",
                    "PublishFoxRunMessagePackBytes",
                    "TryPrepareFoxRunMessagePackRecording",
                    "TryPublishFoxRunMessagePackRecording",
                    "router.PublishCompatible"),
                "185B-2: one captured payload is wired to live, recording, and compatible synchronous sinks");

            var hub = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var hubBehavior = Read("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxgloveLogHubProviderSessionTests.cs");
            Check(
                ContainsAll(
                    hub,
                    "IFoxglovePublishRecordingSource",
                    "!publishWebSocket",
                    "FoxgloveLog_IsRecordingReady",
                    "FoxgloveLog_RecordCaptured",
                    "active.PublishTransportIds",
                    "!HasSelectedPublishProviders(info)",
                    "&& recorded")
                && ContainsAll(
                    hubBehavior,
                    "InheritedPublishUsesFrozenWebSocketSelection",
                    "HiddenRecordingCannotConsumeUnavailableSelectedProvider",
                    "ProviderlessDeclarationMayReportRecordingOnlySuccess"),
                "185B-3: WebSocket-excluded topics retain provider-neutral MCAP without mutable-session routing or false live success");

            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.MessagePack.cs");
            Check(
                ContainsAll(
                    manager,
                    "PublishFoxRunMessagePackBytes",
                    "TryPrepareFoxRunMessagePackRecording",
                    "TryPublishFoxRunMessagePackRecording",
                    "MsgPackEncoding",
                    "string.Empty"),
                "185B-4: Manager keeps live and recording-only MessagePack channels schemaless");

            var generated = Read("Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs");
            Check(
                generated.Contains("/phase185/messagepack/full-duplex", StringComparison.Ordinal)
                && generated.Contains("__BuildFoxRunMessagePack_", StringComparison.Ordinal)
                && generated.Contains("FoxRunEncoding.MessagePack", StringComparison.Ordinal),
                "185B-5: controlled TestLog generated output contains the typed MessagePack contract");

            var ros2Publish = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/Ros2CustomPublishEmitter.cs");
            Check(
                !ros2Publish.Contains("msgpack", StringComparison.OrdinalIgnoreCase),
                "185B-6: ROS2 native publish generation remains on typed ROS2 DTO/CDR contracts");

            Console.WriteLine("FoxRun MessagePack publish/fanout: " + _passed + " checks passed.\n");
        }

        private static string Read(string path) => FoxRunMessagePackPublicContractValidation.Read(path);
        private static bool ContainsAll(string source, params string[] values)
            => FoxRunMessagePackPublicContractValidation.ContainsAll(source, values);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
