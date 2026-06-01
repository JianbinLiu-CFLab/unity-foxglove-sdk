// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free monotonic publish-order guard for async JPEG results.

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>
    /// Ensures publish order for async JPEG results is monotonic in capture timestamp.
    /// </summary>
    public static class CameraJpegPublishOrderPolicy
    {
        /// <summary>
        /// Returns <c>true</c> if a capture with <paramref name="captureUnixNs"/> is newer
        /// than the last published frame timestamp.
        /// </summary>
        public static bool ShouldPublish(ulong captureUnixNs, ulong lastPublishedCaptureUnixNs)
            => captureUnixNs > lastPublishedCaptureUnixNs;
    }
}
