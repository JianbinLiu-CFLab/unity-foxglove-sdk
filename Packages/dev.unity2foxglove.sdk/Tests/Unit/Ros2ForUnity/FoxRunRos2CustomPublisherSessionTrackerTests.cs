// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock custom native publisher replacement to Manager publish-session identity.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Linq;
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
                new[]
                {
                    new FoxRunTransportId(
                        FoxRunRos2TransportProvider.IdValue)
                },
                FoxRunEncoding.JSON,
                10f,
                FoxRunDeliveryPolicy.ProviderDefault);
            var tracker = new FoxRunRos2CustomPublisherSessionTracker();

            Assert.True(tracker.Observe(snapshot));
            Assert.True(tracker.AllowsPublishing);
        }

        [Fact]
        public void LegacyComponentNativeSwitchCannotStopFoxRunPublishDemand()
        {
            Assert.False(FoxRunRos2CustomPublisherHub.ShouldStopFoxRunPublishing(
                publishSessionAllows: true,
                legacyComponentNativeOutputEnabled: false,
                bridgeLifecycleIsShuttingDown: false));
            Assert.True(FoxRunRos2CustomPublisherHub.ShouldStopFoxRunPublishing(
                publishSessionAllows: false,
                legacyComponentNativeOutputEnabled: true,
                bridgeLifecycleIsShuttingDown: false));
            Assert.True(FoxRunRos2CustomPublisherHub.ShouldStopFoxRunPublishing(
                publishSessionAllows: true,
                legacyComponentNativeOutputEnabled: true,
                bridgeLifecycleIsShuttingDown: true));
        }

        [Fact]
        public void PublisherStatusCannotReportReadyBeforeObservedDemandIsBound()
        {
            var beforeScan = FoxRunRos2CustomPublisherHub.BuildTransportStatus(
                sessionActive: true,
                stopping: false,
                scanCompleted: false,
                observedContracts: 0,
                readyContracts: 0,
                failedContracts: 0);
            Assert.Equal(FoxRunTransportObservedState.Starting, beforeScan.State);
            Assert.Equal("R2FU001", beforeScan.Diagnostic?.Code);

            var missingBinding = FoxRunRos2CustomPublisherHub.BuildTransportStatus(
                sessionActive: true,
                stopping: false,
                scanCompleted: true,
                observedContracts: 1,
                readyContracts: 0,
                failedContracts: 0);
            Assert.Equal(FoxRunTransportObservedState.Starting, missingBinding.State);
            Assert.Equal(1, missingBinding.ObservedContractCount);
            Assert.Equal(0, missingBinding.ReadyContractCount);
            Assert.Equal("R2FU001", missingBinding.Diagnostic?.Code);

            var noDemand = FoxRunRos2CustomPublisherHub.BuildTransportStatus(
                sessionActive: true,
                stopping: false,
                scanCompleted: true,
                observedContracts: 0,
                readyContracts: 0,
                failedContracts: 0);
            Assert.Equal(FoxRunTransportObservedState.Ready, noDemand.State);
            Assert.Null(noDemand.Diagnostic);
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

        [Fact]
        public void StaleRemovalContinuesAfterFatalStopAndClearsBookkeeping()
        {
            var stopOrder = new List<string>();
            var first = new FatalHostedBinding("first", stopOrder);
            var second = new FakeHostedBinding("second", stopOrder, throws: false);
            var bindings = new List<IFoxRunRos2CustomPublisherHostedBinding>
            {
                first,
                second
            };
            var stale = bindings.ToArray();
            var existing = new HashSet<string>(StringComparer.Ordinal)
            {
                first.Identity,
                second.Identity
            };

            var thrown = Assert.Throws<OutOfMemoryException>(() =>
                FoxRunRos2CustomPublisherHub.StopStaleBindings(
                    bindings,
                    stale,
                    existing,
                    _ => { }));

            Assert.Equal("first fatal", thrown.Message);
            Assert.Equal(new[] { "first", "second" }, stopOrder);
            Assert.Empty(bindings);
            Assert.Empty(existing);
        }

        [Fact]
        public void StartupPrimarySurvivesFatalCleanupAndAllUnboundStagesRun()
        {
            var boundCleanupCalls = 0;
            var boundPrimary = new OutOfMemoryException("startup-bound-primary");
            var boundThrown = Assert.Throws<OutOfMemoryException>(() =>
                FoxRunRos2CustomPublisherHub.CleanupFailedStartupAndRethrow(
                    boundPrimary,
                    () =>
                    {
                        boundCleanupCalls++;
                        throw new InsufficientMemoryException("stop-secondary");
                    },
                    null,
                    null));
            Assert.Same(boundPrimary, boundThrown);
            Assert.Equal(1, boundCleanupCalls);

            var unboundEvents = new List<string>();
            var unboundPrimary = new OutOfMemoryException("startup-unbound-primary");
            var unboundThrown = Assert.Throws<OutOfMemoryException>(() =>
                FoxRunRos2CustomPublisherHub.CleanupFailedStartupAndRethrow(
                    unboundPrimary,
                    null,
                    () =>
                    {
                        unboundEvents.Add("end-origin");
                        throw new InsufficientMemoryException("origin-secondary");
                    },
                    () =>
                    {
                        unboundEvents.Add("release-node");
                        throw new InsufficientMemoryException("release-secondary");
                    }));

            Assert.Same(unboundPrimary, unboundThrown);
            Assert.Equal(
                new[] { "end-origin", "release-node" },
                unboundEvents);
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

        private sealed class FatalHostedBinding : IFoxRunRos2CustomPublisherHostedBinding
        {
            private readonly List<string> _stopOrder;

            public FatalHostedBinding(string identity, List<string> stopOrder)
            {
                Identity = identity;
                _stopOrder = stopOrder;
            }

            public string Identity { get; }
            public int SourceInstanceId => 0;
            public bool IsStopped { get; private set; }

            public void Stop()
            {
                _stopOrder.Add(Identity);
                IsStopped = true;
                throw new OutOfMemoryException(Identity + " fatal");
            }
        }
    }
}
#endif
