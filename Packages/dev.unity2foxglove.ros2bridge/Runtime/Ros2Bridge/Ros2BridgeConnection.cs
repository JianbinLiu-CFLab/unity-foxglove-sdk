// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: One-reader/one-writer bounded U2R2 v2 connection owner.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Components = Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    /// <remarks>Phase192 source sections: FaultAndDisposal.cs, RequestTypes.cs, SessionLifecycle.cs, WorkerLoops.cs</remarks>
    internal sealed partial class Ros2BridgeConnection :
        IDisposable,
        IRos2BridgeContractWireController
    {
        private readonly object _gate = new object();
        private readonly object _requestAdmissionGate = new object();
        private readonly IRos2BridgeSessionTransport _transport;
        private readonly bool _disposeTransport;
        private readonly U2R2ProtocolLimits _limits;
        private readonly bool _requiresSubscription;
        private readonly int _writerCapacity;
        private readonly int _pendingCapacity;
        private readonly int _timeoutMs;
        private readonly IRos2BridgeInboundContractResolver
            _inboundResolver;
        private readonly IRos2BridgeInboundFrameReceiver
            _inboundReceiver;
        private readonly IRos2BridgeBytePool _inboundPool;
        private readonly Components
            .FoxRunTransportRetirementReservation _retirement;
        private readonly int _readerRetirementIndex;
        private readonly int _writerRetirementIndex;
        private readonly string _retirementIdentity;
        private readonly object _retirementGate = new object();
        private readonly bool[] _workerExited = new bool[2];
        private readonly bool[] _workerRetired = new bool[2];
        private readonly bool[] _workerSlotReturned = new bool[2];
        private readonly Queue<PendingRequest> _writerQueue =
            new Queue<PendingRequest>();
        private readonly Dictionary<ulong, PendingRequest> _pending =
            new Dictionary<ulong, PendingRequest>();
        private readonly AutoResetEvent _writerSignal =
            new AutoResetEvent(false);
        private readonly U2R2RequestIdCounter _requestIds =
            new U2R2RequestIdCounter();

        private Thread _reader;
        private Thread _writer;
        private Ros2BridgeV2SessionSnapshot _snapshot;
        private Ros2BridgeSessionLifecycleState _lifecycleState;
        private Exception _lastFault;
        private bool _stopRequested;
        private bool _disposeRequested;
        private int _workersRemaining;
        private int _disposeTeardownReady;
        private int _resourcesDisposed;
        private int _readerManagedThreadId;
        private int _writerManagedThreadId;

        internal Ros2BridgeConnection(
            IRos2BridgeSessionTransport transport,
            U2R2ProtocolLimits limits,
            bool requiresSubscription,
            int writerCapacity,
            int pendingCapacity,
            int timeoutMs,
            IRos2BridgeInboundContractResolver inboundResolver = null,
            IRos2BridgeInboundFrameReceiver inboundReceiver = null,
            IRos2BridgeBytePool inboundPool = null,
            Components.FoxRunTransportRetirementReservation
                retirement = null,
            int readerRetirementIndex = -1,
            int writerRetirementIndex = -1,
            string retirementIdentity = null,
            bool disposeTransport = true)
        {
            _transport = transport
                ?? throw new ArgumentNullException(nameof(transport));
            _disposeTransport = disposeTransport;
            _limits = limits
                ?? throw new ArgumentNullException(nameof(limits));
            if (writerCapacity <= 0
                || checked((ulong)writerCapacity)
                > limits.MaxTotalQueueDepth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(writerCapacity));
            }
            if (pendingCapacity <= 0
                || checked((ulong)pendingCapacity)
                > limits.MaxOutstandingRequests)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pendingCapacity));
            }
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            _requiresSubscription = requiresSubscription;
            _writerCapacity = writerCapacity;
            _pendingCapacity = pendingCapacity;
            _timeoutMs = timeoutMs;
            if ((inboundResolver == null)
                != (inboundReceiver == null))
            {
                throw new ArgumentException(
                    "Bridge inbound resolution and ownership must be configured together.");
            }
            _inboundResolver = inboundResolver;
            _inboundReceiver = inboundReceiver;
            _inboundPool = inboundPool
                           ?? Ros2BridgeSharedBytePool.Instance;
            if (retirement == null)
            {
                if (readerRetirementIndex != -1
                    || writerRetirementIndex != -1
                    || !string.IsNullOrEmpty(
                        retirementIdentity))
                {
                    throw new ArgumentException(
                        "Bridge worker retirement indexes require a reservation.");
                }
            }
            else
            {
                if (readerRetirementIndex < 0
                    || writerRetirementIndex < 0
                    || readerRetirementIndex
                    == writerRetirementIndex
                    || readerRetirementIndex
                    >= retirement.WorkerCount
                    || writerRetirementIndex
                    >= retirement.WorkerCount
                    || string.IsNullOrWhiteSpace(
                        retirementIdentity))
                {
                    throw new ArgumentException(
                        "Bridge reader and writer require distinct reserved worker slots.");
                }
            }
            _retirement = retirement;
            _readerRetirementIndex =
                readerRetirementIndex;
            _writerRetirementIndex =
                writerRetirementIndex;
            _retirementIdentity =
                retirementIdentity?.Trim() ?? string.Empty;
            _lifecycleState =
                Ros2BridgeSessionLifecycleState.Stopped;
        }

        internal Ros2BridgeSessionLifecycleState LifecycleState
        {
            get
            {
                lock (_gate)
                    return _lifecycleState;
            }
        }

        internal Exception LastFault
        {
            get
            {
                lock (_gate)
                    return _lastFault;
            }
        }

        internal int ReaderManagedThreadId
            => Volatile.Read(ref _readerManagedThreadId);

        internal int WriterManagedThreadId
            => Volatile.Read(ref _writerManagedThreadId);

        internal bool HasInboundPipeline
            => _inboundResolver != null
               && _inboundReceiver != null;
    }
}
