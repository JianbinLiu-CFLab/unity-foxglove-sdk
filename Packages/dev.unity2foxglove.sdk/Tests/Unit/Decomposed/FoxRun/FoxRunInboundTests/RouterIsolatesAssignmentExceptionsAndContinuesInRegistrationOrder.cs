// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.UnitTests.Harness;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunInboundTests
    {
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

        private sealed class SilentRejectingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public SilentRejectingInput(string topic)
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
                return false;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class OwnedRecordingInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;
            private readonly bool _ready;

            public OwnedRecordingInput(string topic, bool ready = true)
            {
                _topic = InputTopic(
                    topic,
                    FoxRunEncoding.JSON,
                    isStream: true);
                _ready = ready;
            }

            public int ClearCount { get; private set; }
            public int StageCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                StageCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                error = _ready
                    ? string.Empty
                    : "FoxRunStream field must be initialized before registration.";
                return _ready;
            }
            public void FoxgloveInput_ClearOwned(int topicIndex) => ClearCount++;
        }

        private sealed class BlockingOwnedStreamInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public BlockingOwnedStreamInput(string topic)
            {
                _topic = InputTopic(
                    topic,
                    FoxRunEncoding.JSON,
                    isStream: true);
                Stream = new FoxRunStream<byte>();
            }

            public ManualResetEventSlim StageEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim FinishStage { get; } = new ManualResetEventSlim();
            public FoxRunStream<byte> Stream { get; }
            public int ClearCount;
            public int DisposedCount;
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                StageEntered.Set();
                if (!FinishStage.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The teardown race did not release staging.");
                Stream.TryEnqueueOwned(
                    payload[0],
                    _ => Interlocked.Increment(ref DisposedCount));
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                error = string.Empty;
                return true;
            }

            public void FoxgloveInput_ClearOwned(int topicIndex)
            {
                Interlocked.Increment(ref ClearCount);
                Stream.Clear();
            }
        }

        private sealed class ThrowingSecondTopicOwnedInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo _first = InputTopic(
                "/phase184/partial",
                FoxRunEncoding.JSON,
                isStream: true);

            public int ClearCount { get; private set; }
            public int FoxgloveInput_TopicCount => 2;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index)
                => index == 0 ? _first : throw new InvalidOperationException("second topic failed");
            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                error = string.Empty;
                return true;
            }
            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                error = string.Empty;
                return true;
            }
            public void FoxgloveInput_ClearOwned(int topicIndex) => ClearCount++;
        }

        private sealed class ThrowingSecondAcquireOwnedInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo[] _topics =
            {
                InputTopic(
                    "/phase184/acquire-first",
                    FoxRunEncoding.JSON,
                    isStream: true),
                InputTopic(
                    "/phase184/acquire-second",
                    FoxRunEncoding.JSON,
                    isStream: true),
            };

            public int FirstClearCount { get; private set; }
            public int SecondClearCount { get; private set; }
            public int FoxgloveInput_TopicCount => _topics.Length;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topics[index];
            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                error = string.Empty;
                return true;
            }
            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                if (topicIndex == 1)
                    throw new InvalidOperationException("second ownership acquisition failed");
                error = string.Empty;
                return true;
            }
            public void FoxgloveInput_ClearOwned(int topicIndex)
            {
                if (topicIndex == 0)
                    FirstClearCount++;
                else if (topicIndex == 1)
                    SecondClearCount++;
            }
        }

    }
}
