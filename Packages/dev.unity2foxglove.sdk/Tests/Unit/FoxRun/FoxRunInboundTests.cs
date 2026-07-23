// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.UnitTests.Harness;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunInboundTests
    {
        [Fact]
        public void InputTopicAndRouterExposeProviderCapabilitySessionSurface()
        {
            Assert.NotNull(typeof(FoxgloveInputTopicInfo).GetProperty("DeclaredSubscriptionProvider"));
            Assert.NotNull(typeof(FoxgloveInputTopicInfo).GetProperty("SupportsWebSocket"));
            Assert.NotNull(typeof(FoxgloveInputTopicInfo).GetProperty("SupportsRos2Native"));
            Assert.NotNull(typeof(FoxRunInputRouter).GetProperty("DefaultSubscriptionProvider"));

            var constructor = typeof(FoxgloveInputTopicInfo).GetConstructor(new[]
            {
                typeof(string),
                typeof(FoxRunWireEncoding),
                typeof(FoxRunFlow),
                typeof(FoxRunSubscriptionProvider),
                typeof(bool),
                typeof(bool)
            });
            Assert.NotNull(constructor);
        }

        [Fact]
        public void RouterRoutesCoexistingContractsOnlyToCapturedWebSocketProvider()
        {
            var input = new MultiProviderInput();
            var router = new FoxRunInputRouter
            {
                DefaultSubscriptionProvider = FoxRunSubscriptionProvider.FoxgloveWebSocket,
                DefaultSubscriptionWireEncoding = FoxRunWireEncoding.Protobuf
            };
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase179/json", Array.Empty<byte>(), "json", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase179/dual", Array.Empty<byte>(), "protobuf", 2).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/phase179/native", Array.Empty<byte>(), "protobuf", 3).Status);

            router.DefaultSubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native;

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase179/dual", Array.Empty<byte>(), "protobuf", 4).Status);

            var nativeDefaultRouter = new FoxRunInputRouter
            {
                DefaultSubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
                DefaultSubscriptionWireEncoding = FoxRunWireEncoding.Json
            };
            nativeDefaultRouter.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                nativeDefaultRouter.Dispatch("/phase179/json", Array.Empty<byte>(), "json", 5).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                nativeDefaultRouter.Dispatch("/phase179/dual", Array.Empty<byte>(), "json", 6).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                nativeDefaultRouter.Dispatch("/phase179/native", Array.Empty<byte>(), "json", 7).Status);
            Assert.Equal(new[] { 2, 2, 0 }, input.ApplyCounts);
        }

        [Fact]
        public void ExplicitWebSocketJsonAndProtobufRemainUsableWhenNativeDefaultCannotServeOrdinaryDto()
        {
            var input = new NativeUnavailableCoexistenceInput();
            var router = new FoxRunInputRouter
            {
                DefaultSubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native,
                DefaultSubscriptionWireEncoding = FoxRunWireEncoding.Protobuf
            };
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase179/coexist/json", Array.Empty<byte>(), "json", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase179/coexist/protobuf", Array.Empty<byte>(), "protobuf", 2).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/phase179/coexist/ordinary-dto", Array.Empty<byte>(), "protobuf", 3).Status);
            Assert.Equal(new[] { 1, 1, 0 }, input.ApplyCounts);
        }

        [Fact]
        public void JsonDecoderReadsDeclaredVectorShape()
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"incomingVelocity\":{\"x\":1.5,\"y\":-2,\"z\":3.25}}");

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "incomingVelocity",
                out Vector3 value,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(1.5f, value.x);
            Assert.Equal(-2f, value.y);
            Assert.Equal(3.25f, value.z);
        }

        [Fact]
        public void JsonDecoderRejectsPolymorphicTypeHints()
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"incomingVelocity\":{\"$type\":\"System.Version\",\"x\":1,\"y\":2,\"z\":3}}");

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "incomingVelocity",
                out Vector3 _,
                out var error);

            Assert.False(ok);
            Assert.Contains("$type", error, StringComparison.Ordinal);
        }

        [Fact]
        public void JsonDecoderRejectsExcessiveNestingBeforeRecursiveScanOverflows()
        {
            var sb = new StringBuilder("{\"value\":");
            for (var i = 0; i < 40; i++)
                sb.Append('[');
            sb.Append('1');
            for (var i = 0; i < 40; i++)
                sb.Append(']');
            sb.Append('}');

            var ok = FoxRunInboundJson.TryRead(
                Encoding.UTF8.GetBytes(sb.ToString()),
                "value",
                out int _,
                out var error);

            Assert.False(ok);
            Assert.Contains("nesting", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void JsonDecoderReadsGeneratedDecimalAndCharInputs()
        {
            var payload = Encoding.UTF8.GetBytes("{\"amount\":12.5,\"key\":\"A\"}");

            Assert.True(FoxRunInboundJson.TryRead(payload, "amount", out decimal amount, out var decimalError), decimalError);
            Assert.True(FoxRunInboundJson.TryRead(payload, "key", out char key, out var charError), charError);

            Assert.Equal(12.5m, amount);
            Assert.Equal('A', key);
        }

        [Fact]
        public void JsonDecoderRejectsMultiCharacterCharInputs()
        {
            var payload = Encoding.UTF8.GetBytes("{\"key\":\"AB\"}");

            var ok = FoxRunInboundJson.TryRead(payload, "key", out char _, out var error);

            Assert.False(ok);
            Assert.Contains("single character", error, StringComparison.Ordinal);
        }

        [Fact]
        public void RouterUsesGeneratedAllowlistAndRegistrationOrder()
        {
            var first = new RecordingInput("/phase157/cmd");
            var second = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter();
            router.Register(first);
            router.Register(second);

            var result = router.Dispatch(
                "/phase157/cmd",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, result.Status);
            Assert.Equal(1, first.ApplyCount);
            Assert.Equal(1, second.ApplyCount);
        }

        [Fact]
        public void RouterStagesNewestValueUntilTheMainThreadFlush()
        {
            var input = new StagedRecordingInput("/phase183/staged");
            var router = new FoxRunInputRouter();
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase183/staged", new byte[] { 1 }, "json", nowSeconds: 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase183/staged", new byte[] { 2 }, "json", nowSeconds: 1.01).Status);
            Assert.Equal(0, input.AppliedCount);

            Assert.Equal(1, router.Flush(nowSeconds: 2, inheritedSubscribeRateHz: 60));
            Assert.Equal(1, input.AppliedCount);
            Assert.Equal(2, input.LastAppliedValue);
            Assert.Equal(0, router.Flush(nowSeconds: 3, inheritedSubscribeRateHz: 60));
        }

        [Fact]
        public void RouterRejectsUnknownOversizedAndRateLimitedMessages()
        {
            var input = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter(maxPayloadBytes: 16, maxMessagesPerSecondPerTopic: 1);
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/other", Array.Empty<byte>(), "json", 0).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.PayloadTooLarge,
                router.Dispatch("/phase157/cmd", new byte[17], "json", 0).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase157/cmd", Encoding.UTF8.GetBytes("{}"), "json", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.RateLimited,
                router.Dispatch("/phase157/cmd", Encoding.UTF8.GetBytes("{}"), "json", 1.1).Status);
        }

        [Fact]
        public void RouterUnregisterStopsAssignment()
        {
            var input = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter();
            router.Register(input);
            router.Unregister(input);

            var result = router.Dispatch("/phase157/cmd", Array.Empty<byte>(), "json", 1);

            Assert.Equal(FoxRunInputDispatchStatus.UnknownTopic, result.Status);
            Assert.Equal(0, input.ApplyCount);
        }

        [Fact]
        public void RouterResolvesInheritedInputAgainstTheCurrentSubscriptionDefault()
        {
            var input = new InheritedRecordingInput("/phase175/inherit");
            var router = new FoxRunInputRouter();
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "protobuf", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.DecodeRejected,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "json", 2).Status);
            Assert.Equal(1, input.ApplyCount);

            router.DefaultSubscriptionWireEncoding = FoxRunWireEncoding.Json;

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "json", 3).Status);
            Assert.Equal(2, input.ApplyCount);
        }

        [Fact]
        public void RouterEncodingMismatchNamesExpectedAndClientAdvertisedEncodings()
        {
            var input = new InheritedRecordingInput("/phase175/protobuf");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var result = router.Dispatch("/phase175/protobuf", Array.Empty<byte>(), "json", 1);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, result.Status);
            Assert.Contains("expected \"protobuf\"", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("client advertised \"json\"", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RouterRejectsWrongEncodingBeforeItConsumesTheTopicRateQuota()
        {
            var input = new RecordingInput("/phase182/encoding");
            var router = new FoxRunInputRouter(maxMessagesPerSecondPerTopic: 1);
            router.Register(input);

            var wrongEncoding = router.Dispatch(
                "/phase182/encoding",
                Encoding.UTF8.GetBytes("{\"value\":1}"),
                "protobuf",
                nowSeconds: 1);
            var matchingEncoding = router.Dispatch(
                "/phase182/encoding",
                Encoding.UTF8.GetBytes("{\"value\":2}"),
                "json",
                nowSeconds: 1.1);
            var rateLimited = router.Dispatch(
                "/phase182/encoding",
                Encoding.UTF8.GetBytes("{\"value\":3}"),
                "json",
                nowSeconds: 1.2);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, wrongEncoding.Status);
            Assert.Equal(FoxRunInputDispatchStatus.Staged, matchingEncoding.Status);
            Assert.Equal(FoxRunInputDispatchStatus.RateLimited, rateLimited.Status);
            Assert.Equal(1, input.ApplyCount);
        }

        [Fact]
        public void RouterConsumesOneQuotaAndAppliesOnlyMatchingSharedTopicRegistrations()
        {
            var json = new RecordingInput("/phase182/shared");
            var protobuf = new InheritedRecordingInput("/phase182/shared");
            var router = new FoxRunInputRouter(maxMessagesPerSecondPerTopic: 1);
            router.Register(json);
            router.Register(protobuf);

            var matching = router.Dispatch(
                "/phase182/shared",
                Encoding.UTF8.GetBytes("{\"value\":1}"),
                "protobuf",
                nowSeconds: 1);
            var rateLimited = router.Dispatch(
                "/phase182/shared",
                Encoding.UTF8.GetBytes("{\"value\":2}"),
                "protobuf",
                nowSeconds: 1.1);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, matching.Status);
            Assert.Equal(0, json.ApplyCount);
            Assert.Equal(1, protobuf.ApplyCount);
            Assert.Equal(FoxRunInputDispatchStatus.RateLimited, rateLimited.Status);
        }

        [Theory]
        [InlineData("ros2")]
        [InlineData("cdr")]
        public void RouterRejectsNativeAdvertisedEncodingWithoutApplyingSource(string encoding)
        {
            var input = new RecordingInput("/phase179/websocket-only");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var result = router.Dispatch(
                "/phase179/websocket-only",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                encoding,
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, result.Status);
            Assert.Equal(0, input.ApplyCount);
            Assert.Contains("client advertised \"" + encoding + "\"", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void InputHubSafelyRebindsSessionPolicyAndAppliesTheCurrentSnapshotImmediately()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var setManager = TestSources.ExtractMethod(source, "private void SetManager(FoxgloveManager manager)");
            var unsubscribeIndex = setManager.IndexOf(
                "_manager.FoxRunSubscriptionSessionChanged -= OnFoxRunSubscriptionSessionChanged;",
                StringComparison.Ordinal);
            var assignIndex = setManager.IndexOf("_manager = manager;", StringComparison.Ordinal);
            var subscribeIndex = setManager.IndexOf(
                "_manager.FoxRunSubscriptionSessionChanged += OnFoxRunSubscriptionSessionChanged;",
                StringComparison.Ordinal);
            var applyIndex = setManager.IndexOf("ApplyManagerPolicy();", StringComparison.Ordinal);

            Assert.True(unsubscribeIndex >= 0, "SetManager must unsubscribe the previous Manager session event.");
            Assert.True(assignIndex >= 0, "SetManager must assign the new Manager.");
            Assert.True(subscribeIndex >= 0, "SetManager must subscribe the new Manager session event.");
            Assert.True(applyIndex >= 0, "SetManager must immediately apply the current session snapshot.");
            Assert.True(unsubscribeIndex < assignIndex, "Unsubscribe must happen before Manager assignment.");
            Assert.True(assignIndex < subscribeIndex, "Manager assignment must happen before subscription.");
            Assert.True(subscribeIndex < applyIndex, "Subscription must happen before the current snapshot is applied.");

            var onDisable = TestSources.ExtractMethod(source, "private void OnDisable()");
            var onDestroy = TestSources.ExtractMethod(source, "private void OnDestroy()");
            Assert.Contains("SetManager(null);", onDisable, StringComparison.Ordinal);
            Assert.Contains("SetManager(null);", onDestroy, StringComparison.Ordinal);
        }

        [Fact]
        public void InputHubRebuildsWebSocketRegistrationsAtActiveSessionBoundaries()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var sessionChanged = TestSources.ExtractMethod(
                source,
                "private void OnFoxRunSubscriptionSessionChanged(FoxRunSubscriptionSessionPolicy policy)");
            var applyIndex = sessionChanged.IndexOf(
                "ApplySubscriptionSessionPolicy(policy);",
                StringComparison.Ordinal);
            var rebuildIndex = sessionChanged.IndexOf(
                "RebuildRouterRegistrationsForActiveSession();",
                StringComparison.Ordinal);

            Assert.True(applyIndex >= 0, "The new session policy must be applied first.");
            Assert.True(
                rebuildIndex > applyIndex,
                "An active replacement session must rebuild registrations after applying its provider.");

            var setManager = TestSources.ExtractMethod(
                source,
                "private void SetManager(FoxgloveManager manager)");
            var managerApplyIndex = setManager.IndexOf("ApplyManagerPolicy();", StringComparison.Ordinal);
            var managerRebuildIndex = setManager.IndexOf(
                "RebuildRouterRegistrationsForActiveSession();",
                StringComparison.Ordinal);
            Assert.True(
                managerRebuildIndex > managerApplyIndex,
                "Manager rebinding must apply its current session before rebuilding registrations.");

            var rebuild = TestSources.ExtractMethod(
                source,
                "private void RebuildRouterRegistrationsForActiveSession()");
            Assert.Contains("if (!_subscriptionsEnabled)", rebuild, StringComparison.Ordinal);
            Assert.Contains("RemoveStaleSources();", rebuild, StringComparison.Ordinal);
            Assert.Contains("_scanSources.Sort(CompareInputSourceOrder);", rebuild, StringComparison.Ordinal);
            var unregisterIndex = rebuild.IndexOf("_router.Unregister(source);", StringComparison.Ordinal);
            var registerIndex = rebuild.IndexOf("_router.Register(source);", StringComparison.Ordinal);
            Assert.True(unregisterIndex >= 0, "Existing byte-router registrations must be removed.");
            Assert.True(
                registerIndex > unregisterIndex,
                "Sources must be registered again only after all old provider resolutions are removed.");
        }

        [Fact]
        public void InputHubRefreshesSessionPolicyBeforeFirstMessageDispatch()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var dispatch = TestSources.ExtractMethod(
                source,
                "private void OnClientMessage(uint clientId, uint channelId, string topic, string encoding, byte[] payload)");
            var refreshIndex = dispatch.IndexOf("ApplyManagerPolicy();", StringComparison.Ordinal);
            var enabledIndex = dispatch.IndexOf("if (!_subscriptionsEnabled)", StringComparison.Ordinal);

            Assert.True(refreshIndex >= 0, "Dispatch must refresh the current session snapshot.");
            Assert.True(enabledIndex > refreshIndex, "Snapshot refresh must happen before the enabled-state gate.");
            Assert.DoesNotContain("EnableFoxRunInbound", dispatch, StringComparison.Ordinal);
            Assert.Contains("IsFoxRunInboundAuthorized", dispatch, StringComparison.Ordinal);
        }

        [Fact]
        public void InputHubSeparatesFrozenEncodingAndRateFromLivePayloadPolicy()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var managerPolicy = TestSources.ExtractMethod(source, "private void ApplyManagerPolicy()");
            var sessionPolicy = TestSources.ExtractMethod(
                source,
                "private void ApplySubscriptionSessionPolicy(FoxRunSubscriptionSessionPolicy policy)");

            Assert.Contains(
                "_router.MaxPayloadBytes = _manager.FoxRunSubscriptionMaxPayloadBytes;",
                managerPolicy,
                StringComparison.Ordinal);
            Assert.Contains(
                "ApplySubscriptionSessionPolicy(_manager.ActiveFoxRunSubscriptionSessionPolicy);",
                managerPolicy,
                StringComparison.Ordinal);
            Assert.Contains(
                "_router.DefaultSubscriptionWireEncoding = policy.WebSocketSubscriptionEncoding;",
                sessionPolicy,
                StringComparison.Ordinal);
            Assert.Contains(
                "_router.DefaultSubscriptionProvider = policy.DefaultProvider;",
                sessionPolicy,
                StringComparison.Ordinal);
            Assert.Contains(
                "_router.MaxMessagesPerSecondPerTopic = policy.TransportAdmissionRateLimitHz;",
                sessionPolicy,
                StringComparison.Ordinal);
            Assert.DoesNotContain("cdr", source, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void JsonAndProtobufInputsDecodeAfterRuntimeRestartWithinOneSubscriptionSession()
        {
            var transport = new RestartInputTransport();
            using var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            var sessionState = new FoxRunSubscriptionSessionState();
            var policy = sessionState.BeginIfNeeded(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.Default,
                nativeCopyBudgetBytes: 4 * 1024 * 1024,
                transportAdmissionRateLimitHz: 60,
                defaultSubscribeRateHz: 60);
            var generation = policy.SessionGeneration;
            var input = new RestartDecodingInput();
            var router = new FoxRunInputRouter();
            router.Register(input);
            var dispatches = new List<FoxRunInputDispatchResult>();
            var nowSeconds = 0d;

            void StartAndAttach(FoxRunSubscriptionSessionPolicy activePolicy)
            {
                runtime.Start("phase179-restart", enableCdrClientPublish: false);
                router.DefaultSubscriptionWireEncoding = activePolicy.WebSocketSubscriptionEncoding;
                router.MaxMessagesPerSecondPerTopic = activePolicy.TransportAdmissionRateLimitHz;
                runtime.Session.OnClientMessageWithEncoding += (_, _, topic, encoding, payload) =>
                    dispatches.Add(router.Dispatch(topic, payload, encoding, nowSeconds += 2d));
            }

            void PublishBoth(int jsonValue, int protobufValue)
            {
                transport.ReceiveText(
                    17,
                    "{\"op\":\"advertise\",\"channels\":["
                    + "{\"id\":1,\"topic\":\"/phase179/json\",\"encoding\":\"json\"},"
                    + "{\"id\":2,\"topic\":\"/phase179/protobuf\",\"encoding\":\"protobuf\"}]}");
                transport.ReceiveBinary(
                    17,
                    ClientMessageFrame(
                        1,
                        Encoding.UTF8.GetBytes("{\"value\":" + jsonValue + "}")));
                var protobuf = new List<byte>();
                FoxRunProtobufWire.WriteInt32(protobuf, 1, protobufValue);
                transport.ReceiveBinary(17, ClientMessageFrame(2, protobuf.ToArray()));
                Assert.Equal(
                    2,
                    router.Flush(
                        nowSeconds += 0.1d,
                        inheritedSubscribeRateHz: 60));
            }

            StartAndAttach(policy);
            PublishBoth(jsonValue: 4, protobufValue: 8);
            Assert.Equal(4, input.JsonValue);
            Assert.Equal(8, input.ProtobufValue);
            runtime.Stop();

            var frozenPolicy = sessionState.BeginIfNeeded(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunWireEncoding.Json,
                FoxRunRos2QosPreset.SensorData,
                nativeCopyBudgetBytes: 1024,
                transportAdmissionRateLimitHz: 1,
                defaultSubscribeRateHz: 1);
            Assert.Same(policy, frozenPolicy);
            Assert.Equal(generation, frozenPolicy.SessionGeneration);
            Assert.Equal(FoxRunWireEncoding.Protobuf, frozenPolicy.WebSocketSubscriptionEncoding);
            Assert.Equal(60, frozenPolicy.TransportAdmissionRateLimitHz);
            Assert.Equal(60, frozenPolicy.DefaultSubscribeRateHz);

            StartAndAttach(frozenPolicy);
            PublishBoth(jsonValue: 12, protobufValue: 16);

            Assert.Equal(12, input.JsonValue);
            Assert.Equal(16, input.ProtobufValue);
            Assert.Equal(4, dispatches.Count);
            Assert.All(dispatches, result => Assert.Equal(FoxRunInputDispatchStatus.Staged, result.Status));
            Assert.Equal(generation, sessionState.Current.SessionGeneration);
        }

        [Fact]
        public void RouterIsolatesAssignmentExceptionsAndContinuesInRegistrationOrder()
        {
            var throwing = new ThrowingInput("/phase157/cmd");
            var recording = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter();
            router.Register(throwing);
            router.Register(recording);

            var result = router.Dispatch(
                "/phase157/cmd",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, result.Status);
            Assert.Equal(1, result.StagedCount);
            Assert.Contains("staging failed", result.Diagnostic, StringComparison.Ordinal);
            Assert.Equal(1, recording.ApplyCount);
        }

        [Fact]
        public void RouterReportsFlushExceptionsWithoutBlockingHealthySources()
        {
            var throwing = new ThrowingFlushInput("/phase184/throwing");
            var healthy = new StagedRecordingInput("/phase184/healthy");
            var diagnostics = new List<string>();
            var router = new FoxRunInputRouter();
            router.Register(throwing);
            router.Register(healthy);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch(
                    "/phase184/healthy",
                    new byte[] { 7 },
                    "json",
                    nowSeconds: 1).Status);

            Assert.Equal(
                1,
                router.Flush(
                    nowSeconds: 2,
                    inheritedSubscribeRateHz: 60,
                    reportApplyFailure: diagnostics.Add));

            var diagnostic = Assert.Single(diagnostics);
            Assert.Contains(nameof(ThrowingFlushInput), diagnostic, StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidOperationException), diagnostic, StringComparison.Ordinal);
            Assert.Equal(7, healthy.LastAppliedValue);
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.20.30.40")]
        [InlineData("localhost")]
        [InlineData("::1")]
        public void InboundAuthorizationAllowsEnabledLoopback(string host)
        {
            Assert.True(FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                true,
                host,
                false,
                "",
                out var diagnostic));
            Assert.Empty(diagnostic);
        }

        [Fact]
        public void InboundAuthorizationFailsClosedForRemoteWithoutExplicitTokenPolicy()
        {
            Assert.False(FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                true,
                "0.0.0.0",
                false,
                "secret",
                out var noOptIn));
            Assert.False(FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                true,
                "0.0.0.0",
                true,
                "",
                out var noToken));
            Assert.Contains("explicitly enabled", noOptIn, StringComparison.Ordinal);
            Assert.Contains("shared token", noToken, StringComparison.Ordinal);
        }

        [Fact]
        public void InboundAuthorizationRequiresMatchingRemoteTokenWhenTokenIsAvailable()
        {
            Assert.False(FoxRunInboundAuthorization.IsAuthorized(
                true,
                "0.0.0.0",
                true,
                "secret",
                "wrong",
                out var mismatch));
            Assert.True(FoxRunInboundAuthorization.IsAuthorized(
                true,
                "0.0.0.0",
                true,
                "secret",
                "secret",
                out var diagnostic));

            Assert.Contains("token", mismatch, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(diagnostic);
        }

        [Fact]
        public void RouterDispatchUsesRegistrationSnapshotWithoutPerMessageArrayCopy()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");
            var dispatch = TestSources.Slice(
                source,
                "public FoxRunInputDispatchResult Dispatch",
                "        private void AddSourceSnapshotEntry");

            Assert.Contains("Dictionary<string, Registration[]> _registrationSnapshots", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToArray()", dispatch, StringComparison.Ordinal);
        }

        private sealed class RecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public RecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int ApplyCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                ApplyCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class StagedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;
            private bool _hasPending;
            private byte _pending;

            public StagedRecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int AppliedCount { get; private set; }
            public byte LastAppliedValue { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                _pending = payload[0];
                _hasPending = true;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
            {
                if (!_hasPending)
                    return 0;

                LastAppliedValue = _pending;
                AppliedCount++;
                _hasPending = false;
                return 1;
            }
        }

        private sealed class MultiProviderInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo[] _topics =
            {
                new(
                    "/phase179/json",
                    FoxRunWireEncoding.Json,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.FoxgloveWebSocket,
                    supportsWebSocket: true,
                    supportsRos2Native: false),
                new(
                    "/phase179/dual",
                    FoxRunWireEncoding.Inherit,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.Inherit,
                    supportsWebSocket: true,
                    supportsRos2Native: true),
                new(
                    "/phase179/native",
                    FoxRunWireEncoding.Inherit,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.Ros2Native,
                    supportsWebSocket: true,
                    supportsRos2Native: true)
            };

            public int[] ApplyCounts { get; } = new int[3];
            public int FoxgloveInput_TopicCount => _topics.Length;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topics[index];

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                ApplyCounts[topicIndex]++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class NativeUnavailableCoexistenceInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo[] _topics =
            {
                new(
                    "/phase179/coexist/json",
                    FoxRunWireEncoding.Json,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.FoxgloveWebSocket,
                    supportsWebSocket: true,
                    supportsRos2Native: false),
                new(
                    "/phase179/coexist/protobuf",
                    FoxRunWireEncoding.Protobuf,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.FoxgloveWebSocket,
                    supportsWebSocket: true,
                    supportsRos2Native: false),
                new(
                    "/phase179/coexist/ordinary-dto",
                    FoxRunWireEncoding.Protobuf,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.Inherit,
                    supportsWebSocket: true,
                    supportsRos2Native: false)
            };

            public int[] ApplyCounts { get; } = new int[3];
            public int FoxgloveInput_TopicCount => _topics.Length;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topics[index];

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                ApplyCounts[topicIndex]++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class ThrowingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                throw new InvalidOperationException("staging failed");
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class ThrowingFlushInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingFlushInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
                => throw new InvalidOperationException("apply failed");
        }

        private sealed class InheritedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public InheritedRecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, FoxRunWireEncoding.Inherit, FoxRunFlow.Subscribe);
            }

            public int ApplyCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                ApplyCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private static byte[] ClientMessageFrame(uint channelId, byte[] payload)
        {
            var frame = new byte[5 + payload.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, channelId);
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        private sealed class RestartDecodingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo[] _topics =
            {
                new("/phase179/json", FoxRunWireEncoding.Json, FoxRunFlow.Subscribe),
                new("/phase179/protobuf", FoxRunWireEncoding.Inherit, FoxRunFlow.Subscribe)
            };

            public int JsonValue { get; private set; }
            public int ProtobufValue { get; private set; }
            private bool HasPendingJson { get; set; }
            private bool HasPendingProtobuf { get; set; }
            private int PendingJsonValue { get; set; }
            private int PendingProtobufValue { get; set; }
            public int FoxgloveInput_TopicCount => _topics.Length;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topics[index];

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                if (topicIndex == 0)
                {
                    if (!FoxRunInboundJson.TryRead(payload, "value", out int value, out error))
                        return false;
                    PendingJsonValue = value;
                    HasPendingJson = true;
                    return true;
                }

                if (!FoxRunInboundProtobuf.TryRead(payload, 1, out int protobufValue, out error))
                    return false;
                PendingProtobufValue = protobufValue;
                HasPendingProtobuf = true;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
            {
                var applied = 0;
                if (HasPendingJson)
                {
                    JsonValue = PendingJsonValue;
                    HasPendingJson = false;
                    applied++;
                }
                if (HasPendingProtobuf)
                {
                    ProtobufValue = PendingProtobufValue;
                    HasPendingProtobuf = false;
                    applied++;
                }
                return applied;
            }
        }

        private sealed class RestartInputTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void ReceiveText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void ReceiveBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
