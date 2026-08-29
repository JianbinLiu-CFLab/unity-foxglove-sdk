// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
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
        public void RetirementStopsRemainingEventSurfacesAndLaterEvents()
        {
            var state = new ClientEventDispatchState();
            var live = true;
            var legacyCalls = 0;
            var encodedCalls = 0;
            var laterCalls = 0;
            var failures = new List<Exception>();

            Action<uint> legacy = _ =>
            {
                legacyCalls++;
                live = false;
            };
            Action<uint> encoded = _ => encodedCalls++;
            Action<uint> later = _ => laterCalls++;

            // Keep the RED behavioral assertion runnable on the unmodified tree:
            // before the guarded dispatcher exists, the existing unguarded Invoke
            // calls deliver all three surfaces after the first callback retires.
            var guarded = typeof(ClientEventDispatchState).GetMethod(
                "InvokeIfLive",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic);
            if (guarded == null)
            {
                state.Invoke(legacy, 1U, failures.Add);
                state.Invoke(encoded, 1U, failures.Add);
                state.Invoke(later, 2U, failures.Add);
            }
            else
            {
                var result = (bool)guarded.Invoke(
                    state,
                    new object[]
                    {
                        (Func<bool>)(() => live),
                        (Action)(() => state.Invoke(legacy, 1U, failures.Add)),
                        (Action)(() => state.Invoke(encoded, 1U, failures.Add)),
                    });
                Assert.False(result);
                if (result)
                    state.Invoke(later, 2U, failures.Add);
            }

            Assert.Equal(1, legacyCalls);
            Assert.Equal(0, encodedCalls);
            Assert.Equal(0, laterCalls);
            Assert.Empty(failures);
        }
    }
}
