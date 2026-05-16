// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Spike publisher for foxglove.CompressedPointCloud Draco payloads.

using System;
using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Experimental publisher for the Phase 87 Draco feasibility spike.
    /// It reuses the raw point-cloud QoS path, then publishes a separate
    /// protobuf-only <c>foxglove.CompressedPointCloud</c> topic.
    /// </summary>
    [AddComponentMenu("Foxglove/Experimental/Compressed Point Cloud Draco Publisher")]
    public class FoxgloveCompressedPointCloudPublisher : FoxglovePointCloudPublisher
    {
        [Header("Draco Spike")]
        [SerializeField] private string _helperExecutablePath = "";
        [SerializeField, Min(1)] private int _encodeTimeoutMs = 5000;
        [SerializeField] private bool _logDracoFailures = true;

        private DracoPointCloudEncoderSidecar _sidecar;
        private bool _warnedDracoFailure;

        protected override string SchemaNameOverride => "foxglove.CompressedPointCloud";
        protected override string DefaultTopic => "/unity/point_cloud_draco";
        public override bool SupportsJsonEncoding => false;
        public override bool SupportsProtobufEncoding => true;

        protected override void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)
        {
            if (frame == null || frame.Points.Count == 0)
                return;

            if (!EnsureSidecarStarted())
                return;

            if (!_sidecar.TryEncode(frame, Math.Max(1, _encodeTimeoutMs), out var dracoPayload))
            {
                LogDracoFailure((_sidecar?.LastError ?? "Draco helper failed.") + " The spike publisher publishes nothing on the Draco topic.");
                StopSidecar();
                return;
            }

            _warnedDracoFailure = false;
            var payload = CompressedPointCloudMessageBuilder.SerializeProtobuf(frame, dracoPayload);
            PublishProto(payload, unixNs);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopSidecar();
        }

        private bool EnsureSidecarStarted()
        {
            if (_sidecar != null && _sidecar.IsRunning)
                return true;

            StopSidecar();
            _sidecar = new DracoPointCloudEncoderSidecar();
            if (_sidecar.Start(_helperExecutablePath))
            {
                _warnedDracoFailure = false;
                return true;
            }

            LogDracoFailure((_sidecar.LastError ?? "Failed to start Draco helper.") + " The spike publisher publishes nothing on the Draco topic.");
            StopSidecar();
            return false;
        }

        private void StopSidecar()
        {
            if (_sidecar == null)
                return;

            _sidecar.Dispose();
            _sidecar = null;
        }

        private void LogDracoFailure(string message)
        {
            if (!_logDracoFailures || _warnedDracoFailure)
                return;

            Debug.LogWarning("[Foxglove] CompressedPointCloud Draco spike disabled: " + message);
            _warnedDracoFailure = true;
        }
    }
}
