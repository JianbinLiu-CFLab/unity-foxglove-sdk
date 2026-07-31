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

        private void OnEnable() => EnsureAttached();

        private void OnDisable() => Detach();

        private void OnDestroy() => Detach();

        private bool EnsureAttached()
        {
            var manager = GetComponent<FoxgloveManager>();
            if (manager == null)
                return false;

            if (!ReferenceEquals(_manager, manager))
            {
                Detach();
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
            Interlocked.Exchange(ref _activeGeneration, checked((long)generation));
            _publisherHub.SetProviderSessionActive(true);
            _subscriptionHub.SetProviderSessionActive(true);
        }

        private void Release(ulong generation)
        {
            var expected = checked((long)generation);
            if (Interlocked.CompareExchange(
                    ref _activeGeneration,
                    -1,
                    expected) != expected)
            {
                return;
            }

            _publisherHub?.SetProviderSessionActive(false);
            _subscriptionHub?.SetProviderSessionActive(false);
        }

        private void Detach()
        {
            Interlocked.Exchange(ref _activeGeneration, -1);
            _publisherHub?.SetProviderSessionActive(false);
            _subscriptionHub?.SetProviderSessionActive(false);
            _publisherHub?.BindProviderOwner(null, null);
            _subscriptionHub?.BindProviderOwner(null, null);

            var manager = _manager;
            _manager = null;
            if (manager != null
                && Interlocked.Exchange(ref _registered, 0) != 0)
            {
                manager.UnregisterFoxRunTransportProvider(this);
            }
            else
            {
                Interlocked.Exchange(ref _registered, 0);
            }
        }

        private sealed class Session :
            IFoxRunTransportSession,
            IFoxRunTransportStatusSource
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

            public FoxRunTransportSubscribeResult Subscribe(
                in FoxRunTransportSubscribeRoute route)
                => FoxRunTransportSubscribeResult.Rejected(
                    "R2FU subscriptions are emitted as generated typed ROS2 bindings.");

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                owner?.Release(Generation);
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
