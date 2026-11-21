// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/OpenH264
// Purpose: Stable Windows file identity for the OpenH264 safe-file layer.

using System;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Identifies a Windows file-system object and records reparse status.</summary>
    internal readonly struct OpenH264WindowsFileIdentity : IEquatable<OpenH264WindowsFileIdentity>
    {
        /// <summary>Creates a file identity from native handle metadata.</summary>
        internal OpenH264WindowsFileIdentity(
            uint volumeSerialNumber,
            uint fileIndexHigh,
            uint fileIndexLow,
            bool isReparsePoint)
        {
            VolumeSerialNumber = volumeSerialNumber;
            FileIndexHigh = fileIndexHigh;
            FileIndexLow = fileIndexLow;
            IsReparsePoint = isReparsePoint;
        }

        internal uint VolumeSerialNumber { get; }
        internal uint FileIndexHigh { get; }
        internal uint FileIndexLow { get; }
        internal bool IsReparsePoint { get; }

        public bool Equals(OpenH264WindowsFileIdentity other)
            => VolumeSerialNumber == other.VolumeSerialNumber
                && FileIndexHigh == other.FileIndexHigh
                && FileIndexLow == other.FileIndexLow;

        public override bool Equals(object obj)
            => obj is OpenH264WindowsFileIdentity other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(VolumeSerialNumber, FileIndexHigh, FileIndexLow);
    }
}
