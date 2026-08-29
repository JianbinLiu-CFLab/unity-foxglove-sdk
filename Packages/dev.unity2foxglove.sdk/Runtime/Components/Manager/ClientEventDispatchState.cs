// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Main-thread subscriber dispatch state for queued client events.

using System;

namespace Unity.FoxgloveSDK.Components
{
    internal sealed class ClientEventDispatchState
    {
        private const long FailureWarningIntervalTicks =
            5L * 1000L * 1000L * 10L;
        private long _lastFailureWarningTicks;

        internal bool InvokeIfLive(
            Func<bool> isLive,
            Action first,
            Action second)
        {
            if (isLive == null || !isLive())
                return false;

            first?.Invoke();
            if (!isLive())
                return false;

            second?.Invoke();
            return isLive();
        }

        internal void Invoke<T>(
            Action<T> subscribers,
            T value,
            Action<Exception> reportFailure)
        {
            if (subscribers == null)
                return;

            foreach (Action<T> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(value);
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy.IsRecoverable(exception))
                {
                    ReportFailure(exception, reportFailure);
                }
            }
        }

        internal void Invoke<T1, T2, T3, T4>(
            Action<T1, T2, T3, T4> subscribers,
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4,
            Action<Exception> reportFailure)
        {
            if (subscribers == null)
                return;

            foreach (Action<T1, T2, T3, T4> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(
                        value1,
                        value2,
                        value3,
                        value4);
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy.IsRecoverable(exception))
                {
                    ReportFailure(exception, reportFailure);
                }
            }
        }

        internal void Invoke<T1, T2, T3, T4, T5>(
            Action<T1, T2, T3, T4, T5> subscribers,
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4,
            T5 value5,
            Action<Exception> reportFailure)
        {
            if (subscribers == null)
                return;

            foreach (Action<T1, T2, T3, T4, T5> subscriber in
                     subscribers.GetInvocationList())
            {
                try
                {
                    subscriber(
                        value1,
                        value2,
                        value3,
                        value4,
                        value5);
                }
                catch (Exception exception)
                    when (FoxRunExceptionPolicy.IsRecoverable(exception))
                {
                    ReportFailure(exception, reportFailure);
                }
            }
        }

        private void ReportFailure(
            Exception exception,
            Action<Exception> reportFailure)
        {
            if (reportFailure == null
                || !WarningDebouncer.TryUpdateCooldown(
                    ref _lastFailureWarningTicks,
                    DateTime.UtcNow.Ticks,
                    FailureWarningIntervalTicks))
            {
                return;
            }

            try
            {
                reportFailure(exception);
            }
            catch (Exception diagnosticException)
                when (FoxRunExceptionPolicy.IsRecoverable(
                    diagnosticException))
            {
                // Diagnostics are outside the client-event delivery contract.
            }
        }
    }
}
