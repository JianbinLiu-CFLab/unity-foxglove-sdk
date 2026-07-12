// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.UnitTests.Harness;
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

            Assert.Equal(FoxRunInputDispatchStatus.Applied, result.Status);
            Assert.Equal(1, first.ApplyCount);
            Assert.Equal(1, second.ApplyCount);
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
                FoxRunInputDispatchStatus.Applied,
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
        public void RouterResolvesInheritedInputAgainstTheCurrentSessionDefault()
        {
            var input = new InheritedRecordingInput("/phase175/inherit");
            var router = new FoxRunInputRouter();
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Applied,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "protobuf", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.DecodeRejected,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "json", 2).Status);
            Assert.Equal(1, input.ApplyCount);

            router.DefaultWireEncoding = FoxRunWireEncoding.Json;

            Assert.Equal(
                FoxRunInputDispatchStatus.Applied,
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
            var dispatch = TestSources.Slice(source, "public FoxRunInputDispatchResult Dispatch", "        private bool AcceptRate");

            Assert.Contains("Dictionary<string, Registration[]> _registrationSnapshots", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToArray()", dispatch, StringComparison.Ordinal);
        }

        private sealed class RecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public RecordingInput(string topic)
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

        private sealed class InheritedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public InheritedRecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, FoxRunWireEncoding.Inherit, FoxRunMode.SubscribeOnly);
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
    }
}
