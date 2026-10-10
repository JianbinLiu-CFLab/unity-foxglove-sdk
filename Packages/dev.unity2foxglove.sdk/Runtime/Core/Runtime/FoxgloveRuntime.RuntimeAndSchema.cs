// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime
// Purpose: FoxgloveRuntime RuntimeAndSchema responsibilities.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using static Unity.FoxgloveSDK.Transport.TransportStatsSnapshot;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveRuntime
    {
        // ── Tick ──

        /// <summary>
        /// Called every frame from Unity. Drains service calls, ticks the
        /// replay engine when active, or broadcasts wall-clock time.
        /// </summary>
        public void Tick()
        {
            _tickCoordinator.UpdateReplaySnapshotCapacity(ResolveReplaySnapshotCapacity(_transport));
            _tickCoordinator.Tick(_session, _playbackClock, _replay, _wallClock, _externalReplayCursorController);
        }

        // ── Transport Health ──

        /// <summary>
        /// Get a read-only transport health snapshot.
        /// Returns <see cref="TransportStatsSnapshot.Unsupported"/> for transports
        /// that do not implement <see cref="IFoxgloveTransportStatsProvider"/>.
        /// </summary>
        public TransportStatsSnapshot GetTransportStatsSnapshot()
        {
            if (_transport is IFoxgloveTransportStatsProvider provider)
                return provider.GetStatsSnapshot();
            return Unsupported;
        }

        /// <summary>
        /// Stops the server, clears parameters and services, disposes
        /// recording, replay, and transport.
        /// </summary>
        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            Volatile.Write(ref _disposeRequested, 1);
            if (Interlocked.Exchange(ref _disposing, 1) != 0)
                return;

            ExceptionDispatchInfo firstFailure = null;
            try
            {
                if (!_stopCleanup.IsComplete || _sessionPendingCleanup != null)
                {
                    try
                    {
                        Stop();
                    }
                    catch (Exception exception)
                    {
                        firstFailure ??= ExceptionDispatchInfo.Capture(exception);
                    }
                }

                TryCleanup(
                    () => _parameters.ClearDuringCleanup(),
                    ref _parametersCleared,
                    ref firstFailure);
                TryCleanup(
                    () => _services.Clear(),
                    ref _servicesCleared,
                    ref firstFailure);
                // The remaining owned helpers are pure managed state and do not implement IDisposable.
                TryCleanup(
                    _recording.Dispose,
                    ref _recordingDisposed,
                    ref firstFailure);
                TryCleanup(
                    _replay.Dispose,
                    ref _replayDisposed,
                    ref firstFailure);
                // Transport shutdown is independent of session callback removal.
                // Closing it here guarantees that a permanently failing custom
                // event accessor cannot keep the listener socket alive.
                TryCleanup(
                    _transport.Dispose,
                    ref _transportDisposed,
                    ref firstFailure);

                if (_transportDisposed && _sessionPendingCleanup != null)
                {
                    // Retry only the failed per-session substeps once after the
                    // transport is closed. If a custom accessor still refuses to
                    // detach, the disposed transport is the terminal ownership
                    // boundary and the retired session can be abandoned.
                    ExceptionDispatchInfo sessionRetryFailure = null;
                    RunStopCleanup(ref sessionRetryFailure);
                    firstFailure ??= sessionRetryFailure;
                    if (_sessionPendingCleanup != null)
                    {
                        try
                        {
                            _logger.LogWarning(
                                "Abandoning retired session callbacks after transport disposal completed.");
                        }
                        catch
                        {
                            // Diagnostics cannot prevent terminal resource release.
                        }
                        _sessionPendingCleanup = null;
                        _stopCleanup.MarkComplete(RuntimeStopCleanupStep.Session);
                        _stopCleanupComplete = _stopCleanup.IsResourceCleanupComplete;
                    }
                }

                if (_stopCleanupComplete
                    && _sessionPendingCleanup == null
                    && _parametersCleared
                    && _servicesCleared
                    && _recordingDisposed
                    && _replayDisposed
                    && _transportDisposed)
                {
                    Volatile.Write(ref _disposed, 1);
                }

                firstFailure?.Throw();
            }
            finally
            {
                Volatile.Write(ref _disposing, 0);
            }
        }

        private static void TryCleanup(
            Action cleanup,
            ref bool completed,
            ref ExceptionDispatchInfo firstFailure)
        {
            if (completed)
                return;

            try
            {
                cleanup();
                completed = true;
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private static void TryCleanup(
            Action cleanup,
            ref ExceptionDispatchInfo firstFailure)
        {
            try
            {
                cleanup?.Invoke();
            }
            catch (Exception exception)
            {
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void ThrowIfSessionCleanupPending()
        {
            if (_sessionPendingCleanup != null)
                throw new InvalidOperationException(
                    "Runtime configuration is unavailable while the previous session cleanup is pending.");
        }

        /// <summary>
        /// Public schema access is a guarded view rather than the raw injected
        /// registry.  Callers may retain the view across a failed Stop, but a
        /// registration cannot mutate the next-session definition set until the
        /// retired session has finished cleanup.
        /// </summary>
        private sealed class GuardedSchemaRegistry : IEncodingAwareSchemaRegistry, ISchemaRegistrySnapshot
        {
            private readonly ISchemaRegistry _inner;
            private readonly Func<bool> _mutationAllowed;

            internal GuardedSchemaRegistry(ISchemaRegistry inner, Func<bool> mutationAllowed)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _mutationAllowed = mutationAllowed;
            }

            public bool TryGetSchema(string name, out SchemaEntry entry)
                => _inner.TryGetSchema(name, out entry);

            public bool TryGetSchema(string name, string encoding, out SchemaEntry entry)
            {
                if (_inner is IEncodingAwareSchemaRegistry encodingAware)
                    return encodingAware.TryGetSchema(name, encoding, out entry);

                if (!_inner.TryGetSchema(name, out entry))
                    return false;

                return string.Equals(entry.Encoding, encoding, StringComparison.OrdinalIgnoreCase);
            }

            public void Register(SchemaEntry entry)
            {
                if (_mutationAllowed != null && !_mutationAllowed())
                    throw new InvalidOperationException(
                        "Schema registry mutations are unavailable while session cleanup is pending.");
                _inner.Register(entry);
            }

            public IReadOnlyList<SchemaEntry> GetSchemaSnapshot()
                => _inner is ISchemaRegistrySnapshot snapshot
                    ? snapshot.GetSchemaSnapshot()
                    : Array.Empty<SchemaEntry>();
        }
    }
}
