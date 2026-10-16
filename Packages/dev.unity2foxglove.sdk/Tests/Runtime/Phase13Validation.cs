// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase13Validation
    {
        /// <remarks>Phase192 source sections: RecordingAndReplayTests.cs, ReplayRuntimeTests.cs, PlaybackAndHistoryTests.cs, PriorityAndFixtures.cs, TransportFixtures.cs</remarks>
        private static int _passCount;
        private static readonly byte[] PlaybackControlRequestIdBytes = Encoding.UTF8.GetBytes("phase13-paused-seek");

        private static void Assert(bool condition, string label)
        {
            if (condition) { _passCount++; Console.WriteLine($"[PASS] {label}"); }
            else throw new Exception($"[FAIL] {label}");
        }

        /// <summary>
        /// Entry point: runs all Phase 13 tests covering IRuntimeContext
        /// indirection, recording/replay controllers, McapBinaryReader
        /// bounds checks, client publish auto-increment, seek boundary
        /// behavior, coordinate roundtrip, and handler non-accumulation.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 13 Tests ---");
            _passCount = 0;
            TestIruntimeContextIndirection();
            TestRecordingController();
            TestReplayController();
            TestReplayTickSortsCrossChannelLogTimes();
            TestReplayDefaultTickBudgetLimitsSeekBacklog();
            TestMcapBinaryReaderBounds();
            TestClientPublishAutoIncrementCid();
            TestSeekBeforeStartTime();
            TestSeekAtFirstChunkBoundary();
            TestClientPublishMessageIndex();
            TestClientPublishDoesNotCreateMixedTopicSchemas();
            TestCoordinateRoundtrip();
            TestReplayHandlerNoAccumulate();
            TestRuntimeReplayPlayAdvancesPlaybackClock();
            TestReplayAdvertisesProtobufSchemaAsBase64();
            TestReplayObjectAdapterRoutesProtobufBeforeJsonParse();
            TestReplayControllerSerializesReplayCursorAccess();
            TestPlaybackControlRunsOnRuntimeTick();
            TestPlaybackSeekTargetsDidSeekStateToRequestingClient();
            TestPausedReplaySeekFollowsOfficialPlaybackControlFlow();
            TestPausedReplaySeekAppliesSceneOnlySnapshot();
            TestActivePausedScrubDoesNotEmitPanelHistoryUntilSettled();
            TestReplayPlayInvalidatesPanelHistoryDeltaWatermark();
            TestReplaySubscribeRequestsPanelHistoryBackfill();
            TestReplayLateSubscribeBackfillsMissedPanelHistory();
            TestReplaySuppressesRuntimeLivePublish();
            TestReplaySuppressesRuntimeLiveChannelAdvertise();
            TestReplayUsesDataPriorityForSeekReset();
            Console.WriteLine($"Phase 13: {_passCount} checks passed.");
        }

        // ── IRuntimeContext indirection ──

        /// <summary>
        /// Verifies FoxgloveRuntime implements IRuntimeContext and a
        /// session can be started after receiving the context via
        /// <c>SetRuntimeContext</c>.
        /// </summary>
    }
}
