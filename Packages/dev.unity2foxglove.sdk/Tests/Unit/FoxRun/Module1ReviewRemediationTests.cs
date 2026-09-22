using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Components.Publishing;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Module1ReviewRemediationTests
    {
        [Fact]
        public void ReplayForwardersInvokeEverySubscriberAfterOneThrows()
        {
            var logger = new RecordingLogger();
            var orchestrator = new ReplayOrchestrator(logger);
            var messages = new List<string>();
            orchestrator.OnReplayMessage += (_, _) => throw new InvalidOperationException("first");
            orchestrator.OnReplayMessage += (topic, _) => messages.Add(topic);
            Invoke(orchestrator, "SafeInvokeReplayMessage", "topic", new byte[] { 1 });
            Assert.Equal(new[] { "topic" }, messages);
            Assert.Single(logger.Warnings);
        }

        [Fact]
        public void ReplayContextAndBatchForwardersPreservePayloadAndMetadata()
        {
            var orchestrator = new ReplayOrchestrator(new RecordingLogger());
            ReplayMessageContext receivedMessage = default;
            ReplayBatchContext receivedBatch = default;
            orchestrator.OnReplayMessageContext += context => receivedMessage = context;
            orchestrator.OnReplayBatchCompleted += context => receivedBatch = context;
            var payload = new byte[] { 4, 5 };
            var message = new ReplayMessageContext(7, "/topic", "json", "schema", "ros", 11, 12, payload, 13);
            var batch = new ReplayBatchContext(21, 22, 3, "mcap", 23);
            Invoke(orchestrator, "SafeInvokeReplayMessageContext", message);
            Invoke(orchestrator, "SafeInvokeReplayBatchCompleted", batch);
            Assert.Equal(message.Topic, receivedMessage.Topic);
            Assert.Same(payload, receivedMessage.Payload);
            Assert.Equal(23UL, receivedBatch.ReplaySessionId);
            Assert.Equal(3, receivedBatch.MessageCount);
        }

        [Fact]
        public void ComponentSessionBuilderCapturesGenerationAndReferenceOwnership()
        {
            var publisher = new object();
            var snapshot = new ComponentPublisherSessionBuilder().Build(
                42,
                new[] { new ComponentPublisherContractDraft(
                    publisher, "scene/A", typeof(object), "mode", "/a", "schema.a",
                    PublisherEffectiveEncoding.Json, PublisherEffectiveEncoding.Json) });
            Assert.Equal(42UL, snapshot.Generation);
            Assert.True(snapshot.TryGetEntry(publisher, out var entry));
            Assert.Same(publisher, entry.Publisher);
            Assert.False(snapshot.TryGetEntry(new object(), out _));
        }

        [Fact]
        public void ComponentSessionBuilderRejectsUnsupportedAndOverBudgetEntries()
        {
            var unsupported = new ComponentPublisherContractDraft(
                new object(), "scene/A", typeof(object), "mode", "/a", "schema",
                PublisherEffectiveEncoding.MsgPack, PublisherEffectiveEncoding.Unsupported);
            var oversized = new ComponentPublisherContractDraft(
                new object(), "scene/B", typeof(object), "mode", new string('x', 600), "schema",
                PublisherEffectiveEncoding.Json, PublisherEffectiveEncoding.Json);
            var builder = new ComponentPublisherSessionBuilder();
            Assert.False(builder.Build(1, new[] { unsupported }).Entries.Single().IsAvailable);
            Assert.False(builder.Build(1, new[] { oversized }).Entries.Single().IsAvailable);
        }

        [Fact]
        public void ManagerTeardownRunsAllStepsAndRethrowsFirstFailure()
        {
            var calls = new List<string>();
            var error = Assert.Throws<InvalidOperationException>(() => FoxgloveManagerTeardownState.RunStopServer(
                () => { calls.Add("runtime"); throw new InvalidOperationException("first"); },
                () => calls.Add("clock"), () => calls.Add("remote"), () => calls.Add("cursor"),
                () => calls.Add("cert"), () => calls.Add("channels"), () => calls.Add("clients"),
                () => calls.Add("ids"), () => calls.Add("publishers")));
            Assert.Equal("first", error.Message);
            Assert.Equal(9, calls.Count);
            Assert.Equal("publishers", calls[^1]);
        }

        [Fact]
        public void RuntimeDisposeRetriesBeforeReleasingRuntimeReference()
        {
            var attempts = 0;
            var releases = 0;
            var reports = 0;
            FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(
                () => { attempts++; if (attempts == 1) throw new InvalidOperationException("partial"); },
                () => releases++, _ => reports++);
            Assert.Equal(2, attempts);
            Assert.Equal(1, releases);
            Assert.Equal(1, reports);
        }

        private static void Invoke(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

        private sealed class RecordingLogger : IFoxgloveLogger
        {
            public List<string> Warnings { get; } = new();
            public void LogWarning(string message) => Warnings.Add(message);
            public void LogError(string message) { }
        }
    }
}
