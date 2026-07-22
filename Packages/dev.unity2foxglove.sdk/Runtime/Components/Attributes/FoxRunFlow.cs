// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Direction-aware FoxRun declaration flow.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares whether a FoxRun member publishes, subscribes, or does both.
    /// <see cref="PublishAndSubscribe"/> is a full-duplex debugging and
    /// integration convenience; production declarations normally choose one
    /// explicit direction.
    /// </summary>
    public enum FoxRunFlow
    {
        /// <summary>Unity publishes the member value.</summary>
        Publish = 1,

        /// <summary>Unity applies the selected inbound source to the member.</summary>
        Subscribe = 2,

        /// <summary>Unity publishes and subscribes through one full-duplex declaration.</summary>
        PublishAndSubscribe = 3
    }
}
