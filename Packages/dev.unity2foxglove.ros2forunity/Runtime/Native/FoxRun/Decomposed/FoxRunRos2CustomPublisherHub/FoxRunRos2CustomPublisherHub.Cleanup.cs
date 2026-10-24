// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Binding cleanup, retry, teardown, and shutdown lifecycle.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class FoxRunRos2CustomPublisherHub
    {
        private void StopBindings()
        {
            try
            {
                StopAllBindings(
                    _bindings,
                    exception => WarnOnce(
                        "stop|" + exception.GetType().FullName + "|" + exception.Message,
                        "Custom native ROS2 publisher teardown failed: "
                        + exception.GetType().Name));
            }
            finally
            {
                RemoveCompletedBindings();
                _stale.Clear();
                _existing.Clear();
                for (var index = 0; index < _bindings.Count; index++)
                    if (_bindings[index].CleanupPending)
                        _existing.Add(_bindings[index].Identity);
                _seen.Clear();
                _nativeDemand.Clear();
                _terminalRegistrationFailures.Clear();
                _observedNativeContractCount = 0;
                _scanCompleted = false;
            }
        }

        private void RetryPendingBindings()
        {
            for (var index = 0; index < _bindings.Count; index++)
            {
                var binding = _bindings[index];
                if (!binding.CleanupPending)
                    continue;
                try
                {
                    if (binding.CleanupRetryExhausted && !binding.CleanupRetryFatal)
                        binding.TryForceRetryCleanup();
                    else
                        binding.TryRetryCleanup();
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    WarnOnce(
                        binding.Identity + "|cleanup-retry|" + exception.GetType().Name,
                        "Custom native ROS2 publisher cleanup retry failed: "
                        + exception.GetType().Name);
                }
                if (binding.CleanupRetryExhausted)
                {
                    WarnOnce(
                        binding.Identity + "|cleanup-stalled",
                        "Custom native ROS2 publisher cleanup is stalled; ownership is retained until native removal succeeds.");
                }
            }

            RemoveCompletedBindings();
        }

        private void RemoveCompletedBindings()
        {
            for (var index = _bindings.Count - 1; index >= 0; index--)
            {
                if (_bindings[index].IsStopped && !_bindings[index].CleanupPending)
                {
                    _existing.Remove(_bindings[index].Identity);
                    _bindings.RemoveAt(index);
                }
            }
        }

        internal static void StopAllBindings(
            IReadOnlyList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
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
                        // Diagnostics must not interrupt the remaining teardown.
                    }
                    catch (Exception reportException)
                    {
                        fatal ??= ExceptionDispatchInfo.Capture(reportException);
                    }
                }
                catch (Exception exception)
                {
                    // Finish the remaining mandatory endpoint cleanup, then
                    // preserve the first fatal exception and its stack.
                    fatal ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            fatal?.Throw();
        }

        internal static void StopStaleBindings(
            IList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
            IReadOnlyList<IFoxRunRos2CustomPublisherHostedBinding> stale,
            ISet<string> existing,
            Action<Exception> reportFailure)
        {
            if (stale == null)
                return;

            ExceptionDispatchInfo fatal = null;
            for (var index = 0; index < stale.Count; index++)
            {
                var binding = stale[index];
                try
                {
                    binding?.Stop();
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
                        // Diagnostics must not interrupt mandatory bookkeeping.
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
                finally
                {
                    if (binding != null && !binding.CleanupPending)
                    {
                        bindings?.Remove(binding);
                        existing?.Remove(binding.Identity);
                    }
                }
            }

            fatal?.Throw();
        }

        internal static void CleanupFailedStartupAndRethrow(
            Exception primaryException,
            Action stopBinding,
            Action endPublisher,
            Action releaseNode)
        {
            if (primaryException == null)
                throw new ArgumentNullException(nameof(primaryException));

            var primary = ExceptionDispatchInfo.Capture(primaryException);
            if (stopBinding != null)
            {
                try
                {
                    stopBinding();
                }
                catch (Exception)
                {
                    // The startup failure remains primary.
                }
            }
            else
            {
                try
                {
                    endPublisher?.Invoke();
                }
                catch (Exception)
                {
                    // Continue to the independently owned node release.
                }

                try
                {
                    releaseNode?.Invoke();
                }
                catch (Exception)
                {
                    // The startup failure remains primary.
                }
            }

            primary.Throw();
        }

        internal static bool ShouldStopFoxRunPublishing(
            bool publishSessionAllows,
            bool legacyComponentNativeOutputEnabled,
            bool bridgeLifecycleIsShuttingDown)
        {
            // The legacy switch owns component publishers only. FoxRun demand
            // is frozen in its directional publish session and explicit
            // contracts, so the switch must not disable these endpoints.
            _ = legacyComponentNativeOutputEnabled;
            return !publishSessionAllows || bridgeLifecycleIsShuttingDown;
        }

        internal static bool ShouldRegisterNativePublisher(
            FoxRunRos2CustomPublisherContract contract,
            FoxRunRos2RouteEndpoint defaultSource,
            FoxRunRos2RouteEndpoint defaultTargets,
            out FoxRunRos2RouteResolution resolution)
        {
            if (contract == null)
            {
                resolution = default;
                return false;
            }

            resolution = contract.ResolveTopology(defaultSource, defaultTargets);
            return resolution.Success
                   && (resolution.Route.Targets
                       & FoxRunRos2RouteEndpoint.R2fu) != 0;
        }

        private void WarnOnce(string key, string message)
        {
            if (_warnings.Count >= MaximumBindings)
                return;
            if (_warnings.Add(key))
                Debug.LogWarning("[FoxRun ROS2] " + message);
        }

        internal static void StopForNativeRuntimeShutdown()
        {
            var instances = FindObjectsByType<FoxRunRos2CustomPublisherHub>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                if (instance == null)
                    continue;
                instance._stopping = true;
                instance.SetManager(null);
                instance.StopBindings();
            }
        }

        private static int CompareBehaviours(MonoBehaviour left, MonoBehaviour right)
        {
            var leftName = left == null ? string.Empty : left.GetType().FullName ?? string.Empty;
            var rightName = right == null ? string.Empty : right.GetType().FullName ?? string.Empty;
            var result = string.CompareOrdinal(leftName, rightName);
            return result != 0
                ? result
                : (left?.GetInstanceID() ?? 0).CompareTo(right?.GetInstanceID() ?? 0);
        }

        private void OnApplicationQuit()
        {
            _stopping = true;
            SetManager(null);
            StopBindings();
        }

        private void OnDisable()
        {
            _stopping = true;
            SetManager(null);
            StopBindings();
        }

        private void OnDestroy()
        {
            _stopping = true;
            SetManager(null);
            StopBindings();
        }

    }
}
#endif
