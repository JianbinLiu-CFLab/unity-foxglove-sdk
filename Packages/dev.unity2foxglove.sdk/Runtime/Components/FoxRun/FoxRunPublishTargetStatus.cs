// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Transport-neutral result model for one logical FoxRun publication.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Aggregate health of the selected targets for one declaration.</summary>
    public enum FoxRunPublishTargetStatus
    {
        Ready = 1,
        Degraded = 2,
        Unavailable = 3
    }

    /// <summary>Immutable outcome of one logical multi-target dispatch.</summary>
    public readonly struct FoxRunPublishDispatchResult
    {
        internal FoxRunPublishDispatchResult(
            FoxRunPublishTargetStatus status,
            FoxRunEndpoint succeededTargets,
            FoxRunEndpoint failedTargets)
        {
            Status = status;
            SucceededTargets = succeededTargets;
            FailedTargets = failedTargets;
        }

        public FoxRunPublishTargetStatus Status { get; }
        public FoxRunEndpoint SucceededTargets { get; }
        public FoxRunEndpoint FailedTargets { get; }
        public bool Published => SucceededTargets != 0;
    }

    /// <summary>
    /// Pure one-capture dispatcher. Readiness is evaluated before capture, then
    /// each ready selected target receives the same sample reference and timestamp.
    /// </summary>
    internal static class FoxRunPublishFanout
    {
        private static readonly FoxRunEndpoint[] OrderedTargets =
        {
            FoxRunEndpoint.Foxglove,
            FoxRunEndpoint.Ros2Native,
            FoxRunEndpoint.Ros2Bridge
        };

        public static FoxRunPublishDispatchResult Dispatch<T>(
            FoxRunResolvedPublishContract contract,
            ulong timestampNs,
            Func<T> capture,
            Func<FoxRunEndpoint, bool> isReady,
            Func<FoxRunEndpoint, T, ulong, bool> publish,
            Action<FoxRunEndpoint, string, Exception> onTargetFault = null)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (capture == null)
                throw new ArgumentNullException(nameof(capture));
            if (isReady == null)
                throw new ArgumentNullException(nameof(isReady));
            if (publish == null)
                throw new ArgumentNullException(nameof(publish));

            var ready = (FoxRunEndpoint)0;
            var failed = (FoxRunEndpoint)0;
            for (var index = 0; index < OrderedTargets.Length; index++)
            {
                var target = OrderedTargets[index];
                if (!contract.Selects(target))
                    continue;

                try
                {
                    if (isReady(target))
                        ready |= target;
                    else
                        failed |= target;
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    failed |= target;
                    ReportTargetFault(
                        onTargetFault,
                        target,
                        "readiness",
                        ex);
                }
            }

            if (ready == 0)
            {
                return new FoxRunPublishDispatchResult(
                    FoxRunPublishTargetStatus.Unavailable,
                    0,
                    contract.Targets);
            }

            var sample = capture();
            var succeeded = (FoxRunEndpoint)0;
            for (var index = 0; index < OrderedTargets.Length; index++)
            {
                var target = OrderedTargets[index];
                if ((ready & target) == 0)
                    continue;

                try
                {
                    if (publish(target, sample, timestampNs))
                        succeeded |= target;
                    else
                        failed |= target;
                }
                catch (Exception ex) when (FoxRunExceptionPolicy.IsRecoverable(ex))
                {
                    failed |= target;
                    ReportTargetFault(
                        onTargetFault,
                        target,
                        "publish",
                        ex);
                }
            }

            if (succeeded == 0)
            {
                return new FoxRunPublishDispatchResult(
                    FoxRunPublishTargetStatus.Unavailable,
                    0,
                    contract.Targets);
            }

            return new FoxRunPublishDispatchResult(
                failed == 0
                    ? FoxRunPublishTargetStatus.Ready
                    : FoxRunPublishTargetStatus.Degraded,
                succeeded,
                failed);
        }

        private static void ReportTargetFault(
            Action<FoxRunEndpoint, string, Exception> onTargetFault,
            FoxRunEndpoint target,
            string operation,
            Exception exception)
        {
            try
            {
                onTargetFault?.Invoke(target, operation, exception);
            }
            catch (Exception diagnosticException) when (
                FoxRunExceptionPolicy.IsRecoverable(diagnosticException))
            {
                // Diagnostics are observational and cannot change fanout.
            }
        }

    }

    /// <summary>
    /// Tracks whether the current value still belongs to a remote apply. A
    /// local mutation clears remote ownership; an explicit trigger can always
    /// publish the current value.
    /// </summary>
    internal sealed class FoxRunPublishOriginState<T>
    {
        private readonly IEqualityComparer<T> _comparer;
        private bool _remoteOwned;
        private T _remoteValue;

        public FoxRunPublishOriginState(IEqualityComparer<T> comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public void MarkRemoteApplied(T value)
        {
            _remoteValue = value;
            _remoteOwned = true;
        }

        public bool CanPublishScheduled(T current)
        {
            if (!_remoteOwned)
                return true;
            if (_comparer.Equals(_remoteValue, current))
                return false;

            _remoteOwned = false;
            _remoteValue = default;
            return true;
        }

        public bool CanPublishExplicit(T current)
        {
            _ = current;
            return true;
        }
    }

    /// <summary>Allocation-free comparison for generated structural fingerprints.</summary>
    public static class FoxRunOriginSnapshot
    {
        public static bool BytesEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }
            return true;
        }
    }

    /// <summary>One fail-fast policy shared by FoxRun dispatch boundaries.</summary>
    internal static class FoxRunExceptionPolicy
    {
        internal static bool IsRecoverable(Exception exception)
            => !(exception is OutOfMemoryException)
               && !(exception is StackOverflowException)
               && !(exception is AccessViolationException)
               && !(exception is AppDomainUnloadedException);
    }
}
