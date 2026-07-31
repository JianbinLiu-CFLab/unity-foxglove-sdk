// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 171 mirror sink routing behavior for optional remote gateway output.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.UnitTests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class Ros2BridgeNetworkTimingSerialCollection
    {
        public const string Name = "ROS 2 Bridge network timing serial";
    }

    [Trait("Phase", "171")]
    [Trait("Domain", "Routing")]
    [Collection(Ros2BridgeNetworkTimingSerialCollection.Name)]
    public sealed class MirrorSinkRoutingTests
    {
        [Fact]
        public void ReplaySuppressionWarningsDeduplicateUntilCleared()
        {
            var logger = new RecordingLogger();
            using var runtime = new FoxgloveRuntime(
                new MirrorTransport(),
                new SystemClock(),
                new DefaultSchemaRegistry(),
                logger);

            InvokeReplaySuppression(runtime, "WarnReplaySuppressed", "Publish", 7U);
            InvokeReplaySuppression(runtime, "WarnReplaySuppressed", "Publish", 7U);

            Assert.Single(logger.Warnings);
            Assert.Contains("Publish for channel 7", logger.Warnings[0], StringComparison.Ordinal);

            InvokeReplaySuppression(runtime, "ClearReplaySuppressionWarnings");
            InvokeReplaySuppression(runtime, "WarnReplaySuppressed", "Publish", 7U);

            Assert.Equal(2, logger.Warnings.Count);
        }

        [Fact]
        public void SessionMirrorSinkReceivesRegistrationDemandAndPublish()
        {
            var transport = new MirrorTransport();
            using var session = new FoxgloveSession("mirror-session", transport, schemaRegistry: new DefaultSchemaRegistry());
            var sink = new RecordingMirrorSink();
            session.SetMirrorSink(sink);

            RegisterJsonChannel(session, 1, "/mirror/session");
            Assert.True(session.HasChannelDemand(1));

            var payload = new byte[] { 1, 2, 3 };
            session.Publish(1, payload, 1710UL);

            Assert.Single(sink.Registered);
            Assert.Equal("/mirror/session", sink.Registered[0].Topic);
            Assert.Single(sink.Published);
            Assert.Equal(1U, sink.Published[0].Channel.Id);
            Assert.Equal(1710UL, sink.Published[0].LogTimeNs);
            Assert.Same(payload, sink.Published[0].Payload);
        }

        [Fact]
        public void RuntimeCanAttachMirrorSinkAfterChannelsExist()
        {
            var transport = new MirrorTransport();
            using var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("mirror-runtime");
            runtime.RegisterChannel(new AdvertiseChannel
            {
                Id = 7,
                Topic = "/mirror/runtime",
                Encoding = "json",
                SchemaName = "Mirror.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });
            Assert.False(runtime.HasChannelDemand(7));

            var sink = new RecordingMirrorSink();
            runtime.SetMirrorSink(sink);

            Assert.True(runtime.HasChannelDemand(7));
            Assert.Single(sink.Registered);
            runtime.Publish(7, new byte[] { 9 }, 1711UL);
            Assert.Single(sink.Published);

            runtime.SetMirrorSink(null);
            Assert.False(runtime.HasChannelDemand(7));
            Assert.Single(sink.Unregistered);
            Assert.Equal(7U, sink.Unregistered[0]);
            runtime.Publish(7, new byte[] { 10 }, 1712UL);
            Assert.Single(sink.Published);
        }

        [Fact]
        public void RecordingOnlyChannelNeverLeaksIntoLateMirrorAndMirrorDemandIsNotRecordingDemand()
        {
            var transport = new MirrorTransport();
            using var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            runtime.Start("recording-only-mirror-isolation");
            runtime.RegisterRecordingOnlyChannel(RecordingOnlyChannel(17));

            var first = new RecordingMirrorSink();
            runtime.SetMirrorSink(first);

            Assert.Empty(first.Registered);
            Assert.False(runtime.HasRecordingDemand(17));
            Assert.False(runtime.PublishRecordingOnlyRos2Cdr(
                17,
                new byte[] { 0, 1, 0, 0 },
                184_017UL));

            var second = new RecordingMirrorSink();
            runtime.SetMirrorSink(second);

            Assert.Empty(first.Unregistered);
            Assert.Empty(second.Registered);
            Assert.Empty(first.Published);
            Assert.Empty(second.Published);
            Assert.Empty(transport.BroadcastTexts);
            Assert.Empty(transport.BroadcastBinaries);
        }

        [Fact]
        public void RecordingOnlyChannelWritesMcapWithoutLiveOrMirrorAdvertisement()
        {
            var transport = new MirrorTransport();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession(
                "recording-only-mcap",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var mirror = new RecordingMirrorSink();
            session.SetMirrorSink(mirror);
            session.SetRecorder(recorder);
            session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(18));

            Assert.True(session.HasRecordingDemand(18));
            session.PublishRos2Cdr(18, new byte[] { 0, 1, 0, 0, 42 }, 184_018UL);

            session.SetRecorder(null);
            recorder.Close();
            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();

            var message = Assert.Single(result.Messages);
            Assert.Equal(184_018UL, message.LogTime);
            Assert.Equal(new byte[] { 0, 1, 0, 0, 42 }, message.Data);
            Assert.Empty(mirror.Registered);
            Assert.Empty(mirror.Published);
            Assert.Empty(transport.BroadcastTexts);
            Assert.Empty(transport.BroadcastBinaries);
        }

        [Fact]
        public void RecordingOnlyChannelNeverAppearsInLateClientSnapshot()
        {
            var transport = new MirrorTransport();
            using var session = new FoxgloveSession(
                "recording-only-late-client",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            RegisterJsonChannel(session, 19, "/live/visible");
            session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(20));

            transport.Connect(184U);

            Assert.Contains(
                transport.TargetTexts,
                sent => sent.ClientId == 184U
                        && sent.Json.Contains("\"op\":\"advertise\"", StringComparison.Ordinal)
                        && sent.Json.Contains("/live/visible", StringComparison.Ordinal));
            Assert.DoesNotContain(
                transport.TargetTexts,
                sent => sent.Json.Contains("/recording/only/20", StringComparison.Ordinal)
                        || sent.Json.Contains("phase184_msgs/msg/Hidden", StringComparison.Ordinal));
        }

        [Fact]
        public void RecordingOnlyVisibilityTransitionCleansAndRestoresLiveAndMirrorState()
        {
            var transport = new MirrorTransport();
            using var session = new FoxgloveSession(
                "recording-only-transition",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var mirror = new RecordingMirrorSink();
            session.SetMirrorSink(mirror);
            var live = new AdvertiseChannel
            {
                Id = 21,
                Topic = "/live/transition",
                Encoding = "json",
                SchemaName = "Mirror.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            };

            session.RegisterChannel(live);
            Assert.Single(mirror.Registered);
            transport.BroadcastTexts.Clear();

            session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(21));

            Assert.Equal(new[] { 21U }, mirror.Unregistered);
            Assert.Contains(
                transport.BroadcastTexts,
                json => json.Contains("\"op\":\"unadvertise\"", StringComparison.Ordinal));
            Assert.DoesNotContain(
                transport.BroadcastTexts,
                json => json.Contains("phase184_msgs/msg/Hidden", StringComparison.Ordinal));

            transport.BroadcastTexts.Clear();
            session.RegisterChannel(live);

            Assert.Equal(2, mirror.Registered.Count);
            Assert.Contains(
                transport.BroadcastTexts,
                json => json.Contains("\"op\":\"advertise\"", StringComparison.Ordinal)
                        && json.Contains("/live/transition", StringComparison.Ordinal));
        }

        [Fact]
        public void AttachedRecorderRejectsLiveToRecordingOnlyDescriptorReplacement()
        {
            var transport = new MirrorTransport();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession(
                "recording-only-live-conflict",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            session.SetRecorder(recorder);
            RegisterJsonChannel(session, 22, "/live/recorded");

            var error = Assert.Throws<InvalidOperationException>(
                () => session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(22)));

            Assert.Contains("MCAP", error.Message, StringComparison.Ordinal);
            Assert.Equal("/live/recorded", session.Channels.Get(22).Topic);
            Assert.False(session.HasRecordingDemand(22));
            var payload = new byte[] { 1, 8, 4 };
            session.Publish(22, payload, 184_022UL);

            session.SetRecorder(null);
            recorder.Close();
            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();
            var channel = Assert.Single(result.Summary.Channels);
            var schema = Assert.Single(result.Summary.Schemas);

            Assert.Equal("/live/recorded", channel.Topic);
            Assert.Equal("json", channel.MessageEncoding);
            Assert.Equal("Mirror.Schema", schema.Name);
            Assert.Equal("jsonschema", schema.Encoding);
            Assert.Equal(payload, Assert.Single(result.Messages).Data);
            Assert.DoesNotContain(
                transport.BroadcastTexts,
                json => json.Contains("phase184_msgs/msg/Hidden", StringComparison.Ordinal));
        }

        [Fact]
        public void AttachedRecorderRejectsRecordingOnlyToLiveDescriptorReplacement()
        {
            var transport = new MirrorTransport();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession(
                "recording-only-hidden-conflict",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            session.SetRecorder(recorder);
            session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(23));

            var error = Assert.Throws<InvalidOperationException>(
                () => RegisterJsonChannel(session, 23, "/live/replacement"));

            Assert.Contains("MCAP", error.Message, StringComparison.Ordinal);
            Assert.Equal("/recording/only/23", session.Channels.Get(23).Topic);
            Assert.True(session.HasRecordingDemand(23));
            var payload = new byte[] { 0, 1, 0, 0, 23 };
            session.PublishRos2Cdr(23, payload, 184_023UL);

            session.SetRecorder(null);
            recorder.Close();
            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();
            var channel = Assert.Single(result.Summary.Channels);
            var schema = Assert.Single(result.Summary.Schemas);

            Assert.Equal("/recording/only/23", channel.Topic);
            Assert.Equal("cdr", channel.MessageEncoding);
            Assert.Equal("phase184_msgs/msg/Hidden", schema.Name);
            Assert.Equal("ros2msg", schema.Encoding);
            Assert.Equal(payload, Assert.Single(result.Messages).Data);
            Assert.DoesNotContain(
                transport.BroadcastTexts,
                json => json.Contains("/live/replacement", StringComparison.Ordinal));
        }

        [Fact]
        public async Task RecordingOnlyTransitionSerializesLateConnectAndSubscribe()
        {
            var transport = new MirrorTransport();
            using var session = new FoxgloveSession(
                "recording-only-atomic-transition",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var mirror = new BlockingUnregisterMirrorSink();
            session.SetMirrorSink(mirror);
            RegisterJsonChannel(session, 24, "/live/atomic-transition");
            transport.BroadcastTexts.Clear();

            var transition = Task.Run(
                () => session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(24)));
            Assert.True(mirror.UnregisterEntered.Wait(TimeSpan.FromSeconds(5)));

            using var connectStarted = new ManualResetEventSlim();
            using var subscribeStarted = new ManualResetEventSlim();
            var connect = Task.Run(() =>
            {
                connectStarted.Set();
                transport.Connect(184U);
            });
            var subscribe = Task.Run(() =>
            {
                subscribeStarted.Set();
                transport.ReceiveText(
                    184U,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":184,\"channelId\":24}]}");
            });

            try
            {
                Assert.True(connectStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(subscribeStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.NotSame(connect, await Task.WhenAny(connect, Task.Delay(100)));
                Assert.NotSame(subscribe, await Task.WhenAny(subscribe, Task.Delay(100)));
            }
            finally
            {
                mirror.Release.Set();
                await Task.WhenAll(transition, connect, subscribe).WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.DoesNotContain(
                transport.TargetTexts,
                sent => sent.Json.Contains("/recording/only/24", StringComparison.Ordinal)
                        || sent.Json.Contains("phase184_msgs/msg/Hidden", StringComparison.Ordinal));
            Assert.False(session.HasChannelDemand(24));
        }

        [Fact]
        public void ReassertedRecordingOnlyChannelSeedsReplacementRecorder()
        {
            var transport = new MirrorTransport();
            using var firstStream = new MemoryStream();
            using var secondStream = new MemoryStream();
            using var firstRecorder = new McapRecorder(firstStream);
            using var secondRecorder = new McapRecorder(secondStream);
            using var session = new FoxgloveSession(
                "recording-only-recorder-replacement",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var channel = RecordingOnlyChannel(25);
            var firstPayload = new byte[] { 0, 1, 0, 0, 25 };
            var secondPayload = new byte[] { 0, 1, 0, 0, 26 };

            session.SetRecorder(firstRecorder);
            session.RegisterRecordingOnlyChannel(channel);
            session.PublishRos2Cdr(25, firstPayload, 184_025UL);
            session.SetRecorder(null);
            firstRecorder.Close();

            session.SetRecorder(secondRecorder);
            session.RegisterRecordingOnlyChannel(channel);
            Assert.True(session.HasRecordingDemand(25));
            session.PublishRos2Cdr(25, secondPayload, 184_026UL);
            session.SetRecorder(null);
            secondRecorder.Close();

            AssertRecordedPayload(firstStream, firstPayload);
            AssertRecordedPayload(secondStream, secondPayload);
        }

        [Fact]
        public void ReassertedRecordingOnlyChannelSeedsNewlyAllowingMcapFilter()
        {
            var transport = new MirrorTransport();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession(
                "recording-only-filter-transition",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var filter = new MutableSinkFilter { Allow = false };
            var channel = RecordingOnlyChannel(26);
            var payload = new byte[] { 0, 1, 0, 0, 27 };
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, filter);
            session.SetRecorder(recorder);

            session.RegisterRecordingOnlyChannel(channel);
            Assert.False(session.HasRecordingDemand(26));
            filter.Allow = true;
            session.RegisterRecordingOnlyChannel(channel);
            Assert.True(session.HasRecordingDemand(26));
            session.PublishRos2Cdr(26, payload, 184_027UL);

            session.SetRecorder(null);
            recorder.Close();
            AssertRecordedPayload(stream, payload);
        }

        [Fact]
        public void RejectedMcapChannelNeverReportsDemandOrPublishSuccess()
        {
            var transport = new MirrorTransport();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            runtime.Start("recording-only-rejected-channel");
            runtime.Session.SetRecorder(recorder);
            var accepted = new AdvertiseChannel
            {
                Id = 27,
                Topic = "/recording/conflicting-signature",
                Encoding = "cdr",
                SchemaName = "phase184_msgs/msg/Accepted",
                SchemaEncoding = "ros2msg",
                Schema = "uint8 value"
            };
            var rejected = new AdvertiseChannel
            {
                Id = 28,
                Topic = accepted.Topic,
                Encoding = "cdr",
                SchemaName = "phase184_msgs/msg/Rejected",
                SchemaEncoding = "ros2msg",
                Schema = "uint16 value"
            };
            runtime.RegisterRecordingOnlyChannel(accepted);
            runtime.RegisterRecordingOnlyChannel(rejected);

            Assert.True(runtime.HasRecordingDemand(27));
            Assert.False(runtime.HasRecordingDemand(28));
            Assert.True(runtime.PublishRecordingOnlyRos2Cdr(
                27,
                new byte[] { 0, 1, 0, 0, 27 },
                184_028UL));
            Assert.False(runtime.PublishRecordingOnlyRos2Cdr(
                28,
                new byte[] { 0, 1, 0, 0, 28 },
                184_029UL));

            runtime.Session.SetRecorder(null);
            recorder.Close();
            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();
            var message = Assert.Single(result.Messages);
            Assert.Equal(new byte[] { 0, 1, 0, 0, 27 }, message.Data);
            Assert.Single(result.Summary.Channels);
        }

        [Fact]
        public void RecordingOnlySignatureConflictIsDeduplicatedPerRecorder()
        {
            var transport = new MirrorTransport();
            var logger = new RecordingLogger();
            using var firstStream = new MemoryStream();
            using var secondStream = new MemoryStream();
            using var firstRecorder = new McapRecorder(firstStream, logger);
            using var secondRecorder = new McapRecorder(secondStream, logger);
            using var session = new FoxgloveSession(
                "recording-only-signature-conflict",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var hidden = RecordingOnlyChannel(31);
            hidden.Topic = "/foo";

            session.SetRecorder(firstRecorder);
            RegisterJsonChannel(session, 30, "/foo");
            session.RegisterRecordingOnlyChannel(hidden);
            session.RegisterRecordingOnlyChannel(hidden);
            session.RegisterRecordingOnlyChannel(hidden);

            Assert.False(session.HasRecordingDemand(31));
            Assert.Single(
                logger.Warnings,
                warning => warning.Contains(
                    "skipping server channel for topic '/foo'",
                    StringComparison.Ordinal));

            session.UnregisterChannel(30);
            session.SetRecorder(null);
            firstRecorder.Close();
            session.SetRecorder(secondRecorder);
            session.RegisterRecordingOnlyChannel(hidden);

            Assert.True(session.HasRecordingDemand(31));
            Assert.True(secondRecorder.HasServerChannel(31));
            Assert.Single(
                logger.Warnings,
                warning => warning.Contains(
                    "skipping server channel for topic '/foo'",
                    StringComparison.Ordinal));
        }

        [Fact]
        public void McapFilterGenerationReopensOneRecordingOnlyAdmissionAttempt()
        {
            var transport = new MirrorTransport();
            var logger = new RecordingLogger();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream, logger);
            using var session = new FoxgloveSession(
                "recording-only-filter-conflict",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var filter = new MutableSinkFilter { Allow = true };
            var hidden = RecordingOnlyChannel(33);
            hidden.Topic = "/foo";
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, filter);
            session.SetRecorder(recorder);
            RegisterJsonChannel(session, 32, "/foo");

            session.RegisterRecordingOnlyChannel(hidden);
            session.RegisterRecordingOnlyChannel(hidden);

            Assert.Single(
                logger.Warnings,
                warning => warning.Contains(
                    "skipping server channel for topic '/foo'",
                    StringComparison.Ordinal));

            filter.Allow = false;
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, filter);
            session.RegisterRecordingOnlyChannel(hidden);
            filter.Allow = true;
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, filter);
            session.RegisterRecordingOnlyChannel(hidden);
            session.RegisterRecordingOnlyChannel(hidden);

            Assert.Equal(
                2,
                logger.Warnings.Count(
                    warning => warning.Contains(
                        "skipping server channel for topic '/foo'",
                        StringComparison.Ordinal)));
        }

        [Fact]
        public void McapAdmissionWriteFailureLeavesPriorLiveTopologyIntact()
        {
            var transport = new MirrorTransport();
            using var stream = new ToggleFailingMemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession(
                "recording-only-write-failure",
                transport,
                schemaRegistry: new DefaultSchemaRegistry());
            var filter = new MutableSinkFilter { Allow = false };
            var mirror = new RecordingMirrorSink();
            session.SetSinkChannelFilter(FoxgloveSinkKind.McapRecording, filter);
            session.SetRecorder(recorder);
            session.SetMirrorSink(mirror);
            RegisterJsonChannel(session, 29, "/live/write-failure");
            transport.BroadcastTexts.Clear();
            filter.Allow = true;
            stream.FailNextWrite = true;

            Assert.Throws<IOException>(
                () => session.RegisterRecordingOnlyChannel(RecordingOnlyChannel(29)));

            Assert.False(recorder.HasServerChannel(29));
            Assert.Equal("/live/write-failure", session.Channels.Get(29).Topic);
            Assert.Empty(mirror.Unregistered);
            Assert.DoesNotContain(
                transport.BroadcastTexts,
                json => json.Contains("\"op\":\"unadvertise\"", StringComparison.Ordinal)
                        || json.Contains("/recording/only/29", StringComparison.Ordinal));
        }

        [Fact]
        public void RecordingOnlyOwnershipSurfaceRemainsExplicit()
        {
            var recordingOnlyChannels = typeof(FoxgloveSession).GetField(
                "_recordingOnlyChannels",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(recordingOnlyChannels);
            Assert.Equal(
                typeof(HashSet<uint>),
                recordingOnlyChannels.FieldType);
            var channelLifecycleLock = typeof(FoxgloveSession).GetField(
                "_channelLifecycleLock",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(channelLifecycleLock);
            Assert.Equal(typeof(object), channelLifecycleLock.FieldType);

            AssertInternalInstanceMethod(
                typeof(FoxgloveSession),
                nameof(FoxgloveSession.RegisterRecordingOnlyChannel));
            AssertInternalInstanceMethod(
                typeof(FoxgloveSession),
                nameof(FoxgloveSession.HasRecordingDemand));
            AssertInternalInstanceMethod(
                typeof(FoxgloveRuntime),
                nameof(FoxgloveRuntime.RegisterRecordingOnlyChannel));
            Assert.NotNull(
                typeof(FoxgloveRuntime).GetMethod(
                    nameof(FoxgloveRuntime.HasRecordingDemand),
                    BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(
                typeof(FoxgloveRuntime).GetMethod(
                    nameof(FoxgloveRuntime.PublishRecordingOnly),
                    BindingFlags.Instance | BindingFlags.Public));
        }

        private static AdvertiseChannel RecordingOnlyChannel(uint id)
            => new AdvertiseChannel
            {
                Id = id,
                Topic = "/recording/only/" + id,
                Encoding = "cdr",
                SchemaName = "phase184_msgs/msg/Hidden",
                SchemaEncoding = "ros2msg",
                Schema = "uint8 value"
            };

        private static void RegisterJsonChannel(FoxgloveSession session, uint id, string topic)
            => session.RegisterChannel(new AdvertiseChannel
            {
                Id = id,
                Topic = topic,
                Encoding = "json",
                SchemaName = "Mirror.Schema",
                SchemaEncoding = "jsonschema",
                Schema = "{}"
            });

        private static void AssertRecordedPayload(MemoryStream stream, byte[] expected)
        {
            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();
            Assert.Equal(expected, Assert.Single(result.Messages).Data);
        }

        private static void InvokeReplaySuppression(FoxgloveRuntime runtime, string methodName, params object[] arguments)
        {
            var method = typeof(FoxgloveRuntime).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(runtime, arguments);
        }

        private static void AssertInternalInstanceMethod(Type type, string methodName)
        {
            Assert.Null(type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public));
            Assert.NotNull(type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic));
        }

        private static void AssertInternalStaticMethod(Type type, string methodName)
        {
            Assert.Null(type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public));
            Assert.NotNull(type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic));
        }

        private sealed class RecordingLogger : IFoxgloveLogger
        {
            public readonly List<string> Warnings = new List<string>();

            public void LogWarning(string message) => Warnings.Add(message);
            public void LogError(string message) => throw new InvalidOperationException(message);
        }

        private sealed class RecordingMirrorSink : IFoxgloveMirrorSink
        {
            public readonly List<AdvertiseChannel> Registered = new List<AdvertiseChannel>();
            public readonly List<uint> Unregistered = new List<uint>();
            public readonly List<PublishRecord> Published = new List<PublishRecord>();

            public bool HasChannelDemand(AdvertiseChannel channel) => channel != null;
            public void RegisterChannel(AdvertiseChannel channel) => Registered.Add(channel);
            public void UnregisterChannel(uint channelId) => Unregistered.Add(channelId);
            public void Publish(AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
                => Published.Add(new PublishRecord(channel, logTimeNs, payload));
        }

        private sealed class BlockingUnregisterMirrorSink : IFoxgloveMirrorSink
        {
            public readonly ManualResetEventSlim UnregisterEntered = new ManualResetEventSlim();
            public readonly ManualResetEventSlim Release = new ManualResetEventSlim();

            public bool HasChannelDemand(AdvertiseChannel channel) => channel != null;
            public void RegisterChannel(AdvertiseChannel channel) { }

            public void UnregisterChannel(uint channelId)
            {
                UnregisterEntered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("Timed out waiting to release the blocking mirror sink.");
            }

            public void Publish(AdvertiseChannel channel, ulong logTimeNs, byte[] payload) { }
        }

        private sealed class MutableSinkFilter : ISinkChannelFilter
        {
            public bool Allow { get; set; }
            public bool AllowChannel(SinkChannelFilterContext context) => Allow;
        }

        private sealed class ToggleFailingMemoryStream : MemoryStream
        {
            public bool FailNextWrite { get; set; }

            public override void Write(byte[] buffer, int offset, int count)
            {
                ThrowIfRequested();
                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                ThrowIfRequested();
                base.Write(buffer);
            }

            public override void WriteByte(byte value)
            {
                ThrowIfRequested();
                base.WriteByte(value);
            }

            private void ThrowIfRequested()
            {
                if (!FailNextWrite)
                    return;
                FailNextWrite = false;
                throw new IOException("Injected MCAP admission write failure.");
            }
        }

        private readonly struct PublishRecord
        {
            public PublishRecord(AdvertiseChannel channel, ulong logTimeNs, byte[] payload)
            {
                Channel = channel;
                LogTimeNs = logTimeNs;
                Payload = payload;
            }

            public AdvertiseChannel Channel { get; }
            public ulong LogTimeNs { get; }
            public byte[] Payload { get; }
        }

        private sealed class MirrorTransport : IFoxgloveTransport
        {
            public readonly List<string> BroadcastTexts = new List<string>();
            public readonly List<byte[]> BroadcastBinaries = new List<byte[]>();
            public readonly List<TargetText> TargetTexts = new List<TargetText>();
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) => BroadcastBinaries.Add(data);
            public void SendText(uint clientId, string json)
                => TargetTexts.Add(new TargetText(clientId, json));
            public void SendBinary(uint clientId, byte[] data) { }

            public void Connect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void ReceiveText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }

        private readonly struct TargetText
        {
            public TargetText(uint clientId, string json)
            {
                ClientId = clientId;
                Json = json ?? string.Empty;
            }

            public uint ClientId { get; }
            public string Json { get; }
        }
    }
}
