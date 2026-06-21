// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunInboundTests
    {
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
        public void RouterUsesGeneratedAllowlistAndRegistrationOrder()
        {
            var first = new RecordingInput("/phase157/cmd", 0);
            var second = new RecordingInput("/phase157/cmd", 0);
            var router = new FoxRunInputRouter();
            router.Register(first);
            router.Register(second);

            var result = router.Dispatch(
                "/phase157/cmd",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.Applied, result.Status);
            Assert.Equal(1, first.ApplyCount);
            Assert.Equal(1, second.ApplyCount);
        }

        [Fact]
        public void RouterRejectsUnknownOversizedAndRateLimitedMessages()
        {
            var input = new RecordingInput("/phase157/cmd", 0);
            var router = new FoxRunInputRouter(maxPayloadBytes: 16, maxMessagesPerSecondPerTopic: 1);
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/other", Array.Empty<byte>(), "json", 0).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.PayloadTooLarge,
                router.Dispatch("/phase157/cmd", new byte[17], "json", 0).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Applied,
                router.Dispatch("/phase157/cmd", Encoding.UTF8.GetBytes("{}"), "json", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.RateLimited,
                router.Dispatch("/phase157/cmd", Encoding.UTF8.GetBytes("{}"), "json", 1.1).Status);
        }

        [Fact]
        public void RouterUnregisterStopsAssignment()
        {
            var input = new RecordingInput("/phase157/cmd", 0);
            var router = new FoxRunInputRouter();
            router.Register(input);
            router.Unregister(input);

            var result = router.Dispatch("/phase157/cmd", Array.Empty<byte>(), "json", 1);

            Assert.Equal(FoxRunInputDispatchStatus.UnknownTopic, result.Status);
            Assert.Equal(0, input.ApplyCount);
        }

        [Fact]
        public void RouterIsolatesAssignmentExceptionsAndContinuesInRegistrationOrder()
        {
            var throwing = new ThrowingInput("/phase157/cmd");
            var recording = new RecordingInput("/phase157/cmd", 0);
            var router = new FoxRunInputRouter();
            router.Register(throwing);
            router.Register(recording);

            var result = router.Dispatch(
                "/phase157/cmd",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.Applied, result.Status);
            Assert.Equal(1, result.AppliedCount);
            Assert.Contains("assignment failed", result.Diagnostic, StringComparison.Ordinal);
            Assert.Equal(1, recording.ApplyCount);
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.20.30.40")]
        [InlineData("localhost")]
        [InlineData("::1")]
        public void InboundAuthorizationAllowsEnabledLoopback(string host)
        {
            Assert.True(FoxRunInboundAuthorization.IsAuthorized(
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
            Assert.False(FoxRunInboundAuthorization.IsAuthorized(
                true,
                "0.0.0.0",
                false,
                "secret",
                out var noOptIn));
            Assert.False(FoxRunInboundAuthorization.IsAuthorized(
                true,
                "0.0.0.0",
                true,
                "",
                out var noToken));
            Assert.Contains("explicitly enabled", noOptIn, StringComparison.Ordinal);
            Assert.Contains("shared token", noToken, StringComparison.Ordinal);
        }

        private sealed class RecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public RecordingInput(string topic, int index)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunMode.SubscribeOnly);
            }

            public int ApplyCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryApply(int topicIndex, byte[] payload, string encoding, out string error)
            {
                ApplyCount++;
                error = string.Empty;
                return true;
            }
        }

        private sealed class ThrowingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunMode.SubscribeOnly);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryApply(int topicIndex, byte[] payload, string encoding, out string error)
            {
                throw new InvalidOperationException("assignment failed");
            }
        }
    }
}
