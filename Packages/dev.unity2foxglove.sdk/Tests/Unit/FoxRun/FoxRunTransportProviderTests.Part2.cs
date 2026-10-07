// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the neutral, Manager-local FoxRun transport provider contract.

using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests
{
    public sealed partial class FoxRunTransportProviderTests
    {
        [Fact]
        public void OrdinaryFanoutClassifiesInvalidProviderResultAsFailure()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new OrdinarySession(
                    new FoxRunTransportId("unity2foxglove.invalid-result"),
                    generation: 187,
                    calls,
                    default(FoxRunTransportPublishResult))
            };
            var request = new FoxRunOrdinaryPayloadRequest(
                "ordinary-invalid-result-fixture",
                "/phase187/invalid-result",
                "Demo.Value",
                value: 42,
                logTimeNs: 187,
                sequence: 1,
                FoxRunDeliveryPolicy.ProviderDefault);

            var result = FoxRunOrdinaryTransportFanout.Publish(
                sessions,
                in request);

            Assert.Equal(
                new[] { "unity2foxglove.invalid-result" },
                calls);
            Assert.Equal(1, result.Matched);
            Assert.Equal(0, result.Accepted);
            Assert.Equal(0, result.Rejected);
            Assert.Equal(0, result.Unavailable);
            Assert.Equal(1, result.Failed);
            Assert.Equal(
                result.Matched,
                result.Accepted
                + result.Rejected
                + result.Unavailable
                + result.Failed);
            Assert.False(result.AnyAccepted);
            Assert.False(result.AllAccepted);
        }

        [Fact]
        public void GeneratedFanoutUsesExplicitRoutesAndClassifiesEverySelectedProvider()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new GeneratedSession(
                    "unity2foxglove.alpha",
                    calls,
                    FoxRunTransportPublishResult.Accepted()),
                new GeneratedSession(
                    "unity2foxglove.bravo",
                    calls,
                    FoxRunTransportPublishResult.Failed("fixture")),
                new GeneratedSession(
                    "unity2foxglove.charlie",
                    calls,
                    FoxRunTransportPublishResult.Rejected("fixture"))
            };
            var source = new GeneratedSource();
            var request = new FoxRunGeneratedTransportPublishRequest(
                source,
                topicIndex: 0,
                "/phase186/generated",
                logTimeNs: 186);

            var result = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                explicitTransportIds: new[]
                {
                    "unity2foxglove.charlie",
                    "unity2foxglove.alpha"
                },
                inheritedTransportIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.bravo")
                },
                in request);

            Assert.Equal(
                new[]
                {
                    "unity2foxglove.alpha",
                    "unity2foxglove.charlie"
                },
                calls);
            Assert.Equal(2, result.Matched);
            Assert.Equal(1, result.Accepted);
            Assert.Equal(1, result.Rejected);
            Assert.Equal(0, result.Unavailable);
            Assert.Equal(0, result.Failed);
            Assert.True(result.AnyAccepted);
            Assert.False(result.AllAccepted);
            Assert.Collection(
                result.TargetResults,
                target =>
                {
                    Assert.Equal(
                        new FoxRunTransportId("unity2foxglove.alpha"),
                        target.TransportId);
                    Assert.Equal(
                        FoxRunTransportRouteResultState.Accepted,
                        target.State);
                    Assert.Equal(string.Empty, target.Reason);
                },
                target =>
                {
                    Assert.Equal(
                        new FoxRunTransportId("unity2foxglove.charlie"),
                        target.TransportId);
                    Assert.Equal(
                        FoxRunTransportRouteResultState.Rejected,
                        target.State);
                    Assert.Equal("fixture", target.Reason);
                });
        }

        [Fact]
        public void GeneratedFanoutSkipsSessionsThatDoNotOwnTheCapturedTopic()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new OwnershipSession(
                    "unity2foxglove.alpha",
                    calls,
                    ownedTopic: "/phase181/other"),
                new OwnershipSession(
                    "unity2foxglove.bravo",
                    calls,
                    ownedTopic: "/phase181/owned")
            };
            var request = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(),
                topicIndex: 0,
                "/phase181/owned",
                logTimeNs: 181);

            var result = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                explicitTransportIds: null,
                inheritedTransportIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.alpha"),
                    new FoxRunTransportId("unity2foxglove.bravo")
                },
                in request);

            Assert.Equal(new[] { "unity2foxglove.bravo" }, calls);
            Assert.Equal(1, result.Matched);
            Assert.Equal(1, result.Accepted);
            Assert.Equal(0, result.Rejected + result.Unavailable + result.Failed);
            var target = Assert.Single(result.TargetResults);
            Assert.Equal(
                new FoxRunTransportId("unity2foxglove.bravo"),
                target.TransportId);

            calls.Clear();
            var unowned = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(),
                topicIndex: 0,
                "/phase181/unowned",
                logTimeNs: 182);
            var skipped = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                explicitTransportIds: null,
                inheritedTransportIds: new[]
                {
                    new FoxRunTransportId("unity2foxglove.alpha"),
                    new FoxRunTransportId("unity2foxglove.bravo")
                },
                in unowned);

            Assert.Empty(calls);
            Assert.Equal(0, skipped.Matched);
            Assert.False(skipped.AnyAccepted);
            Assert.Empty(skipped.TargetResults);
        }

        [Fact]
        public void GeneratedProviderFailureDiagnosticPreservesTargetReason()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new GeneratedSession(
                    "unity2foxglove.ros2bridge",
                    calls,
                    FoxRunTransportPublishResult.Rejected(
                        "logical schema mismatch"))
            };
            var request = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(),
                topicIndex: 0,
                "/phase186/generated",
                logTimeNs: 186);
            var result = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                explicitTransportIds: new[]
                {
                    "unity2foxglove.ros2bridge"
                },
                inheritedTransportIds: Array.Empty<FoxRunTransportId>(),
                in request);
            var diagnostic = FoxRunGeneratedTransportFanout.FormatFailure(
                in result);
            Assert.Contains("1 rejected", diagnostic);
            Assert.Contains(
                "unity2foxglove.ros2bridge: logical schema mismatch",
                diagnostic,
                StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedFanoutSuppressesOnlyExactRemoteProviderGeneration()
        {
            var calls = new System.Collections.Generic.List<string>();
            var sessions = new IFoxRunTransportSession[]
            {
                new GeneratedSession(
                    "unity2foxglove.ros2bridge",
                    calls,
                    FoxRunTransportPublishResult.Accepted(),
                    generation: 17),
                new GeneratedSession(
                    "unity2foxglove.r2fu",
                    calls,
                    FoxRunTransportPublishResult.Accepted(),
                    generation: 17),
            };
            var request = new FoxRunGeneratedTransportPublishRequest(
                new GeneratedSource(),
                topicIndex: 0,
                "/phase186/generated-origin",
                logTimeNs: 186);
            var selected = new[]
            {
                "unity2foxglove.ros2bridge",
                "unity2foxglove.r2fu",
            };

            var exact = FoxRunGeneratedTransportFanout.Publish(
                sessions,
                selected,
                inheritedTransportIds: null,
                in request,
                suppressedTransportId:
                    "unity2foxglove.ros2bridge",
                suppressedGeneration: 17);
            Assert.Equal(
                new[] { "unity2foxglove.r2fu" },
                calls);
            Assert.Equal(1, exact.Matched);
            Assert.Equal(1, exact.Accepted);

            calls.Clear();
            var staleGeneration =
                FoxRunGeneratedTransportFanout.Publish(
                    sessions,
                    selected,
                    inheritedTransportIds: null,
                    in request,
                    suppressedTransportId:
                        "unity2foxglove.ros2bridge",
                    suppressedGeneration: 16);
            Assert.Equal(selected, calls);
            Assert.Equal(2, staleGeneration.Matched);
            Assert.Equal(2, staleGeneration.Accepted);
        }

        [Fact]
        public void ObservedStatusSeparatesDirectionsAndBoundsDiagnostics()
        {
            var longMessage = new string('x', 700);
            var publish = new FoxRunTransportDirectionStatus(
                FoxRunTransportDirection.Publish,
                selected: true,
                FoxRunTransportObservedState.Ready,
                observedContractCount: 2,
                readyContractCount: 2,
                failedContractCount: 0,
                new FoxRunTransportDiagnostic(
                    "FOXTRANSPORT101",
                    longMessage));
            var subscribe = new FoxRunTransportDirectionStatus(
                FoxRunTransportDirection.Subscribe,
                selected: true,
                FoxRunTransportObservedState.Failed,
                observedContractCount: 1,
                readyContractCount: 0,
                failedContractCount: 1,
                new FoxRunTransportDiagnostic(
                    "FOXTRANSPORT102",
                    "decode failed"));
            var diagnostics = Enumerable.Range(
                    0,
                    FoxRunTransportStatusSnapshot.MaximumDiagnostics + 4)
                .Select(index => new FoxRunTransportDiagnostic(
                    index == 0
                        ? "FOXTRANSPORT101"
                        : "FOXTRANSPORT" + (200 + index),
                    "diagnostic-" + index))
                .ToArray();

            var snapshot = new FoxRunTransportStatusSnapshot(
                new FoxRunTransportId("unity2foxglove.status"),
                generation: 42,
                publish,
                subscribe,
                diagnostics);

            Assert.Equal(FoxRunTransportObservedState.Degraded, snapshot.State);
            Assert.True(snapshot.Publish.IsReady);
            Assert.False(snapshot.Subscribe.IsReady);
            Assert.Equal(
                FoxRunTransportStatusSnapshot.MaximumDiagnostics,
                snapshot.Diagnostics.Count);
            Assert.Equal(
                snapshot.Diagnostics.Count,
                snapshot.Diagnostics
                    .Select(diagnostic => diagnostic.Code)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(
                snapshot.Diagnostics,
                diagnostic => Assert.InRange(
                    diagnostic.Message.Length,
                    1,
                    FoxRunTransportDiagnostic.MaximumMessageChars));
        }

    }
}
