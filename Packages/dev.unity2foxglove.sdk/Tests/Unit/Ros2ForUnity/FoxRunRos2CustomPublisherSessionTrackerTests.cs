// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock custom native publisher replacement to Manager publish-session identity.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity2Foxglove.Tests.Ros2ForUnity
{
    public sealed class FoxRunRos2CustomPublisherSessionTrackerTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FailedPublisherBindingRetentionFollowsCleanupOwnership(bool cleanupPending)
        {
            Assert.Equal(
                cleanupPending,
                FoxRunRos2CustomPublisherHub.ShouldTrackFailedBinding(cleanupPending));
        }

        [Fact]
        public void FailedPublisherBindingIsRetainedWhenCleanupIsPending()
        {
            var bindings = new List<IFoxRunRos2CustomPublisherHostedBinding>();
            var existing = new HashSet<string>(StringComparer.Ordinal);
            var binding = new FakeHostedBinding("pending", new List<string>(), throws: false, cleanupPending: true);

            Assert.True(FoxRunRos2CustomPublisherHub.TrackFailedBinding(
                bindings,
                existing,
                binding.Identity,
                binding));
            Assert.Same(binding, Assert.Single(bindings));
            Assert.Contains(binding.Identity, existing);
        }

        [Fact]
        public void CompletedFailedPublisherBindingIsNotRetained()
        {
            var bindings = new List<IFoxRunRos2CustomPublisherHostedBinding>();
            var existing = new HashSet<string>(StringComparer.Ordinal);
            var binding = new FakeHostedBinding("completed", new List<string>(), throws: false);

            Assert.False(FoxRunRos2CustomPublisherHub.TrackFailedBinding(
                bindings,
                existing,
                binding.Identity,
                binding));
            Assert.Empty(bindings);
            Assert.Empty(existing);
        }

        [Fact]
        public void NativeSurfaceFailureSuppressesRepeatedRegistrationUntilSessionReset()
        {
            var terminalFailures = new HashSet<string>(StringComparer.Ordinal);

            Assert.False(
                FoxRunRos2CustomPublisherHub.ShouldSuppressTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract"));
            Assert.True(
                FoxRunRos2CustomPublisherHub.RecordTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract",
                    FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable));
            Assert.True(
                FoxRunRos2CustomPublisherHub.ShouldSuppressTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract"));
            Assert.False(
                FoxRunRos2CustomPublisherHub.RecordTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract",
                    FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable));

            terminalFailures.Clear();

            Assert.False(
                FoxRunRos2CustomPublisherHub.ShouldSuppressTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract"));
        }

        [Fact]
        public void OrdinaryPublisherFailureDoesNotSuppressTheNextRegistration()
        {
            var terminalFailures = new HashSet<string>(StringComparer.Ordinal);

            Assert.False(
                FoxRunRos2CustomPublisherHub.RecordTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract",
                    FoxRunRos2RegistrationError.PublisherBackendFailure));
            Assert.False(
                FoxRunRos2CustomPublisherHub.ShouldSuppressTerminalRegistrationFailure(
                    terminalFailures,
                    "source|contract"));
        }

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
        public void TerminalRegistrationFailuresRemainFailedAfterBindingRemoval()
        {
            Assert.Equal(
                1,
                FoxRunRos2CustomPublisherHub.CountObservedContracts(
                    observedContracts: 0,
                    bindingCount: 0,
                    terminalFailureCount: 1));
            Assert.Equal(
                1,
                FoxRunRos2CustomPublisherHub.CountFailedContracts(
                    bindingCount: 0,
                    readyContracts: 0,
                    terminalFailureCount: 1));
            Assert.Equal(
                3,
                FoxRunRos2CustomPublisherHub.CountFailedContracts(
                    bindingCount: 1,
                    readyContracts: 0,
                    terminalFailureCount: 2));
            Assert.Equal(
                2,
                FoxRunRos2CustomPublisherHub.CountFailedContracts(
                    bindingCount: 1,
                    readyContracts: 0,
                    terminalFailureCount: 1));
            var terminalFailures = new HashSet<string>(StringComparer.Ordinal)
            {
                "terminal-1",
                "terminal-2"
            };
            var bindings = new IFoxRunRos2CustomPublisherHostedBinding[]
            {
                new FakeHostedBinding("terminal-1", new List<string>(), throws: false),
                new FakeHostedBinding("live", new List<string>(), throws: false)
            };
            var nativeDemand = new HashSet<string>(StringComparer.Ordinal)
            {
                "live"
            };
            Assert.Equal(
                1,
                FoxRunRos2CustomPublisherHub.CountUnboundTerminalFailures(
                    terminalFailures,
                    bindings));
            Assert.Equal(
                1,
                FoxRunRos2CustomPublisherHub.CountUnobservedTerminalFailures(
                    terminalFailures,
                    bindings,
                    nativeDemand));
            var status = FoxRunRos2CustomPublisherHub.BuildTransportStatus(
                sessionActive: true,
                stopping: false,
                scanCompleted: true,
                observedContracts: 1,
                readyContracts: 0,
                failedContracts: 1);
            Assert.Equal(FoxRunTransportObservedState.Failed, status.State);
            Assert.Equal(1, status.ObservedContractCount);
            Assert.Equal(1, status.FailedContractCount);
        }

        [Fact]
        public void ScanAndCaptureStatusUsesIdentityUnionForRetainedCleanupAndSuppressedDemand()
        {
            var hub = new FoxRunRos2CustomPublisherHub();
            var retainedBinding = new FakeHostedBinding(
                "retained-binding",
                new List<string>(),
                throws: false,
                cleanupPending: true);
            retainedBinding.Stop();
            GetPrivateField<List<IFoxRunRos2CustomPublisherHostedBinding>>(hub, "_bindings")
                .Add(retainedBinding);

            var topic = new FoxTopicContract(
                "/phase181/status-union",
                "phase181.Status",
                "json",
                "phase181.Status",
                "status-union",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);
            var bus = new FoxTopicBus();
            const string origin = "phase181-status-union";
            Assert.True(bus.Register(topic, origin).Accepted);
            var source = new LifecyclePublisherSource(CreateStatusUnionContract(), topic, origin)
            {
                isActiveAndEnabled = true
            };
            UnityEngine.Object.RegisterForTests(source);
            var identity = source.GetInstanceID() + "|" + source.Contract.Id;
            GetPrivateField<HashSet<string>>(hub, "_terminalRegistrationFailures")
                .Add(identity);

            var previousRmw = Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION");
            try
            {
                Environment.SetEnvironmentVariable(
                    "RMW_IMPLEMENTATION",
                    "rmw_fastrtps_cpp");
                FoxRunRos2CustomTypesupportCatalogRegistry.ResetForTests();
                FoxRunRos2CustomTypesupportCatalogRegistry.Register(
                    new LifecycleTypesupportCatalog());
                hub.SetProviderSessionActive(true);
                InvokeScanAndReconcile(hub, bus);

                var status = hub.CaptureTransportStatus();

                Assert.Equal(2, status.ObservedContractCount);
                Assert.Equal(0, status.ReadyContractCount);
                Assert.Equal(2, status.FailedContractCount);
                Assert.Equal(FoxRunTransportObservedState.Failed, status.State);
            }
            finally
            {
                FoxRunRos2CustomTypesupportCatalogRegistry.ResetForTests();
                UnityEngine.Object.ResetForTests();
                Environment.SetEnvironmentVariable("RMW_IMPLEMENTATION", previousRmw);
            }
        }

        [Fact]
        public void CapacityRejectedBindingDemandRemainsObservable()
        {
            var demand = new HashSet<string>(StringComparer.Ordinal);
            Assert.True(FoxRunRos2CustomPublisherHub.ObserveBindingDemand(
                "admitted",
                4095,
                4096,
                demand));
            Assert.False(FoxRunRos2CustomPublisherHub.ObserveBindingDemand(
                "capacity-rejected",
                4096,
                4096,
                demand));
            Assert.Equal(
                new[] { "admitted", "capacity-rejected" },
                demand.OrderBy(value => value, StringComparer.Ordinal));
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
            private readonly bool _cleanupPending;

            public FakeHostedBinding(string identity, List<string> stopOrder, bool throws, bool cleanupPending = false)
            {
                Identity = identity;
                _stopOrder = stopOrder;
                _throws = throws;
                _cleanupPending = cleanupPending;
            }

            public string Identity { get; }
            public int SourceInstanceId => 0;
            public string Topic => string.Empty;
            public bool IsStopped { get; private set; }
            public bool CleanupPending => _cleanupPending;
            public bool CleanupRetryExhausted => false;
            public bool CleanupRetryFatal => false;

            public void Stop()
            {
                _stopOrder.Add(Identity);
                IsStopped = true;
                if (_throws)
                    throw new InvalidOperationException(Identity + " failed");
            }

            public bool TryRetryCleanup() => true;
            public bool TryForceRetryCleanup() => true;
            public FoxRunTransportPublishResult PublishGenerated(
                IFoxRunGeneratedMemberAccess member,
                ulong logTimeNs)
                => FoxRunTransportPublishResult.Rejected("not used");
        }

        private static FoxRunRos2CustomPublisherContract CreateStatusUnionContract()
            => new FoxRunRos2CustomPublisherContract(
                "status-union-contract",
                "/phase181/status-union",
                "Phase181.StatusSource",
                "Status",
                "unity2foxglove_foxrun_interfaces_v1/msg/Status",
                "unity2foxglove_foxrun_interfaces_v1/msg/StatusEnvelope",
                "dev.unity2foxglove.foxrun.ros2.interfaces",
                "unity2foxglove_foxrun_interfaces_v1",
                1,
                "b218c59b845ddf1cc3f23cb0e45f1dceff336a7693cf5648b6851aca3386e7c5",
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                FoxRunFlow.Publish,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false);

        private static void InvokeScanAndReconcile(
            FoxRunRos2CustomPublisherHub hub,
            FoxTopicBus bus)
        {
            var method = typeof(FoxRunRos2CustomPublisherHub).GetMethod(
                "ScanAndReconcile",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(
                hub,
                new object[]
                {
                    bus,
                    FoxRunResolvedQos.Default,
                    FoxRunRos2RouteEndpoint.WebSocket,
                    FoxRunRos2RouteEndpoint.R2fu
                });
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field.GetValue(target);
        }

        private sealed class LifecyclePublisherSource :
            UnityEngine.MonoBehaviour,
            IFoxRunRos2CustomPublisherSource,
            IFoxgloveLogSource,
            IFoxgloveTopicContractSource
        {
            private readonly FoxTopicContract _topic;
            private readonly string _origin;

            public LifecyclePublisherSource(
                FoxRunRos2CustomPublisherContract contract,
                FoxTopicContract topic,
                string origin)
            {
                Contract = contract;
                _topic = topic;
                _origin = origin;
            }

            public FoxRunRos2CustomPublisherContract Contract { get; }
            public int FoxRunRos2CustomPublisherCount => 1;
            public int FoxgloveLog_TopicCount => 1;
            public string FoxgloveLog_Origin => _origin;

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
                => default;

            public FoxTopicContract FoxgloveLog_GetContract(int index)
                => index == 0 ? _topic : null;

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs) { }

            public void FoxRunRos2RegisterCustomPublishers(
                IFoxRunRos2CustomPublisherRegistrar registrar)
                => registrar.Register<TestDto, TestEnvelope>(Contract, null, null);
        }

        private sealed class TestDto { }

        private sealed class TestEnvelope : ROS2.Message, IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class LifecycleTypesupportCatalog :
            IFoxRunRos2CustomTypesupportCatalog
        {
            public string SourcePackageId => "dev.unity2foxglove.foxrun.ros2.interfaces";
            public string RosPackageName => "unity2foxglove_foxrun_interfaces_v1";
            public int InterfaceRevision => 1;
            public string InterfaceDigest =>
                "b218c59b845ddf1cc3f23cb0e45f1dceff336a7693cf5648b6851aca3386e7c5";
            public string BaseRuntimePackageId =>
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
            public string Platform => "win64";
            public IReadOnlyList<string> SupportedRmwImplementations =>
                new[] { "rmw_fastrtps_cpp" };
            public IReadOnlyList<FoxRunRos2CustomTypesupportTypeMapEntry> TypeMap =>
                new[]
                {
                    new FoxRunRos2CustomTypesupportTypeMapEntry(
                        "unity2foxglove_foxrun_interfaces_v1/msg/Status",
                        "unity2foxglove_foxrun_interfaces_v1.msg.Status")
                };
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
            public string Topic => string.Empty;
            public bool IsStopped { get; private set; }
            public bool CleanupPending => false;
            public bool CleanupRetryExhausted => false;
            public bool CleanupRetryFatal => false;

            public void Stop()
            {
                _stopOrder.Add(Identity);
                IsStopped = true;
                throw new OutOfMemoryException(Identity + " fatal");
            }

            public bool TryRetryCleanup() => true;
            public bool TryForceRetryCleanup() => true;
            public FoxRunTransportPublishResult PublishGenerated(
                IFoxRunGeneratedMemberAccess member,
                ulong logTimeNs)
                => FoxRunTransportPublishResult.Rejected("not used");
        }
    }
}
#endif
