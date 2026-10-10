// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify typed native subscription binding lifecycle and ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed partial class FoxRunRos2NativeSubscriptionAdapterTests
    {
        private sealed class FakeBackend : IFoxRunRos2NativeBackend
        {
            private readonly List<string> _events;
            private Action<FakeMessage> _callback;
            private Action<FakeMessage> _lateCallback;
            private readonly List<Action<FakeMessage>> _callbacks = new List<Action<FakeMessage>>();
            private readonly Queue<FoxRunRos2NativeBackendRegistration> _registrations =
                new Queue<FoxRunRos2NativeBackendRegistration>();

            public FakeBackend(List<string> events = null)
            {
                _events = events;
                Next = FoxRunRos2NativeBackendRegistration.Success(new FakeToken());
            }

            public FoxRunRos2NativeBackendRegistration Next { get; set; }
            public Exception RegistrationException { get; set; }
            public FakeMessage SynchronousMessage { get; set; }
            public Action DuringRegister { get; set; }
            public Action AfterRegister { get; set; }
            public ManualResetEventSlim RegisterEntered { get; set; }
            public ManualResetEventSlim ReleaseRegister { get; set; }
            public Exception RemoveException { get; set; }
            public Exception ReleaseException { get; set; }
            public ManualResetEventSlim RemoveEntered { get; set; }
            public ManualResetEventSlim ReleaseRemove { get; set; }
            public int RegisterCount { get; private set; }
            public int RemoveCount { get; private set; }
            public int ReleaseCount { get; private set; }

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                if (RegistrationException != null)
                    throw RegistrationException;
                RegisterCount++;
                RegisterEntered?.Set();
                if (ReleaseRegister != null)
                    Assert.True(ReleaseRegister.Wait(TimeSpan.FromSeconds(10)));
                _callback = message => callback((T)(ROS2.Message)message);
                _lateCallback = _callback;
                _callbacks.Add(_callback);
                if (SynchronousMessage != null)
                    _callback(SynchronousMessage);
                DuringRegister?.Invoke();
                var result = _registrations.Count == 0 ? Next : _registrations.Dequeue();
                AfterRegister?.Invoke();
                return result;
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
            {
                RemoveCount++;
                _events?.Add("remove-subscription");
                RemoveEntered?.Set();
                if (ReleaseRemove != null)
                    Assert.True(ReleaseRemove.Wait(TimeSpan.FromSeconds(10)));
                _callback = null;
                if (RemoveException != null)
                    throw RemoveException;
            }

            public bool ReleaseNodeOwnership()
            {
                ReleaseCount++;
                _events?.Add("release-node");
                if (ReleaseException != null)
                    throw ReleaseException;
                return true;
            }

            public void Invoke(FakeMessage value) => _callback(value);
            public void InvokeLate(FakeMessage value) => _lateCallback(value);
            public void InvokeAttempt(int attemptIndex, FakeMessage value) => _callbacks[attemptIndex](value);
            public void EnqueueRegistration(FoxRunRos2NativeBackendRegistration registration)
                => _registrations.Enqueue(registration);
        }

        private sealed class InspectionFailureNodeDriver : IFoxRunRos2R2fuNodeDriver
        {
            private readonly object _subscription = new object();
            private readonly Exception _inspectionFailure;

            public InspectionFailureNodeDriver(Exception inspectionFailure = null)
                => _inspectionFailure = inspectionFailure
                    ?? new InvalidOperationException("inspection failed before acknowledgement");

            public int CreateSubscriptionCount { get; private set; }
            public int RemoveSubscriptionCount { get; private set; }
            public int ReleaseNodeCount { get; private set; }
            public Exception RemoveSubscriptionFailure { get; set; }

            public object CreateSubscription<T>(
                string topic,
                Action<T> callback,
                ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
            {
                CreateSubscriptionCount++;
                return _subscription;
            }

            public bool IsSubscriptionUsable(object subscription)
                => throw _inspectionFailure;

            public bool RemoveSubscription(object subscription)
            {
                RemoveSubscriptionCount++;
                if (RemoveSubscriptionFailure != null)
                    throw RemoveSubscriptionFailure;
                return ReferenceEquals(subscription, _subscription);
            }

            public object CreatePublisher<T>(string topic, ROS2.QualityOfServiceProfile qos)
                where T : ROS2.Message, new()
                => throw new NotSupportedException();

            public bool IsPublisherUsable<T>(object publisher)
                where T : ROS2.Message, new()
                => false;

            public bool Publish<T>(object publisher, T message)
                where T : ROS2.Message, new()
                => false;

            public bool RemovePublisher<T>(object publisher)
                where T : ROS2.Message, new()
                => false;

            public bool ReleaseNode()
            {
                ReleaseNodeCount++;
                return true;
            }
        }

        private sealed class FakeToken : IFoxRunRos2NativeSubscriptionToken
        {
            public FakeToken(bool isUsable = true)
            {
                IsUsable = isUsable;
            }

            public bool IsUsable { get; }
        }

        private sealed class FakeHostedCleanup :
            IFoxRunRos2SubscriptionHostedCleanup
        {
            private readonly string _name;
            private readonly List<string> _stopOrder;
            private readonly Exception _failure;

            public FakeHostedCleanup(
                string name,
                List<string> stopOrder,
                Exception failure)
            {
                _name = name;
                _stopOrder = stopOrder;
                _failure = failure;
            }

            public void Stop()
            {
                _stopOrder.Add(_name);
                if (_failure != null)
                    throw _failure;
            }

            public bool CleanupComplete => true;
        }

        private sealed class FakeDeferredHostedCleanup :
            IFoxRunRos2SubscriptionHostedCleanup
        {
            private readonly FoxRunRos2HostCleanupQueue _queue;
            private readonly List<string> _cleanupOrder;
            private readonly bool _dispatchCleanup;
            private int _cleanupComplete;
            private int _stopped;

            public FakeDeferredHostedCleanup(
                FoxRunRos2HostCleanupQueue queue,
                List<string> cleanupOrder,
                bool dispatchCleanup)
            {
                _queue = queue;
                _cleanupOrder = cleanupOrder;
                _dispatchCleanup = dispatchCleanup;
            }

            public bool CleanupComplete
                => Volatile.Read(ref _cleanupComplete) != 0;

            public void Stop()
            {
                if (Interlocked.Exchange(ref _stopped, 1) != 0)
                    return;
                _cleanupOrder.Add("stop");
                if (!_dispatchCleanup)
                    return;
                var callback = new Thread(() => _queue.Dispatch(() =>
                {
                    _cleanupOrder.Add("cleanup");
                    Volatile.Write(ref _cleanupComplete, 1);
                }))
                {
                    IsBackground = true
                };
                callback.Start();
            }
        }

        private sealed class CapturingSynchronizationContext :
            SynchronizationContext
        {
            private readonly Queue<Action> _callbacks = new Queue<Action>();

            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (_callbacks)
                    _callbacks.Enqueue(() => callback(state));
            }

            public bool RunOne()
            {
                Action callback;
                lock (_callbacks)
                {
                    if (_callbacks.Count == 0)
                        return false;
                    callback = _callbacks.Dequeue();
                }
                callback();
                return true;
            }
        }

        private sealed class ManagedQosFactory : IFoxRunRos2NativeQosProfileFactory
        {
            public IFoxRunRos2NativeQosProfile Create(ROS2.QosPresetProfile preset)
                => new ManagedQosProfile();
        }

        private sealed class ManagedQosProfile : IFoxRunRos2NativeQosProfile
        {
            public ROS2.QualityOfServiceProfile NativeProfile => null;

            public void SetHistory(ROS2.HistoryPolicy history, int depth) { }

            public void SetPolicies(
                ROS2.HistoryPolicy history,
                int depth,
                ROS2.ReliabilityPolicy reliability,
                ROS2.DurabilityPolicy durability) { }

            public void Dispose() { }
        }

        private sealed class FakeMessage : ROS2.Message, IDisposable
        {
            public string Data { get; set; }
            public int DisposeCount { get; private set; }
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                DisposeCount++;
                IsDisposed = true;
            }
        }
    }
}
#endif
