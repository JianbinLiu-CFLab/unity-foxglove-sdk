// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Fixtures
// Purpose: Minimal partial owner used to execute the manager client-event
//          drain in the .NET test surface without compiling the full Unity
//          MonoBehaviour.

using System;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        // The client-event-focused surface is also visible to the C4 Roslyn
        // declaration probes through the trusted assembly references. Keep
        // the generated publication contract available without compiling the
        // full Unity manager implementation into this unit-test lane.
        public bool IsRunning { get; set; }
        public bool SuppressLivePublishersForReplay { get; set; }
        public ulong NowNs { get; set; }

        public void PublishJson<T>(
            string topic,
            string schemaName,
            T payload,
            ulong logTimeNs) { }

        public void PublishProto<T>(
            string topic,
            string schemaName,
            T payload,
            ulong logTimeNs) { }

        public void PublishFoxRunJsonBytes(
            string topic,
            string schemaName,
            byte[] payload,
            ulong logTimeNs) { }

        public void PublishFoxRunMessagePackBytes(
            string topic,
            byte[] payload,
            ulong logTimeNs) { }

        public bool TryPrepareFoxRunMessagePackRecording(
            string topic,
            out uint channelId,
            out string reason)
        {
            channelId = 0;
            reason = string.Empty;
            return false;
        }

        public bool TryPublishFoxRunMessagePackRecording(
            string topic,
            byte[] payload,
            ulong logTimeNs,
            out string reason)
        {
            reason = string.Empty;
            return false;
        }

        private readonly ConnectionRuntimeState _connectionState =
            new ConnectionRuntimeState(1);
        private readonly WarningDebounceState _warningDebounceState =
            new WarningDebounceState();

        public event Action<uint, uint, string, byte[]> OnClientMessage;
        public event Action<uint, uint, string, string, byte[]> OnClientMessageWithEncoding;
        public event Action<uint> OnClientConnected;
        public event Action<uint> OnClientDisconnected;

        internal void TestActivateClientEventGeneration(ulong generation)
        {
            _connectionState.ChannelSessionGeneration = generation;
            _clientEventAdmission.Activate(generation);
        }

        private void AdvanceChannelSessionGeneration()
            => _connectionState.AdvanceChannelSessionGeneration();

        internal void TestSetClientEventGeneration(ulong generation)
            => _connectionState.ChannelSessionGeneration = generation;

        internal void TestRetireClientEvents()
            => RetireClientEventIngress();

        internal void TestClearClientEvents()
            => ClearClientEvents();

        internal void TestEnqueueMessage(ClientEvent evt)
            => EnqueueClientMessageEvent(evt);

        internal void TestEnqueueLifecycle(ClientEvent evt)
            => EnqueueClientLifecycleEvent(evt);

        internal void TestDrainMessages()
            => DrainClientEventQueue(_clientMessageEvents);

        internal void TestDrainLifecycle()
            => DrainClientEventQueue(_clientLifecycleEvents);

        internal int TestMessageQueueCount => _clientMessageEvents.Count;
        internal int TestLifecycleQueueCount => _clientLifecycleEvents.Count;

        internal long TestRetirementDropCount
            => _clientEventAdmission.TotalRetirementDropCount;
    }
}

namespace UnityEngine.Scripting
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class PreserveAttribute : Attribute { }
}
