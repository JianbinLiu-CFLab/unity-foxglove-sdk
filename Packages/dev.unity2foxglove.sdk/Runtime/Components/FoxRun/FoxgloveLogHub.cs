// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Provider-neutral runtime scheduler for generated FoxRun sources.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Util;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs);
    }

    public interface IFoxgloveTopicContractSource
    {
        string FoxgloveLog_Origin { get; }
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    public interface IFoxgloveTopicBusSource
    {
        void FoxgloveLog_PublishToBus(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicBusDemandSource
    {
        bool FoxgloveLog_HasBusSubscribers(
            int topicIndex,
            FoxTopicBus bus);
    }

    public interface IFoxgloveTopicObserverSource
    {
        bool FoxgloveLog_HasObservers(
            int topicIndex,
            FoxTopicBus bus);
        void FoxgloveLog_PublishCapturedToObservers(
            int topicIndex,
            FoxTopicBus bus,
            ulong nowNs);
    }

    public interface IFoxgloveTopicSinkSource
    {
        void FoxgloveLog_PublishToSinks(
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong nowNs);
    }

    public interface IFoxglovePublishCaptureSource
    {
        bool FoxgloveLog_BeginCapture(int topicIndex);
        void FoxgloveLog_EndCapture(int topicIndex);
    }

    /// <summary>
    /// Optional generated MCAP seam for a topic whose selected publish
    /// transports do not include the built-in WebSocket transport. When
    /// WebSocket is selected, its ordinary Manager publish already records the
    /// same channel and this seam must not be invoked.
    /// </summary>
    public interface IFoxglovePublishRecordingSource
    {
        bool FoxgloveLog_IsRecordingReady(
            int topicIndex,
            FoxgloveManager manager,
            out string reason);

        bool FoxgloveLog_RecordCaptured(
            int topicIndex,
            FoxgloveManager manager,
            ulong nowNs,
            out string reason);
    }

    /// <summary>
    /// Optional generated policy seam for hidden recording. It lets a source
    /// remember that the current logical Change revision has already been
    /// accepted by MCAP while a selected live Provider remains pending.
    /// </summary>
    public interface IFoxglovePublishRecordingPolicySource
    {
        bool FoxgloveLog_ShouldRecord(int topicIndex);
        void FoxgloveLog_MarkRecorded(int topicIndex);
    }

    /// <summary>
    /// Optional generated seam used to freeze an inherited WebSocket encoding
    /// before capture. Other Providers own their wire encoding independently.
    /// </summary>
    public interface IFoxRunWebSocketCaptureSource
    {
        void FoxgloveLog_SetWebSocketEncoding(
            int topicIndex,
            FoxRunEncoding encoding);
    }

    public interface IFoxglovePublishOriginSource
    {
        bool FoxgloveLog_CanPublishOrigin(
            int topicIndex,
            bool explicitTrigger);
    }

    public interface IFoxgloveLogPolicySource
    {
        bool FoxgloveLog_ShouldPublish(
            int topicIndex,
            double nowSeconds);
        void FoxgloveLog_MarkPublished(
            int topicIndex,
            double nowSeconds);
    }

    [AddComponentMenu("")]
    /// <remarks>Phase192 source sections: LifecycleAndRegistration.cs, PublishingAndEncoding.cs, DeferredSourceChanges.cs, ScanningAndDiagnostics.cs</remarks>
    public sealed partial class FoxgloveLogHub : MonoBehaviour
    {
        private const float ManagerSearchIntervalSeconds = 3f;
        private const float ScanIntervalSeconds = 2f;
        private const float MaxScanIntervalSeconds = 30f;
        private const int MaximumReportedFailures = 256;

        private static FoxgloveLogHub _instance;
        private static readonly object PendingGate = new object();
        private static readonly List<IFoxgloveLogSource> Pending =
            new List<IFoxgloveLogSource>();
        private static readonly HashSet<IFoxgloveLogSource> PendingSet =
            new HashSet<IFoxgloveLogSource>();

        [SerializeField]
        private bool _enableFallbackSceneScan = true;

        private readonly Dictionary<IFoxgloveLogSource, SourceState>
            _sources =
                new Dictionary<IFoxgloveLogSource, SourceState>();
        private readonly List<IFoxgloveLogSource> _stale =
            new List<IFoxgloveLogSource>();
        private readonly List<IFoxgloveLogSource> _deferredAdds =
            new List<IFoxgloveLogSource>();
        private readonly List<IFoxgloveLogSource> _deferredRemoves =
            new List<IFoxgloveLogSource>();
        private readonly List<DeferredTrigger> _deferredTriggers =
            new List<DeferredTrigger>();
        private readonly FoxTopicBus _topicBus = new FoxTopicBus();
        private readonly FoxTopicSinkRouter _sinkRouter =
            new FoxTopicSinkRouter();
        private readonly HashSet<string> _reportedFailures =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _reportedFailureOrder =
            new Queue<string>();

        private FoxgloveManager _manager;
        private bool _iterating;
        private float _managerSearchCooldown;
        private float _scanTimer;
        private float _scanInterval = ScanIntervalSeconds;

        public FoxTopicBus TopicBus => _topicBus;
        public FoxTopicSinkRouter TopicSinkRouter => _sinkRouter;
    }
}
