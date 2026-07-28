// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Regression coverage for independent live WebSocket and typed native-bus fanout.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxgloveLogHubNativeFanoutTests
    {
        [Fact]
        public void TriggerDispatchesTypedBusWhenWebSocketIsStopped()
        {
            var fixture = new HubFixture(isRunning: false);
            fixture.Subscribe();

            Assert.True(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(1, fixture.Source.BusPublishes);
            Assert.Equal(1, fixture.BusDeliveries);
            Assert.Equal(1, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void TriggerDispatchesLiveAndTypedBusExactlyOnceWhenBothRoutesAreAvailable()
        {
            var fixture = new HubFixture(isRunning: true);
            fixture.Subscribe();

            Assert.True(fixture.Trigger());
            Assert.Equal(1, fixture.Source.LivePublishes);
            Assert.Equal(1, fixture.Source.BusPublishes);
            Assert.Equal(1, fixture.BusDeliveries);
            Assert.Equal(1, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void TriggerDuringReplaySuppressesBothLiveAndTypedBusRoutes()
        {
            var fixture = new HubFixture(isRunning: true, suppressLivePublishersForReplay: true);
            fixture.Subscribe();

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(0, fixture.Source.BusPublishes);
            Assert.Equal(0, fixture.BusDeliveries);
            Assert.Equal(0, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void RecoverableLiveFailureDoesNotBlockTypedBusDispatch()
        {
            var fixture = new HubFixture(isRunning: true);
            fixture.Source.ThrowOnLivePublish = true;
            fixture.Subscribe();

            Assert.True(fixture.Trigger());
            Assert.Equal(1, fixture.Source.LivePublishes);
            Assert.Equal(1, fixture.Source.BusPublishes);
            Assert.Equal(1, fixture.BusDeliveries);
            Assert.Equal(1, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void TriggerDoesNotAdvancePolicyWhenNeitherLiveNorTypedBusRouteNeedsOutput()
        {
            var fixture = new HubFixture(isRunning: false);

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(0, fixture.Source.BusPublishes);
            Assert.Equal(0, fixture.Source.MarkPublishedCalls);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(99)]
        public void ExplicitTriggerRejectsNonTriggerRetiredAndUnknownPolicies(int policyValue)
        {
            var fixture = new HubFixture(isRunning: true);
            fixture.Source.Policy = (FoxRunPolicy)policyValue;
            fixture.Subscribe();

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
            Assert.Equal(0, fixture.Source.BusPublishes);
            Assert.Equal(0, fixture.BusDeliveries);
            Assert.Equal(0, fixture.Source.MarkPublishedCalls);
        }

        [Fact]
        public void WarmedWebSocketOnlyTriggerDoesNotAllocatePerDispatch()
        {
            var fixture = new WebSocketOnlyHubFixture();
            Assert.True(fixture.Trigger());
            fixture.Source.Reset();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 64; index++)
            {
                if (!fixture.Trigger())
                    throw new InvalidOperationException("Expected warmed live-only trigger to dispatch.");
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(64, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ExplicitQosWithInheritedAllFoxgloveProfileFailsBeforeLivePublish()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Foxglove,
                hasExplicitQos: true);

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ExplicitQosDoesNotTreatDisabledInheritedNativeSubscriptionAsRos2Direction()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Foxglove,
                hasExplicitQos: true,
                flow: FoxRunFlow.PublishAndSubscribe,
                disabledNativeSubscription: true);

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ExplicitQosWithInheritedAllFoxgloveProfileNeverRegistersExternalSink()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Foxglove,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);

            Assert.True(fixture.AddSource());
            Assert.Equal(0, sink.RegisterCalls);
        }

        [Fact]
        public void ExplicitQosWithInheritedNativeProfileRegistersExternalSink()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Ros2Native,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);

            Assert.True(fixture.AddSource());
            Assert.Equal(1, sink.RegisterCalls);
        }

        [Fact]
        public void PublishSessionChangesDisposeAndRecreateExternalSinkContract()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Ros2Native,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);
            Assert.True(fixture.AddSource());
            Assert.Equal(1, sink.RegisterCalls);

            fixture.ChangePublishTargets(FoxRunEndpoint.Foxglove);

            Assert.Equal(1, sink.UnregisterCalls);
            Assert.Equal(1, sink.RegisterCalls);

            fixture.ChangePublishTargets(FoxRunEndpoint.Ros2Native);

            Assert.Equal(1, sink.UnregisterCalls);
            Assert.Equal(2, sink.RegisterCalls);
        }

        [Fact]
        public void EndingPublishSessionDisposesWithoutRecreatingExternalSinkContract()
        {
            var fixture = new WebSocketOnlyHubFixture(
                FoxRunEndpoint.Ros2Native,
                hasExplicitQos: true);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);
            Assert.True(fixture.AddSource());
            Assert.Equal(1, sink.RegisterCalls);

            fixture.EndPublishSession();

            Assert.Equal(1, sink.UnregisterCalls);
            Assert.Equal(1, sink.RegisterCalls);
        }

        [Fact]
        public void InvalidInheritedWriterDoesNotGhostOwnTopicBeforeValidWriter()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Foxglove);
            var invalid = new TargetAwareSource(
                targets: 0,
                topic: OwnershipHubFixture.SharedTopic,
                origin: "invalid-a",
                hasExplicitTargets: false,
                hasExplicitQos: true);
            var valid = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: OwnershipHubFixture.SharedTopic,
                origin: "valid-b",
                hasExplicitQos: true);

            Assert.True(fixture.AddSource(invalid));
            Assert.True(fixture.AddSource(valid));

            Assert.False(fixture.Trigger(invalid));
            Assert.True(fixture.Trigger(valid));
        }

        [Fact]
        public void InvalidatedInheritedWriterReleasesTopicForExplicitWriter()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Ros2Native);
            var inherited = new TargetAwareSource(
                targets: 0,
                topic: OwnershipHubFixture.SharedTopic,
                origin: "inherited-a",
                hasExplicitTargets: false,
                hasExplicitQos: true);
            var explicitWriter = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: OwnershipHubFixture.SharedTopic,
                origin: "explicit-b",
                hasExplicitQos: true);

            Assert.True(fixture.AddSource(inherited));
            Assert.True(fixture.AddSource(explicitWriter));
            Assert.True(fixture.Trigger(inherited));
            Assert.False(fixture.Trigger(explicitWriter));

            fixture.ChangePublishTargets(FoxRunEndpoint.Foxglove);

            Assert.False(fixture.Trigger(inherited));
            Assert.True(fixture.Trigger(explicitWriter));
        }

        [Fact]
        public void EndingPublishSessionClearsPreviouslyReadyTargetStatus()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Ros2Native);
            var source = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: OwnershipHubFixture.StatusTopic,
                origin: "status-owner");
            Assert.True(fixture.AddSource(source));
            Assert.True(fixture.Trigger(source));
            Assert.True(fixture.TryGetStatus(source, out var ready));
            Assert.Equal(FoxRunPublishTargetStatus.Ready, ready.Status);

            fixture.EndPublishSession();

            Assert.False(fixture.TryGetStatus(source, out _));
        }

        [Fact]
        public void EndingPublishSessionClearsTargetStatusWhenSinkUnregisterIsFatal()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Ros2Native);
            var sink = new FatalTopicUnregisterSink(
                OwnershipHubFixture.StatusTopic);
            fixture.AddSink(sink);
            var source = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: OwnershipHubFixture.StatusTopic,
                origin: "fatal-status-owner");
            Assert.True(fixture.AddSource(source));
            Assert.True(fixture.Trigger(source));
            Assert.True(fixture.TryGetStatus(source, out var ready));
            Assert.Equal(FoxRunPublishTargetStatus.Ready, ready.Status);

            var fatal = Assert.Throws<OutOfMemoryException>(
                () => fixture.EndPublishSession());

            Assert.Equal("first-topic-unregister", fatal.Message);
            Assert.Contains(OwnershipHubFixture.StatusTopic, sink.UnregisteredTopics);
            Assert.False(fixture.TryGetStatus(source, out _));
        }

        [Fact]
        public void MultiWriterReconcileAndSingleRemovalKeepOneStableSinkEndpoint()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Ros2Native);
            var sink = new LifecycleRecordingSink();
            fixture.AddSink(sink);
            var first = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: OwnershipHubFixture.MultiWriterTopic,
                origin: "multi-a",
                writerPolicy: FoxTopicWriterPolicy.MultiWriter);
            var second = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: OwnershipHubFixture.MultiWriterTopic,
                origin: "multi-b",
                writerPolicy: FoxTopicWriterPolicy.MultiWriter);
            Assert.True(fixture.AddSource(first));
            Assert.True(fixture.AddSource(second));
            Assert.Equal(1, sink.RegisterCalls);

            fixture.ChangePublishTargets(FoxRunEndpoint.Ros2Native);
            fixture.ChangePublishTargets(FoxRunEndpoint.Ros2Native);
            Assert.Equal(1, sink.RegisterCalls);
            Assert.Equal(0, sink.UnregisterCalls);

            fixture.RemoveSource(first);
            Assert.Equal(0, sink.UnregisterCalls);
            Assert.True(fixture.Trigger(second));

            fixture.RemoveSource(second);
            Assert.Equal(1, sink.UnregisterCalls);
        }

        [Fact]
        public void FatalFirstTopicUnregisterStillCleansLaterTopicsAndRemovesSource()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Ros2Native);
            var sink = new FatalTopicUnregisterSink(
                MultiTopicContractSource.FirstTopic);
            fixture.AddSink(sink);
            var source = new MultiTopicContractSource();
            Assert.True(fixture.AddSource(source));

            var invocation = Assert.Throws<TargetInvocationException>(
                () => fixture.RemoveSource(source));

            var fatal = Assert.IsType<OutOfMemoryException>(
                invocation.InnerException);
            Assert.Equal("first-topic-unregister", fatal.Message);
            Assert.Equal(
                new[]
                {
                    MultiTopicContractSource.FirstTopic,
                    MultiTopicContractSource.SecondTopic
                },
                sink.UnregisteredTopics);

            var replacement = new TargetAwareSource(
                FoxRunEndpoint.Ros2Native,
                topic: MultiTopicContractSource.FirstTopic,
                origin: "post-fatal-replacement");
            Assert.True(fixture.AddSource(replacement));
            Assert.True(fixture.Trigger(replacement));
        }

        [Fact]
        public void FatalLaterTopicAdmissionRollsBackWholeSourceAndAllowsRetry()
        {
            var fixture = new OwnershipHubFixture(FoxRunEndpoint.Ros2Native);
            var sink = new FatalTopicRegisterSink(
                MultiTopicContractSource.SecondTopic);
            fixture.AddSink(sink);
            var source = new MultiTopicContractSource();

            var invocation = Assert.Throws<TargetInvocationException>(
                () => fixture.AddSource(source));
            var fatal = Assert.IsType<OutOfMemoryException>(
                invocation.InnerException);

            Assert.Equal("second-topic-register", fatal.Message);
            Assert.Equal(0, fixture.RegisteredSourceCount);
            Assert.Equal(0, fixture.SourceRegistrationOrderCount);
            Assert.False(fixture.IsBusRegistered(source, 0));
            Assert.False(fixture.IsBusRegistered(source, 1));
            Assert.Equal(
                new[]
                {
                    MultiTopicContractSource.SecondTopic,
                    MultiTopicContractSource.FirstTopic
                },
                sink.UnregisteredTopics);

            sink.FailRegistration = false;
            Assert.True(fixture.AddSource(source));
            Assert.Equal(1, fixture.RegisteredSourceCount);
            Assert.Equal(1, fixture.SourceRegistrationOrderCount);
            Assert.True(fixture.IsBusRegistered(source, 0));
            Assert.True(fixture.IsBusRegistered(source, 1));
        }

        [Fact]
        public void ScheduledPublishUsesFrozenProfileRateWhenDeclarationOmitsHz()
        {
            var fixture = new ScheduledRateFixture(
                inheritedRateHz: 2f,
                declaredRateHz: 10f,
                hasExplicitHz: false);

            Assert.True(fixture.Tick(0d));
            Assert.False(fixture.Tick(0.25d));
            Assert.True(fixture.Tick(0.5d));
            Assert.Equal(2, fixture.Source.LivePublishes);
        }

        [Fact]
        public void ScheduledPublishKeepsExplicitRateInsteadOfProfileRate()
        {
            var fixture = new ScheduledRateFixture(
                inheritedRateHz: 2f,
                declaredRateHz: 4f,
                hasExplicitHz: true);

            Assert.True(fixture.Tick(0d));
            Assert.True(fixture.Tick(0.25d));
            Assert.Equal(2, fixture.Source.LivePublishes);
        }

        [Fact]
        public void TargetAwareTriggerCapturesOnceSharesTimestampAndContinuesAfterTargetFailure()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Foxglove
                | FoxRunEndpoint.Ros2Native
                | FoxRunEndpoint.Ros2Bridge);
            fixture.Source.Sink(FoxRunEndpoint.Foxglove).PublishException =
                new InvalidOperationException("expected live failure");

            Assert.True(fixture.Trigger());

            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(1, fixture.Source.EndCaptureCount);
            Assert.Equal(0, fixture.Source.LegacyPublishCount);
            Assert.Equal(
                new[]
                {
                    FoxRunEndpoint.Foxglove,
                    FoxRunEndpoint.Ros2Native,
                    FoxRunEndpoint.Ros2Bridge
                },
                fixture.Source.PublishOrder);
            var successfulDeliveries = new[]
            {
                Assert.Single(fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Deliveries),
                Assert.Single(fixture.Source.Sink(FoxRunEndpoint.Ros2Bridge).Deliveries)
            };
            Assert.All(
                successfulDeliveries,
                delivery =>
                {
                    Assert.Same(fixture.Source.LastCapture, delivery.Sample);
                    Assert.Equal(TargetAwareHubFixture.TimestampNs, delivery.TimestampNs);
                });
            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Degraded, status.Status);
            Assert.Equal(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                status.SucceededTargets);
            Assert.Equal(FoxRunEndpoint.Foxglove, status.FailedTargets);
        }

        [Fact]
        public void FoxgloveOnlyTargetStillPublishesOneOrdinaryObserverSideChannel()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Foxglove);
            fixture.Source.ObserverDemand = true;

            Assert.True(fixture.Trigger());

            Assert.Equal(1, fixture.Source.ObserverPublishes);
            Assert.Equal(
                new[] { FoxRunEndpoint.Foxglove },
                fixture.Source.PublishOrder);
            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(1, fixture.Source.EndCaptureCount);
        }

        [Fact]
        public void BridgeOnlyTargetStillPublishesObserverWithoutCallingNative()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Bridge);
            fixture.Source.ObserverDemand = true;

            Assert.True(fixture.Trigger());

            Assert.Equal(1, fixture.Source.ObserverPublishes);
            Assert.Equal(
                new[] { FoxRunEndpoint.Ros2Bridge },
                fixture.Source.PublishOrder);
            Assert.DoesNotContain(
                FoxRunEndpoint.Ros2Native,
                fixture.Source.PublishOrder);
        }

        [Fact]
        public void UnavailableNativeTargetStillPublishesObserverWithoutClearingFailure()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Native);
            fixture.Source.ObserverDemand = true;
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Ready = false;

            Assert.False(fixture.Trigger());

            Assert.Equal(1, fixture.Source.ObserverPublishes);
            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(1, fixture.Source.EndCaptureCount);
            Assert.Empty(fixture.Source.PublishOrder);
            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, status.Status);
        }

        [Fact]
        public void UnavailableTargetReadinessIsStatusWithoutExceptionWarning()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Native);
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Ready = false;

            Assert.False(fixture.Trigger());

            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, status.Status);
            Assert.Equal(FoxRunEndpoint.Ros2Native, status.FailedTargets);
            Assert.Equal(0, fixture.WarningCount);
        }

        [Fact]
        public void FoxgloveAndNativeTargetsPublishOrdinaryObserverOnlyOnce()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native);
            fixture.Source.ObserverDemand = true;

            Assert.True(fixture.Trigger());

            Assert.Equal(1, fixture.Source.ObserverPublishes);
            Assert.Equal(
                new[]
                {
                    FoxRunEndpoint.Foxglove,
                    FoxRunEndpoint.Ros2Native
                },
                fixture.Source.PublishOrder);
        }

        [Fact]
        public void FatalTargetFailureRemainsPrimaryWhenCaptureCleanupAlsoFails()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).PublishException =
                new OutOfMemoryException("target-primary");
            fixture.Source.EndCaptureException =
                new OutOfMemoryException("cleanup-secondary");

            var invocation = Assert.Throws<TargetInvocationException>(
                () => fixture.Trigger());

            var fatal = Assert.IsType<OutOfMemoryException>(
                invocation.InnerException);
            Assert.Equal("target-primary", fatal.Message);
            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(1, fixture.Source.EndCaptureCount);
        }

        [Fact]
        public void FatalTargetFailureClearsPreviouslyReadyStatus()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            Assert.True(fixture.Trigger());
            Assert.True(fixture.TryGetStatus(out var ready));
            Assert.Equal(FoxRunPublishTargetStatus.Ready, ready.Status);
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).PublishException =
                new OutOfMemoryException("later-target-primary");

            var invocation = Assert.Throws<TargetInvocationException>(
                () => fixture.Trigger());
            var fatal = Assert.IsType<OutOfMemoryException>(
                invocation.InnerException);

            Assert.Equal("later-target-primary", fatal.Message);
            Assert.False(fixture.TryGetStatus(out _));
        }

        [Fact]
        public void TargetAwareTriggerWithAllTargetsUnavailableDoesNotCaptureOrFallback()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge);
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Ready = false;
            fixture.Source.Sink(FoxRunEndpoint.Ros2Bridge).Ready = false;

            Assert.False(fixture.Trigger());

            Assert.Equal(0, fixture.Source.BeginCaptureCount);
            Assert.Equal(0, fixture.Source.EndCaptureCount);
            Assert.Equal(0, fixture.Source.LegacyPublishCount);
            Assert.Empty(fixture.Source.PublishOrder);
            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, status.Status);
            Assert.Equal((FoxRunEndpoint)0, status.SucceededTargets);
            Assert.Equal(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                status.FailedTargets);
        }

        [Fact]
        public void TargetAwareTriggerWithAllReadyPublishesFailingIsUnavailableWithoutFallback()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge);
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).PublishResult = false;
            fixture.Source.Sink(FoxRunEndpoint.Ros2Bridge).PublishResult = false;

            Assert.False(fixture.Trigger());

            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(1, fixture.Source.EndCaptureCount);
            Assert.Equal(0, fixture.Source.LegacyPublishCount);
            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, status.Status);
            Assert.Equal((FoxRunEndpoint)0, status.SucceededTargets);
            Assert.Equal(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                status.FailedTargets);
        }

        [Fact]
        public void RecordingSuccessDoesNotReportTriggerSuccessWhenSelectedLiveTargetsFail()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            fixture.Source.RecordingReady = true;
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).PublishResult = false;

            Assert.False(fixture.Trigger());

            Assert.Equal(1, fixture.Source.RecordCount);
            Assert.Equal(0, fixture.Source.MarkPublishedCount);
            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, status.Status);
            Assert.Equal((FoxRunEndpoint)0, status.SucceededTargets);
            Assert.Equal(FoxRunEndpoint.Ros2Native, status.FailedTargets);
        }

        [Fact]
        public void RecordingSuccessDoesNotConsumeChangeSampleAndRecoveredTargetRetriesIt()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Native,
                FoxRunPolicy.Change);
            fixture.Source.RecordingReady = true;
            fixture.Source.Value = 184;
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).PublishResult = false;

            Assert.False(fixture.Tick(0d));
            Assert.Equal(1, fixture.Source.RecordCount);
            Assert.Equal(0, fixture.Source.MarkPublishedCount);

            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).PublishResult = true;

            Assert.True(fixture.Tick(0.01d));
            var delivery = Assert.Single(
                fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Deliveries);
            Assert.Equal(184, Assert.IsType<CapturedValue>(delivery.Sample).Value);
            Assert.Equal(2, fixture.Source.RecordCount);
            Assert.Equal(1, fixture.Source.MarkPublishedCount);
        }

        [Fact]
        public void RejectedDuplicateSourceCannotScheduleOrOverwriteTheAcceptedRoute()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            var duplicate = fixture.AddSource(
                new TargetAwareSource(
                    FoxRunEndpoint.Ros2Native,
                    fixture.Source.TopicInfo.Topic,
                    origin: "duplicate-writer"));

            Assert.False(fixture.Trigger(duplicate));
            Assert.True(fixture.Trigger());
            Assert.Empty(
                duplicate.Sink(FoxRunEndpoint.Ros2Native).Deliveries);
            Assert.Single(
                fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Deliveries);
        }

        [Fact]
        public void RejectedDuplicateAcquiresTopicAndSinkOwnershipAfterAcceptedWriterLeaves()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            var replacement = fixture.AddSource(
                new TargetAwareSource(
                    FoxRunEndpoint.Ros2Native,
                    fixture.Source.TopicInfo.Topic,
                    origin: "replacement-writer"));

            Assert.False(fixture.Trigger(replacement));

            fixture.RemoveSource(fixture.Source);

            Assert.True(fixture.Trigger(replacement));
            Assert.Single(
                replacement.Sink(FoxRunEndpoint.Ros2Native).Deliveries);
        }

        [Fact]
        public void CaptureFailureWithRecordingDemandIsNotRetried()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            fixture.Source.ThrowOnBeginCapture = true;
            fixture.Source.RecordingReady = true;

            Assert.False(fixture.Trigger());

            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(0, fixture.Source.EndCaptureCount);
            Assert.Equal(0, fixture.Source.RecordCount);
            Assert.Equal(0, fixture.Source.LegacyPublishCount);
            Assert.True(fixture.TryGetStatus(out var status));
            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, status.Status);
        }

        [Fact]
        public void RecordingOnlyDemandUsesTheSameCaptureAndTimestampAsTheLiveTarget()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            fixture.Source.RecordingReady = true;

            Assert.True(fixture.Trigger());

            var delivery = Assert.Single(
                fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Deliveries);
            Assert.Equal(1, fixture.Source.BeginCaptureCount);
            Assert.Equal(1, fixture.Source.RecordCount);
            Assert.Same(delivery.Sample, fixture.Source.RecordedSample);
            Assert.Equal(delivery.TimestampNs, fixture.Source.RecordedTimestampNs);
            Assert.Equal(TargetAwareHubFixture.TimestampNs, delivery.TimestampNs);
        }

        [Fact]
        public void OrdinaryObserverMutationCannotChangeEarlierRecordingSnapshot()
        {
            var fixture = new TargetAwareHubFixture(
                FoxRunEndpoint.Ros2Native);
            fixture.Source.Value = 184;
            fixture.Source.RecordingReady = true;
            fixture.Source.ObserverDemand = true;
            fixture.Source.ObserverMutationValue = 999;

            Assert.True(fixture.Trigger());

            Assert.Equal(184, fixture.Source.RecordedValue);
            Assert.Equal(
                999,
                Assert.IsType<CapturedValue>(
                    fixture.Source.LastCapture).Value);
            Assert.Equal(1, fixture.Source.ObserverPublishes);
            Assert.Equal(1, fixture.Source.RecordCount);
        }

        [Fact]
        public void EmptyRecordingReasonIsNormalNoDemandAndDoesNotCreateAHubFailure()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).Ready = false;
            fixture.Source.Sink(FoxRunEndpoint.Ros2Native).ReadinessReason = string.Empty;
            fixture.Source.RecordingReady = false;
            fixture.Source.RecordingReason = string.Empty;

            Assert.False(fixture.Trigger());
            Assert.Equal(0, fixture.Source.BeginCaptureCount);
            Assert.Equal(0, fixture.WarningCount);
        }

        [Fact]
        public void ActiveStatusAccessorReadsTheSingletonWithoutSceneDiscovery()
        {
            var fixture = new TargetAwareHubFixture(FoxRunEndpoint.Ros2Native);
            Assert.True(fixture.Trigger());
            var instanceField = typeof(FoxgloveLogHub).GetField(
                "_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(instanceField);
            var previous = instanceField.GetValue(null);

            try
            {
                instanceField.SetValue(null, fixture.Hub);

                Assert.True(FoxgloveLogHub.TryGetActivePublishTargetStatus(
                    fixture.Source,
                    0,
                    out var status));
                Assert.Equal(FoxRunPublishTargetStatus.Ready, status.Status);
                Assert.Equal(FoxRunEndpoint.Ros2Native, status.SucceededTargets);
            }
            finally
            {
                instanceField.SetValue(null, previous);
            }
        }

        [Fact]
        public void NativeQosRemainsFrozenForSourcesAddedMidSessionAndRecapturesAfterRestart()
        {
            var fixture = new NativeQosFreezeHubFixture(FoxRunResolvedQos.SensorData);
            var first = fixture.AddSource("/phase184/qos-freeze/first");

            fixture.ConfiguredNativeQos = FoxRunResolvedQos.SystemDefault;
            var second = fixture.AddSource("/phase184/qos-freeze/second");

            Assert.True(fixture.Trigger(first));
            Assert.True(fixture.Trigger(second));
            Assert.Equal(FoxRunResolvedQos.SensorData, first.LastNativeQos);
            Assert.Equal(FoxRunResolvedQos.SensorData, second.LastNativeQos);

            fixture.RestartSession(FoxRunResolvedQos.SystemDefault);

            Assert.True(fixture.Trigger(first));
            Assert.True(fixture.Trigger(second));
            Assert.Equal(FoxRunResolvedQos.SystemDefault, first.LastNativeQos);
            Assert.Equal(FoxRunResolvedQos.SystemDefault, second.LastNativeQos);
        }

        private sealed class HubFixture
        {
            private static readonly FieldInfo ManagerField = typeof(FoxgloveLogHub).GetField(
                "_mgr",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private readonly FoxgloveManager _manager;

            public HubFixture(bool isRunning, bool suppressLivePublishersForReplay = false)
            {
                Assert.NotNull(ManagerField);
                Assert.NotNull(TriggerMethod);
                _manager = new FoxgloveManager
                {
                    IsRunning = isRunning,
                    SuppressLivePublishersForReplay = suppressLivePublishersForReplay,
                    NowNs = 123_456_789UL
                };
                ManagerField.SetValue(_hub, _manager);
                Source = new FanoutSource();
            }

            public FanoutSource Source { get; }
            public int BusDeliveries { get; private set; }

            public void Subscribe()
            {
                _hub.TopicBus.Subscribe<int>(FanoutSource.Topic, _ => BusDeliveries++);
            }

            public bool Trigger()
            {
                return (bool)TriggerMethod.Invoke(
                    _hub,
                    new object[] { Source, 0 });
            }
        }

        private sealed class WebSocketOnlyHubFixture
        {
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo AddSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly Func<IFoxgloveLogSource, int, bool> _trigger;
            private readonly FoxgloveLogHub _hub;
            private readonly FoxgloveManager _manager;

            public WebSocketOnlyHubFixture(
                FoxRunEndpoint publishTargets = FoxRunEndpoint.Foxglove,
                bool hasExplicitQos = false,
                FoxRunFlow flow = FoxRunFlow.Publish,
                bool disabledNativeSubscription = false)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(TriggerMethod);
                Assert.NotNull(AddSourceMethod);
                _hub = new FoxgloveLogHub();
                _manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = 123_456_789UL,
                    ActiveFoxRunPublishTargets = publishTargets,
                };
                if (disabledNativeSubscription)
                {
                    _manager.ActiveFoxRunSubscriptionSource = FoxRunEndpoint.Ros2Native;
                    _manager.ActiveFoxRunSubscriptionSessionPolicy =
                        FoxRunSubscriptionSessionPolicy.Disabled(0);
                }
                SetManagerMethod.Invoke(
                    _hub,
                    new object[] { _manager });
                _trigger = (Func<IFoxgloveLogSource, int, bool>)Delegate.CreateDelegate(
                    typeof(Func<IFoxgloveLogSource, int, bool>),
                    _hub,
                    TriggerMethod);
                Source = new WebSocketOnlySource(hasExplicitQos, flow);
            }

            public WebSocketOnlySource Source { get; }

            public bool Trigger()
                => _trigger(Source, 0);

            public bool AddSource()
                => (bool)AddSourceMethod.Invoke(_hub, new object[] { Source });

            public void AddSink(IFoxTopicSink sink)
                => _hub.TopicSinkRouter.AddSink(sink);

            public void ChangePublishTargets(FoxRunEndpoint targets)
            {
                _manager.ActiveFoxRunPublishTargets = targets;
                _manager.RaiseFoxRunPublishSessionChanged();
            }

            public void EndPublishSession()
            {
                _manager.ActiveFoxRunPublishSessionPolicy =
                    FoxRunPublishSessionPolicy.Disabled(1);
                _manager.RaiseFoxRunPublishSessionChanged();
            }
        }

        private sealed class ScheduledRateFixture
        {
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo ScheduledMethod = typeof(FoxgloveLogHub).GetMethod(
                "TryPublishScheduledTopic",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo AddSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private readonly FoxgloveLogTopicInfo _topic;
            private FixedRatePublishState _timer;

            public ScheduledRateFixture(
                float inheritedRateHz,
                float declaredRateHz,
                bool hasExplicitHz)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(ScheduledMethod);
                Assert.NotNull(AddSourceMethod);
                var manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = 123_456_789UL,
                    ActiveFoxRunDefaultPublishRateHz = inheritedRateHz
                };
                SetManagerMethod.Invoke(_hub, new object[] { manager });
                _topic = new FoxgloveLogTopicInfo(
                    ScheduledSource.Topic,
                    declaredRateHz,
                    FoxRunPolicy.FixedRate,
                    0f,
                    FoxRunFlow.Publish,
                    declaredSource: 0,
                    hasExplicitSource: false,
                    declaredTargets: 0,
                    hasExplicitTargets: false,
                    hasExplicitQos: false,
                    hasExplicitHz: hasExplicitHz);
                Source = new ScheduledSource(_topic);
                Assert.True((bool)AddSourceMethod.Invoke(
                    _hub,
                    new object[] { Source }));
            }

            public ScheduledSource Source { get; }

            public bool Tick(double nowSeconds)
            {
                var arguments = new object[]
                {
                    Source,
                    _topic,
                    0,
                    _timer,
                    123_456_789UL,
                    nowSeconds
                };
                var published = (bool)ScheduledMethod.Invoke(_hub, arguments);
                _timer = (FixedRatePublishState)arguments[3];
                return published;
            }
        }

        private sealed class OwnershipHubFixture
        {
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo AddSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo RemoveSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "RemoveSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo TimersField = typeof(FoxgloveLogHub).GetField(
                "_timers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo SourceRegistrationOrderField = typeof(FoxgloveLogHub).GetField(
                "_sourceRegistrationOrder",
                BindingFlags.Instance | BindingFlags.NonPublic);

            internal const string SharedTopic = "/phase184/ownership/shared";
            internal const string StatusTopic = "/phase184/ownership/status";
            internal const string MultiWriterTopic = "/phase184/ownership/multi";

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private readonly FoxgloveManager _manager;

            public OwnershipHubFixture(FoxRunEndpoint targets)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(AddSourceMethod);
                Assert.NotNull(TriggerMethod);
                Assert.NotNull(RemoveSourceMethod);
                Assert.NotNull(TimersField);
                Assert.NotNull(SourceRegistrationOrderField);
                _manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = TargetAwareHubFixture.TimestampNs,
                    ActiveFoxRunPublishTargets = targets,
                    ActiveFoxRunPublishEncoding = FoxRunEncoding.JSON,
                    ActiveFoxRunSubscriptionSource = FoxRunEndpoint.Foxglove,
                    ActiveFoxRunSubscriptionEncoding = FoxRunEncoding.JSON,
                    DefaultFoxRunNativePublishQos = FoxRunResolvedQos.Default,
                    ActiveFoxRunBridgePublishQos = FoxRunResolvedQos.Default,
                };
                SetManagerMethod.Invoke(_hub, new object[] { _manager });
            }

            public bool AddSource(IFoxgloveLogSource source)
                => (bool)AddSourceMethod.Invoke(_hub, new object[] { source });

            public bool Trigger(TargetAwareSource source)
                => (bool)TriggerMethod.Invoke(_hub, new object[] { source, 0 });

            public void RemoveSource(IFoxgloveLogSource source)
                => RemoveSourceMethod.Invoke(_hub, new object[] { source });

            public void AddSink(IFoxTopicSink sink)
                => _hub.TopicSinkRouter.AddSink(sink);

            public int RegisteredSourceCount
                => ((System.Collections.IDictionary)TimersField.GetValue(_hub)).Count;

            public int SourceRegistrationOrderCount
                => ((System.Collections.ICollection)SourceRegistrationOrderField.GetValue(_hub)).Count;

            public bool IsBusRegistered(
                MultiTopicContractSource source,
                int topicIndex)
                => _hub.TopicBus.IsRegistered(
                    source.FoxgloveLog_GetContract(topicIndex),
                    source.FoxgloveLog_Origin);

            public bool TryGetStatus(
                TargetAwareSource source,
                out FoxRunPublishDispatchResult result)
                => _hub.TryGetPublishTargetStatus(source, 0, out result);

            public void ChangePublishTargets(FoxRunEndpoint targets)
            {
                _manager.ActiveFoxRunPublishTargets = targets;
                _manager.RaiseFoxRunPublishSessionChanged();
            }

            public void EndPublishSession()
            {
                _manager.ActiveFoxRunPublishSessionPolicy =
                    FoxRunPublishSessionPolicy.Disabled(1);
                _manager.RaiseFoxRunPublishSessionChanged();
            }
        }

        private sealed class TargetAwareHubFixture
        {
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo AddSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo ScheduledMethod = typeof(FoxgloveLogHub).GetMethod(
                "TryPublishScheduledTopic",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo RemoveSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "RemoveSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo WarningsField = typeof(FoxgloveLogHub).GetField(
                "_warnedSourceFailures",
                BindingFlags.Instance | BindingFlags.NonPublic);

            internal const ulong TimestampNs = 184_000_004UL;

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private FixedRatePublishState _timer;

            public TargetAwareHubFixture(
                FoxRunEndpoint targets,
                FoxRunPolicy policy = FoxRunPolicy.Trigger)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(TriggerMethod);
                Assert.NotNull(AddSourceMethod);
                Assert.NotNull(ScheduledMethod);
                Assert.NotNull(RemoveSourceMethod);
                Assert.NotNull(WarningsField);
                var manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = TimestampNs,
                    ActiveFoxRunPublishTargets = FoxRunEndpoint.Foxglove,
                    ActiveFoxRunPublishEncoding = FoxRunEncoding.JSON,
                    ActiveFoxRunSubscriptionSource = FoxRunEndpoint.Foxglove,
                    ActiveFoxRunSubscriptionEncoding = FoxRunEncoding.JSON,
                    DefaultFoxRunNativePublishQos = FoxRunResolvedQos.Default,
                    ActiveFoxRunBridgePublishQos = FoxRunResolvedQos.Default
                };
                SetManagerMethod.Invoke(_hub, new object[] { manager });
                Source = new TargetAwareSource(targets, policy: policy);
                Assert.True((bool)AddSourceMethod.Invoke(_hub, new object[] { Source }));
            }

            public TargetAwareSource Source { get; }
            public FoxgloveLogHub Hub => _hub;

            public int WarningCount
            {
                get
                {
                    var warnings = WarningsField.GetValue(_hub);
                    var count = warnings?.GetType().GetProperty("Count");
                    Assert.NotNull(count);
                    return (int)count.GetValue(warnings);
                }
            }

            public bool Trigger()
                => (bool)TriggerMethod.Invoke(_hub, new object[] { Source, 0 });

            public bool Trigger(TargetAwareSource source)
                => (bool)TriggerMethod.Invoke(_hub, new object[] { source, 0 });

            public TargetAwareSource AddSource(TargetAwareSource source)
            {
                Assert.True((bool)AddSourceMethod.Invoke(
                    _hub,
                    new object[] { source }));
                return source;
            }

            public void RemoveSource(TargetAwareSource source)
                => RemoveSourceMethod.Invoke(_hub, new object[] { source });

            public bool Tick(double nowSeconds)
            {
                var arguments = new object[]
                {
                    Source,
                    Source.TopicInfo,
                    0,
                    _timer,
                    TimestampNs,
                    nowSeconds
                };
                var published = (bool)ScheduledMethod.Invoke(_hub, arguments);
                _timer = (FixedRatePublishState)arguments[3];
                return published;
            }

            public bool TryGetStatus(out FoxRunPublishDispatchResult result)
                => _hub.TryGetPublishTargetStatus(Source, 0, out result);
        }

        private sealed class NativeQosFreezeHubFixture
        {
            private static readonly MethodInfo SetManagerMethod = typeof(FoxgloveLogHub).GetMethod(
                "SetManager",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo TriggerMethod = typeof(FoxgloveLogHub).GetMethod(
                "TriggerSource",
                BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly MethodInfo AddSourceMethod = typeof(FoxgloveLogHub).GetMethod(
                "AddSourceNow",
                BindingFlags.Instance | BindingFlags.NonPublic);

            private readonly FoxgloveLogHub _hub = new FoxgloveLogHub();
            private readonly FoxgloveManager _manager;
            private ulong _generation = 1;

            public NativeQosFreezeHubFixture(FoxRunResolvedQos initialQos)
            {
                Assert.NotNull(SetManagerMethod);
                Assert.NotNull(TriggerMethod);
                Assert.NotNull(AddSourceMethod);
                _manager = new FoxgloveManager
                {
                    IsRunning = true,
                    NowNs = TargetAwareHubFixture.TimestampNs,
                    ActiveFoxRunPublishTargets = FoxRunEndpoint.Ros2Native,
                    ActiveFoxRunPublishEncoding = FoxRunEncoding.JSON,
                    ActiveFoxRunSubscriptionSource = FoxRunEndpoint.Foxglove,
                    ActiveFoxRunSubscriptionEncoding = FoxRunEncoding.JSON,
                    DefaultFoxRunNativePublishQos = initialQos,
                    ActiveFoxRunBridgePublishQos = FoxRunResolvedQos.Default,
                    ActiveFoxRunPublishSessionPolicy = ActivePolicy(_generation, initialQos)
                };
                SetManagerMethod.Invoke(_hub, new object[] { _manager });
            }

            public FoxRunResolvedQos ConfiguredNativeQos
            {
                set => _manager.DefaultFoxRunNativePublishQos = value;
            }

            public TargetAwareSource AddSource(string topic)
            {
                var source = new TargetAwareSource(FoxRunEndpoint.Ros2Native, topic);
                Assert.True((bool)AddSourceMethod.Invoke(_hub, new object[] { source }));
                return source;
            }

            public bool Trigger(TargetAwareSource source)
                => (bool)TriggerMethod.Invoke(_hub, new object[] { source, 0 });

            public void RestartSession(FoxRunResolvedQos qos)
            {
                _manager.ActiveFoxRunPublishSessionPolicy =
                    FoxRunPublishSessionPolicy.Disabled(++_generation);
                _manager.RaiseFoxRunPublishSessionChanged();
                _manager.ActiveFoxRunPublishSessionPolicy =
                    ActivePolicy(++_generation, qos);
                _manager.RaiseFoxRunPublishSessionChanged();
            }

            private static FoxRunPublishSessionPolicy ActivePolicy(
                ulong generation,
                FoxRunResolvedQos qos)
                => new FoxRunPublishSessionPolicy(
                    generation,
                    sessionActive: true,
                    defaultTargets: FoxRunEndpoint.Ros2Native,
                    foxgloveEncoding: FoxRunEncoding.JSON,
                    defaultPublishRateHz: 10f,
                    nativeRos2Qos: qos,
                    bridgeRos2Qos: FoxRunResolvedQos.Default);
        }

        private sealed class FanoutSource : IFoxgloveLogSource, IFoxgloveTopicBusSource, IFoxgloveTopicBusDemandSource, IFoxgloveLogPolicySource
        {
            internal const string Topic = "/phase181/native-fanout";
            private static readonly FoxTopicContract Contract = new FoxTopicContract(
                Topic,
                "phase181.Fanout",
                "json",
                "phase181.Fanout",
                "phase181-fanout",
                FoxTopicVisibility.Exported,
                FoxTopicWriterPolicy.SingleWriter);

            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }
            public int BusPublishes { get; private set; }
            public int MarkPublishedCalls { get; private set; }
            public bool ThrowOnLivePublish { get; set; }
            public FoxRunPolicy Policy { get; set; } = FoxRunPolicy.Trigger;

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                Assert.Equal(0, index);
                return new FoxgloveLogTopicInfo(Topic, 30f, Policy, 0f);
            }

            public void FoxgloveLog_Publish(int topicIndex, FoxgloveManager manager, ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(manager);
                Assert.Equal(123_456_789UL, nowNs);
                LivePublishes++;
                if (ThrowOnLivePublish)
                    throw new InvalidOperationException("expected live route failure");
            }

            public void FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(bus);
                Assert.Equal(123_456_789UL, nowNs);
                BusPublishes++;
                var payload = 181;
                bus.Publish(Contract, nowNs, in payload, "phase181-test");
            }

            public bool FoxgloveLog_HasBusSubscribers(int topicIndex, FoxTopicBus bus)
            {
                Assert.Equal(0, topicIndex);
                return bus != null && bus.HasSubscribers(Topic);
            }

            public bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds)
            {
                Assert.Equal(0, topicIndex);
                return true;
            }

            public void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds)
            {
                Assert.Equal(0, topicIndex);
                MarkPublishedCalls++;
            }
        }

        private sealed class TargetAwareSource :
            IFoxgloveLogSource,
            IFoxglovePublishCaptureSource,
            IFoxglovePublishTargetSource,
            IFoxgloveTopicObserverSource,
            IFoxglovePublishRecordingSource,
            IFoxgloveLogPolicySource,
            IFoxgloveTopicContractSource
        {
            private readonly FoxgloveLogTopicInfo _topic;
            private readonly FoxTopicWriterPolicy _writerPolicy;
            private readonly Dictionary<FoxRunEndpoint, RecordingTargetSink> _sinks =
                new Dictionary<FoxRunEndpoint, RecordingTargetSink>
                {
                    [FoxRunEndpoint.Foxglove] = new RecordingTargetSink(),
                    [FoxRunEndpoint.Ros2Native] = new RecordingTargetSink(),
                    [FoxRunEndpoint.Ros2Bridge] = new RecordingTargetSink()
                };
            private object _currentCapture;

            public TargetAwareSource(
                FoxRunEndpoint targets,
                string topic = "/phase184/hub-target-aware",
                FoxRunPolicy policy = FoxRunPolicy.Trigger,
                string origin = "accepted-writer",
                bool hasExplicitTargets = true,
                bool hasExplicitQos = false,
                FoxTopicWriterPolicy writerPolicy =
                    FoxTopicWriterPolicy.SingleWriter)
            {
                _topic = new FoxgloveLogTopicInfo(
                    topic,
                    10f,
                    policy,
                    0f,
                    FoxRunFlow.Publish,
                    declaredSource: 0,
                    hasExplicitSource: false,
                    declaredTargets: targets,
                    hasExplicitTargets: hasExplicitTargets,
                    declaredEncoding: FoxRunEncoding.JSON,
                    hasExplicitEncoding:
                        (targets & FoxRunEndpoint.Foxglove) != 0,
                    qosProfile: hasExplicitQos
                        ? FoxRunQosProfile.Default
                        : (FoxRunQosProfile)0,
                    hasExplicitQosProfile: hasExplicitQos,
                    qosReliability: 0,
                    hasExplicitReliability: false,
                    qosDurability: 0,
                    hasExplicitDurability: false,
                    qosHistory: 0,
                    hasExplicitHistory: false,
                    qosDepth: 0,
                    hasExplicitDepth: false,
                    hasExplicitHz: false);
                FoxgloveLog_Origin = origin;
                _writerPolicy = writerPolicy;
            }

            public int FoxgloveLog_TopicCount => 1;
            public int BeginCaptureCount { get; private set; }
            public int EndCaptureCount { get; private set; }
            public int LegacyPublishCount { get; private set; }
            public int RecordCount { get; private set; }
            public int ObserverPublishes { get; private set; }
            public int MarkPublishedCount { get; private set; }
            public int Value { get; set; }
            public bool ThrowOnBeginCapture { get; set; }
            public Exception EndCaptureException { get; set; }
            public bool ObserverDemand { get; set; }
            public int? ObserverMutationValue { get; set; }
            public bool RecordingReady { get; set; }
            public string RecordingReason { get; set; } = string.Empty;
            public object LastCapture { get; private set; }
            public object RecordedSample { get; private set; }
            public ulong RecordedTimestampNs { get; private set; }
            public int RecordedValue { get; private set; }
            public FoxRunResolvedQos LastNativeQos { get; private set; }
            public List<FoxRunEndpoint> PublishOrder { get; } =
                new List<FoxRunEndpoint>();
            public FoxgloveLogTopicInfo TopicInfo => _topic;
            public string FoxgloveLog_Origin { get; }

            public FoxTopicContract FoxgloveLog_GetContract(int index)
            {
                Assert.Equal(0, index);
                return new FoxTopicContract(
                    _topic.Topic,
                    "phase184.TargetAware",
                    "json",
                    "phase184.TargetAware",
                    "phase184-target-aware-v1",
                    FoxTopicVisibility.Exported,
                    _writerPolicy);
            }

            public RecordingTargetSink Sink(FoxRunEndpoint target) => _sinks[target];

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                Assert.Equal(0, index);
                return _topic;
            }

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
                => LegacyPublishCount++;

            public bool FoxgloveLog_BeginCapture(int topicIndex)
            {
                Assert.Equal(0, topicIndex);
                BeginCaptureCount++;
                if (ThrowOnBeginCapture)
                    throw new InvalidOperationException("capture getter failed");
                _currentCapture = new CapturedValue(Value);
                LastCapture = _currentCapture;
                return true;
            }

            public void FoxgloveLog_EndCapture(int topicIndex)
            {
                Assert.Equal(0, topicIndex);
                EndCaptureCount++;
                _currentCapture = null;
                if (EndCaptureException != null)
                    throw EndCaptureException;
            }

            public bool FoxgloveLog_IsTargetReady(
                int topicIndex,
                FoxRunEndpoint target,
                FoxRunResolvedPublishContract contract,
                FoxgloveManager manager,
                FoxTopicBus bus,
                FoxTopicSinkRouter router,
                out string reason)
            {
                Assert.Equal(0, topicIndex);
                if (target == FoxRunEndpoint.Ros2Native)
                    LastNativeQos = contract.NativeQos;
                return _sinks[target].IsReady(out reason);
            }

            public bool FoxgloveLog_PublishCaptured(
                int topicIndex,
                FoxRunEndpoint target,
                FoxRunResolvedPublishContract contract,
                FoxgloveManager manager,
                FoxTopicBus bus,
                FoxTopicSinkRouter router,
                ulong nowNs,
                out string reason)
            {
                Assert.Equal(0, topicIndex);
                PublishOrder.Add(target);
                return _sinks[target].Publish(_currentCapture, nowNs, out reason);
            }

            public bool FoxgloveLog_HasObservers(
                int topicIndex,
                FoxTopicBus bus)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(bus);
                return ObserverDemand;
            }

            public void FoxgloveLog_PublishCapturedToObservers(
                int topicIndex,
                FoxTopicBus bus,
                ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(bus);
                Assert.Equal(TargetAwareHubFixture.TimestampNs, nowNs);
                Assert.NotNull(_currentCapture);
                ObserverPublishes++;
                if (ObserverMutationValue.HasValue)
                {
                    Assert.IsType<CapturedValue>(_currentCapture).Value =
                        ObserverMutationValue.Value;
                }
            }

            public bool FoxgloveLog_IsRecordingReady(
                int topicIndex,
                FoxRunResolvedPublishContract contract,
                FoxgloveManager manager,
                out string reason)
            {
                Assert.Equal(0, topicIndex);
                reason = RecordingReason;
                return RecordingReady;
            }

            public bool FoxgloveLog_RecordCaptured(
                int topicIndex,
                FoxRunResolvedPublishContract contract,
                FoxgloveManager manager,
                ulong nowNs,
                out string reason)
            {
                Assert.Equal(0, topicIndex);
                RecordCount++;
                RecordedSample = _currentCapture;
                RecordedTimestampNs = nowNs;
                RecordedValue =
                    Assert.IsType<CapturedValue>(_currentCapture).Value;
                reason = string.Empty;
                return true;
            }

            public bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds)
                => _topic.Policy != FoxRunPolicy.Change
                   || !_hasLastPublishedValue
                   || Value != _lastPublishedValue;

            public void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds)
            {
                MarkPublishedCount++;
                _lastPublishedValue = Value;
                _hasLastPublishedValue = true;
            }

            private bool _hasLastPublishedValue;
            private int _lastPublishedValue;
        }

        private sealed class CapturedValue
        {
            public CapturedValue(int value)
            {
                Value = value;
            }

            public int Value { get; set; }
        }

        private sealed class RecordingTargetSink
        {
            public bool Ready { get; set; } = true;
            public string ReadinessReason { get; set; } = "target unavailable";
            public bool PublishResult { get; set; } = true;
            public Exception PublishException { get; set; }
            public List<TargetDelivery> Deliveries { get; } =
                new List<TargetDelivery>();

            public bool IsReady(out string reason)
            {
                reason = Ready ? string.Empty : ReadinessReason;
                return Ready;
            }

            public bool Publish(object sample, ulong timestampNs, out string reason)
            {
                if (PublishException != null)
                    throw PublishException;
                if (!PublishResult)
                {
                    reason = "target rejected";
                    return false;
                }
                Deliveries.Add(new TargetDelivery(sample, timestampNs));
                reason = string.Empty;
                return true;
            }
        }

        private readonly struct TargetDelivery
        {
            public TargetDelivery(object sample, ulong timestampNs)
            {
                Sample = sample;
                TimestampNs = timestampNs;
            }

            public object Sample { get; }
            public ulong TimestampNs { get; }
        }

        private sealed class WebSocketOnlySource : IFoxgloveLogSource
        {
            private readonly bool _hasExplicitQos;
            private readonly FoxRunFlow _flow;

            public WebSocketOnlySource(
                bool hasExplicitQos = false,
                FoxRunFlow flow = FoxRunFlow.Publish)
            {
                _hasExplicitQos = hasExplicitQos;
                _flow = flow;
            }

            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                return new FoxgloveLogTopicInfo(
                    "/phase181/websocket-only",
                    30f,
                    FoxRunPolicy.Trigger,
                    0f,
                    _flow,
                    declaredSource: 0,
                    hasExplicitSource: false,
                    declaredTargets: 0,
                    hasExplicitTargets: false,
                    hasExplicitQos: _hasExplicitQos);
            }

            public void FoxgloveLog_Publish(int topicIndex, FoxgloveManager manager, ulong nowNs)
            {
                LivePublishes++;
            }

            public void Reset()
            {
                LivePublishes = 0;
            }
        }

        private sealed class ScheduledSource : IFoxgloveLogSource
        {
            internal const string Topic = "/phase184/rate/scheduled";

            private readonly FoxgloveLogTopicInfo _topic;

            public ScheduledSource(FoxgloveLogTopicInfo topic)
            {
                _topic = topic;
            }

            public int FoxgloveLog_TopicCount => 1;
            public int LivePublishes { get; private set; }

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
            {
                Assert.Equal(0, index);
                return _topic;
            }

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
            {
                Assert.Equal(0, topicIndex);
                Assert.NotNull(manager);
                Assert.Equal(123_456_789UL, nowNs);
                LivePublishes++;
            }
        }

        private sealed class LifecycleRecordingSink : IFoxTopicSink, IFoxTopicSinkContractLifecycle
        {
            public string Name => "recording-lifecycle";
            public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.External;
            public int RegisterCalls { get; private set; }
            public int UnregisterCalls { get; private set; }

            public void Register(FoxTopicContract contract) => RegisterCalls++;
            public void Unregister(string topic) => UnregisterCalls++;
            public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalTopicUnregisterSink :
            IFoxTopicSink,
            IFoxTopicSinkContractLifecycle
        {
            private readonly string _fatalTopic;

            public FatalTopicUnregisterSink(string fatalTopic)
            {
                _fatalTopic = fatalTopic;
            }

            public string Name => "fatal-topic-unregister";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.External;
            public List<string> UnregisteredTopics { get; } =
                new List<string>();

            public void Register(FoxTopicContract contract) { }

            public void Unregister(string topic)
            {
                UnregisteredTopics.Add(topic);
                if (string.Equals(topic, _fatalTopic, StringComparison.Ordinal))
                    throw new OutOfMemoryException("first-topic-unregister");
            }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
            }

            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class FatalTopicRegisterSink :
            IFoxTopicSink,
            IFoxTopicSinkContractLifecycle
        {
            private readonly string _fatalTopic;

            public FatalTopicRegisterSink(string fatalTopic)
            {
                _fatalTopic = fatalTopic;
            }

            public string Name => "fatal-topic-register";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.External;
            public bool FailRegistration { get; set; } = true;
            public List<string> UnregisteredTopics { get; } =
                new List<string>();

            public void Register(FoxTopicContract contract)
            {
                if (FailRegistration
                    && string.Equals(
                        contract.Topic,
                        _fatalTopic,
                        StringComparison.Ordinal))
                {
                    throw new OutOfMemoryException("second-topic-register");
                }
            }

            public void Unregister(string topic)
                => UnregisteredTopics.Add(topic);

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
            }

            public void Flush() { }
            public void Dispose() { }
        }

        private sealed class MultiTopicContractSource :
            IFoxgloveLogSource,
            IFoxgloveTopicContractSource
        {
            internal const string FirstTopic =
                "/phase184/fatal-cleanup/first";
            internal const string SecondTopic =
                "/phase184/fatal-cleanup/second";

            private static readonly string[] Topics =
            {
                FirstTopic,
                SecondTopic
            };

            public int FoxgloveLog_TopicCount => Topics.Length;
            public string FoxgloveLog_Origin => "fatal-cleanup-owner";

            public FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index)
                => new FoxgloveLogTopicInfo(
                    Topics[index],
                    10f,
                    FoxRunPolicy.Trigger,
                    0f,
                    FoxRunFlow.Publish,
                    declaredSource: 0,
                    hasExplicitSource: false,
                    declaredTargets: FoxRunEndpoint.Ros2Native,
                    hasExplicitTargets: true,
                    declaredEncoding: 0,
                    hasExplicitEncoding: false,
                    qosProfile: FoxRunQosProfile.Default,
                    hasExplicitQosProfile: true,
                    qosReliability: 0,
                    hasExplicitReliability: false,
                    qosDurability: 0,
                    hasExplicitDurability: false,
                    qosHistory: 0,
                    hasExplicitHistory: false,
                    qosDepth: 0,
                    hasExplicitDepth: false,
                    hasExplicitHz: false);

            public FoxTopicContract FoxgloveLog_GetContract(int index)
                => new FoxTopicContract(
                    Topics[index],
                    "phase184.FatalCleanup",
                    "json",
                    "phase184.FatalCleanup",
                    "phase184-fatal-cleanup-v1",
                    FoxTopicVisibility.Exported,
                    FoxTopicWriterPolicy.SingleWriter);

            public void FoxgloveLog_Publish(
                int topicIndex,
                FoxgloveManager manager,
                ulong nowNs)
            {
            }
        }
    }
}
#endif
