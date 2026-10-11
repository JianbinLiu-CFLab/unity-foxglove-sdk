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
    internal sealed partial class Ros2BridgeConnection :
        IDisposable,
        IRos2BridgeContractWireController
    {
        private sealed class PendingRequest : IDisposable
        {
            private readonly object _gate = new object();
            private readonly ManualResetEventSlim _completed =
                new ManualResetEventSlim(false);
            private byte[] _response;
            private Exception _error;
            private bool _settled;

            internal PendingRequest(Ros2BridgeV2Request request)
            {
                Request = request
                    ?? throw new ArgumentNullException(nameof(request));
            }

            internal Ros2BridgeV2Request Request { get; }

            internal bool Wait(int timeoutMs)
                => _completed.Wait(timeoutMs);

            internal bool TryComplete(byte[] response)
            {
                lock (_gate)
                {
                    if (_settled)
                        return false;
                    _response = response
                        ?? throw new ArgumentNullException(
                            nameof(response));
                    _settled = true;
                }
                _completed.Set();
                return true;
            }

            internal bool TryFail(Exception error)
            {
                lock (_gate)
                {
                    if (_settled)
                        return false;
                    _error = error
                        ?? throw new ArgumentNullException(
                            nameof(error));
                    _settled = true;
                }
                _completed.Set();
                return true;
            }

            internal byte[] GetResponse()
            {
                Exception error;
                byte[] response;
                lock (_gate)
                {
                    if (!_settled)
                    {
                        throw new InvalidOperationException(
                            "The Bridge request has not completed.");
                    }
                    error = _error;
                    response = _response;
                }
                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
                return response;
            }

            public void Dispose() => _completed.Dispose();
        }

        private sealed class WorkerRetirementLease :
            Components.IFoxRunDetachedRetirementLease
        {
            private Ros2BridgeConnection _owner;

            internal WorkerRetirementLease(
                Ros2BridgeConnection owner)
            {
                _owner = owner
                    ?? throw new ArgumentNullException(
                        nameof(owner));
            }

            public void Dispose()
                => Interlocked.Exchange(
                    ref _owner,
                    null);
        }
    }
}
