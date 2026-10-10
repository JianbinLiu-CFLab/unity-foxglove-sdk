// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Lock the native bounded-stream callback and teardown ownership contract.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    public sealed partial class FoxRunRos2StreamOwnershipTests
    {
        private static FoxRunRos2StreamSubscriptionBinding<FakeMessage, OwnedSample> Binding(
            FakeBackend backend,
            Func<bool> tryAdmitInput,
            Func<FakeMessage, FoxRunRos2CopyContext, OwnedSample> materializeOwned,
            Action<OwnedSample> transferOwned,
            Action clearOwned = null,
            Action<Action> dispatchCleanup = null,
            Func<bool> cancelAdmissionCredit = null)
            => new FoxRunRos2StreamSubscriptionBinding<FakeMessage, OwnedSample>(
                Contract(),
                7,
                () => 7,
                1024,
                tryAdmitInput,
                materializeOwned,
                transferOwned,
                clearOwned ?? (() => { }),
                dispatchCleanup ?? (action => action()),
                backend,
                FoxRunResolvedQos.Default,
                new ManagedQosFactory(),
                cancelAdmissionCredit: cancelAdmissionCredit);

        private static FoxRunRos2GeneratedContract Contract()
            => new FoxRunRos2GeneratedContract(
                "stream-contract",
                "/stream",
                "Demo.Receiver",
                "_stream",
                "std_msgs/msg/String",
                FoxRunFlow.Subscribe,
                FoxRunRos2RouteEndpoint.R2fu,
                FoxRunQosProfile.Default,
                hasExplicitQosProfile: true,
                qosReliability: default,
                hasExplicitQosReliability: false,
                qosDurability: default,
                hasExplicitQosDurability: false,
                qosHistory: default,
                hasExplicitQosHistory: false,
                qosDepth: 0,
                hasExplicitQosDepth: false,
                supportsRos2Native: true);

        private sealed class OwnedSample
        {
            public OwnedSample(string value) => Value = value;
            public string Value { get; }
            public int DisposeCount { get; set; }
        }

        private sealed class FakeMessage : ROS2.Message
        {
            public string Data { get; set; }
            public bool IsDisposed { get; private set; }
            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeBackend : IFoxRunRos2NativeBackend
        {
            private Action<FakeMessage> _callback;
            private Action<FakeMessage> _lateCallback;

            public System.Collections.Generic.List<string> Events { get; }
                = new System.Collections.Generic.List<string>();
            public int RemoveCount;
            public int ReleaseCount { get; private set; }
            public int ReleaseThreadId { get; private set; }
            public IFoxRunRos2NativeSubscriptionToken ReturnedToken { get; set; }
                = new FakeToken();
            public bool InvokeSynchronouslyOnRegister { get; set; }
            public bool InvokeAsynchronouslyOnRegister { get; set; }
            public int RegistrationFailuresRemaining { get; set; }
            public int ReleaseFailuresRemaining { get; set; }
            public Exception RemoveException { get; set; }
            public ManualResetEventSlim RegisterEntered { get; set; }
            public ManualResetEventSlim ReleaseRegister { get; set; }
            public ManualResetEventSlim AsyncCallbackEntered { get; set; }
            private Thread _registrationCallback;

            public FoxRunRos2NativeBackendRegistration Register<T>(
                FoxRunRos2GeneratedContract contract,
                IFoxRunRos2NativeQosProfile qosProfile,
                Action<T> callback)
                where T : ROS2.Message, new()
            {
                _callback = message => callback((T)(ROS2.Message)message);
                _lateCallback = _callback;
                if (InvokeSynchronouslyOnRegister)
                    _callback(new FakeMessage { Data = "synchronous" });
                if (InvokeAsynchronouslyOnRegister)
                {
                    var registrationCallback = _callback;
                    _registrationCallback = new Thread(
                        () => registrationCallback(new FakeMessage { Data = "asynchronous" }))
                    {
                        IsBackground = true
                    };
                    _registrationCallback.Start();
                    if (AsyncCallbackEntered != null
                        && !AsyncCallbackEntered.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting for the asynchronous registration callback.");
                    }
                }
                RegisterEntered?.Set();
                if (ReleaseRegister != null
                    && !ReleaseRegister.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out waiting to release native registration.");
                }
                if (RegistrationFailuresRemaining > 0)
                {
                    RegistrationFailuresRemaining--;
                    return FoxRunRos2NativeBackendRegistration.Failure(
                        FoxRunRos2RegistrationError.RuntimeUnavailable,
                        "runtime unavailable");
                }
                return FoxRunRos2NativeBackendRegistration.Success(ReturnedToken);
            }

            public void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token)
            {
                Interlocked.Increment(ref RemoveCount);
                Events.Add("remove");
                _callback = null;
                if (RemoveException != null)
                    throw RemoveException;
            }

            public bool ReleaseNodeOwnership()
            {
                ReleaseCount++;
                ReleaseThreadId = Thread.CurrentThread.ManagedThreadId;
                Events.Add("release");
                if (ReleaseFailuresRemaining > 0)
                {
                    ReleaseFailuresRemaining--;
                    return false;
                }
                return true;
            }

            public void Invoke(FakeMessage message) => _callback(message);
            public void InvokeLate(FakeMessage message) => _lateCallback(message);
            public bool JoinRegistrationCallback(TimeSpan timeout)
                => _registrationCallback == null || _registrationCallback.Join(timeout);
        }

        private sealed class TestHostDispatcher
        {
            private readonly int _hostThreadId;
            private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _pending =
                new System.Collections.Concurrent.ConcurrentQueue<Action>();

            public TestHostDispatcher(int hostThreadId) => _hostThreadId = hostThreadId;

            public int PendingCount => _pending.Count;

            public void Dispatch(Action action)
            {
                if (Thread.CurrentThread.ManagedThreadId == _hostThreadId)
                    action();
                else
                    _pending.Enqueue(action);
            }

            public void RunPending()
            {
                Assert.Equal(_hostThreadId, Thread.CurrentThread.ManagedThreadId);
                while (_pending.TryDequeue(out var action))
                    action();
            }
        }

        private sealed class FakeToken : IFoxRunRos2NativeSubscriptionToken
        {
            private readonly bool _isUsable;
            private readonly bool _throwOnInspection;

            public FakeToken(bool isUsable = true, bool throwOnInspection = false)
            {
                _isUsable = isUsable;
                _throwOnInspection = throwOnInspection;
            }

            public bool IsUsable
                => _throwOnInspection
                    ? throw new InvalidOperationException("token inspection failed")
                    : _isUsable;
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
    }
}
#endif
