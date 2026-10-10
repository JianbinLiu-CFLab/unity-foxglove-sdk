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
        private sealed class ProbeTransportSession : IFoxRunTransportSession, IFoxRunTransportStatusSource
        {
            public ProbeTransportSession(FoxRunTransportId id, FoxRunTransportCapabilities capabilities, ulong generation)
            {
                Id = id;
                Capabilities = capabilities;
                Generation = generation;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities { get; }
            public ulong Generation { get; }
            public bool Disposed { get; private set; }
            public string StatusIdOverride { get; set; }

            public FoxRunTransportPublishResult Publish(in FoxRunTransportPublishRoute route)
                => FoxRunTransportPublishResult.Accepted();

            public FoxRunTransportSubscribeResult Subscribe(in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose() => Disposed = true;

            public FoxRunTransportStatusSnapshot CaptureStatus(FoxRunTransportCapabilities selected)
            {
                var publish = (selected & FoxRunTransportCapabilities.Publish) != 0;
                var subscribe = (selected & FoxRunTransportCapabilities.Subscribe) != 0;
                return new FoxRunTransportStatusSnapshot(
                    StatusIdOverride == null ? Id : new FoxRunTransportId(StatusIdOverride),
                    Generation,
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Publish,
                        publish,
                        publish ? FoxRunTransportObservedState.Ready : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0),
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Subscribe,
                        subscribe,
                        subscribe ? FoxRunTransportObservedState.Ready : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0));
            }
        }

        private sealed class ProbeTransportProvider : IFoxRunTransportProvider
        {
            public ProbeTransportProvider(string id, FoxRunTransportCapabilities capabilities)
            {
                Id = new FoxRunTransportId(id);
                CapabilitiesValue = capabilities;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities CapabilitiesValue { get; set; }
            public FoxRunTransportCapabilities Capabilities => CapabilitiesValue;
            public Action OnLifecycle { get; set; }
            public bool ThrowLifecycle { get; set; }
            public bool Reject { get; set; }
            public ulong? SessionGenerationOverride { get; set; }
            public string SessionIdOverride { get; set; }
            public string StatusIdOverride { get; set; }
            public int Captures { get; private set; }
            public ProbeTransportSession Last { get; private set; }

            public FoxRunTransportLifecycleState LifecycleState
            {
                get
                {
                    if (ThrowLifecycle)
                        throw new InvalidOperationException("hostile lifecycle getter");
                    var callback = OnLifecycle;
                    OnLifecycle = null;
                    callback?.Invoke();
                    return FoxRunTransportLifecycleState.Available;
                }
            }

            public bool TryCaptureSession(ulong generation, out IFoxRunTransportSession session, out string reason)
            {
                Captures++;
                if (Reject)
                {
                    session = null;
                    reason = "probe reject";
                    return false;
                }

                Last = new ProbeTransportSession(
                    SessionIdOverride == null ? Id : new FoxRunTransportId(SessionIdOverride),
                    Capabilities,
                    SessionGenerationOverride ?? generation)
                {
                    StatusIdOverride = StatusIdOverride,
                };
                session = Last;
                reason = string.Empty;
                return true;
            }
        }

        private sealed class FakeProvider : IFoxRunTransportProvider
        {
            internal FakeProvider(
                string id,
                FoxRunTransportCapabilities capabilities,
                FoxRunTransportLifecycleState lifecycleState = FoxRunTransportLifecycleState.Available)
            {
                Id = new FoxRunTransportId(id);
                Capabilities = capabilities;
                LifecycleState = lifecycleState;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities { get; }
            public FoxRunTransportLifecycleState LifecycleState { get; }
            internal int CaptureCount { get; private set; }
            internal FakeSession LastCapturedSession { get; private set; }

            public bool TryCaptureSession(
                ulong generation,
                out IFoxRunTransportSession session,
                out string reason)
            {
                CaptureCount++;
                LastCapturedSession = new FakeSession(Id, Capabilities, generation);
                session = LastCapturedSession;
                reason = string.Empty;
                return true;
            }
        }

        private sealed class ReentrantMutationProvider : IFoxRunTransportProvider
        {
            private readonly Action _mutate;
            private bool _mutated;

            internal ReentrantMutationProvider(FoxRunTransportId id, Action mutate)
            {
                Id = id;
                _mutate = mutate;
            }

            public FoxRunTransportId Id { get; }

            public FoxRunTransportCapabilities Capabilities
                => FoxRunTransportCapabilities.Publish;

            public FoxRunTransportLifecycleState LifecycleState
            {
                get
                {
                    if (!_mutated)
                    {
                        _mutated = true;
                        _mutate();
                    }

                    return FoxRunTransportLifecycleState.Available;
                }
            }

            public bool TryCaptureSession(
                ulong generation,
                out IFoxRunTransportSession session,
                out string reason)
            {
                session = null;
                reason = "not used";
                return false;
            }
        }
        private sealed class FakeEmitterContribution :
            IFoxRunTransportEmitterContribution
        {
            public string ProviderId => "unity2foxglove.example";
            public string HintNameSuffix => "transport";

            public void Emit(
                in FoxRunTransportEmitterContext context,
                StringBuilder output)
            {
                output.AppendLine(
                    "namespace Demo { partial class Source { private const int ProviderMarker = 1; } }");
            }
        }

        private sealed class OrdinaryProvider :
            IFoxRunTransportProvider
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly bool _failPublish;

            internal OrdinaryProvider(
                string id,
                System.Collections.Generic.IList<string> calls,
                bool failPublish)
            {
                Id = new FoxRunTransportId(id);
                _calls = calls;
                _failPublish = failPublish;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public FoxRunTransportLifecycleState LifecycleState =>
                FoxRunTransportLifecycleState.Available;

            public bool TryCaptureSession(
                ulong generation,
                out IFoxRunTransportSession session,
                out string reason)
            {
                session = new OrdinarySession(
                    Id,
                    generation,
                    _calls,
                    _failPublish);
                reason = string.Empty;
                return true;
            }
        }

        private sealed class OrdinarySession :
            IFoxRunTransportSession,
            IFoxRunOrdinaryPayloadMapper
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly bool _failPublish;
            private readonly FoxRunTransportPublishResult? _publishResult;

            internal OrdinarySession(
                FoxRunTransportId id,
                ulong generation,
                System.Collections.Generic.IList<string> calls,
                bool failPublish)
            {
                Id = id;
                Generation = generation;
                _calls = calls;
                _failPublish = failPublish;
            }

            internal OrdinarySession(
                FoxRunTransportId id,
                ulong generation,
                System.Collections.Generic.IList<string> calls,
                FoxRunTransportPublishResult publishResult)
            {
                Id = id;
                Generation = generation;
                _calls = calls;
                _publishResult = publishResult;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public ulong Generation { get; }
            public string StableMapperId => Id.Value + ".ordinary";

            public bool TryMap(
                in FoxRunOrdinaryPayloadRequest request,
                out FoxRunOrdinaryPayloadContribution contribution,
                out string reason)
            {
                contribution = new FoxRunOrdinaryPayloadContribution(
                    request.LogicalSchemaName,
                    new byte[] { 1 },
                    "fixture",
                    "fixture");
                reason = string.Empty;
                return true;
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
            {
                _calls.Add(Id.Value);
                if (_failPublish)
                    throw new InvalidOperationException("fixture failure");
                return _publishResult
                       ?? FoxRunTransportPublishResult.Accepted();
            }

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
            }
        }

        private sealed class FakeSession :
            IFoxRunTransportSession,
            IFoxRunTransportStatusSource
        {
            internal FakeSession(
                FoxRunTransportId id,
                FoxRunTransportCapabilities capabilities,
                ulong generation)
            {
                Id = id;
                Capabilities = capabilities;
                Generation = generation;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities { get; }
            public ulong Generation { get; }
            internal bool Disposed { get; private set; }
            internal FoxRunTransportCapabilities LastStatusDirections
            {
                get;
                private set;
            }

            public FoxRunTransportStatusSnapshot CaptureStatus(
                FoxRunTransportCapabilities selectedDirections)
            {
                LastStatusDirections = selectedDirections;
                var publishSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Publish) != 0;
                var subscribeSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Subscribe) != 0;
                return new FoxRunTransportStatusSnapshot(
                    Id,
                    Generation,
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Publish,
                        publishSelected,
                        publishSelected
                            ? FoxRunTransportObservedState.Ready
                            : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0),
                    new FoxRunTransportDirectionStatus(
                        FoxRunTransportDirection.Subscribe,
                        subscribeSelected,
                        subscribeSelected
                            ? FoxRunTransportObservedState.Ready
                            : FoxRunTransportObservedState.Stopped,
                        0,
                        0,
                        0));
            }

            public FoxRunTransportPublishResult Publish(in FoxRunTransportPublishRoute route)
                => FoxRunTransportPublishResult.Accepted();

            public FoxRunTransportSubscribeResult Subscribe(in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class GeneratedSource :
            IFoxRunGeneratedTransportSource
        {
            public int FoxRunTransport_MemberCount => 0;

            public IFoxRunGeneratedMemberAccess FoxRunTransport_GetMember(
                int index)
                => throw new ArgumentOutOfRangeException(nameof(index));

            public ulong FoxRunTransport_GetCaptureSequence(int topicIndex)
                => 0;
        }

        private sealed class GeneratedSession :
            IFoxRunTransportSession,
            IFoxRunGeneratedTransportSession
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly FoxRunTransportPublishResult _result;

            internal GeneratedSession(
                string id,
                System.Collections.Generic.IList<string> calls,
                FoxRunTransportPublishResult result,
                ulong generation = 186)
            {
                Id = new FoxRunTransportId(id);
                _calls = calls;
                _result = result;
                Generation = generation;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public ulong Generation { get; }

            public FoxRunTransportPublishResult PublishGenerated(
                in FoxRunGeneratedTransportPublishRequest request)
            {
                _calls.Add(Id.Value);
                return _result;
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
                => throw new NotSupportedException();

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
            }
        }

        private sealed class OwnershipSession :
            IFoxRunTransportSession,
            IFoxRunGeneratedTransportSession,
            IFoxRunGeneratedTransportOwnership
        {
            private readonly System.Collections.Generic.IList<string> _calls;
            private readonly string _ownedTopic;

            internal OwnershipSession(
                string id,
                System.Collections.Generic.IList<string> calls,
                string ownedTopic)
            {
                Id = new FoxRunTransportId(id);
                _calls = calls;
                _ownedTopic = ownedTopic;
            }

            public FoxRunTransportId Id { get; }
            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish;
            public ulong Generation => 181;

            public bool OwnsGeneratedTopic(
                in FoxRunGeneratedTransportPublishRequest request)
                => string.Equals(
                    request.Topic,
                    _ownedTopic,
                    StringComparison.Ordinal);

            public FoxRunTransportPublishResult PublishGenerated(
                in FoxRunGeneratedTransportPublishRequest request)
            {
                _calls.Add(Id.Value);
                return FoxRunTransportPublishResult.Accepted();
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
                => throw new NotSupportedException();

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected("not used");

            public void Dispose()
            {
            }
        }

        private sealed class FakeDetachedLease : IFoxRunDetachedRetirementLease
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class BlockingDetachedLease : IFoxRunDetachedRetirementLease
        {
            internal System.Threading.ManualResetEventSlim DisposeEntered { get; } =
                new System.Threading.ManualResetEventSlim(false);

            internal System.Threading.ManualResetEventSlim AllowDispose { get; } =
                new System.Threading.ManualResetEventSlim(false);

            public void Dispose()
            {
                DisposeEntered.Set();
                Assert.True(AllowDispose.Wait(TimeSpan.FromSeconds(2)));
            }
        }

        private sealed class ThrowingDetachedLease : IFoxRunDetachedRetirementLease
        {
            public void Dispose()
                => throw new InvalidOperationException("test cleanup failure");
        }
    }
}
