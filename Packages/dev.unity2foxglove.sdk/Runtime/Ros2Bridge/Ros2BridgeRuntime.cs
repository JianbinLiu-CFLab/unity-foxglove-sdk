// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Background queue and reconnect runtime for the ROS2 Bridge mirror.

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Manager-owned background sender with bounded queueing and reconnect lifecycle for ROS2 Bridge frames.</summary>
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
        private long _workerGeneration;
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

            _host = NormalizeLoopbackHost(host);
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
                if (_worker != null && !_worker.IsAlive)
                    _worker = null;
                if (!_enabled || !_autoConnect || _worker != null)
                    return;

                var generation = ++_workerGeneration;
                _worker = new Thread(() => WorkerLoop(generation))
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
            if (frame.PayloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
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
                if (!_autoConnect)
                {
                    reason = "ROS2 Bridge auto-connect is disabled; connect before sending frames.";
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
            IRos2BridgeSink sinkToClose;
            lock (_gate)
            {
                _enabled = false;
                _stopRequested = true;
                _workerGeneration++;
                if (_queue.Count > 0)
                {
                    _droppedFrames += _queue.Count;
                    _queue.Clear();
                }
                worker = _worker;
                sinkToClose = _sink;
                _sink = null;
                _connected = false;
                _connecting = false;
                _lastDisconnectedUnixMs = NowUnixMs();
            }

            CloseSink(sinkToClose);
            _signal.Set();
            var joinTimeoutMs = Math.Max(1000, _sendTimeoutMs + 250);
            if (worker != null && worker.IsAlive && !worker.Join(joinTimeoutMs))
            {
                lock (_gate)
                {
                    _lastError = "ROS2 Bridge worker did not stop within timeout.";
                    if (_worker == worker)
                        _worker = null;
                }
            }

            lock (_gate)
            {
                if (_worker == worker && (worker == null || !worker.IsAlive))
                    _worker = null;
            }
        }

        public void Connect(string host, int port, int timeoutMs)
        {
            var normalizedHost = NormalizeLoopbackHost(host);
            if (!string.Equals(normalizedHost, _host, StringComparison.OrdinalIgnoreCase) || port != _port)
            {
                throw new InvalidOperationException(
                    "ROS2 Bridge runtime Connect must use the configured host and port; create a new runtime for a different endpoint.");
            }
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS2 Bridge connect timeout must be positive.");
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

        private void WorkerLoop(long generation)
        {
            while (true)
            {
                try
                {
                    if (ShouldStop(generation))
                        return;

                    if (!EnsureConnected(generation))
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

                    IRos2BridgeSink sink;
                    lock (_gate)
                    {
                        if (_stopRequested || !_enabled || generation != _workerGeneration)
                            return;
                        sink = _sink;
                    }

                    if (sink == null)
                    {
                        MarkFailure("ROS2 Bridge sink is not connected.", disconnect: true, countFrameFailure: false);
                        continue;
                    }

                    try
                    {
                        sink.Send(frame, _sendTimeoutMs);
                        lock (_gate)
                        {
                            if (generation != _workerGeneration)
                                return;
                            _sentFrames++;
                            _lastError = string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        MarkFailure(ex.Message, disconnect: true);
                    }
                }
                catch (ObjectDisposedException) when (ShouldStop(generation))
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (ShouldStop(generation))
                        return;
                    MarkFailure(ex.Message, disconnect: true);
                }
            }
        }

        private bool EnsureConnected(long generation)
        {
            lock (_gate)
            {
                if (_stopRequested || !_enabled || generation != _workerGeneration)
                    return false;
                if (_connected && _sink != null && _sink.IsConnected)
                    return true;
                _connecting = true;
            }

            IRos2BridgeSink sink = null;
            try
            {
                sink = _sinkFactory();
                sink.Connect(_host, _port, _sendTimeoutMs);
                IRos2BridgeSink previousSink = null;
                lock (_gate)
                {
                    if (_stopRequested || !_enabled || generation != _workerGeneration)
                    {
                        _connected = false;
                        _connecting = false;
                        _lastDisconnectedUnixMs = NowUnixMs();
                    }
                    else
                    {
                        previousSink = _sink;
                        _sink = sink;
                        sink = null;
                        _connected = true;
                        _connecting = false;
                        _lastConnectedUnixMs = NowUnixMs();
                        _lastError = string.Empty;
                    }
                }

                CloseSink(previousSink);
                if (sink == null)
                    return true;

                CloseSink(sink);
                return false;
            }
            catch (Exception ex)
            {
                CloseSink(sink);
                MarkFailure(ex.Message, disconnect: true, countFrameFailure: false);
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

        private bool ShouldStop(long generation)
        {
            lock (_gate)
                return _stopRequested || generation != _workerGeneration;
        }

        private void MarkFailure(string message, bool disconnect, bool countFrameFailure = true)
        {
            IRos2BridgeSink sink = null;
            lock (_gate)
            {
                if (countFrameFailure)
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

            CloseSink(sink);
        }

        private static void CloseSink(IRos2BridgeSink sink)
        {
            if (sink == null)
                return;

            try
            {
                sink.Disconnect();
            }
            catch
            {
                // Shutdown is best-effort; state has already been updated.
            }
            try
            {
                sink.Dispose();
            }
            catch
            {
                // Shutdown is best-effort; state has already been updated.
            }
        }

        private static long NowUnixMs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string NormalizeLoopbackHost(string host)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return "127.0.0.1";
            return IPAddress.TryParse(host, out var address) ? address.ToString() : host.Trim();
        }
    }
}
