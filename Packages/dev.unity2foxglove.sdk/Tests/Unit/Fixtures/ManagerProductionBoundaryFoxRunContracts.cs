#if MANAGER_PRODUCTION_BOUNDARY
// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Fixtures
// Purpose: Preserve generated FoxRun contract names while the boundary lane
//          intentionally excludes the full Unity FoxgloveLogHub host.
// Contract source of truth: Runtime/Components/FoxRun/FoxgloveLogHub.cs.
// Keep these declarations signature-identical to that production host while
// the boundary lane links only the Manager wiring under test.

namespace Unity.FoxgloveSDK.Components
{
    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(int topicIndex, FoxgloveManager manager, ulong nowNs);
    }

    public interface IFoxgloveTopicContractSource
    {
        string FoxgloveLog_Origin { get; }
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    public interface IFoxgloveTopicBusSource
    {
        void FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs);
    }

    public interface IFoxgloveTopicBusDemandSource
    {
        bool FoxgloveLog_HasBusSubscribers(int topicIndex, FoxTopicBus bus);
    }

    public interface IFoxgloveTopicObserverSource
    {
        bool FoxgloveLog_HasObservers(int topicIndex, FoxTopicBus bus);
        void FoxgloveLog_PublishCapturedToObservers(int topicIndex, FoxTopicBus bus, ulong nowNs);
    }

    public interface IFoxgloveTopicSinkSource
    {
        void FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs);
    }

    public interface IFoxglovePublishCaptureSource
    {
        bool FoxgloveLog_BeginCapture(int topicIndex);
        void FoxgloveLog_EndCapture(int topicIndex);
    }

    public interface IFoxglovePublishRecordingSource
    {
        bool FoxgloveLog_IsRecordingReady(int topicIndex, FoxgloveManager manager, out string reason);
        bool FoxgloveLog_RecordCaptured(int topicIndex, FoxgloveManager manager, ulong nowNs, out string reason);
    }

    public interface IFoxglovePublishRecordingPolicySource
    {
        bool FoxgloveLog_ShouldRecord(int topicIndex);
        void FoxgloveLog_MarkRecorded(int topicIndex);
    }

    public interface IFoxRunWebSocketCaptureSource
    {
        void FoxgloveLog_SetWebSocketEncoding(int topicIndex, FoxRunEncoding encoding);
    }

    public interface IFoxglovePublishOriginSource
    {
        bool FoxgloveLog_CanPublishOrigin(int topicIndex, bool explicitTrigger);
    }

    public interface IFoxgloveLogPolicySource
    {
        bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds);
        void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds);
    }
}
#endif
