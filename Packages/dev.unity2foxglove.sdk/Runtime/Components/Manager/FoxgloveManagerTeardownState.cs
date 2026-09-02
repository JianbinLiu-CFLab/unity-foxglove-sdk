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
        /// Completes the StopServer tail even when runtime.Stop reports a
        /// failure, then rethrows the first failure after every tail action has
        /// had an opportunity to run.
        /// </summary>
        internal static void RunStopServer(
            Action stopRuntime,
            Action resetSensorClock,
            Action stopRemoteMcapFileServer,
            Action stopReplayCursorEndpoint,
            Action stopCertificateDistributor,
            Action clearChannelCaches,
            Action clearClientEvents,
            Action resetChannelIds,
            Action restoreLivePublishers)
            => RunMandatoryCleanup(
                stopRuntime,
                resetSensorClock,
                stopRemoteMcapFileServer,
                stopReplayCursorEndpoint,
                stopCertificateDistributor,
                clearChannelCaches,
                clearClientEvents,
                resetChannelIds,
                restoreLivePublishers);

        /// <summary>
        /// Attempts runtime disposal twice when the first attempt reports a
        /// recoverable partial-cleanup failure. The owner releases its reference
        /// only after an attempt completes so a permanently incomplete runtime
        /// remains reachable for a later lifecycle retry. The first failure is
        /// rethrown only when the immediate retry also fails.
        /// </summary>
        internal static void RunRuntimeDisposeWithRetry(
            Action disposeRuntime,
            Action releaseRuntime,
            Action<Exception> reportFailure)
        {
            ExceptionDispatchInfo firstFailure = null;
            try
            {
                disposeRuntime?.Invoke();
            }
            catch (Exception exception)
            {
                firstFailure = ExceptionDispatchInfo.Capture(exception);
                ReportFailure(reportFailure, exception);
            }

            if (firstFailure != null)
            {
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

            try
            {
                releaseRuntime?.Invoke();
            }
            catch (Exception releaseFailure)
            {
                ReportFailure(reportFailure, releaseFailure);
                ExceptionDispatchInfo.Capture(releaseFailure).Throw();
                throw;
            }
        }

        /// <summary>
        /// A stopped Manager must still enter the teardown tail while either an
        /// active session or a retired session retains callback ownership.
        /// </summary>
        internal static bool ShouldRunStopServer(
            bool isRunning,
            bool hasActiveSession,
            bool hasPendingSessionCleanup)
            => isRunning || hasActiveSession || hasPendingSessionCleanup;

        /// <summary>
        /// Runs a set of pre-tail cleanup actions to completion and returns the
        /// first captured failure without throwing. StopServer uses this for
        /// event-detach and registry steps that must not prevent the mandatory
        /// runtime-stop tail from running.
        /// </summary>
        internal static ExceptionDispatchInfo RunCleanupReturningFirstFailure(
            params Action[] steps)
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

            return firstFailure;
        }

        private static void ReportFailure(Action<Exception> reportFailure, Exception exception)
        {
            try
            {
                reportFailure?.Invoke(exception);
            }
            catch
            {
                // Diagnostics must not prevent the cleanup retry.
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
