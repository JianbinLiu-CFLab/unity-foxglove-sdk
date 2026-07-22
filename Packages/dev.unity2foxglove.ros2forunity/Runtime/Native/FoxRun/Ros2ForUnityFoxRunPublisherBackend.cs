// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Closed-generic custom ROS2 publisher backend over the shared FoxRun node.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Threading;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// One output binding lease over the shared FoxRun R2FU node. It deliberately
    /// owns no QoS policy: custom DTO publishers use the active R2FU package's
    /// native default until a future contract introduces an explicit publisher
    /// QoS axis.
    /// </summary>
    internal sealed class Ros2ForUnityFoxRunPublisherBackend : IFoxRunRos2NativePublisherBackend
    {
        private readonly Ros2ForUnityFoxRunNodeOwner _owner;
        private readonly IFoxRunRos2R2fuNodeDriver _driver;
        private readonly Func<bool> _canUseNativeRuntime;
        private int _released;

        internal Ros2ForUnityFoxRunPublisherBackend(
            Ros2ForUnityFoxRunNodeOwner owner,
            IFoxRunRos2R2fuNodeDriver driver,
            Func<bool> canUseNativeRuntime)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _canUseNativeRuntime = canUseNativeRuntime
                                   ?? throw new ArgumentNullException(nameof(canUseNativeRuntime));
        }

        public FoxRunRos2NativePublisherRegistration Register<T>(
            FoxRunRos2CustomPublisherContract contract)
            where T : ROS2.Message, new()
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (Volatile.Read(ref _released) != 0)
            {
                return FoxRunRos2NativePublisherRegistration.Failure(
                    FoxRunRos2RegistrationError.Stopped,
                    "The FoxRun ROS2 publisher lease is stopped.");
            }

            if (!contract.SupportsNativeOutput)
            {
                return FoxRunRos2NativePublisherRegistration.Failure(
                    FoxRunRos2RegistrationError.RegistrationRejected,
                    "The custom ROS2 publisher contract is incomplete or does not permit native output.");
            }

            try
            {
                if (!_canUseNativeRuntime())
                {
                    return FoxRunRos2NativePublisherRegistration.Failure(
                        FoxRunRos2RegistrationError.RuntimeUnavailable,
                        "The shared lifecycle gate denied native publisher creation.");
                }

                var publisher = _driver.CreatePublisher<T>(contract.Topic);
                var token = new PublisherToken<T>(_driver, publisher);
                if (!token.IsUsable)
                {
                    token.TryRemove();
                    return FoxRunRos2NativePublisherRegistration.Failure(
                        FoxRunRos2RegistrationError.InvalidPublisherToken,
                        "R2FU returned no usable native publisher token.");
                }

                return FoxRunRos2NativePublisherRegistration.Success(token);
            }
            catch (NotSupportedException exception)
            {
                return FoxRunRos2NativePublisherRegistration.Failure(
                    FoxRunRos2RegistrationError.UnsupportedMessageType,
                    Describe(exception));
            }
            catch (Exception exception)
            {
                return FoxRunRos2NativePublisherRegistration.Failure(
                    FoxRunRos2RegistrationError.PublisherBackendFailure,
                    Describe(exception));
            }
        }

        public bool TryPublish<T>(IFoxRunRos2NativePublisherToken token, T message)
            where T : ROS2.Message, new()
        {
            if (Volatile.Read(ref _released) != 0 || message == null || !_canUseNativeRuntime())
                return false;
            return token is PublisherToken<T> owned && owned.TryPublish(message);
        }

        public void RemovePublisher(IFoxRunRos2NativePublisherToken token)
        {
            if (token is not IPublisherToken owned)
                throw new ArgumentException("Publisher token was not created by this backend.", nameof(token));
            if (!owned.TryRemove())
                throw new InvalidOperationException("R2FU publisher was not found during removal.");
        }

        public void ReleaseNodeOwnership()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;
            _owner.ReleaseBindingOwnership();
        }

        private static string Describe(Exception exception)
        {
            var cause = exception;
            for (var depth = 0; depth < 4 && cause.InnerException != null; depth++)
                cause = cause.InnerException;
            return cause.GetType().Name + ": " + cause.Message;
        }

        private interface IPublisherToken : IFoxRunRos2NativePublisherToken
        {
            bool TryRemove();
        }

        private sealed class PublisherToken<T> : IPublisherToken
            where T : ROS2.Message, new()
        {
            private readonly IFoxRunRos2R2fuNodeDriver _driver;
            private object _publisher;

            internal PublisherToken(IFoxRunRos2R2fuNodeDriver driver, object publisher)
            {
                _driver = driver;
                _publisher = publisher;
            }

            public bool IsUsable
            {
                get
                {
                    var publisher = Volatile.Read(ref _publisher);
                    return publisher != null && _driver.IsPublisherUsable<T>(publisher);
                }
            }

            internal bool TryPublish(T message)
            {
                var publisher = Volatile.Read(ref _publisher);
                return publisher != null
                       && _driver.IsPublisherUsable<T>(publisher)
                       && _driver.Publish(publisher, message);
            }

            public bool TryRemove()
            {
                var publisher = Interlocked.Exchange(ref _publisher, null);
                return publisher == null || _driver.RemovePublisher<T>(publisher);
            }
        }
    }
}
#endif
