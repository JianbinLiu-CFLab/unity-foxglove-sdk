// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Camera
// Purpose: Schema-neutral camera profile contract for optional sensor-unit wiring.

using System.Numerics;

namespace Unity.FoxgloveSDK.Schemas.Camera
{
    /// <summary>
    /// Minimal camera-facing view of a shared sensor unit profile.
    /// Keeps camera publishers independent from the optional sensor assemblies.
    /// </summary>
    public interface ISensorCameraProfile
    {
        string SensorFrameId { get; }
        string CameraFrameId { get; }
        string CameraImageTopic { get; }
        string CameraInfoTopic { get; }
        Vector3 CameraToSensorTranslationMeters { get; }
        Quaternion CameraToSensorRotation { get; }
    }
}
