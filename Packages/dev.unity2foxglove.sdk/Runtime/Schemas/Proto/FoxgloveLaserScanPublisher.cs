// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto
// Purpose: Publishes foxglove.LaserScan messages with JSON/protobuf encoding.

using System;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes a planar laser scan. Empty range arrays produce a small synthetic scan
    /// for smoke tests and first-run setup.
    /// </summary>
    public class FoxgloveLaserScanPublisher : FoxglovePublisherBase
    {
        [Header("Laser Scan")]
        [SerializeField] private string _frameId = "laser";
        [SerializeField] private double _startAngleDegrees = -45;
        [SerializeField] private double _endAngleDegrees = 45;
        [SerializeField] private double[] _ranges;
        [SerializeField] private double[] _intensities;
        [SerializeField, Min(1)] private int _syntheticSampleCount = 16;
        [SerializeField] private double _syntheticRangeMeters = 2.0;

        private bool _warnedIntensityMismatch;
        private double[] _syntheticRanges;
        private int _syntheticRangesCount;
        private double _syntheticRangesMeters;

        protected override string SchemaName => FoxgloveSchemaDefinitions.LaserScanSchemaName;
        public override bool SupportsProtobufEncoding => true;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/unity/laser_scan";
        }

        private void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

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

            var unixNs = CurrentLogTimeNs;
            var startRad = _startAngleDegrees * Math.PI / 180.0;
            var endRad = _endAngleDegrees * Math.PI / 180.0;

            if (EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                var payload = LaserScanMessageBuilder.SerializeProtobuf(unixNs, _frameId, startRad, endRad, ranges, intensities);
                PublishProto(payload, unixNs);
            }
            else
            {
                var message = LaserScanMessageBuilder.CreateJson(unixNs, _frameId, startRad, endRad, ranges, intensities);
                Publish(message, unixNs);
            }
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
                || Math.Abs(_syntheticRangesMeters - _syntheticRangeMeters) > double.Epsilon)
            {
                _syntheticRanges = new double[count];
                _syntheticRangesCount = count;
                _syntheticRangesMeters = _syntheticRangeMeters;
                for (var i = 0; i < _syntheticRanges.Length; i++)
                    _syntheticRanges[i] = _syntheticRangeMeters;
            }
            return _syntheticRanges;
        }
    }
}
