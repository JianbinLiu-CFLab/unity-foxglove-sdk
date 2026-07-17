// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Production R2FU subscription backend and shared node ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Threading;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Narrow R2FU node seam. It keeps tests off the live ROS graph while the
    /// production adapter still creates a closed generic endpoint.
    /// </summary>
    internal interface IFoxRunRos2R2fuNodeDriver
    {
        object CreateSubscription<T>(
            string topic,
            Action<T> callback,
            ROS2.QualityOfServiceProfile qos)
            where T : ROS2.Message, new();

        bool IsSubscriptionUsable(object subscription);

        bool RemoveSubscription(object subscription);

        void ReleaseNode();
    }

    /// <summary>
    /// Owns the single deterministic FoxRun node. The host and every binding
    /// hold independent leases; the last release removes the node exactly once.
    /// </summary>
    internal sealed class Ros2ForUnityFoxRunNodeOwner
    {
        private readonly object _sync = new object();
        private readonly IFoxRunRos2R2fuNodeDriver _driver;
        private readonly Func<bool> _canUseNativeRuntime;
        private int _bindingLeases;
        private bool _hostOwnershipReleased;
        private bool _nodeReleased;

        internal Ros2ForUnityFoxRunNodeOwner(IFoxRunRos2R2fuNodeDriver driver)
            : this(driver, () => true)
        {
        }

        internal Ros2ForUnityFoxRunNodeOwner(
            IFoxRunRos2R2fuNodeDriver driver,
            Func<bool> canUseNativeRuntime)
        {
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _canUseNativeRuntime = canUseNativeRuntime
                                   ?? throw new ArgumentNullException(nameof(canUseNativeRuntime));
        }

        internal IFoxRunRos2NativeBackend AcquireBackend()
        {
            lock (_sync)
            {
                if (_hostOwnershipReleased || _nodeReleased)
                    throw new ObjectDisposedException(nameof(Ros2ForUnityFoxRunNodeOwner));
                checked { _bindingLeases++; }
            }

            return new Ros2ForUnityFoxRunInboundBackend(
                this,
                _driver,
                _canUseNativeRuntime);
        }

        internal void ReleaseHostOwnership()
        {
            var release = false;
            lock (_sync)
            {
                if (_hostOwnershipReleased)
                    return;
                _hostOwnershipReleased = true;
                release = TryClaimNodeReleaseUnderLock();
            }

            if (release)
                _driver.ReleaseNode();
        }

        internal void ReleaseBindingOwnership()
        {
            var release = false;
            lock (_sync)
            {
                if (_bindingLeases <= 0)
                    return;
                _bindingLeases--;
                release = TryClaimNodeReleaseUnderLock();
            }

            if (release)
                _driver.ReleaseNode();
        }

        private bool TryClaimNodeReleaseUnderLock()
        {
            if (!_hostOwnershipReleased || _bindingLeases != 0 || _nodeReleased)
                return false;
            _nodeReleased = true;
            return true;
        }
    }

    /// <summary>One binding lease over the host's shared R2FU node.</summary>
    internal sealed class Ros2ForUnityFoxRunInboundBackend : IFoxRunRos2NativeBackend
    {
        private readonly Ros2ForUnityFoxRunNodeOwner _owner;
        private readonly IFoxRunRos2R2fuNodeDriver _driver;
        private readonly Func<bool> _canUseNativeRuntime;
        private int _released;

        internal Ros2ForUnityFoxRunInboundBackend(
            Ros2ForUnityFoxRunNodeOwner owner,
            IFoxRunRos2R2fuNodeDriver driver,
            Func<bool> canUseNativeRuntime)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _canUseNativeRuntime = canUseNativeRuntime
                                   ?? throw new ArgumentNullException(nameof(canUseNativeRuntime));
        }

        public FoxRunRos2NativeBackendRegistration Register<T>(
            FoxRunRos2GeneratedContract contract,
            IFoxRunRos2NativeQosProfile qosProfile,
            Action<T> callback)
            where T : ROS2.Message, new()
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (qosProfile == null)
                throw new ArgumentNullException(nameof(qosProfile));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (Volatile.Read(ref _released) != 0)
            {
                return FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.Stopped,
                    "The FoxRun ROS2 node lease is stopped.");
            }

            try
            {
                if (!_canUseNativeRuntime())
                {
                    return FoxRunRos2NativeBackendRegistration.Failure(
                        FoxRunRos2RegistrationError.RuntimeUnavailable,
                        "The shared lifecycle gate denied native subscription creation.");
                }

                // The profile is borrowed for this synchronous call. R2FU copies
                // its policies before CreateSubscription returns.
                var subscription = _driver.CreateSubscription(
                    contract.Topic,
                    callback,
                    qosProfile.NativeProfile);
                var token = new SubscriptionToken(_driver, subscription);
                if (!token.IsUsable)
                {
                    token.TryRemove();
                    return FoxRunRos2NativeBackendRegistration.Failure(
                        FoxRunRos2RegistrationError.InvalidSubscriptionToken,
                        "R2FU returned no usable native subscription token.");
                }

                return FoxRunRos2NativeBackendRegistration.Success(token);
            }
            catch (NotSupportedException exception)
            {
                return FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.UnsupportedMessageType,
                    Describe(exception));
            }
            catch (Exception exception)
            {
                return FoxRunRos2NativeBackendRegistration.Failure(
                    FoxRunRos2RegistrationError.BackendFailure,
                    Describe(exception));
            }
        }

        public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
        {
            if (token is not SubscriptionToken owned)
                throw new ArgumentException("Subscription token was not created by this backend.", nameof(token));
            owned.TryRemove();
        }

        public void ReleaseNodeOwnership()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;
            _owner.ReleaseBindingOwnership();
        }

        private static string Describe(Exception exception)
            => exception.GetType().Name + ": " + exception.Message;

        private sealed class SubscriptionToken : IFoxRunRos2NativeSubscriptionToken
        {
            private readonly IFoxRunRos2R2fuNodeDriver _driver;
            private object _subscription;

            internal SubscriptionToken(IFoxRunRos2R2fuNodeDriver driver, object subscription)
            {
                _driver = driver;
                _subscription = subscription;
            }

            public bool IsUsable
            {
                get
                {
                    var subscription = Volatile.Read(ref _subscription);
                    return subscription != null && _driver.IsSubscriptionUsable(subscription);
                }
            }

            internal void TryRemove()
            {
                var subscription = Interlocked.Exchange(ref _subscription, null);
                if (subscription != null && !_driver.RemoveSubscription(subscription))
                    throw new InvalidOperationException("R2FU subscription was not found during removal.");
            }
        }
    }

    /// <summary>Production adapter over the packaged R2FU node API.</summary>
    internal sealed class Ros2ForUnityFoxRunR2fuNodeDriver : IFoxRunRos2R2fuNodeDriver
    {
        private readonly ROS2.ROS2UnityComponent _owner;
        private ROS2.ROS2Node _node;

        internal Ros2ForUnityFoxRunR2fuNodeDriver(
            ROS2.ROS2UnityComponent owner,
            ROS2.ROS2Node node)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _node = node ?? throw new ArgumentNullException(nameof(node));
        }

        public object CreateSubscription<T>(
            string topic,
            Action<T> callback,
            ROS2.QualityOfServiceProfile qos)
            where T : ROS2.Message, new()
            => Volatile.Read(ref _node)?.CreateSubscription(topic, callback, qos)
               ?? throw new ObjectDisposedException(nameof(Ros2ForUnityFoxRunR2fuNodeDriver));

        public bool IsSubscriptionUsable(object subscription)
            => subscription is ROS2.ISubscriptionBase;

        public bool RemoveSubscription(object subscription)
        {
            var node = Volatile.Read(ref _node);
            if (node != null && subscription is ROS2.ISubscriptionBase typed)
                return node.RemoveSubscription(typed);
            return false;
        }

        public void ReleaseNode()
        {
            var node = Interlocked.Exchange(ref _node, null);
            if (node != null)
                _owner.RemoveNode(node);
        }
    }
}
#endif
