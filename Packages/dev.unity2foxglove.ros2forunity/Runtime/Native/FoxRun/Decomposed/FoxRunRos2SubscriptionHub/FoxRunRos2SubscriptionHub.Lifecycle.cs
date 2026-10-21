// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Hidden main-thread host for generated native FoxRun subscriptions.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.Profiling;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class FoxRunRos2SubscriptionHub : MonoBehaviour
    {
        private static double EffectiveSubscribeRateHz(
            FoxRunRos2GeneratedContract contract,
            int managerDefaultSubscribeRateHz,
            int transportAdmissionRateLimitHz)
            => Math.Min(
                contract.HasExplicitHz
               && !float.IsNaN(contract.Hz)
               && !float.IsInfinity(contract.Hz)
               && contract.Hz > 0f
                ? contract.Hz
                : Math.Max(1, managerDefaultSubscribeRateHz),
                Math.Max(1, transportAdmissionRateLimitHz));

        private long ActiveGeneration()
            => _activeSession.ReadGeneration();

        private void DispatchCleanupToHostThread(Action cleanup)
        {
            var queue = _hostCleanupQueue;
            if (queue == null)
            {
                throw new InvalidOperationException(
                    "The Unity host cleanup queue is unavailable for deferred ROS2 stream cleanup.");
            }
            queue.Dispatch(cleanup);
        }

        private void DrainPendingHostCleanup()
        {
            _hostCleanupQueue?.Drain(exception => WarnHostOnce(
                "deferred-cleanup|" + exception.GetType().Name,
                "Deferred native ROS2 stream cleanup failed: "
                + FoxRunRos2PublicDiagnostic.Describe(
                    FoxRunRos2RegistrationError.TeardownFailure)));
        }

        private void RecordUnsupported(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            FoxRunRos2RegistrationError error,
            string diagnostic)
            => UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, FoxRunResolvedQos.Default, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.Unsupported,
                error, diagnostic,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        private void RecordWaiting(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            FoxRunResolvedQos qos,
            string diagnostic)
            => UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, qos, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.WaitingForRuntime,
                FoxRunRos2RegistrationError.RuntimeUnavailable, diagnostic,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        private void RecordFailed(
            string endpointIdentity,
            FoxRunRos2GeneratedContract contract,
            Exception exception)
        {
            UpdateDiagnostic(endpointIdentity, new FoxRunRos2SubscriptionBindingSnapshot(
                contract, FoxRunResolvedQos.Default, ActiveGeneration(), FoxRunRos2SubscriptionBindingState.Failed,
                FoxRunRos2RegistrationError.BackendFailure,
                FoxRunRos2PublicDiagnostic.Describe(FoxRunRos2RegistrationError.BackendFailure),
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        private void UpdateDiagnostic(
            string endpointIdentity,
            FoxRunRos2SubscriptionBindingSnapshot snapshot)
        {
            _diagnostics.Update(endpointIdentity, snapshot, _runtimeDiagnosticContext);
            if (_diagnostics.ShouldLog(endpointIdentity, snapshot))
                Debug.LogWarning("[FoxRun ROS2] " + snapshot.ContractId + ": " + snapshot.Diagnostic);
        }

        private static long CheckedGeneration(ulong generation)
        {
            if (generation > long.MaxValue)
                throw new InvalidOperationException("FoxRun subscription generation exceeds the native host range.");
            return (long)generation;
        }

        private void StopBindingsAndNode()
        {
            ExceptionDispatchInfo fatal = null;
            var cleanupComplete = false;
            var owner = _nodeOwner;
            var hostReleased = owner == null;
            var cleanupQueue = EnsureHostCleanupQueue();
            try
            {
                StopHostedBindingsAndDrainDeferredCleanupThenReleaseHost(
                    _bindings,
                    cleanupQueue,
                    TimeSpan.FromMilliseconds(DeferredCleanupDrainTimeoutMilliseconds),
                    exception => WarnHostOnce(
                        "stop-binding|" + exception.GetType().Name,
                        "Native ROS2 subscription teardown failed: "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TeardownFailure)),
                    () =>
                    {
                        hostReleased = owner == null || owner.ReleaseHostOwnership();
                        return hostReleased;
                    },
                    out cleanupComplete);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            if (!cleanupComplete)
            {
                WarnHostOnce(
                    "stop-binding|deferred-cleanup-timeout",
                    "Native ROS2 subscription teardown remains pending after the bounded host cleanup window.");
            }
            if (cleanupComplete && hostReleased)
            {
                _nodeOwner = null;
                _bindings.Clear();
                _ros2Unity = null;
            }
            else
            {
                // Keep bindings that still own a native token. A later host
                // update must be able to retry removal instead of losing the
                // only managed retirement owner after the bounded drain.
                for (var index = _bindings.Count - 1; index >= 0; index--)
                {
                    if (_bindings[index].CleanupComplete)
                        _bindings.RemoveAt(index);
                }
            }
            _stale.Clear();
            _sources.Clear();
            _seenSources.Clear();
            _seenEndpoints.Clear();
            _existingBindings.Clear();
            _diagnostics.Clear();
            _runtimeDiagnosticContext = FoxRunRos2RuntimeDiagnosticContext.Unknown;
            fatal?.Throw();
        }

        internal static void StopHostedBindings(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings,
            Action<Exception> reportFailure)
        {
            if (bindings == null)
                return;

            ExceptionDispatchInfo fatal = null;
            for (var index = 0; index < bindings.Count; index++)
            {
                try
                {
                    bindings[index]?.Stop();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    try
                    {
                        reportFailure?.Invoke(exception);
                    }
                    catch (Exception reportException) when (
                        FoxRunRos2NativeExceptionPolicy.IsRecoverable(reportException))
                    {
                        // Diagnostics cannot interrupt remaining teardown.
                    }
                    catch (Exception reportException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(reportException);
                    }
                }
                catch (Exception exception)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            fatal?.Throw();
        }

        internal static void StopHostedBindingsAndDrainDeferredCleanup(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings,
            FoxRunRos2HostCleanupQueue cleanupQueue,
            TimeSpan timeout,
            Action<Exception> reportFailure,
            out bool cleanupComplete)
        {
            if (cleanupQueue == null)
                throw new ArgumentNullException(nameof(cleanupQueue));

            cleanupComplete = false;
            ExceptionDispatchInfo fatal = null;
            try
            {
                StopHostedBindings(bindings, reportFailure);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                cleanupComplete = cleanupQueue.DrainUntil(
                    () => HostedCleanupIsComplete(bindings),
                    timeout,
                    reportFailure);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
                cleanupComplete = HostedCleanupIsComplete(bindings);
            }

            fatal?.Throw();
        }

        internal static void StopHostedBindingsAndDrainDeferredCleanupThenReleaseHost(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings,
            FoxRunRos2HostCleanupQueue cleanupQueue,
            TimeSpan timeout,
            Action<Exception> reportFailure,
            Func<bool> releaseHostOwnership,
            out bool cleanupComplete)
        {
            if (releaseHostOwnership == null)
                throw new ArgumentNullException(nameof(releaseHostOwnership));

            cleanupComplete = false;
            ExceptionDispatchInfo fatal = null;
            try
            {
                StopHostedBindingsAndDrainDeferredCleanup(
                    bindings,
                    cleanupQueue,
                    timeout,
                    reportFailure,
                    out cleanupComplete);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }

            try
            {
                if (!releaseHostOwnership())
                    cleanupComplete = false;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                try
                {
                    reportFailure?.Invoke(exception);
                }
                catch (Exception reportException)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(reportException);
                }
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            fatal?.Throw();
        }

        private static bool HostedCleanupIsComplete(
            IReadOnlyList<IFoxRunRos2SubscriptionHostedCleanup> bindings)
        {
            if (bindings == null)
                return true;
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding != null && !binding.CleanupComplete)
                    return false;
            }
            return true;
        }

        private void BeginShutdown()
        {
            if (!_stopping)
                _stopping = true;
            _activeSession.Deactivate();
            SetManager(null);
            StopBindingsAndNode();
            Application.quitting -= OnApplicationQuitting;
        }

        internal static void StopForNativeRuntimeShutdown()
        {
            var instances = FindObjectsByType<FoxRunRos2SubscriptionHub>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                if (instance == null)
                    continue;
                instance.BeginShutdown();
            }
        }

        private void WarnHostOnce(string key, string message)
        {
            if (_hostWarnings.Add(key))
                Debug.LogWarning("[FoxRun ROS2] " + message);
        }

        private void OnApplicationQuitting() => BeginShutdown();

        private void OnApplicationQuit() => BeginShutdown();

        private void OnDisable()
        {
            _stopping = true;
            BeginShutdown();
        }

        private void OnDestroy()
        {
            BeginShutdown();
            Application.quitting -= OnApplicationQuitting;
        }

    }
}
#endif
