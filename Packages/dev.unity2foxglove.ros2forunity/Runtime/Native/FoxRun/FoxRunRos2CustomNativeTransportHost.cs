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

        internal FoxRunRos2CustomNativeTransportLeaseTracker(
            Func<Ros2ForUnityFoxRunNodeOwner> createOwner)
        {
            _createOwner = createOwner ?? throw new ArgumentNullException(nameof(createOwner));
        }

        internal bool TryAcquireSubscriptionBackend(out IFoxRunRos2NativeBackend backend)
        {
            backend = null;
            if (!TryAcquireOwner(out var owner, out var releaseAfterFailure))
                return false;

            try
            {
                var inner = owner.AcquireBackend();
                lock (_sync)
                    checked { _leaseCount++; }
                backend = new SubscriptionLease(inner, ReleaseLease);
                return true;
            }
            catch (Exception)
            {
                ReleaseOwnerAfterAcquireFailure(owner, releaseAfterFailure);
                return false;
            }
        }

        internal bool TryAcquirePublisherBackend(out IFoxRunRos2NativePublisherBackend backend)
        {
            backend = null;
            if (!TryAcquireOwner(out var owner, out var releaseAfterFailure))
                return false;

            try
            {
                var inner = owner.AcquirePublisherBackend();
                lock (_sync)
                    checked { _leaseCount++; }
                backend = new PublisherLease(inner, ReleaseLease);
                return true;
            }
            catch (Exception)
            {
                ReleaseOwnerAfterAcquireFailure(owner, releaseAfterFailure);
                return false;
            }
        }

        private bool TryAcquireOwner(
            out Ros2ForUnityFoxRunNodeOwner owner,
            out bool releaseAfterFailure)
        {
            owner = null;
            releaseAfterFailure = false;
            lock (_sync)
            {
                owner = _owner;
                if (owner != null)
                    return true;

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
                releaseAfterFailure = true;
                return true;
            }
        }

        private void ReleaseOwnerAfterAcquireFailure(
            Ros2ForUnityFoxRunNodeOwner owner,
            bool releaseAfterFailure)
        {
            if (!releaseAfterFailure)
                return;

            lock (_sync)
            {
                if (_leaseCount != 0 || !ReferenceEquals(_owner, owner))
                    return;
                _owner = null;
            }

            owner.ReleaseHostOwnership();
        }

        private void ReleaseLease()
        {
            Ros2ForUnityFoxRunNodeOwner ownerToRelease = null;
            lock (_sync)
            {
                if (_leaseCount <= 0)
                    return;

                _leaseCount--;
                if (_leaseCount == 0)
                {
                    ownerToRelease = _owner;
                    _owner = null;
                }
            }

            ownerToRelease?.ReleaseHostOwnership();
        }

        private sealed class SubscriptionLease : IFoxRunRos2NativeBackend
        {
            private readonly IFoxRunRos2NativeBackend _inner;
            private readonly Action _releaseLease;
            private int _released;

            internal SubscriptionLease(IFoxRunRos2NativeBackend inner, Action releaseLease)
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

            public void ReleaseNodeOwnership()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;
                try
                {
                    _inner.ReleaseNodeOwnership();
                }
                finally
                {
                    _releaseLease();
                }
            }
        }

        private sealed class PublisherLease : IFoxRunRos2NativePublisherBackend
        {
            private readonly IFoxRunRos2NativePublisherBackend _inner;
            private readonly Action _releaseLease;
            private int _released;

            internal PublisherLease(IFoxRunRos2NativePublisherBackend inner, Action releaseLease)
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

            public void ReleaseNodeOwnership()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;
                try
                {
                    _inner.ReleaseNodeOwnership();
                }
                finally
                {
                    _releaseLease();
                }
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
                ros2Unity = gameObject.AddComponent<ROS2.ROS2UnityComponent>();
            if (!ros2Unity.Ok())
                return null;

            var node = ros2Unity.CreateNode(NodeName);
            if (node == null)
                return null;

            _ros2Unity = ros2Unity;
            return new Ros2ForUnityFoxRunNodeOwner(
                new Ros2ForUnityFoxRunR2fuNodeDriver(ros2Unity, node),
                () => !_stopping
                      && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                          gameObject.scene));
        }

        private void OnApplicationQuit()
        {
            _stopping = true;
        }

        private void OnDisable()
        {
            _stopping = true;
        }

        private void OnDestroy()
        {
            _stopping = true;
            if (_instance == this)
                _instance = null;
        }
    }
}
#endif
