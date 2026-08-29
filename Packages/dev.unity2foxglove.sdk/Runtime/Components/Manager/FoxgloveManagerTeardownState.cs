// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.ExceptionServices;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Runs Manager-owned lifecycle cleanup to completion while preserving the
    /// first failure and its original stack for the Unity lifecycle caller.
    /// </summary>
    internal static class FoxgloveManagerTeardownState
    {
        internal static void RunDisable(
            Action endSubscriptionSession,
            Action endTransportSession,
            Action stopServer,
            Action endPublishSession,
            Action resetOutputModeWatch,
            Action resetProfiler)
            => RunMandatoryCleanup(
                endSubscriptionSession,
                endTransportSession,
                stopServer,
                endPublishSession,
                resetOutputModeWatch,
                resetProfiler);

        internal static void RunDestroy(
            Action endSubscriptionSession,
            Action stopServer,
            Action endTransportSession,
            Action disposeReplayCursorEndpoint,
            Action disposeCertificateDistributor,
            Action disposeRuntime,
            Action endPublishSession,
            Action resetProfiler)
            => RunMandatoryCleanup(
                endSubscriptionSession,
                stopServer,
                endTransportSession,
                disposeReplayCursorEndpoint,
                disposeCertificateDistributor,
                disposeRuntime,
                endPublishSession,
                resetProfiler);

        /// <summary>
        /// Attempts runtime disposal twice when the first attempt reports a
        /// recoverable partial-cleanup failure, while always releasing the
        /// owner's reference. The first failure is rethrown only when the retry
        /// also fails; transient failures are reported through the callback.
        /// </summary>
        internal static void RunRuntimeDisposeWithRetry(
            Action disposeRuntime,
            Action releaseRuntime,
            Action<Exception> reportFailure)
        {
            ExceptionDispatchInfo firstFailure = null;
            try
            {
                try
                {
                    disposeRuntime?.Invoke();
                    return;
                }
                catch (Exception exception)
                {
                    firstFailure = ExceptionDispatchInfo.Capture(exception);
                    ReportFailure(reportFailure, exception);
                }

                try
                {
                    disposeRuntime?.Invoke();
                }
                catch (Exception exception)
                {
                    ReportFailure(reportFailure, exception);
                    firstFailure.Throw();
                }
            }
            finally
            {
                releaseRuntime?.Invoke();
            }
        }

        private static void ReportFailure(Action<Exception> reportFailure, Exception exception)
        {
            try
            {
                reportFailure?.Invoke(exception);
            }
            catch
            {
                // Diagnostics must not prevent the cleanup retry or reference release.
            }
        }

        private static void RunMandatoryCleanup(params Action[] steps)
        {
            ExceptionDispatchInfo firstFailure = null;
            foreach (var step in steps)
            {
                try
                {
                    step?.Invoke();
                }
                catch (Exception exception)
                {
                    firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            firstFailure?.Throw();
        }
    }
}
