// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Executable coverage for client-event admission and retirement.

using System;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Manager
{
    public sealed class ClientEventAdmissionStateTests
    {
        [Fact]
        public void InvalidateAndClearClosesAdmissionBeforeLateCallbackCanEnqueue()
        {
            var admission = new ClientEventAdmissionState();
            var queue = new BoundedEventQueue<int>(4, 0, null);
            BoundedEventQueueOverflow overflow;

            admission.Activate(7UL);
            Assert.Equal(
                ClientEventAdmissionResult.Enqueued,
                admission.TryEnqueue(queue, 7UL, 1, out overflow));
            Assert.Equal(1, queue.Count);

            var clearCalls = 0;
            admission.InvalidateAndClear(() =>
            {
                clearCalls++;
                queue.Clear();
            });

            Assert.Equal(1, clearCalls);
            Assert.False(admission.IsAccepting(7UL));
            Assert.Equal(0, queue.Count);
            Assert.Equal(
                ClientEventAdmissionResult.Retired,
                admission.TryEnqueue(queue, 7UL, 2, out overflow));
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void InvalidateAndClearKeepsAdmissionLockedThroughQueueClear()
        {
            var admission = new ClientEventAdmissionState();
            var queue = new BoundedEventQueue<int>(4, 0, null);
            admission.Activate(9UL);

            using var clearEntered = new ManualResetEventSlim(false);
            using var allowClearReturn = new ManualResetEventSlim(false);
            using var enqueueStarted = new ManualResetEventSlim(false);
            using var enqueueFinished = new ManualResetEventSlim(false);
            var clearWaitSucceeded = false;
            var enqueueResult = ClientEventAdmissionResult.Enqueued;

            var invalidationThread = new Thread(() =>
                admission.InvalidateAndClear(() =>
                {
                    queue.Clear();
                    clearEntered.Set();
                    clearWaitSucceeded = allowClearReturn.Wait(TimeSpan.FromSeconds(5));
                })) { IsBackground = true };
            invalidationThread.Start();
            Assert.True(clearEntered.Wait(TimeSpan.FromSeconds(2)));

            var enqueueThread = new Thread(() =>
            {
                enqueueStarted.Set();
                BoundedEventQueueOverflow overflow;
                enqueueResult = admission.TryEnqueue(queue, 9UL, 2, out overflow);
                enqueueFinished.Set();
            }) { IsBackground = true };
            enqueueThread.Start();
            Assert.True(enqueueStarted.Wait(TimeSpan.FromSeconds(2)));

            // A clear callback still owns the admission gate.  The mutant that
            // releases the gate before invoking clearQueues lets this enqueue
            // complete and repopulate the queue.
            Assert.False(enqueueFinished.Wait(TimeSpan.FromMilliseconds(250)));

            allowClearReturn.Set();
            Assert.True(invalidationThread.Join(TimeSpan.FromSeconds(2)));
            Assert.True(enqueueThread.Join(TimeSpan.FromSeconds(2)));
            Assert.True(clearWaitSucceeded);
            Assert.Equal(ClientEventAdmissionResult.Retired, enqueueResult);
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void CompiledManagerDrainRejectsStampedEventFromDifferentEpoch()
        {
            var manager = new FoxgloveManager();
            var delivered = 0;
            manager.OnClientMessage += (client, channel, topic, payload) => delivered++;
            manager.OnClientMessageWithEncoding += (client, channel, topic, encoding, payload) => delivered++;

            manager.TestActivateClientEventGeneration(7UL);
            manager.TestEnqueueMessage(ClientEvent.Message(
                7UL, 1U, 2U, "/stale", "json", new byte[] { 3 }));
            manager.TestSetClientEventGeneration(8UL);
            Debug.Reset();

            manager.TestDrainMessages();

            Assert.Equal(0, delivered);
            Assert.True(Debug.WarningCount > 0);
        }

        [Fact]
        public void CompiledManagerDrainReportsRetirementDrops()
        {
            var manager = new FoxgloveManager();
            manager.TestActivateClientEventGeneration(11UL);
            manager.TestEnqueueMessage(ClientEvent.Message(
                11UL, 1U, 2U, "/queued", "json", new byte[] { 4 }));
            manager.TestSetClientEventGeneration(12UL);
            Debug.Reset();

            manager.TestDrainMessages();

            Assert.True(Debug.WarningCount > 0);
        }

        [Fact]
        public void ReentrantRetirementStopsTheRemainingDrainSnapshot()
        {
            var manager = new FoxgloveManager();
            var legacyCalls = 0;
            var encodedCalls = 0;
            manager.OnClientMessage += (client, channel, topic, payload) =>
            {
                legacyCalls++;
                manager.TestRetireClientEvents();
            };
            manager.OnClientMessageWithEncoding +=
                (client, channel, topic, encoding, payload) => encodedCalls++;
            manager.TestActivateClientEventGeneration(15UL);
            manager.TestEnqueueMessage(ClientEvent.Message(
                15UL, 1U, 2U, "/first", "json", new byte[] { 1 }));
            manager.TestEnqueueMessage(ClientEvent.Message(
                15UL, 1U, 3U, "/later", "json", new byte[] { 2 }));
            Debug.Reset();

            manager.TestDrainMessages();

            Assert.Equal(1, legacyCalls);
            Assert.Equal(1, encodedCalls);
            Assert.Equal(1, Debug.WarningCount);
            Assert.Contains("Dropped 1 queued client event(s)", Debug.LastWarning);
        }

        [Fact]
        public void RetiredAdmissionRejectsLatePayloadAndCountsIt()
        {
            var manager = new FoxgloveManager();
            manager.TestActivateClientEventGeneration(21UL);
            manager.TestRetireClientEvents();
            manager.TestEnqueueMessage(ClientEvent.Message(
                21UL, 1U, 2U, "/late", "json", new byte[] { 5, 6 }));

            Assert.Equal(0, manager.TestMessageQueueCount);
            Assert.Equal(1L, manager.TestRetirementDropCount);
        }

        [Fact]
        public void ClearClientEventsClosesAdmissionForLateCallbacks()
        {
            var manager = new FoxgloveManager();
            manager.TestActivateClientEventGeneration(25UL);
            manager.TestEnqueueMessage(ClientEvent.Message(
                25UL, 1U, 2U, "/queued", "json", new byte[] { 1 }));

            manager.TestClearClientEvents();
            manager.TestEnqueueMessage(ClientEvent.Message(
                25UL, 1U, 3U, "/late", "json", new byte[] { 2, 3 }));

            Assert.Equal(0, manager.TestMessageQueueCount);
            Assert.Equal(2L, manager.TestRetirementDropCount);
        }

        [Fact]
        public void LateRetirementDropKeepsItsOriginGenerationWhenNextSessionActivates()
        {
            var manager = new FoxgloveManager();
            manager.TestActivateClientEventGeneration(41UL);
            manager.TestRetireClientEvents();

            // This callback was captured by the retired session and resumes
            // after the retirement warning has already been flushed.
            manager.TestEnqueueMessage(ClientEvent.Message(
                41UL, 1U, 2U, "/late", "json", new byte[] { 9, 10 }));

            // A new session must not consume the old drop under its own epoch.
            manager.TestActivateClientEventGeneration(42UL);
            Debug.Reset();
            manager.TestDrainMessages();

            Assert.True(Debug.WarningCount > 0);
            Assert.Contains("generation=41", Debug.LastWarning);
        }

        [Fact]
        public void FatalSubscriberReportsOnlyEventsThatNeverStarted()
        {
            var manager = new FoxgloveManager();
            manager.TestActivateClientEventGeneration(31UL);
            manager.OnClientMessage += (client, channel, topic, payload) =>
                throw new OutOfMemoryException("fatal subscriber probe");
            manager.TestEnqueueMessage(ClientEvent.Message(
                31UL, 1U, 2U, "/fatal", "json", new byte[] { 7 }));
            manager.TestEnqueueMessage(ClientEvent.Message(
                31UL, 1U, 3U, "/remainder", "json", new byte[] { 8, 9 }));
            Debug.Reset();

            Assert.Throws<OutOfMemoryException>(() => manager.TestDrainMessages());

            Assert.Equal(1, Debug.WarningCount);
            Assert.Contains("Dropped 1 queued client event(s)", Debug.LastWarning);
            Assert.Contains("payloadBytes=2", Debug.LastWarning);
        }

        [Fact]
        public void SessionSetupCallbackRunsBeforeTransportCanRaiseEvents()
        {
            var transport = new SetupCallbackTransport();
            using var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            var observed = 0;

            runtime.StartWithSessionSetup(
                "c9-start-order",
                "127.0.0.1",
                8765,
                session =>
                {
                    Assert.Same(transport, session.Transport);
                    transport.OnClientConnected += _ => observed++;
                });

            Assert.Equal(1, transport.StartCalls);
            Assert.Equal(1, observed);
            runtime.Stop();
        }

        private sealed class SetupCallbackTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public int StartCalls { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port)
            {
                StartCalls++;
                IsRunning = true;
                OnClientConnected?.Invoke(42U);
            }

            public void Stop() => IsRunning = false;
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() => IsRunning = false;
        }
    }
}
