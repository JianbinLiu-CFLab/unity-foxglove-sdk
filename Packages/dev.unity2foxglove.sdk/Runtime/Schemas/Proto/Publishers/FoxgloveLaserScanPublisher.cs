// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes foxglove.LaserScan messages with JSON/protobuf encoding.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes a planar laser scan. Empty range arrays produce a small synthetic scan
    /// for smoke tests and first-run setup.
    /// </summary>
    public class FoxgloveLaserScanPublisher : FoxglovePublisherBase
    {
        private const int MaxQueuedPublishFrames = 8;

        [Header("Laser Scan")]
        [SerializeField] private string _frameId = "laser";
        [SerializeField] private double _startAngleDegrees = -45;
        [SerializeField] private double _endAngleDegrees = 45;
        [SerializeField] private double[] _ranges;
        [SerializeField] private double[] _intensities;
        [SerializeField, Min(1)] private int _syntheticSampleCount = 16;
        [SerializeField] private double _syntheticRangeMeters = 2.0;

        private bool _warnedIntensityMismatch;
        private bool _warnedPublishFailure;
        private bool _warnedOffMainThreadPublishFrame;
        private double[] _syntheticRanges;
        private int _syntheticRangesCount;
        private double _syntheticRangesMeters;
        private readonly ConcurrentQueue<QueuedLaserScanFrame> _queuedPublishFrames = new ConcurrentQueue<QueuedLaserScanFrame>();
        private int _queuedPublishFrameCount;
        private int _queuedOffMainThreadPublishFrameCount;
        private int _droppedQueuedPublishFrameCount;
        private int _unityThreadId;

        protected override string SchemaName => FoxgloveSchemaDefinitions.LaserScanSchemaName;
        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.LaserScan;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/laser_scan";
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Publish one event-driven scan immediately, bypassing the regular Update cadence.
        /// Calls from worker threads are copied into a bounded queue and published on
        /// the next Unity main-thread update.
        /// </summary>
        public void PublishFrame(
            ulong logTimeNs,
            string frameId,
            double startAngleRadians,
            double endAngleRadians,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities = null)
        {
            if (!IsUnityMainThread())
            {
                QueuePublishFrame(logTimeNs, frameId, startAngleRadians, endAngleRadians, ranges, intensities);
                return;
            }

            PublishFrameOnMainThread(logTimeNs, frameId, startAngleRadians, endAngleRadians, ranges, intensities);
        }

        private void PublishFrameOnMainThread(
            ulong logTimeNs,
            string frameId,
            double startAngleRadians,
            double endAngleRadians,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities)
        {
            ResolveManager();
            if (_manager == null) return;
            if (!ShouldPrepareAnyPublishPayload()) return;

            TryPublishScan(logTimeNs, frameId, startAngleRadians, endAngleRadians, ranges, intensities ?? Array.Empty<double>());
        }

        private void Update()
        {
            if (_manager == null) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            DrainQueuedPublishFrames();
            if (!_publishOnEnable) return;
            if (!ShouldPublishNow()) return;
            if (!ShouldPrepareAnyPublishPayload()) return;

            var ranges = ResolveRanges();
            var intensities = _intensities ?? Array.Empty<double>();
            if (intensities.Length != 0 && intensities.Length != ranges.Length)
            {
                if (!_warnedIntensityMismatch)
                {
                    Debug.LogWarning("[Foxglove] LaserScan intensities must be empty or match ranges length; skipping publish.");
                    _warnedIntensityMismatch = true;
                }
                return;
            }
            _warnedIntensityMismatch = false;

            var unixNs = CurrentLogTimeNs;
            var startRad = _startAngleDegrees * Math.PI / 180.0;
            var endRad = _endAngleDegrees * Math.PI / 180.0;
            TryPublishScan(unixNs, _frameId, startRad, endRad, ranges, intensities);
        }

        private bool TryPublishScan(
            ulong unixNs,
            string frameId,
            double startRad,
            double endRad,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities)
        {
            try
            {
                PublishScan(unixNs, frameId, startRad, endRad, ranges, intensities);
                _warnedPublishFailure = false;
                return true;
            }
            catch (Exception ex) when (IsRecoverablePublishException(ex))
            {
                if (!_warnedPublishFailure)
                {
                    Debug.LogWarning($"[Foxglove] LaserScan publish failed; skipping until valid data is provided. {ex.Message}");
                    _warnedPublishFailure = true;
                }

                return false;
            }
        }

        private void PublishScan(
            ulong unixNs,
            string frameId,
            double startRad,
            double endRad,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities)
        {
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = LaserScanMessageBuilder.SerializeProtobuf(unixNs, frameId, startRad, endRad, ranges, intensities);
                PublishProto(payload, unixNs);
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                ros2Payload = Ros2CdrLaserScanBuilder.Serialize(unixNs, frameId, startRad, endRad, ranges, intensities);
                PublishRos2(ros2Payload, unixNs);
            }
            else if (publishWebSocket)
            {
                var message = LaserScanMessageBuilder.CreateJson(unixNs, frameId, startRad, endRad, ranges, intensities);
                Publish(message, unixNs);
            }

            if (publishBridge)
            {
                ros2Payload ??= Ros2CdrLaserScanBuilder.Serialize(unixNs, frameId, startRad, endRad, ranges, intensities);
                PublishRos2Bridge(ros2Payload, unixNs);
            }
        }

        private void QueuePublishFrame(
            ulong logTimeNs,
            string frameId,
            double startAngleRadians,
            double endAngleRadians,
            IEnumerable<double> ranges,
            IEnumerable<double> intensities)
        {
            var rangeCopy = CopyRequiredValues(ranges, nameof(ranges));
            var intensityCopy = CopyOptionalValues(intensities);
            var queuedCount = Interlocked.Increment(ref _queuedPublishFrameCount);
            if (queuedCount > MaxQueuedPublishFrames)
            {
                Interlocked.Decrement(ref _queuedPublishFrameCount);
                Interlocked.Increment(ref _droppedQueuedPublishFrameCount);
                return;
            }

            _queuedPublishFrames.Enqueue(new QueuedLaserScanFrame(
                logTimeNs,
                frameId,
                startAngleRadians,
                endAngleRadians,
                rangeCopy,
                intensityCopy));
            Interlocked.Increment(ref _queuedOffMainThreadPublishFrameCount);
        }

        private void DrainQueuedPublishFrames()
        {
            var queuedFromWorker = Interlocked.Exchange(ref _queuedOffMainThreadPublishFrameCount, 0);
            if (queuedFromWorker > 0 && !_warnedOffMainThreadPublishFrame)
            {
                Debug.LogWarning("[Foxglove] LaserScan PublishFrame was called off the Unity main thread; frames are queued and published during Update.");
                _warnedOffMainThreadPublishFrame = true;
            }

            var dropped = Interlocked.Exchange(ref _droppedQueuedPublishFrameCount, 0);
            if (dropped > 0)
                Debug.LogWarning($"[Foxglove] LaserScan dropped {dropped} queued PublishFrame request(s) because the main-thread queue is full.");

            while (_queuedPublishFrames.TryDequeue(out var frame))
            {
                Interlocked.Decrement(ref _queuedPublishFrameCount);
                PublishFrameOnMainThread(
                    frame.LogTimeNs,
                    frame.FrameId,
                    frame.StartAngleRadians,
                    frame.EndAngleRadians,
                    frame.Ranges,
                    frame.Intensities);
            }
        }

        private bool IsUnityMainThread()
        {
            return _unityThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _unityThreadId;
        }

        private static double[] CopyRequiredValues(IEnumerable<double> values, string parameterName)
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            return values is double[] array ? (double[])array.Clone() : values.ToArray();
        }

        private static double[] CopyOptionalValues(IEnumerable<double> values)
        {
            if (values == null)
                return Array.Empty<double>();
            return values is double[] array ? (double[])array.Clone() : values.ToArray();
        }

        private static bool IsRecoverablePublishException(Exception ex)
        {
            return !(ex is OutOfMemoryException)
                   && !(ex is StackOverflowException)
                   && !(ex is AccessViolationException)
                   && !(ex is AppDomainUnloadedException);
        }

        private double[] ResolveRanges()
        {
            if (_ranges != null && _ranges.Length > 0)
                return _ranges;

            return BuildSyntheticRanges();
        }

        private double[] BuildSyntheticRanges()
        {
            var count = Mathf.Max(1, _syntheticSampleCount);
            if (_syntheticRanges == null
                || _syntheticRangesCount != count
                || _syntheticRangesMeters != _syntheticRangeMeters)
            {
                _syntheticRanges = new double[count];
                _syntheticRangesCount = count;
                _syntheticRangesMeters = _syntheticRangeMeters;
                for (var i = 0; i < _syntheticRanges.Length; i++)
                    _syntheticRanges[i] = _syntheticRangeMeters;
            }
            return _syntheticRanges;
        }

        private sealed class QueuedLaserScanFrame
        {
            public QueuedLaserScanFrame(
                ulong logTimeNs,
                string frameId,
                double startAngleRadians,
                double endAngleRadians,
                double[] ranges,
                double[] intensities)
            {
                LogTimeNs = logTimeNs;
                FrameId = frameId;
                StartAngleRadians = startAngleRadians;
                EndAngleRadians = endAngleRadians;
                Ranges = ranges;
                Intensities = intensities;
            }

            public ulong LogTimeNs { get; }
            public string FrameId { get; }
            public double StartAngleRadians { get; }
            public double EndAngleRadians { get; }
            public double[] Ranges { get; }
            public double[] Intensities { get; }
        }
    }
}
