// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Manager
{
    public sealed class ClientEventDispatchStateTests
    {
        [Fact]
        public void ThrowingSubscriberDoesNotDiscardLaterSubscribersOrEvents()
        {
            var state = new ClientEventDispatchState();
            var received = new List<uint>();
            var failures = new List<Exception>();
            Action<uint> subscribers = _ =>
                throw new InvalidOperationException("fixture");
            subscribers += received.Add;

            state.Invoke(subscribers, 11U, failures.Add);
            state.Invoke(subscribers, 12U, failures.Add);

            Assert.Equal(new uint[] { 11U, 12U }, received);
            Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(failures[0]);
        }

        [Fact]
        public void BothMessageEventShapesIsolateSubscriberFailures()
        {
            var state = new ClientEventDispatchState();
            var legacyCalls = 0;
            var encodedCalls = 0;
            var failures = new List<Exception>();
            Action<uint, uint, string, byte[]> legacy =
                (_, _, _, _) => throw new InvalidOperationException("legacy");
            legacy += (_, _, _, _) => legacyCalls++;
            Action<uint, uint, string, string, byte[]> encoded =
                (_, _, _, _, _) => throw new InvalidOperationException("encoded");
            encoded += (_, _, _, _, _) => encodedCalls++;

            state.Invoke(
                legacy,
                1U,
                2U,
                "/topic",
                new byte[] { 3 },
                failures.Add);
            state.Invoke(
                encoded,
                1U,
                2U,
                "/topic",
                "json",
                new byte[] { 3 },
                failures.Add);

            Assert.Equal(1, legacyCalls);
            Assert.Equal(1, encodedCalls);
            Assert.Single(failures);
        }

        [Fact]
        public void MessageSurfacesRemainAtomicWhenRetirementOccursDuringFirstSurface()
        {
            var state = new ClientEventDispatchState();
            var retired = false;
            var legacyCalls = 0;
            var encodedCalls = 0;
            var failures = new List<Exception>();

            Action<uint, uint, string, byte[]> legacy = (clientId, channelId, topic, payload) =>
            {
                Assert.Equal(1U, clientId);
                Assert.Equal(2U, channelId);
                Assert.Equal("/topic", topic);
                Assert.Equal(new byte[] { 3 }, payload);
                legacyCalls++;
                retired = true;
            };
            Action<uint, uint, string, string, byte[]> encoded =
                (clientId, channelId, topic, encoding, payload) =>
            {
                Assert.True(retired);
                Assert.Equal(1U, clientId);
                Assert.Equal(2U, channelId);
                Assert.Equal("/topic", topic);
                Assert.Equal("json", encoding);
                Assert.Equal(new byte[] { 3 }, payload);
                encodedCalls++;
            };

            state.InvokeMessage(
                legacy,
                encoded,
                1U,
                2U,
                "/topic",
                "json",
                new byte[] { 3 },
                failures.Add);

            Assert.Equal(1, legacyCalls);
            Assert.Equal(1, encodedCalls);
            Assert.Empty(failures);
        }

        [Fact]
        public void GenerationGateRejectsEventsFromRetiredSessions()
        {
            Assert.True(ClientEventGenerationGate.IsCurrent(7UL, 7UL));
            Assert.False(ClientEventGenerationGate.IsCurrent(6UL, 7UL));
        }

        [Fact]
        public void ManagerDrainUsesStampedEpochAndAvoidsPerEventLivenessClosures()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            var serverSource = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Assert.Contains(
                "ClientEventGenerationGate.IsCurrent(evt.Generation, generation)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "Volatile.Read(ref _connectionState.ChannelSessionGeneration)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "_clientEventDispatchState.InvokeMessage(",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "WarnClientEventRetirementDrop(discardedEvents, discardedBytes, generation)",
                source,
                StringComparison.Ordinal);
            Assert.Contains("drainIndex = _clientEventDrainScratch.Count", source, StringComparison.Ordinal);
            Assert.DoesNotContain("InvokeIfLive", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Func<bool>", source, StringComparison.Ordinal);
            Assert.Contains(
                "session.OnClientMessageWithEncoding += _clientMessageForwarder",
                serverSource,
                StringComparison.Ordinal);
            Assert.Contains(
                "_runtimeForwarderSession = session",
                serverSource,
                StringComparison.Ordinal);
            Assert.Contains(
                "session ??= _runtimeForwarderSession ?? _runtime?.CleanupSession",
                serverSource,
                StringComparison.Ordinal);
            Assert.Contains(
                "private void ClearRuntimeForwarderSessionIfDetached()",
                serverSource,
                StringComparison.Ordinal);
            Assert.Contains(
                "ClearRuntimeForwarderSessionIfDetached();\n            firstFailure?.Throw();",
                serverSource,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "_runtime.Session.OnClientMessageWithEncoding += _clientMessageForwarder",
                serverSource,
                StringComparison.Ordinal);
            Assert.Contains(
                "private void CleanupStartupAfterFailure()\n        {\n            RetireClientEventIngress();",
                serverSource,
                StringComparison.Ordinal);
        }
    }
}
