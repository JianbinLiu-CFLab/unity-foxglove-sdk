// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>One fail-fast policy shared by FoxRun dispatch boundaries.</summary>
    public static class FoxRunExceptionPolicy
    {
        /// <summary>
        /// Returns whether a Provider boundary may isolate the exception
        /// without masking a process-corrupting runtime failure.
        /// </summary>
        public static bool IsRecoverable(Exception exception)
            => !(exception is OutOfMemoryException)
               && !(exception is StackOverflowException)
               && !(exception is AccessViolationException)
               && !(exception is AppDomainUnloadedException);
    }
}
