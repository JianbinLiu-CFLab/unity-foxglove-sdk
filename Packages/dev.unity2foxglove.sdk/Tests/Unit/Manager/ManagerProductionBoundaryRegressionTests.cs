#if MANAGER_PRODUCTION_BOUNDARY
// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Exercise the Manager and publisher production boundaries directly.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.UnitTests.Harness;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Manager
{
    public sealed partial class ManagerProductionBoundaryRegressionTests
    {
        [Fact]
        public void ManagerCaptureResolvesPublisherWhenManagerLifecycleRunsFirst()
        {
            UnityEngine.Object.ResetRegistry();
            var manager = new FoxgloveManager();
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.gameObject = manager.gameObject;
            UnityEngine.Object.Register(manager);
            UnityEngine.Object.Register(publisher);

            // Deliberately do not call publisher.OnEnable. Capture itself must
            // resolve the Manager before the publisher lifecycle callback.
            var snapshot = manager.CaptureForTest(41UL);

            Assert.Same(manager, publisher.ConfiguredManager);
            Assert.True(snapshot.IsAvailable);
            Assert.Equal(41UL, snapshot.Generation);
            Assert.True(snapshot.TryGetEntry(publisher, out _));
        }

        [Fact]
        public void ComponentSessionStopTailClearsActualManagerSnapshot()
        {
            UnityEngine.Object.ResetRegistry();
            var manager = new FoxgloveManager();
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.gameObject = manager.gameObject;
            UnityEngine.Object.Register(manager);
            UnityEngine.Object.Register(publisher);

            manager.SetActiveSessionForTest(manager.CaptureForTest(7UL));
            Assert.NotNull(manager.ActiveComponentPublisherSession);

            // This calls the same production stop-tail method used by the
            // FoxgloveManager.StopServer cleanup tail.
            manager.ClearActiveSessionForTest();

            Assert.Null(manager.ActiveComponentPublisherSession);
        }

        [Fact]
        public void ManagerAttachPublishesTheSameGenerationAsTheActiveSnapshot()
        {
            UnityEngine.Object.ResetRegistry();
            var manager = new FoxgloveManager();
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.gameObject = manager.gameObject;
            UnityEngine.Object.Register(manager);
            UnityEngine.Object.Register(publisher);

            var generation = manager.AttachComponentSessionForTest();

            Assert.NotNull(manager.ActiveComponentPublisherSession);
            Assert.Equal(generation, manager.ActiveComponentPublisherSession.Generation);
            Assert.True(manager.ActiveComponentPublisherSession.TryGetEntry(publisher, out _));
        }

        [Fact]
        public void SerializedManagerProfileMutationStaysDeferredUntilFoxRunSessionRestart()
        {
            var manager = new FoxgloveManager
            {
                DefaultPublishRateHz = 11f,
                DefaultFoxRunPublishEncoding = FoxRunEncoding.JSON
            };
            manager.BeginFoxRunPublishSessionForTest();
            var first = manager.ActiveFoxRunPublishSessionPolicy;
            Assert.True(first.SessionActive);
            Assert.Equal(FoxRunEncoding.JSON, first.WebSocketEncoding);
            Assert.Equal(11f, first.DefaultPublishRateHz);

            var serializedObject = new BoundaryManagerSerializedObject(manager);
            serializedObject.FindProperty("_defaultFoxRunPublishEncoding").enumValueIndex =
                (int)FoxRunEncoding.MessagePack;
            serializedObject.FindProperty("_defaultPublishRateHz").floatValue = 42f;
            Assert.True(serializedObject.ApplyModifiedProperties());

            var active = manager.ActiveFoxRunPublishSessionPolicy;
            Assert.Same(first, active);
            Assert.Equal(FoxRunEncoding.JSON, active.WebSocketEncoding);
            Assert.Equal(11f, active.DefaultPublishRateHz);

            manager.EndFoxRunPublishSessionForTest();
            manager.BeginFoxRunPublishSessionForTest();
            var recaptured = manager.ActiveFoxRunPublishSessionPolicy;
            Assert.NotSame(first, recaptured);
            Assert.Equal(FoxRunEncoding.MessagePack, recaptured.WebSocketEncoding);
            Assert.Equal(42f, recaptured.DefaultPublishRateHz);
        }

        [Fact]
        public void ManagerAttachStampsClientEventsAndActivatesAdmissionForTheSameGeneration()
        {
            UnityEngine.Object.ResetRegistry();
            var manager = new FoxgloveManager();
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.gameObject = manager.gameObject;
            UnityEngine.Object.Register(manager);
            UnityEngine.Object.Register(publisher);

            var generation = manager.AttachComponentSessionForTest(keepSessionAlive: true);
            try
            {
                manager.EmitBoundaryClientEventsForTest();

                Assert.True(manager.BoundaryAdmissionAcceptsForTest(generation));
                Assert.Equal(2, manager.BoundaryEnqueuedEventsForTest.Count);
                Assert.All(
                    manager.BoundaryEnqueuedEventsForTest,
                    evt => Assert.Equal(generation, evt.Generation));
                Assert.Contains(manager.BoundaryEnqueuedEventsForTest, evt => evt.IsConnect);
                Assert.Contains(manager.BoundaryEnqueuedEventsForTest, evt => evt.IsMessage);
            }
            finally
            {
                manager.DisposeBoundarySessionForTest();
            }
        }

        [Fact]
        public void BoundaryFoxRunContractsMatchProductionInterfaceSignatures()
        {
            var production = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var fixture = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Tests/Unit/Fixtures/ManagerProductionBoundaryFoxRunContracts.cs");
            var interfaceNames = new[]
            {
                "IFoxgloveLogSource",
                "IFoxgloveTopicContractSource",
                "IFoxgloveTopicBusSource",
                "IFoxgloveTopicBusDemandSource",
                "IFoxgloveTopicObserverSource",
                "IFoxgloveTopicSinkSource",
                "IFoxglovePublishCaptureSource",
                "IFoxglovePublishRecordingSource",
                "IFoxglovePublishRecordingPolicySource",
                "IFoxRunWebSocketCaptureSource",
                "IFoxglovePublishOriginSource",
                "IFoxgloveLogPolicySource",
            };

            foreach (var interfaceName in interfaceNames)
            {
                Assert.Equal(
                    NormalizeInterfaceDeclaration(production, interfaceName),
                    NormalizeInterfaceDeclaration(fixture, interfaceName));
                Assert.Equal(
                    NormalizeInterfaceBody(production, interfaceName),
                    NormalizeInterfaceBody(fixture, interfaceName));
            }
        }

        [Fact]
        public void BoundaryClientEventFixtureMatchesProductionShape()
        {
            var production = NormalizeSource(TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs"));
            var fixture = NormalizeSource(TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Tests/Unit/Fixtures/ManagerProductionBoundaryCompileStubs.cs"));
            var declarations = new[]
            {
                "internal readonly struct ClientEvent",
                "private ClientEvent(ulong generation, uint clientId, uint channelId, string topic, string encoding, byte[] payload, bool isConnect, bool isMessage)",
                "public static ClientEvent Connect(uint clientId)",
                "public static ClientEvent Connect(ulong generation, uint clientId)",
                "public static ClientEvent Disconnect(uint clientId)",
                "public static ClientEvent Disconnect(ulong generation, uint clientId)",
                "public static ClientEvent Message(uint clientId, uint channelId, string topic, string encoding, byte[] payload)",
                "public static ClientEvent Message(ulong generation, uint clientId, uint channelId, string topic, string encoding, byte[] payload)",
                "public readonly ulong Generation",
                "public readonly uint ClientId",
                "public readonly uint ChannelId",
                "public readonly string Topic",
                "public readonly string Encoding",
                "public readonly byte[] Payload",
                "public readonly bool IsConnect",
                "public readonly bool IsMessage",
            };

            foreach (var declaration in declarations)
            {
                var normalized = NormalizeSource(declaration);
                Assert.Contains(normalized, production, StringComparison.Ordinal);
                Assert.Contains(normalized, fixture, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void PublicReplayEventsContinueAfterThrowingSubscriber()
        {
            Debug.Reset();
            var manager = new FoxgloveManager();
            var messageCalls = 0;
            manager.OnReplayMessage += (_, __) => throw new InvalidOperationException("message probe");
            manager.OnReplayMessage += (_, __) => messageCalls++;
            manager.InvokeReplayMessageForTest("/topic", Array.Empty<byte>());

            var contextCalls = 0;
            manager.OnReplayMessageContext += _ => throw new InvalidOperationException("context probe");
            manager.OnReplayMessageContext += _ => contextCalls++;
            manager.InvokeReplayMessageContextForTest(default);

            var batchCalls = 0;
            manager.OnReplayBatchCompleted += _ => throw new InvalidOperationException("batch probe");
            manager.OnReplayBatchCompleted += _ => batchCalls++;
            manager.InvokeReplayBatchForTest(default);

            Assert.Equal(1, messageCalls);
            Assert.Equal(1, contextCalls);
            Assert.Equal(1, batchCalls);
            Assert.Equal(3, Debug.WarningCount);
        }

        private static string NormalizeInterfaceBody(string source, string interfaceName)
        {
            var marker = "public interface " + interfaceName;
            var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(markerIndex >= 0, "Missing interface " + interfaceName);
            var openBrace = source.IndexOf('{', markerIndex);
            Assert.True(openBrace >= 0, "Missing interface body " + interfaceName);
            var depth = 0;
            for (var index = openBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}' && --depth == 0)
                {
                    var body = source.Substring(openBrace + 1, index - openBrace - 1);
                    body = Regex.Replace(body, @"//[^\r\n]*", string.Empty);
                    body = Regex.Replace(body, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                    body = Regex.Replace(body, @"\s+", " ").Trim();
                    return Regex.Replace(body, @"\s*([(),;{}])\s*", "$1");
                }
            }

            throw new InvalidOperationException("Unclosed interface " + interfaceName);
        }

        private static string NormalizeInterfaceDeclaration(string source, string interfaceName)
        {
            var marker = "public interface " + interfaceName;
            var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(markerIndex >= 0, "Missing interface " + interfaceName);
            var openBrace = source.IndexOf('{', markerIndex);
            Assert.True(openBrace >= 0, "Missing interface declaration " + interfaceName);
            return NormalizeSource(source.Substring(markerIndex, openBrace - markerIndex));
        }

        private static string NormalizeSource(string source)
        {
            source = Regex.Replace(source, @"//[^\r\n]*", string.Empty);
            source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            source = Regex.Replace(source, @"\s+", " ").Trim();
            return Regex.Replace(source, @"\s*([(),;{}])\s*", "$1");
        }

        [Fact]
        public void ReplayRestoreDoesNotOverrideExternalPublisherDisable()
        {
            var publisher = new BoundaryPublisher();
            publisher.ExternalEnableForTest();

            Assert.True(publisher.TryDisableForReplayForTest());
            Assert.True(publisher.enabled);
            Assert.True(publisher.ReplaySuppressedForTest);

            publisher.ExternalDisableForTest();

            publisher.RestoreAfterReplayForTest();
            Assert.False(publisher.enabled);
            Assert.False(publisher.ReplaySuppressedForTest);
        }

        [Fact]
        public void ReplayDoesNotAcquireOwnershipForAlreadyDisabledPublisher()
        {
            var publisher = new BoundaryPublisher();
            publisher.ExternalDisableForTest();

            Assert.False(publisher.TryDisableForReplayForTest());
            Assert.False(publisher.ReplaySuppressedForTest);

            publisher.RestoreAfterReplayForTest();
            Assert.False(publisher.enabled);
        }

        [Fact]
        public void ReplaySuppressionBlocksProductionPublishPreparationWithoutChangingEnabled()
        {
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.ManagerForTest = new FoxgloveManager();
            publisher.ExternalEnableForTest();

            Assert.True(publisher.PreparePublishForTest());
            Assert.True(publisher.TryDisableForReplayForTest());
            Assert.True(publisher.enabled);
            Assert.False(publisher.PreparePublishForTest());

            publisher.RestoreAfterReplayForTest();
            Assert.True(publisher.PreparePublishForTest());
        }

        [Fact]
        public void ReplaySuppressionSurvivesExternalLifecycleToggle()
        {
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.ManagerForTest = new FoxgloveManager();
            publisher.ExternalEnableForTest();

            Assert.True(publisher.TryDisableForReplayForTest());
            publisher.ExternalDisableForTest();
            publisher.ExternalEnableForTest();

            Assert.True(publisher.enabled);
            Assert.True(publisher.ReplaySuppressedForTest);
            Assert.False(publisher.PreparePublishForTest());

            publisher.RestoreAfterReplayForTest();
            Assert.False(publisher.ReplaySuppressedForTest);
            Assert.True(publisher.PreparePublishForTest());
        }

        [Fact]
        public void ManagerReplaySuppressionBlocksOrdinaryTransportPublicationForEnabledPublisher()
        {
            var manager = new FoxgloveManager { HasOrdinaryTransportDemand = true };
            var publisher = new BoundaryPublisher { TopicForTest = "/boundary" };
            publisher.ManagerForTest = manager;
            publisher.ExternalEnableForTest();

            manager.SuppressLivePublishersForReplay = true;

            var result = publisher.PublishOrdinaryTransportForTest();

            Assert.True(publisher.enabled);
            Assert.Equal(0, manager.OrdinaryTransportPublishCalls);
            Assert.Equal(0, result.Matched);
            Assert.Equal(0, result.Accepted);
            Assert.Equal(0, result.Rejected);
            Assert.Equal(0, result.Unavailable);
            Assert.Equal(0, result.Failed);
        }
    }
}
#endif
