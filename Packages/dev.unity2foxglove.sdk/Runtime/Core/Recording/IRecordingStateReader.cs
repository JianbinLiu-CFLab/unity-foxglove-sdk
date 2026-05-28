// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Recording
// Purpose: Read-only view of recording state used by other subsystems (e.g. replay)
// to enforce mutual-exclusion and coordinate-mode parity checks without
// taking a hard dependency on RecordingController.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Read-only view of recording state used by other subsystems (e.g. replay)
    /// to enforce mutual-exclusion and coordinate-mode parity checks without
    /// taking a hard dependency on <see cref="RecordingController"/>.
    /// </summary>
    public interface IRecordingStateReader
    {
        bool IsEnabled { get; }
        string CoordinateMode { get; }
    }
}
