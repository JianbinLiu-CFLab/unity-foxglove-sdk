// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Cover Phase187 runtime-state review regressions.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{


public sealed partial class RuntimeStateReviewTests
    {
        [Fact]
        public void RuntimeStartRollsBackFactoryConstructorSubscriptions()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextBinaryAdd = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());

            try
            {
                var failure = Assert.Throws<InvalidOperationException>(
                    () => runtime.Start("phase187-b1-factory-constructor"));

                Assert.Equal("binary add failure", failure.Message);
                Assert.Null(runtime.Session);
                Assert.Equal(0, transport.HandlerCount);
                Assert.False(transport.IsRunning);
            }
            finally
            {
                runtime.Dispose();
            }
        }

        [Fact]
        public void RuntimeCannotRestartAfterSuccessfulDispose()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-disposed");

            runtime.Dispose();

            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));
            Assert.Equal(0, transport.HandlerCount);
            Assert.False(transport.IsRunning);
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void RuntimeCannotRestartAfterFailedDispose()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextHandlerRemoval = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-failed-dispose");

            Assert.Throws<InvalidOperationException>(() => runtime.Dispose());
            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));

            runtime.Dispose();
            Assert.Equal(0, transport.HandlerCount);
            Assert.False(transport.IsRunning);
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void RuntimeDisposeRetriesTransportFailureWithoutAllowingAnotherEpoch()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextDispose = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-transport-dispose-failure");

            var failure = Assert.Throws<InvalidOperationException>(() => runtime.Dispose());

            Assert.Equal("transport dispose failure", failure.Message);
            Assert.Equal(0, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(1, transport.StopCalls);
            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));

            runtime.Dispose();

            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(2, transport.DisposeCalls);
            Assert.Equal(1, transport.StopCalls);
            Assert.Equal(0, transport.HandlerCount);
        }

        [Fact]
        public void RuntimeDisposeAbandonsPermanentCallbackFailureOnlyAfterTransportClosure()
        {
            var transport = new ThrowingStopTransport { ThrowOnEveryHandlerRemoval = true };
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            runtime.Start("phase187-b1-permanent-handler-failure");

            Assert.Throws<InvalidOperationException>(() => runtime.Dispose());

            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
            Assert.Equal(1, transport.HandlerCount);
            Assert.Throws<ObjectDisposedException>(() => runtime.Start("phase187-b1-resurrected"));

            runtime.Dispose();
            Assert.Equal(1, transport.DisposeCalls);
        }

        [Fact]
        public void SessionConstructorRollsBackPartialSubscriptionsWhenEventAddThrows()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextBinaryAdd = true };

            var failure = Assert.Throws<InvalidOperationException>(
                () => new FoxgloveSession(
                    "phase187-b1-constructor",
                    transport,
                    logger: new ConsoleLogger()));

            Assert.Equal("binary add failure", failure.Message);
            Assert.Equal(0, transport.HandlerCount);
        }

        [Fact]
        public void SessionDisposeRetriesOnlyFailedCleanupSubstep()
        {
            var transport = new ThrowingStopTransport { ThrowOnNextHandlerRemoval = true };
            var session = new FoxgloveSession(
                "phase187-b1-session-idempotency",
                transport,
                logger: new ConsoleLogger());

            Assert.Throws<InvalidOperationException>(() => session.Dispose());
            Assert.Equal(1, transport.StopCalls);
            Assert.Equal(1, transport.ConnectedRemovalCalls);
            Assert.Equal(1, transport.DisconnectedRemovalCalls);
            Assert.Equal(1, transport.TextRemovalCalls);
            Assert.Equal(1, transport.BinaryRemovalCalls);

            session.Dispose();

            Assert.Equal(1, transport.StopCalls);
            Assert.Equal(2, transport.ConnectedRemovalCalls);
            Assert.Equal(1, transport.DisconnectedRemovalCalls);
            Assert.Equal(1, transport.TextRemovalCalls);
            Assert.Equal(1, transport.BinaryRemovalCalls);
            Assert.Equal(0, transport.HandlerCount);
        }

        [Fact]
        public void SessionDisposeClearsInboundCallbacksWhenBinaryDetachFails()
        {
            var transport = new ThrowingStopTransport { ThrowOnEveryBinaryHandlerRemoval = true };
            var session = new FoxgloveSession(
                "phase187-b1-callback-detach",
                transport,
                logger: new ConsoleLogger());
            try
            {
                var callbackCount = 0;
                session.OnClientMessage += (_, _, _, _) => callbackCount++;

                transport.RaiseText(
                    7,
                    "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/phase187/callback\",\"encoding\":\"json\"}]}");

                Assert.Throws<InvalidOperationException>(() => session.Dispose());
                Assert.Equal(1, transport.HandlerCount);

                // The transport deliberately retains OnClientBinary after the
                // detach failure. A disposed session must still make its callback
                // and recorder graph inert before the failure is rethrown.
                transport.RaiseBinary(7, ClientMessageFrame(1, "after-dispose"));
                Assert.Equal(0, callbackCount);
            }
            finally
            {
                transport.ThrowOnEveryBinaryHandlerRemoval = false;
                session.Dispose();
            }
        }

        [Fact]
        public void RuntimeDisposeRejectsReentrantCleanupWithoutRepeatingTransportDispose()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            transport.DisposeCallback = runtime.Dispose;
            runtime.Start("phase187-b1-reentrant-dispose");

            runtime.Dispose();

            Assert.Equal(1, transport.DisposeCalls);
            Assert.Equal(1, ReadPrivateField<int>(runtime, "_disposed"));
        }

        [Fact]
        public void RuntimeStopTracksCompletionExplicitlyInsteadOfInferringFromSessionNull()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var method = SourceMethod(source, "public void Stop()");

            Assert.Contains("RunStopCleanup(ref firstFailure);", method, StringComparison.Ordinal);
            Assert.DoesNotContain("_stopCleanupComplete = firstFailure == null;", source, StringComparison.Ordinal);
            Assert.Contains("_stopCleanupComplete = _stopCleanup.IsResourceCleanupComplete;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_stopCleanupComplete = _session == null;", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeStopDoesNotRetryBestEffortReplayHistoryAfterRequiredCleanup()
        {
            var transport = new ThrowingStopTransport();
            var runtime = new FoxgloveRuntime(transport, new SystemClock(), new DefaultSchemaRegistry());
            var replay = ReadPrivateField<ReplayController>(runtime, "_replay");
            var panelHistory = ReadPrivateField<object>(replay, "_panelHistory");
            var originalDrains = ReadPrivateField<object>(panelHistory, "_clientDrains");

            try
            {
                // Nulling the private per-client drain map makes only the optional
                // history housekeeping step fail; all resource-owning stop steps
                // still complete. This models a permanently broken replay-history
                // cleanup without changing the transport or session seams.
                WritePrivateField<object>(panelHistory, "_clientDrains", null);
                runtime.Start("phase187-stop-optional-history");

                Assert.Throws<NullReferenceException>(() => runtime.Stop());
                Assert.True(ReadPrivateField<RuntimeStopCleanupState>(runtime, "_stopCleanup").IsResourceCleanupComplete);

                // A second Stop must not retry the best-effort step forever once
                // the required ownership boundaries have been released.
                runtime.Stop();
            }
            finally
            {
                WritePrivateField(panelHistory, "_clientDrains", originalDrains);
                runtime.Dispose();
            }
        }

        [Fact]
        public void ManagerDestroyUsesTestableRuntimeDisposeRetryAndReleasesReference()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var method = SourceMethod(source, "private void OnDestroy()");

            Assert.Contains(
                "FoxgloveManagerTeardownState.RunRuntimeDisposeWithRetry(",
                method,
                StringComparison.Ordinal);
            Assert.Contains("() => _runtime?.Dispose()", method, StringComparison.Ordinal);
            Assert.Contains("() => _runtime = null", method, StringComparison.Ordinal);
        }

        private static T ReadPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field.GetValue(target);
        }

        private static void WritePrivateField<T>(object target, string name, T value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(target, value);
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method: " + signature);
            var brace = source.IndexOf('{', start);
            Assert.True(brace >= 0, "Missing method body: " + signature);
            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(start, i - start + 1);
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private sealed class ThrowingStopTransport : IFoxgloveTransport
        {
            private Action<uint> _connected;
            private Action<uint> _disconnected;
            private Action<uint, string> _textReceived;
            private Action<uint, byte[]> _binaryReceived;

            internal bool ThrowOnNextStop { get; set; }
            internal bool ThrowOnNextStart { get; set; }
            internal bool ThrowOnNextHandlerRemoval { get; set; }
            internal bool ThrowOnEveryHandlerRemoval { get; set; }
            internal bool ThrowOnEveryBinaryHandlerRemoval { get; set; }
            internal bool ThrowOnNextBinaryAdd { get; set; }
            internal bool ThrowOnNextDispose { get; set; }
            internal Action DisposeCallback { get; set; }
            internal int DisposeCalls { get; private set; }
            internal int StopCalls { get; private set; }
            internal int ConnectedRemovalCalls { get; private set; }
            internal int DisconnectedRemovalCalls { get; private set; }
            internal int TextRemovalCalls { get; private set; }
            internal int BinaryRemovalCalls { get; private set; }
            internal int HandlerCount =>
                InvocationCount(_connected)
                + InvocationCount(_disconnected)
                + InvocationCount(_textReceived)
                + InvocationCount(_binaryReceived);

            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected
            {
                add => _connected += value;
                remove
                {
                    ConnectedRemovalCalls++;
                    if (ThrowOnEveryHandlerRemoval || ThrowOnNextHandlerRemoval)
                    {
                        ThrowOnNextHandlerRemoval = false;
                        throw new InvalidOperationException("handler removal failure");
                    }
                    _connected -= value;
                }
            }

            public event Action<uint> OnClientDisconnected
            {
                add => _disconnected += value;
                remove
                {
                    DisconnectedRemovalCalls++;
                    _disconnected -= value;
                }
            }

            public event Action<uint, string> OnTextReceived
            {
                add => _textReceived += value;
                remove
                {
                    TextRemovalCalls++;
                    _textReceived -= value;
                }
            }

            public event Action<uint, byte[]> OnBinaryReceived
            {
                add
                {
                    _binaryReceived += value;
                    if (ThrowOnNextBinaryAdd)
                    {
                        ThrowOnNextBinaryAdd = false;
                        throw new InvalidOperationException("binary add failure");
                    }
                }
                remove
                {
                    BinaryRemovalCalls++;
                    if (ThrowOnEveryBinaryHandlerRemoval)
                        throw new InvalidOperationException("binary handler removal failure");
                    _binaryReceived -= value;
                }
            }

            internal void RaiseText(uint clientId, string json) => _textReceived?.Invoke(clientId, json);
            internal void RaiseBinary(uint clientId, byte[] data) => _binaryReceived?.Invoke(clientId, data);

            public void Start(string host, int port)
            {
                IsRunning = true;
                if (ThrowOnNextStart)
                {
                    ThrowOnNextStart = false;
                    throw new InvalidOperationException("start failure");
                }

            }

            public void Stop()
            {
                StopCalls++;
                IsRunning = false;
                if (!ThrowOnNextStop)
                    return;

                ThrowOnNextStop = false;
                throw new InvalidOperationException("stop failure");
            }

            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }

            public void Dispose()
            {
                DisposeCalls++;
                IsRunning = false;
                DisposeCallback?.Invoke();
                if (ThrowOnNextDispose)
                {
                    ThrowOnNextDispose = false;
                    throw new InvalidOperationException("transport dispose failure");
                }
            }

            private static int InvocationCount(Delegate callback)
                => callback?.GetInvocationList().Length ?? 0;
        }

        private static byte[] ClientMessageFrame(uint channelId, string payload)
        {
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payload);
            var frame = new byte[5 + payloadBytes.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, channelId);
            Buffer.BlockCopy(payloadBytes, 0, frame, 5, payloadBytes.Length);
            return frame;
        }

        /// <summary>Transport seam that can pause one graph broadcast after snapshot creation.</summary>
        private sealed class BlockingTransport : IFoxgloveTransport
        {
            private int _armed;

            internal readonly ManualResetEventSlim SendEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();

            public bool IsRunning => true;
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived { add { } remove { } }
            public event Action<uint, byte[]> OnBinaryReceived { add { } remove { } }

            internal void Arm() => Volatile.Write(ref _armed, 1);
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }

            public void SendText(uint clientId, string json)
            {
                if (Interlocked.Exchange(ref _armed, 0) != 1)
                    return;

                SendEntered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out waiting to release the graph broadcast seam.");
            }

            public void SendBinary(uint clientId, byte[] data) { }

            public void Dispose()
            {
                Release.Set();
                SendEntered.Dispose();
                Release.Dispose();
            }
        }
    }
}
