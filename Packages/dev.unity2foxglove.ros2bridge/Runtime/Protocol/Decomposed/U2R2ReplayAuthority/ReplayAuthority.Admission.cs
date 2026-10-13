// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Bounded exact-request replay authority for U2R2 v2.


using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public sealed partial class U2R2RequestReplayAuthority
    {
        public U2R2ReplayAdmission Admit(
            ulong requestId,
            byte[] canonicalRequest,
            ulong maximumResponseBytes,
            U2R2BoundedOutboundScheduler scheduler)
            => AdmitCore(
                requestId,
                canonicalRequest,
                maximumResponseBytes,
                scheduler,
                boundOperation: null,
                boundIdentity: null);

        internal U2R2ReplayAdmission AdmitContract(
            ulong requestId,
            byte[] canonicalRequest,
            ulong maximumResponseBytes,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2Operation? operation,
            U2R2ContractIdentity identity)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            return AdmitCore(
                requestId,
                canonicalRequest,
                maximumResponseBytes,
                scheduler,
                operation,
                identity);
        }

        private U2R2ReplayAdmission AdmitCore(
            ulong requestId,
            byte[] canonicalRequest,
            ulong maximumResponseBytes,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2Operation? boundOperation,
            U2R2ContractIdentity boundIdentity)
        {
            if (requestId == 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_request_id",
                    "A U2R2 request ID must be nonzero.",
                    terminal: true);
            }
            if (requestId == ulong.MaxValue)
            {
                throw new U2R2ProtocolException(
                    "request_id_exhausted",
                    "The U2R2 request ID space exhausted before the session high-water mark could wrap.",
                    terminal: true);
            }
            if (canonicalRequest == null)
                throw new ArgumentNullException(nameof(canonicalRequest));
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));

            lock (_gate)
            {
                if (_closed)
                {
                    throw new InvalidOperationException(
                        "The U2R2 replay authority is closed.");
                }
                if (_entries.TryGetValue(requestId, out var retained))
                {
                    if (!canonicalRequest.SequenceEqual(retained.Request))
                    {
                        throw new U2R2ProtocolException(
                            "request_id_conflict",
                            "A retained U2R2 request ID has different canonical bytes.",
                            terminal: true);
                    }
                    if (!retained.IsCompleted)
                    {
                        throw new U2R2ProtocolException(
                            "request_in_flight",
                            "The identical U2R2 request is still in flight.",
                            terminal: false);
                    }
                    if (!ReferenceEquals(retained.Scheduler, scheduler))
                    {
                        throw new InvalidOperationException(
                            "The retained U2R2 response belongs to another scheduler.");
                    }
                    if (!scheduler.TryReserveControl(
                            checked((ulong)retained.Response.LongLength),
                            out var replayReservation))
                    {
                        ThrowCapacity("No control capacity remains for exact replay.");
                    }
                    replayReservation.Commit(
                        U2R2OutboundFrame.Control(
                            "replay:" + requestId,
                            retained.Response));
                    return new U2R2ReplayAdmission(
                        this,
                        requestId,
                        U2R2ReplayDecision.ReplayCached,
                        retained.Response,
                        responseReservation: null,
                        boundScheduler: scheduler,
                        boundOperation: retained.BoundOperation,
                        boundIdentity: retained.BoundIdentity)
                    {
                        IsSettled = true,
                    };
                }
                if (requestId <= _highWaterMark)
                {
                    throw new U2R2ProtocolException(
                        "stale_request",
                        "The U2R2 request ID is below the retained session high-water mark.",
                        terminal: false);
                }
                if (_outstandingRequests == _limits.MaxOutstandingRequests)
                    ThrowCapacity("The outstanding U2R2 request limit is exhausted.");

                var requestBytes = checked((ulong)canonicalRequest.LongLength);
                ulong requestedReplayBytes;
                try
                {
                    requestedReplayBytes = checked(requestBytes + maximumResponseBytes);
                }
                catch (OverflowException)
                {
                    ThrowCapacity("Replay byte arithmetic overflowed.");
                    return null;
                }
                if (requestedReplayBytes > _limits.MaxReplayBytes)
                    ThrowCapacity("The request and response reservation exceed replay bytes.");

                if (!scheduler.TryReserveControl(
                        maximumResponseBytes,
                        out var responseReservation))
                {
                    ThrowCapacity("No control capacity remains for the required response.");
                }

                var evictions = SelectEvictions(requestedReplayBytes);
                if (evictions == null)
                {
                    responseReservation.Dispose();
                    ThrowCapacity("The bounded replay cache cannot admit this request.");
                }
                foreach (var evictedId in evictions)
                    Evict(evictedId);

                var entry = new Entry(
                    requestId,
                    canonicalRequest,
                    maximumResponseBytes,
                    scheduler,
                    responseReservation,
                    boundOperation,
                    boundIdentity);
                _entries.Add(requestId, entry);
                _replayBytes += requestedReplayBytes;
                _outstandingRequests++;
                _highWaterMark = requestId;
                return new U2R2ReplayAdmission(
                    this,
                    requestId,
                    U2R2ReplayDecision.BeginMutation,
                    cachedResponse: null,
                    responseReservation);
            }
        }
    }
}
