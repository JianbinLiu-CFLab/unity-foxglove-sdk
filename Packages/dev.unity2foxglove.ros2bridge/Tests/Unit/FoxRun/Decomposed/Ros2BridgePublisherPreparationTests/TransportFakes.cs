// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed partial class Ros2BridgePublisherPreparationTests
    {
        private sealed class PreparationTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly string _status;
            private readonly string _errorCode;
            private readonly string _message;

            public PreparationTransport(
                string status,
                string errorCode = "",
                string message = "")
            {
                _status = status;
                _errorCode = errorCode;
                _message = message;
            }

            public List<byte[]> Requests { get; } = new List<byte[]>();
            public List<long> RequestTimes { get; } = new List<long>();
            public List<Ros2BridgeFrame> SentFrames { get; } = new List<Ros2BridgeFrame>();
            public List<string> Events { get; } = new List<string>();
            public ManualResetEventSlim ResponseGate { get; set; }
            public ManualResetEventSlim SendGate { get; set; }
            public ManualResetEventSlim SendStarted { get; } = new ManualResetEventSlim(false);
            public bool FailNextPublish { get; set; }
            public Exception PreparationFailure { get; set; }
            public Func<byte[], byte[]> MutateResponse { get; set; }
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int DisposeCount { get; private set; }
            public int RequestCount
            {
                get
                {
                    lock (Requests)
                        return Requests.Count;
                }
            }
            public int SentFrameCount
            {
                get
                {
                    lock (SentFrames)
                        return SentFrames.Count;
                }
            }
            public long[] RequestTimesSnapshot
            {
                get
                {
                    lock (Requests)
                        return RequestTimes.ToArray();
                }
            }
            public string[] EventSnapshot
            {
                get
                {
                    lock (Events)
                        return Events.ToArray();
                }
            }

            public void Connect(string host, int port, int timeoutMs)
            {
                ConnectCount++;
                IsConnected = true;
            }

            public byte[] ExchangePublisherPreparation(byte[] request, int timeoutMs)
            {
                lock (Requests)
                {
                    Requests.Add(request);
                    RequestTimes.Add(Environment.TickCount64);
                }
                lock (Events)
                    Events.Add("prepare");
                if (PreparationFailure != null)
                    throw PreparationFailure;
                ResponseGate?.Wait(timeoutMs);
                var parsed = Ros2BridgePublisherPreparationCodec.ParseRequest(request);
                var response = Ros2BridgePublisherPreparationCodec.WriteResponseForTests(
                    parsed.RequestId,
                    _status,
                    _errorCode,
                    _message);
                return MutateResponse == null ? response : MutateResponse(response);
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
            {
                SendStarted.Set();
                SendGate?.Wait(timeoutMs);
                if (FailNextPublish)
                {
                    FailNextPublish = false;
                    throw new InvalidOperationException("forced reconnect");
                }
                lock (SentFrames)
                    SentFrames.Add(frame);
                lock (Events)
                    Events.Add("send");
            }

            public void Disconnect() => IsConnected = false;
            public void Dispose()
            {
                DisposeCount++;
                Disconnect();
            }
        }

        private sealed class ReconnectSequenceTransport :
            IRos2BridgeSink,
            IRos2BridgePublisherPreparationTransport
        {
            private readonly IRos2BridgeSink[] _transports;
            private int _index;

            internal ReconnectSequenceTransport(params IRos2BridgeSink[] transports)
            {
                _transports = transports;
            }

            private IRos2BridgeSink Current => _transports[_index];

            public bool IsConnected => Current.IsConnected;

            public void Connect(string host, int port, int timeoutMs)
                => Current.Connect(host, port, timeoutMs);

            public void Send(Ros2BridgeFrame frame, int timeoutMs)
                => Current.Send(frame, timeoutMs);

            public byte[] ExchangePublisherPreparation(
                byte[] request,
                int timeoutMs)
                => ((IRos2BridgePublisherPreparationTransport)Current)
                    .ExchangePublisherPreparation(request, timeoutMs);

            public void Disconnect()
            {
                Current.Disconnect();
                if (_index + 1 < _transports.Length)
                    _index++;
            }

            public void Dispose()
            {
                for (var i = 0; i < _transports.Length; i++)
                    _transports[i].Dispose();
            }
        }

        private sealed class FatalDisconnectSink : IRos2BridgeSink
        {
            public bool IsConnected => true;
            public int DisposeCount { get; private set; }

            public void Connect(string host, int port, int timeoutMs) { }

            public void Send(Ros2BridgeFrame frame, int timeoutMs) { }

            public void Disconnect()
                => throw new OutOfMemoryException("stop-primary");

            public void Dispose()
                => DisposeCount++;
        }

        private sealed class TrackingLifecycleSink : IRos2BridgeSink
        {
            public bool IsConnected { get; private set; }
            public int ConnectCount { get; private set; }
            public int DisconnectCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Connect(string host, int port, int timeoutMs)
            {
                ConnectCount++;
                IsConnected = true;
            }

            public void Send(Ros2BridgeFrame frame, int timeoutMs) { }

            public void Disconnect()
            {
                DisconnectCount++;
                IsConnected = false;
            }

            public void Dispose()
            {
                DisposeCount++;
                IsConnected = false;
            }
        }

    }
}
