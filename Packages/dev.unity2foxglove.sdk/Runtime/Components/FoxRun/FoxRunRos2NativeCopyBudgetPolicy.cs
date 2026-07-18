// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Portable bounds and recovery rules for native ROS2 copied-message budgets.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>ROS-free portable limits for copied native ROS2 subscription data.</summary>
    public static class FoxRunRos2NativeCopyBudgetPolicy
    {
        /// <summary>Smallest user-editable copied-data budget.</summary>
        public const int MinBytes = 1024;

        /// <summary>Largest portable copied-data budget.</summary>
        public const int MaxBytes = 256 * 1024 * 1024;

        /// <summary>Safe default used when old serialized scenes have no configured budget.</summary>
        public const int DefaultBytes = 4 * 1024 * 1024;

        /// <summary>
        /// Recovers a serialized value while preserving the historic missing-value default.
        /// </summary>
        public static int NormalizeSerializedBytes(int serializedBytes)
        {
            return serializedBytes <= 0
                ? DefaultBytes
                : ClampUserEditedBytes(serializedBytes);
        }

        /// <summary>Clamps a direct user edit to the portable copied-data range.</summary>
        public static int ClampUserEditedBytes(int editedBytes)
        {
            if (editedBytes < MinBytes)
                return MinBytes;

            return editedBytes > MaxBytes
                ? MaxBytes
                : editedBytes;
        }
    }
}
