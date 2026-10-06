// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Demand-created shared R2FU node host for Phase181 custom DTO endpoints.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Keeps one custom-interface node alive for the union of typed input and
    /// output leases. It deliberately owns only node lifetime: bindings retain
    /// their existing endpoint/token ownership and release order.
    /// </summary>
    internal sealed class FoxRunRos2CustomNativeTransportLeaseTracker
    {
        private readonly object _sync = new object();
        private readonly Func<Ros2ForUnityFoxRunNodeOwner> _createOwner;
        private Ros2ForUnityFoxRunNodeOwner _owner;
        private int _leaseCount;
        private bool _pendingRelease;
        private bool _releaseInFlight;

        internal FoxRunRos2CustomNativeTransportLeaseTracker(
            Func<Ros2ForUnityFoxRunNodeOwner> createOwner)
        {
            _createOwner = createOwner ?? throw new ArgumentNullException(nameof(createOwner));
        }

        internal bool TryAcquireSubscriptionBackend(out IFoxRunRos2NativeBackend backend)
        {
            backend = null;
            if (!RetryPendingRelease())
                return false;
            if (!TryReserveOwner(out var owner))
                return false;

            try
            {
                var inner = owner.AcquireBackend();
                backend = new SubscriptionLease(inner, ReleaseLease);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    ReleaseLease();
                }
                catch
                {
                }
                return false;
            }
        }

        internal bool TryAcquirePublisherBackend(out IFoxRunRos2NativePublisherBackend backend)
        {
            backend = null;
            if (!RetryPendingRelease())
                return false;
            if (!TryReserveOwner(out var owner))
                return false;

            try
            {
                var inner = owner.AcquirePublisherBackend();
                backend = new PublisherLease(inner, ReleaseLease);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    ReleaseLease();
                }
                catch
                {
                }
                return false;
            }
        }

        private bool TryReserveOwner(out Ros2ForUnityFoxRunNodeOwner owner)
        {
            owner = null;
            lock (_sync)
            {
                if (_pendingRelease || _releaseInFlight)
                {
                    owner = null;
                    return false;
                }

                owner = _owner;
                if (owner == null)
                {
                    try
                    {
                        owner = _createOwner();
                    }
                    catch (Exception)
                    {
                        return false;
                    }

                    if (owner == null)
                        return false;

                    _owner = owner;
                }

                if (_leaseCount == int.MaxValue)
                {
                    owner = null;
                    return false;
                }

                // Reserve ownership before leaving the tracker lock. The
                // selected owner therefore cannot be detached while its
                // concrete backend acquisition is still in progress.
                _leaseCount++;
                return true;
            }
        }

        private bool ReleaseLease()
        {
            Ros2ForUnityFoxRunNodeOwner ownerToRelease = null;
            lock (_sync)
            {
                if (_leaseCount > 0)
                {
                    _leaseCount--;
                }
                if (_leaseCount == 0 && (_pendingRelease || _owner != null))
                {
                    if (_releaseInFlight)
                        return true;
                    _releaseInFlight = true;
                    ownerToRelease = _owner;
                }
                else if (_leaseCount == 0)
                    return true;
            }

            if (ownerToRelease == null)
            {
                lock (_sync)
                    _releaseInFlight = false;
                return true;
            }

            bool released;
            try
            {
                released = ownerToRelease.ReleaseHostOwnership();
            }
            catch
            {
                released = false;
            }
            lock (_sync)
            {
                // Keep the in-flight marker set until the owner outcome and
                // tracker ownership are committed atomically. Otherwise a
                // concurrent acquisition can reuse a host after its node has
                // already been released but before _pendingRelease is set.
                if (ReferenceEquals(_owner, ownerToRelease) && _leaseCount == 0)
                {
                    _pendingRelease = !released;
                    if (released)
                        _owner = null;
                }
                _releaseInFlight = false;
                if (!ReferenceEquals(_owner, ownerToRelease) || _leaseCount != 0)
                    return released;
            }
            // The binding lease has been relinquished even when native node
            // retirement remains pending; the tracker retains the owner for
            // RetryPendingRelease rather than leaking the binding lease.
            return true;
        }

        internal bool RetryPendingRelease()
        {
            Ros2ForUnityFoxRunNodeOwner ownerToRelease;
            lock (_sync)
            {
                if (_releaseInFlight)
                    return false;
                ownerToRelease = _owner;
            }

            // A contextless owner cannot receive SynchronizationContext.Post.
            // Pump its explicit owner-thread handoff before evaluating the
            // tracker lease state so a callback-thread release cannot strand
            // the final native node lease.
            ownerToRelease?.RetryPendingNodeReleaseOnCurrentThread();

            lock (_sync)
            {
                if (_releaseInFlight)
                    return false;
                if (!_pendingRelease || _leaseCount != 0 || _owner == null)
                    return true;
                _releaseInFlight = true;
                ownerToRelease = _owner;
            }

            bool released;
            try
            {
                released = ownerToRelease.ReleaseHostOwnership();
            }
            catch
            {
                released = false;
            }
            lock (_sync)
            {
                if (ReferenceEquals(_owner, ownerToRelease) && _leaseCount == 0 && released)
                {
                    _owner = null;
                    _pendingRelease = false;
                }
                else if (ReferenceEquals(_owner, ownerToRelease) && _leaseCount == 0)
                {
                    _pendingRelease = true;
                }
                _releaseInFlight = false;
            }
            return released;
        }

        private sealed class SubscriptionLease : IFoxRunRos2NativeBackend
        {
            private readonly IFoxRunRos2NativeBackend _inner;
            private readonly Func<bool> _releaseLease;
            private int _released;

            internal SubscriptionLease(IFoxRunRos2NativeBackend inner, Func<bool> releaseLease)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _releaseLease = releaseLease ?? throw new ArgumentNullException(nameof(releaseLease));
            }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
                => _inner.Register(contract, qosProfile, callback);

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
                => _inner.RemoveSubscription(token);

            public bool ReleaseNodeOwnership()
            {
                if (Volatile.Read(ref _released) != 0)
                    return true;
                if (!_inner.ReleaseNodeOwnership())
                {
                    return false;
                }
                if (!_releaseLease())
                    return false;
                Interlocked.Exchange(ref _released, 1);
                return true;
            }
        }

        private sealed class PublisherLease : IFoxRunRos2NativePublisherBackend
        {
            private readonly IFoxRunRos2NativePublisherBackend _inner;
            private readonly Func<bool> _releaseLease;
            private int _released;

            internal PublisherLease(IFoxRunRos2NativePublisherBackend inner, Func<bool> releaseLease)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _releaseLease = releaseLease ?? throw new ArgumentNullException(nameof(releaseLease));
            }

            public FoxRunRos2NativePublisherRegistration Register<T>(
                FoxRunRos2CustomPublisherContract contract,
                FoxRunResolvedQos qos)
                where T : ROS2.Message, new()
                => _inner.Register<T>(contract, qos);

            public bool TryPublish<T>(IFoxRunRos2NativePublisherToken token, T message)
                where T : ROS2.Message, new()
                => _inner.TryPublish(token, message);

            public void RemovePublisher(IFoxRunRos2NativePublisherToken token)
                => _inner.RemovePublisher(token);

            public bool ReleaseNodeOwnership()
            {
                if (Volatile.Read(ref _released) != 0)
                    return true;
                if (!_inner.ReleaseNodeOwnership())
                    return false;
                if (!_releaseLease())
                    return false;
                Interlocked.Exchange(ref _released, 1);
                return true;
            }
        }
    }

    /// <summary>
    /// Hidden demand-created Unity owner for generated Phase181 custom
    /// interfaces. Packaged Phase179 subscriptions deliberately retain their
    /// original host and node so this host cannot alter existing lifecycle
    /// behavior.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class FoxRunRos2CustomNativeTransportHost : MonoBehaviour
    {
        private const string HostObjectName = "[FoxRun ROS2 Custom Transport Host]";
        private const string NodeName = "unity2foxglove_foxrun_custom";

        private static FoxRunRos2CustomNativeTransportHost _instance;
        private FoxRunRos2CustomNativeTransportLeaseTracker _leases;
        private ROS2.ROS2UnityComponent _ros2Unity;
        private bool _stopping;
        private bool _duplicate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        internal static bool TryAcquireSubscriptionBackend(out IFoxRunRos2NativeBackend backend)
        {
            backend = null;
            var host = EnsureCreated();
            return host != null && host.TryAcquireSubscriptionBackendCore(out backend);
        }

        internal static bool TryAcquirePublisherBackend(out IFoxRunRos2NativePublisherBackend backend)
        {
            backend = null;
            var host = EnsureCreated();
            return host != null && host.TryAcquirePublisherBackendCore(out backend);
        }

        private static FoxRunRos2CustomNativeTransportHost EnsureCreated()
        {
            if (_instance != null)
                return _instance;
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge)
                return null;

            var existing = FindFirstObjectByType<FoxRunRos2CustomNativeTransportHost>();
            if (existing != null)
            {
                _instance = existing;
                return _instance;
            }

            var go = new GameObject(HostObjectName) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FoxRunRos2CustomNativeTransportHost>();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                _duplicate = true;
                _stopping = true;
                Destroy(this);
                return;
            }

            _instance = this;
            _leases = new FoxRunRos2CustomNativeTransportLeaseTracker(CreateNodeOwner);
        }

        private void OnEnable()
        {
            if (_duplicate)
                return;
            _stopping = false;
        }

        private bool TryAcquireSubscriptionBackendCore(out IFoxRunRos2NativeBackend backend)
        {
            backend = null;
            return !_stopping
                   && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene)
                   && _leases != null
                   && _leases.TryAcquireSubscriptionBackend(out backend);
        }

        private bool TryAcquirePublisherBackendCore(out IFoxRunRos2NativePublisherBackend backend)
        {
            backend = null;
            return !_stopping
                   && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene)
                   && _leases != null
                   && _leases.TryAcquirePublisherBackend(out backend);
        }

        private Ros2ForUnityFoxRunNodeOwner CreateNodeOwner()
        {
            if (_stopping
                || !Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene))
                return null;

            var ros2Unity = _ros2Unity ?? GetComponent<ROS2.ROS2UnityComponent>();
            if (ros2Unity == null)
            {
                // Adding a MonoBehaviour invokes Awake synchronously. Do not
                // re-enter the freshly attached R2FU component and create its
                // first native node from that AddComponent call stack. Let
                // Unity run Start first, then retry on the next bounded scan.
                _ros2Unity = gameObject.AddComponent<ROS2.ROS2UnityComponent>();
                return null;
            }
            _ros2Unity = ros2Unity;
            if (!ros2Unity.Ok())
                return null;

            var node = ros2Unity.CreateNode(NodeName);
            if (node == null)
                return null;

            return new Ros2ForUnityFoxRunNodeOwner(
                new Ros2ForUnityFoxRunR2fuNodeDriver(ros2Unity, node),
                () => !_stopping
                      && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                          gameObject.scene),
                ownerThreadId: Thread.CurrentThread.ManagedThreadId,
                ownerContext: SynchronizationContext.Current);
        }

        private void OnApplicationQuit()
        {
            _stopping = true;
        }

        private void Update()
        {
            _leases?.RetryPendingRelease();
        }

        private void OnDisable()
        {
            _stopping = true;
            _leases?.RetryPendingRelease();
        }

        private void OnDestroy()
        {
            _stopping = true;
            _leases?.RetryPendingRelease();
            if (_instance == this)
                _instance = null;
        }
    }
}
#endif
