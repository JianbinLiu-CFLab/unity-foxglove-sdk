// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Retry deadline state for Manager-owned endpoints.

using System;

namespace Unity.FoxgloveSDK.Components
{
    internal enum RetryExecutionResult
    {
        Blocked,
        Succeeded,
        Failed
    }

    /// <summary>Provides deterministic retry deadlines for Manager-owned endpoints.</summary>
    internal sealed class RetryBackoffState
    {
        internal double RetryAt { get; private set; }
        internal bool HasSuccessfulConfiguration { get; private set; }

        internal bool IsBlocked(double now)
            => now < RetryAt;

        internal void RecordFailure(double now, double delaySeconds)
        {
            HasSuccessfulConfiguration = false;
            RetryAt = now + Math.Max(0d, delaySeconds);
        }

        internal void RecordSuccess()
        {
            HasSuccessfulConfiguration = true;
            RetryAt = 0d;
        }

        internal RetryExecutionResult TryExecute(
            double now,
            double delaySeconds,
            Action operation,
            Action<Exception> onFailure)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            if (IsBlocked(now))
                return RetryExecutionResult.Blocked;

            try
            {
                operation();
                RecordSuccess();
                return RetryExecutionResult.Succeeded;
            }
            catch (Exception exception)
            {
                try
                {
                    onFailure(exception);
                }
                finally
                {
                    RecordFailure(now, delaySeconds);
                }

                return RetryExecutionResult.Failed;
            }
        }
    }
}
