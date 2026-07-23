// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Attributes
// Purpose: Direction-aware FoxRun update policy.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Declares when an eligible FoxRun value crosses its Unity boundary.
    /// The same policy governs both independently scheduled halves of an
    /// explicit <see cref="FoxRunFlow.PublishAndSubscribe"/> declaration.
    /// </summary>
    public enum FoxRunPolicy
    {
        /// <summary>Move a fresh value at the eligible fixed cadence.</summary>
        FixedRate = 1,

        /// <summary>Move the first value and later semantic changes.</summary>
        Change = 2,

        /// <summary>Move changes and fresh duplicates after the configured interval.</summary>
        ChangeOrInterval = 3,

        /// <summary>Move a value only through a generated explicit trigger.</summary>
        Trigger = 4
    }
}
