// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock custom native publisher replacement to Manager publish-session identity.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity2Foxglove.Tests.Ros2ForUnity
{
    public sealed class FoxRunRos2CustomPublisherSessionTrackerTests
    {
        [Fact]
        public void SameSnapshotReferenceRequestsOneRebuildOnly()
        {
            var tracker = new FoxRunRos2CustomPublisherSessionTracker();
            var snapshot = FoxRunPublishSessionPolicy.Disabled(7);

            Assert.True(tracker.Observe(snapshot));
            Assert.False(tracker.Observe(snapshot));
            Assert.False(tracker.AllowsPublishing);
        }

        [Fact]
        public void NewSnapshotReferenceRequestsRebuildEvenWhenValuesMatch()
        {
            var tracker = new FoxRunRos2CustomPublisherSessionTracker();
            var first = FoxRunPublishSessionPolicy.Disabled(7);
            var replacementManagerSnapshot = FoxRunPublishSessionPolicy.Disabled(7);

            Assert.True(tracker.Observe(first));
            Assert.True(tracker.Observe(replacementManagerSnapshot));
            Assert.False(tracker.Observe(replacementManagerSnapshot));
            Assert.False(tracker.AllowsPublishing);
        }

        [Fact]
        public void ManagerAppearanceAndRemovalEachRequestOneRebuild()
        {
            var tracker = new FoxRunRos2CustomPublisherSessionTracker();
            var snapshot = FoxRunPublishSessionPolicy.Disabled(1);

            Assert.True(tracker.AllowsPublishing);
            Assert.False(tracker.Observe(null));
            Assert.True(tracker.Observe(snapshot));
            Assert.False(tracker.AllowsPublishing);
            Assert.False(tracker.Observe(snapshot));
            Assert.True(tracker.Observe(null));
            Assert.True(tracker.AllowsPublishing);
            Assert.False(tracker.Observe(null));
        }

        [Fact]
        public void ActiveManagerSnapshotAllowsPublishing()
        {
            var state = new FoxRunPublishSessionState();
            var snapshot = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                10f,
                FoxRunResolvedQos.SensorData,
                FoxRunResolvedQos.Default);
            var tracker = new FoxRunRos2CustomPublisherSessionTracker();

            Assert.True(tracker.Observe(snapshot));
            Assert.True(tracker.AllowsPublishing);
        }

        [Fact]
        public void StopAllBindingsContinuesAfterOneBindingThrows()
        {
            var stopOrder = new List<string>();
            var failures = new List<Exception>();
            var bindings = new IFoxRunRos2CustomPublisherHostedBinding[]
            {
                new FakeHostedBinding("first", stopOrder, throws: true),
                new FakeHostedBinding("second", stopOrder, throws: false)
            };

            FoxRunRos2CustomPublisherHub.StopAllBindings(bindings, failures.Add);

            Assert.Equal(new[] { "first", "second" }, stopOrder);
            Assert.Single(failures);
            Assert.Equal("first failed", failures[0].Message);
        }

        private sealed class FakeHostedBinding : IFoxRunRos2CustomPublisherHostedBinding
        {
            private readonly List<string> _stopOrder;
            private readonly bool _throws;

            public FakeHostedBinding(string identity, List<string> stopOrder, bool throws)
            {
                Identity = identity;
                _stopOrder = stopOrder;
                _throws = throws;
            }

            public string Identity { get; }
            public int SourceInstanceId => 0;
            public bool IsStopped { get; private set; }

            public void Stop()
            {
                _stopOrder.Add(Identity);
                IsStopped = true;
                if (_throws)
                    throw new InvalidOperationException(Identity + " failed");
            }
        }
    }
}
#endif
