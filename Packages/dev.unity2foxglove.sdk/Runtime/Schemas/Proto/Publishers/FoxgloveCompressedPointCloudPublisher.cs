// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Spike publisher for foxglove.CompressedPointCloud Draco payloads.

using Foxglove.Schemas;
using Foxglove.Schemas.PointCloud;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Legacy standalone Phase 87 Draco spike publisher.
    /// It reuses the raw point-cloud QoS path, then publishes a separate
    /// protobuf-only <c>foxglove.CompressedPointCloud</c> topic through the bundled native Draco plugin.
    /// Prefer <see cref="FoxglovePointCloudPublisher"/> with Point Cloud Output Mode set to Draco for normal scenes.
    /// </summary>
    [AddComponentMenu("")]
    public class FoxgloveCompressedPointCloudPublisher : FoxglovePointCloudPublisher
    {
        [Header("Draco Spike")]
        [SerializeField] private bool _logDracoFailures = true;

        private bool _warnedDracoFailure;

        protected override string SchemaNameOverride => "foxglove.CompressedPointCloud";
        protected override string DefaultTopic => "/unity/point_cloud_draco";
        public override bool SupportsJsonEncoding => false;
        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => false;
        protected override string Ros2SchemaName => "";

        protected override void PublishPreparedFrame(PointCloudFrame frame, ulong unixNs)
        {
            if (frame == null || frame.Points.Count == 0)
                return;

            if (!DracoPointCloudNativeEncoder.TryEncode(frame, out var dracoPayload, out var encodeError))
            {
                LogDracoFailure((encodeError ?? "Draco native encoder failed.") + " The spike publisher publishes nothing on the Draco topic.");
                return;
            }

            _warnedDracoFailure = false;
            var payload = CompressedPointCloudMessageBuilder.SerializeProtobuf(frame, dracoPayload);
            PublishProto(payload, unixNs);
        }

        private void LogDracoFailure(string message)
        {
            if (!_logDracoFailures || _warnedDracoFailure)
                return;

            Debug.LogWarning("[Foxglove] CompressedPointCloud Draco native encoder unavailable: " + message);
            _warnedDracoFailure = true;
        }
    }
}
