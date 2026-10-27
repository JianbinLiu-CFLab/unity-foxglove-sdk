// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Background queue and reconnect runtime for the ROS2 Bridge mirror.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using Components = Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeWorkerLease :
        Components.IFoxRunDetachedRetirementLease
    {
        private void ClearInFlightPreparation(
            PublisherPreparationEntry entry,
            string requestId,
            byte[] request)
        {
            lock (_gate)
            {
                if (!_hasInFlightPreparation
                    || !ReferenceEquals(
                        _inFlightPreparation.Entry,
                        entry)
                    || !ReferenceEquals(
                        _inFlightPreparation.Request,
                        request)
                    || !string.Equals(
                        _inFlightPreparation.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _inFlightPreparation = default;
                _hasInFlightPreparation = false;
            }
        }

        private bool ShouldStop(long generation)
        {
            lock (_gate)
                return _stopRequested || generation != _workerGeneration;
        }

        private bool TryRetireAfterTimeout()
        {
            var finalizeDirect = false;
            lock (_retirementGate)
            {
                if (_finalized || _retired)
                    return _retired;
                if (_workerExited)
                {
                    _finalized = true;
                    finalizeDirect = true;
                }
                else
                {
                    CaptureRetainedOwnership(
                        out var retainedBytes,
                        out var retainedResources);
                    if (!_retirement.TryConvertToRetired(
                            workerIndex: 0,
                            this,
                            _workerIdentity,
                            retainedBytes,
                            retainedResources))
                    {
                        throw new InvalidOperationException(
                            "ROS2 Bridge failed to convert its pre-reserved retirement slot.");
                    }
                    _retired = true;
                    return true;
                }
            }

            if (finalizeDirect)
            {
                try
                {
                    DisposeResources();
                }
                finally
                {
                    _retirement.TryReturn(0);
                }
            }
            return false;
        }

        private void CaptureRetainedOwnership(
            out long retainedBytes,
            out int retainedResources)
        {
            lock (_gate)
            {
                retainedBytes = 0;
                retainedResources = 0;
                CountRetainedResource(_ownedSink, ref retainedResources);
                CountRetainedResource(_worker, ref retainedResources);
                CountRetainedResource(_signal, ref retainedResources);
                CountRetainedResource(_outbound, ref retainedResources);
                CountRetainedResource(
                    _preparationQueue,
                    ref retainedResources);
                CountRetainedResource(
                    _preparations,
                    ref retainedResources);
                CountRetainedResource(
                    _v2Session,
                    ref retainedResources);
                CountRetainedResource(
                    _v2RequestIds,
                    ref retainedResources);
                CountRetainedResource(
                    _v2MessageIds,
                    ref retainedResources);
                CountRetainedResource(
                    _duplexConnection,
                    ref retainedResources);

                var queuedDepth = _outbound.TotalQueuedDepth;
                var queuedBytes = _outbound.QueuedBytes;
                var transientBytes = _outbound.TransientBytes;
                var inFlightBytes = _outbound.InFlightBytes;
                retainedResources = checked(
                    retainedResources + checked((int)queuedDepth));
                if (transientBytes != 0)
                    retainedResources = checked(retainedResources + 1);
                if (inFlightBytes != 0)
                    retainedResources = checked(retainedResources + 1);
                retainedBytes = checked(
                    retainedBytes
                    + checked((long)queuedBytes)
                    + checked((long)transientBytes)
                    + checked((long)inFlightBytes));

                if (_hasInFlightPreparation)
                {
                    retainedResources = checked(retainedResources + 1);
                    if (_inFlightPreparation.Request != null)
                    {
                        retainedResources = checked(retainedResources + 1);
                        retainedBytes = checked(
                            retainedBytes
                            + _inFlightPreparation.Request.Length);
                    }
                    CountRetainedResource(
                        _inFlightPreparation.Entry,
                        ref retainedResources);
                    CountRetainedResource(
                        _inFlightPreparation.RequestId,
                        ref retainedResources);
                    CountRetainedResource(
                        _inFlightPreparation.Key.Topic,
                        ref retainedResources);
                    CountRetainedResource(
                        _inFlightPreparation.Key.SchemaName,
                        ref retainedResources);
                }
            }
        }

        private static void CountRetainedResource(
            object resource,
            ref int retainedResources)
        {
            if (resource != null)
                retainedResources = checked(retainedResources + 1);
        }

        private static long AddCounter(long baseline, ulong value)
        {
            var bounded = ToCounter(value);
            return baseline >= long.MaxValue - bounded
                ? long.MaxValue
                : baseline + bounded;
        }

        private static long ToCounter(ulong value)
            => value >= long.MaxValue
                ? long.MaxValue
                : checked((long)value);

        private static U2R2ProtocolLimits CreateOutboundLimits(
            int queueCapacity)
        {
            var defaults = U2R2ProtocolLimits.Default;
            var dataDepth = checked((ulong)queueCapacity);
            var perContractDepth = Math.Min(
                dataDepth,
                defaults.MaxPerContractQueueDepth);
            return defaults.With(
                (
                    "maxTotalQueueDepth",
                    checked(
                        defaults.ReservedControlQueueDepth
                        + dataDepth)),
                (
                    "maxPerContractQueueDepth",
                    perContractDepth));
        }

        private void FinalizeActive()
        {
            lock (_retirementGate)
            {
                if (_finalized || _retired)
                    return;
                _finalized = true;
            }

            try
            {
                DisposeResources();
            }
            finally
            {
                _retirement.TryReturn(0);
            }
        }

    }
}
