// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Background queue and reconnect runtime for the ROS2 Bridge mirror.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Manager-owned background sender for ROS2 Bridge frames.</summary>
    public sealed class Ros2BridgeRuntime : IRos2BridgeSink
    {
        private readonly string _host;
        private readonly int _port;
        private readonly int _queueCapacity;
        private readonly int _reconnectIntervalMs;
        private readonly int _sendTimeoutMs;
        private readonly Func<IRos2BridgeSink> _sinkFactory;
        private readonly object _gate = new object();
        private readonly Queue<Ros2BridgeFrame> _queue;
        private readonly ManualResetEventSlim _signal = new ManualResetEventSlim(false);

        private Thread _worker;
        private bool _stopRequested;
        private bool _enabled;
        private bool _autoConnect;
        private bool _connected;
        private bool _connecting;
        private long _sentFrames;
        private long _droppedFrames;
        private long _failedFrames;
        private string _lastError = string.Empty;
        private long _lastConnectedUnixMs;
        private long _lastDisconnectedUnixMs;
        private IRos2BridgeSink _sink;

        public Ros2BridgeRuntime(
            string host,
            int port,
            int queueCapacity,
            int reconnectIntervalMs,
            int sendTimeoutMs,
            Func<IRos2BridgeSink> sinkFactory = null)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "ROS2 Bridge port must be in 1..65535.");
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity), "ROS2 Bridge queue capacity must be positive.");
            if (reconnectIntervalMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(reconnectIntervalMs), "ROS2 Bridge reconnect interval must be positive.");
            if (sendTimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(sendTimeoutMs), "ROS2 Bridge send timeout must be positive.");

            _host = host;
            _port = port;
            _queueCapacity = queueCapacity;
            _reconnectIntervalMs = reconnectIntervalMs;
            _sendTimeoutMs = sendTimeoutMs;
            _sinkFactory = sinkFactory ?? (() => new Ros2BridgeTcpClient());
            _queue = new Queue<Ros2BridgeFrame>(queueCapacity);
        }

        public bool IsConnected
        {
            get
            {
                lock (_gate)
                    return _connected;
            }
        }

        public void Start(bool enabled, bool autoConnect)
        {
            lock (_gate)
            {
                _enabled = enabled;
                _autoConnect = autoConnect;
                _stopRequested = false;
                if (!_enabled || !_autoConnect || _worker != null)
                    return;

                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "Unity2Foxglove ROS2 Bridge"
                };
                _worker.Start();
            }

            _signal.Set();
        }

        public bool TryEnqueue(Ros2BridgeFrame frame, out string reason)
        {
            reason = string.Empty;
            if (frame == null)
            {
                reason = "ROS2 Bridge frame is null.";
                return false;
            }
            if (frame.Payload.Length > Ros2BridgeFrameWriter.MaxPayloadBytes)
            {
                reason = "ROS2 Bridge payload exceeds the maximum size.";
                return false;
            }

            lock (_gate)
            {
                if (!_enabled)
                {
                    reason = "ROS2 Bridge is disabled.";
                    return false;
                }

                if (_queue.Count >= _queueCapacity)
                {
                    _queue.Dequeue();
                    _droppedFrames++;
                }

                _queue.Enqueue(frame);
            }

            _signal.Set();
            return true;
        }

        public Ros2BridgeStatsSnapshot GetStatsSnapshot()
        {
            lock (_gate)
            {
                return new Ros2BridgeStatsSnapshot(
                    _enabled,
                    _connected,
                    _connecting,
                    _queue.Count,
                    _sentFrames,
                    _droppedFrames,
                    _failedFrames,
                    _lastError,
                    _lastConnectedUnixMs,
                    _lastDisconnectedUnixMs);
            }
        }

        public void Stop()
        {
            Thread worker;
            IRos2BridgeSink sink;
            lock (_gate)
            {
                _enabled = false;
                _stopRequested = true;
                if (_queue.Count > 0)
                {
                    _droppedFrames += _queue.Count;
                    _queue.Clear();
                }
                worker = _worker;
                sink = _sink;
            }

            _signal.Set();
            if (worker != null && worker.IsAlive && !worker.Join(500))
            {
                lock (_gate)
                {
                    _lastError = "ROS2 Bridge worker did not stop within timeout.";
                }
            }

            try
            {
                sink?.Disconnect();
            }
            catch
            {
                // Stop is best-effort; background errors are reflected in stats where possible.
            }
            try
            {
                sink?.Dispose();
            }
            catch
            {
                // Dispose is best-effort during shutdown.
            }

            lock (_gate)
            {
                _worker = null;
                _sink = null;
                _connected = false;
                _connecting = false;
                _lastDisconnectedUnixMs = NowUnixMs();
            }
        }

        public void Connect(string host, int port, int timeoutMs)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            Start(enabled: true, autoConnect: true);
        }

        public void Send(Ros2BridgeFrame frame, int timeoutMs)
        {
            if (!TryEnqueue(frame, out var reason))
                throw new InvalidOperationException(reason);
        }

        public void Disconnect()
        {
            Stop();
        }

        public void Dispose()
        {
            Stop();
            _signal.Dispose();
        }

        private void WorkerLoop()
        {
            while (true)
            {
                if (ShouldStop())
                    return;

                if (!EnsureConnected())
                {
                    _signal.Wait(_reconnectIntervalMs);
                    _signal.Reset();
                    continue;
                }

                var frame = DequeueFrame();
                if (frame == null)
                {
                    _signal.Wait(50);
                    _signal.Reset();
                    continue;
                }

                try
                {
                    _sink.Send(frame, _sendTimeoutMs);
                    lock (_gate)
                    {
                        _sentFrames++;
                        _lastError = string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    MarkFailure(ex.Message, disconnect: true);
                }
            }
        }

        private bool EnsureConnected()
        {
            lock (_gate)
            {
                if (_connected && _sink != null && _sink.IsConnected)
                    return true;
                _connecting = true;
            }

            try
            {
                var sink = _sinkFactory();
                sink.Connect(_host, _port, _sendTimeoutMs);
                lock (_gate)
                {
                    _sink?.Dispose();
                    _sink = sink;
                    _connected = true;
                    _connecting = false;
                    _lastConnectedUnixMs = NowUnixMs();
                    _lastError = string.Empty;
                }
                return true;
            }
            catch (Exception ex)
            {
                MarkFailure(ex.Message, disconnect: true);
                return false;
            }
        }

        private Ros2BridgeFrame DequeueFrame()
        {
            lock (_gate)
            {
                if (_queue.Count == 0)
                    return null;
                return _queue.Dequeue();
            }
        }

        private bool ShouldStop()
        {
            lock (_gate)
                return _stopRequested;
        }

        private void MarkFailure(string message, bool disconnect)
        {
            IRos2BridgeSink sink = null;
            lock (_gate)
            {
                _failedFrames++;
                _lastError = string.IsNullOrWhiteSpace(message) ? "ROS2 Bridge send failed." : message;
                _connecting = false;
                if (disconnect)
                {
                    _connected = false;
                    _lastDisconnectedUnixMs = NowUnixMs();
                    sink = _sink;
                    _sink = null;
                }
            }

            if (sink == null)
                return;

            try
            {
                sink.Disconnect();
            }
            catch
            {
                // Connection is already considered failed.
            }
            try
            {
                sink.Dispose();
            }
            catch
            {
                // Connection is already considered failed.
            }
        }

        private static long NowUnixMs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
