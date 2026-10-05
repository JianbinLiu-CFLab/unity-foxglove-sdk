// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Manager-local R2FU FoxRun transport Provider companion.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Hidden same-GameObject companion that adapts the existing typed R2FU
    /// bindings to the neutral FoxRun Provider lifecycle.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FoxRunRos2TransportProvider :
        MonoBehaviour,
        IFoxRunTransportProvider
    {
        public const string IdValue = "unity2foxglove.r2fu";

        private static readonly FoxRunTransportId StableId =
            new FoxRunTransportId(IdValue);

        [SerializeField]
        private FoxRunRos2QosProfileSettings _publishQos =
            new FoxRunRos2QosProfileSettings();
        [SerializeField]
        private FoxRunRos2QosProfileSettings _subscribeQos =
            new FoxRunRos2QosProfileSettings();
        [SerializeField, Min(
            FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        private int _nativeCopyBudgetBytes =
            FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes;

        private FoxgloveManager _manager;
        private FoxRunRos2CustomPublisherHub _publisherHub;
        private FoxRunRos2SubscriptionHub _subscriptionHub;
        private readonly object _lifecycleGate = new object();
        private long _activeGeneration = -1;
        private int _registered;

        public FoxRunTransportId Id => StableId;

        public FoxRunTransportCapabilities Capabilities =>
            FoxRunTransportCapabilities.Publish
            | FoxRunTransportCapabilities.Subscribe;

        internal FoxRunResolvedQos ActivePublishQos
        {
            get;
            private set;
        } = FoxRunResolvedQos.Default;

        internal FoxRunResolvedQos ActiveSubscribeQos
        {
            get;
            private set;
        } = FoxRunResolvedQos.Default;

        internal int ActiveNativeCopyBudgetBytes
        {
            get;
            private set;
        } = FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes;

        public FoxRunTransportLifecycleState LifecycleState
        {
            get
            {
                if (_manager == null || !isActiveAndEnabled)
                    return FoxRunTransportLifecycleState.Unavailable;
                return Volatile.Read(ref _activeGeneration) >= 0
                    ? FoxRunTransportLifecycleState.Active
                    : FoxRunTransportLifecycleState.Available;
            }
        }

        public bool TryCaptureSession(
            ulong generation,
            out IFoxRunTransportSession session,
            out string reason)
        {
            session = null;
            if (generation > long.MaxValue)
            {
                reason = "R2FU session generation exceeds the supported range.";
                return false;
            }

            if (!EnsureAttached())
            {
                reason =
                    "The R2FU Provider must share a GameObject with one FoxgloveManager.";
                return false;
            }

            if (LifecycleState == FoxRunTransportLifecycleState.Unavailable)
            {
                reason = "The R2FU Provider is disabled or unavailable.";
                return false;
            }

            try
            {
                Activate(generation);
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(
                    exception))
            {
                reason =
                    "R2FU Provider configuration is invalid: "
                    + exception.Message;
                return false;
            }
            session = new Session(this, generation);
            reason = string.Empty;
            return true;
        }

        private void Awake() => EnsureAttached();

        private void OnEnable()
        {
            if (!EnsureAttached())
                return;
            ResumeActiveSessionIfPresent();
        }

        private void OnDisable() => Detach();

        private void OnDestroy() => Detach();

        private bool EnsureAttached()
        {
            var manager = GetComponent<FoxgloveManager>();
            if (manager == null)
                return false;

            lock (_lifecycleGate)
            {
                if (!ReferenceEquals(_manager, manager))
                {
                    if (!DetachUnderLifecycleLock())
                        return false;
                    _manager = manager;
                }

                if (!Application.isPlaying)
                {
                    Interlocked.Exchange(ref _registered, 0);
                    _manager.UnregisterFoxRunTransportProvider(this);
                    return true;
                }

                _publisherHub ??=
                    GetOrAddOwnedHub<FoxRunRos2CustomPublisherHub>();
                _subscriptionHub ??=
                    GetOrAddOwnedHub<FoxRunRos2SubscriptionHub>();
                _publisherHub.BindProviderOwner(_manager, this);
                _subscriptionHub.BindProviderOwner(_manager, this);

                if (!isActiveAndEnabled)
                {
                    Interlocked.Exchange(ref _registered, 0);
                    _manager.UnregisterFoxRunTransportProvider(this);
                    return true;
                }

                if (Interlocked.Exchange(ref _registered, 1) == 0)
                    _manager.RegisterFoxRunTransportProvider(this);
                return true;
            }
        }

        private void ResumeActiveSessionIfPresent()
        {
            var snapshot = _manager?.ActiveFoxRunTransportSession;
            if (snapshot == null
                || !snapshot.TryGetSession(Id, out var session)
                || session == null)
            {
                return;
            }

            try
            {
                Activate(session.Generation);
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                Debug.LogWarning(
                    "[Foxglove] R2FU Provider could not resume its active frozen session: "
                    + exception.Message);
            }
        }

        private T GetOrAddOwnedHub<T>()
            where T : MonoBehaviour
        {
            var component = GetComponent<T>();
            return component != null
                ? component
                : gameObject.AddComponent<T>();
        }

        private void Activate(ulong generation)
        {
            var expected = checked((long)generation);
            lock (_lifecycleGate)
            {
                if (_manager == null || !isActiveAndEnabled)
                    throw new InvalidOperationException(
                        "The R2FU Provider is detached or disabled.");
                if (_publisherHub == null || _subscriptionHub == null)
                    throw new InvalidOperationException(
                        "The R2FU Provider hubs are not attached.");

                _publishQos ??=
                    new FoxRunRos2QosProfileSettings();
                _subscribeQos ??=
                    new FoxRunRos2QosProfileSettings();
                ActivePublishQos = _publishQos.Resolve();
                ActiveSubscribeQos = _subscribeQos.Resolve();
                ActiveNativeCopyBudgetBytes =
                    FoxRunRos2NativeCopyBudgetPolicy
                        .NormalizeSerializedBytes(
                            _nativeCopyBudgetBytes);

                try
                {
                    if (Volatile.Read(ref _activeGeneration) >= 0
                        && Volatile.Read(ref _activeGeneration) != expected)
                    {
                        DisableHubsUnderLifecycleLock();
                        Volatile.Write(ref _activeGeneration, -1);
                    }

                    _publisherHub.SetProviderSessionActive(true);
                    _subscriptionHub.SetProviderSessionActive(true);
                    Volatile.Write(ref _activeGeneration, expected);
                }
                catch
                {
                    try
                    {
                        DisableHubsUnderLifecycleLock();
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogWarning(
                            "[Foxglove] R2FU Provider activation cleanup remains pending: "
                            + cleanupException.Message);
                    }
                    Volatile.Write(ref _activeGeneration, -1);
                    throw;
                }
            }
        }

        private bool Release(ulong generation)
        {
            var expected = checked((long)generation);
            lock (_lifecycleGate)
            {
                if (Volatile.Read(ref _activeGeneration) != expected)
                    return true;

                try
                {
                    DisableHubsUnderLifecycleLock();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Foxglove] R2FU Provider session cleanup remains pending: " + exception.Message);
                    return false;
                }

                Volatile.Write(ref _activeGeneration, -1);
                return true;
            }
        }

        private void Detach()
        {
            lock (_lifecycleGate)
            {
                DetachUnderLifecycleLock();
            }
        }

        private bool DetachUnderLifecycleLock()
        {
            try
            {
                DisableHubsUnderLifecycleLock();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Foxglove] R2FU Provider detach remains pending: " + exception.Message);
                return false;
            }
            Volatile.Write(ref _activeGeneration, -1);
            _publisherHub?.BindProviderOwner(null, null);
            _subscriptionHub?.BindProviderOwner(null, null);

            var manager = _manager;
            _manager = null;
            if (manager != null
                && Interlocked.Exchange(ref _registered, 0) != 0)
                manager.UnregisterFoxRunTransportProvider(this);
            else
                Interlocked.Exchange(ref _registered, 0);
            return true;
        }

        private void DisableHubsUnderLifecycleLock()
        {
            _publisherHub?.SetProviderSessionActive(false);
            _subscriptionHub?.SetProviderSessionActive(false);
        }

        private sealed class Session :
            IFoxRunTransportSession,
            IFoxRunTransportStatusSource,
            IFoxRunGeneratedTransportSession,
            IFoxRunGeneratedTransportOwnership
        {
            private FoxRunRos2TransportProvider _owner;

            internal Session(
                FoxRunRos2TransportProvider owner,
                ulong generation)
            {
                _owner = owner;
                Generation = generation;
            }

            public FoxRunTransportId Id => StableId;

            public FoxRunTransportCapabilities Capabilities =>
                FoxRunTransportCapabilities.Publish
                | FoxRunTransportCapabilities.Subscribe;

            public ulong Generation { get; }

            public FoxRunTransportStatusSnapshot CaptureStatus(
                FoxRunTransportCapabilities selectedDirections)
            {
                var owner = _owner;
                var publishSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Publish) != 0;
                var subscribeSelected =
                    (selectedDirections
                     & FoxRunTransportCapabilities.Subscribe) != 0;
                var active = owner != null
                             && Volatile.Read(
                                 ref owner._activeGeneration)
                             == checked((long)Generation);
                var publish = publishSelected
                    ? owner?._publisherHub?.CaptureTransportStatus()
                      ?? EmptyDirection(
                          FoxRunTransportDirection.Publish,
                          active)
                    : FoxRunTransportDirectionStatus.Unselected(
                        FoxRunTransportDirection.Publish);
                var subscribe = subscribeSelected
                    ? owner?._subscriptionHub?.CaptureTransportStatus()
                      ?? EmptyDirection(
                          FoxRunTransportDirection.Subscribe,
                          active)
                    : FoxRunTransportDirectionStatus.Unselected(
                        FoxRunTransportDirection.Subscribe);
                return new FoxRunTransportStatusSnapshot(
                    Id,
                    Generation,
                    publish,
                    subscribe);
            }

            public FoxRunTransportPublishResult Publish(
                in FoxRunTransportPublishRoute route)
                => FoxRunTransportPublishResult.Rejected(
                    "R2FU routes are emitted as generated typed ROS2 bindings, not untyped byte payloads.");

            public bool OwnsGeneratedTopic(
                in FoxRunGeneratedTransportPublishRequest request)
            {
                var hub = ActivePublisherHub();
                return hub != null && hub.OwnsGeneratedTopic(in request);
            }

            public FoxRunTransportPublishResult PublishGenerated(
                in FoxRunGeneratedTransportPublishRequest request)
            {
                var hub = ActivePublisherHub();
                if (hub == null)
                {
                    return FoxRunTransportPublishResult.Unavailable(
                        "The R2FU Provider session is not active.");
                }

                return hub.PublishGenerated(in request);
            }

            private FoxRunRos2CustomPublisherHub ActivePublisherHub()
            {
                var owner = _owner;
                return owner != null
                       && Volatile.Read(ref owner._activeGeneration)
                       == checked((long)Generation)
                    ? owner._publisherHub
                    : null;
            }

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected(
                    "R2FU subscriptions are emitted as generated typed ROS2 bindings.");

            public void Dispose()
            {
                var owner = Volatile.Read(ref _owner);
                if (owner != null && owner.Release(Generation))
                    Interlocked.CompareExchange(ref _owner, null, owner);
            }

            private static FoxRunTransportDirectionStatus EmptyDirection(
                FoxRunTransportDirection direction,
                bool active)
                => new FoxRunTransportDirectionStatus(
                    direction,
                    selected: true,
                    active
                        ? FoxRunTransportObservedState.Starting
                        : FoxRunTransportObservedState.Stopped,
                    0,
                    0,
                    0,
                    active
                        ? new FoxRunTransportDiagnostic(
                            "R2FU001",
                            "The native Provider session is active but its observed hub is not ready.")
                        : (FoxRunTransportDiagnostic?)null);
        }
    }
}
#endif
