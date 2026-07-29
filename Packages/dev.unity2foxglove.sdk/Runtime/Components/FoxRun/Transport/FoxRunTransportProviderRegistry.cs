// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Manager-local provider registration, conflict detection, and session capture.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunTransportRegistrationResult
    {
        Added = 1,
        AlreadyRegistered = 2,
        Conflict = 3
    }

    public enum FoxRunTransportProviderResolutionState
    {
        Absent = 1,
        Sole = 2,
        Conflicted = 3,
        CapabilityMismatch = 4,
        Unavailable = 5
    }

    public readonly struct FoxRunTransportProviderResolution
    {
        internal FoxRunTransportProviderResolution(
            FoxRunTransportProviderResolutionState state,
            IFoxRunTransportProvider provider)
        {
            State = state;
            Provider = provider;
        }

        public FoxRunTransportProviderResolutionState State { get; }
        public IFoxRunTransportProvider Provider { get; }
    }

    public enum FoxRunTransportSessionCaptureFailure
    {
        None = 0,
        Missing = 1,
        Conflict = 2,
        CapabilityMismatch = 3,
        Unavailable = 4,
        ProviderRejected = 5,
        ProviderFailed = 6
    }

    public readonly struct FoxRunTransportSessionCaptureError
    {
        internal FoxRunTransportSessionCaptureError(
            FoxRunTransportSessionCaptureFailure code,
            FoxRunTransportId transportId,
            string reason)
        {
            Code = code;
            TransportId = transportId;
            Reason = reason ?? string.Empty;
        }

        public FoxRunTransportSessionCaptureFailure Code { get; }
        public FoxRunTransportId TransportId { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Frozen set of provider sessions. Registry changes cannot alter this
    /// instance; disposal releases each unique captured session exactly once.
    /// </summary>
    public sealed class FoxRunTransportSessionSnapshot : IDisposable
    {
        private readonly IFoxRunTransportSession[] _allSessions;
        private readonly IReadOnlyList<IFoxRunTransportSession> _publishView;
        private int _disposed;

        internal FoxRunTransportSessionSnapshot(
            ulong generation,
            IFoxRunTransportSession[] publishTransports,
            IFoxRunTransportSession subscribeTransport,
            IFoxRunTransportSession[] allSessions)
        {
            Generation = generation;
            var publishCopy = (IFoxRunTransportSession[])publishTransports.Clone();
            _publishView = Array.AsReadOnly(publishCopy);
            SubscribeTransport = subscribeTransport;
            _allSessions = (IFoxRunTransportSession[])allSessions.Clone();
        }

        public ulong Generation { get; }
        public IReadOnlyList<IFoxRunTransportSession> PublishTransports => _publishView;
        public IFoxRunTransportSession SubscribeTransport { get; }

        public bool TryGetPublishTransport(
            FoxRunTransportId id,
            out IFoxRunTransportSession session)
        {
            for (var index = 0; index < _publishView.Count; index++)
            {
                if (_publishView[index].Id != id)
                    continue;
                session = _publishView[index];
                return true;
            }

            session = null;
            return false;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Exception first = null;
            for (var i = _allSessions.Length - 1; i >= 0; i--)
            {
                try
                {
                    _allSessions[i].Dispose();
                }
                catch (Exception ex)
                {
                    first ??= ex;
                }
            }

            if (first != null)
                throw first;
        }
    }

    /// <summary>
    /// One Manager-owned registry. It deliberately has no static provider
    /// collection and represents duplicate IDs as conflicts.
    /// </summary>
    public sealed class FoxRunTransportProviderRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<FoxRunTransportId, List<IFoxRunTransportProvider>>
            _providers = new Dictionary<FoxRunTransportId, List<IFoxRunTransportProvider>>();

        public FoxRunTransportRegistrationResult Register(IFoxRunTransportProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            _ = new FoxRunTransportId(provider.Id.Value);
            ValidateCapabilities(provider.Capabilities);

            lock (_gate)
            {
                if (!_providers.TryGetValue(provider.Id, out var instances))
                {
                    instances = new List<IFoxRunTransportProvider>(1);
                    _providers.Add(provider.Id, instances);
                }

                if (instances.Any(candidate => ReferenceEquals(candidate, provider)))
                    return FoxRunTransportRegistrationResult.AlreadyRegistered;

                instances.Add(provider);
                return instances.Count == 1
                    ? FoxRunTransportRegistrationResult.Added
                    : FoxRunTransportRegistrationResult.Conflict;
            }
        }

        public bool Unregister(IFoxRunTransportProvider provider)
        {
            if (provider == null)
                return false;

            lock (_gate)
            {
                if (!_providers.TryGetValue(provider.Id, out var instances))
                    return false;

                var removed = false;
                for (var i = instances.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(instances[i], provider))
                        continue;
                    instances.RemoveAt(i);
                    removed = true;
                }

                if (instances.Count == 0)
                    _providers.Remove(provider.Id);
                return removed;
            }
        }

        public FoxRunTransportProviderResolution Resolve(
            FoxRunTransportId id,
            FoxRunTransportCapabilities requiredCapability)
        {
            ValidateSingleCapability(requiredCapability);
            lock (_gate)
                return ResolveLocked(id, requiredCapability);
        }

        public bool TryCaptureSession(
            FoxRunTransportSelection selection,
            ulong generation,
            out FoxRunTransportSessionSnapshot snapshot,
            out FoxRunTransportSessionCaptureError failure)
        {
            if (selection == null)
                throw new ArgumentNullException(nameof(selection));

            IFoxRunTransportProvider[] publishProviders;
            IFoxRunTransportProvider subscribeProvider = null;
            lock (_gate)
            {
                publishProviders = new IFoxRunTransportProvider[
                    selection.PublishTransportIds.Count];
                for (var i = 0; i < publishProviders.Length; i++)
                {
                    var id = selection.PublishTransportIds[i];
                    var resolution = ResolveLocked(id, FoxRunTransportCapabilities.Publish);
                    if (!TryMapResolution(resolution, id, out publishProviders[i], out failure))
                    {
                        snapshot = null;
                        return false;
                    }
                }

                if (selection.SubscriptionsEnabled)
                {
                    var id = selection.SubscribeTransportId.Value;
                    var resolution = ResolveLocked(id, FoxRunTransportCapabilities.Subscribe);
                    if (!TryMapResolution(resolution, id, out subscribeProvider, out failure))
                    {
                        snapshot = null;
                        return false;
                    }
                }
            }

            var uniqueProviders = new List<IFoxRunTransportProvider>();
            AddUniqueByReference(uniqueProviders, publishProviders);
            if (subscribeProvider != null)
                AddUniqueByReference(uniqueProviders, new[] { subscribeProvider });

            var captured = new Dictionary<IFoxRunTransportProvider, IFoxRunTransportSession>(
                ReferenceIdentityComparer<IFoxRunTransportProvider>.Instance);
            try
            {
                foreach (var provider in uniqueProviders)
                {
                    IFoxRunTransportSession session;
                    string reason;
                    try
                    {
                        if (!provider.TryCaptureSession(generation, out session, out reason)
                            || session == null)
                        {
                            failure = new FoxRunTransportSessionCaptureError(
                                FoxRunTransportSessionCaptureFailure.ProviderRejected,
                                provider.Id,
                                string.IsNullOrWhiteSpace(reason)
                                    ? "Provider rejected session capture."
                                    : reason);
                            DisposeCaptured(captured.Values);
                            snapshot = null;
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = new FoxRunTransportSessionCaptureError(
                            FoxRunTransportSessionCaptureFailure.ProviderFailed,
                            provider.Id,
                            ex.Message);
                        DisposeCaptured(captured.Values);
                        snapshot = null;
                        return false;
                    }

                    if (session.Id != provider.Id
                        || session.Generation != generation
                        || (session.Capabilities & provider.Capabilities) != provider.Capabilities)
                    {
                        session.Dispose();
                        failure = new FoxRunTransportSessionCaptureError(
                            FoxRunTransportSessionCaptureFailure.ProviderFailed,
                            provider.Id,
                            "Provider returned a session with mismatched identity, generation, or capabilities.");
                        DisposeCaptured(captured.Values);
                        snapshot = null;
                        return false;
                    }

                    captured.Add(provider, session);
                }

                var publishSessions = publishProviders
                    .Select(provider => captured[provider])
                    .ToArray();
                var subscribeSession = subscribeProvider == null
                    ? null
                    : captured[subscribeProvider];
                snapshot = new FoxRunTransportSessionSnapshot(
                    generation,
                    publishSessions,
                    subscribeSession,
                    captured.Values.ToArray());
                failure = default;
                return true;
            }
            catch
            {
                DisposeCaptured(captured.Values);
                throw;
            }
        }

        private FoxRunTransportProviderResolution ResolveLocked(
            FoxRunTransportId id,
            FoxRunTransportCapabilities requiredCapability)
        {
            if (!_providers.TryGetValue(id, out var instances) || instances.Count == 0)
                return new FoxRunTransportProviderResolution(
                    FoxRunTransportProviderResolutionState.Absent,
                    null);
            if (instances.Count != 1)
                return new FoxRunTransportProviderResolution(
                    FoxRunTransportProviderResolutionState.Conflicted,
                    null);

            var provider = instances[0];
            if ((provider.Capabilities & requiredCapability) != requiredCapability)
                return new FoxRunTransportProviderResolution(
                    FoxRunTransportProviderResolutionState.CapabilityMismatch,
                    provider);
            if (provider.LifecycleState != FoxRunTransportLifecycleState.Available)
                return new FoxRunTransportProviderResolution(
                    FoxRunTransportProviderResolutionState.Unavailable,
                    provider);
            return new FoxRunTransportProviderResolution(
                FoxRunTransportProviderResolutionState.Sole,
                provider);
        }

        private static bool TryMapResolution(
            FoxRunTransportProviderResolution resolution,
            FoxRunTransportId id,
            out IFoxRunTransportProvider provider,
            out FoxRunTransportSessionCaptureError failure)
        {
            provider = resolution.Provider;
            switch (resolution.State)
            {
                case FoxRunTransportProviderResolutionState.Sole:
                    failure = default;
                    return true;
                case FoxRunTransportProviderResolutionState.Absent:
                    failure = Error(FoxRunTransportSessionCaptureFailure.Missing, id, "Provider is not registered.");
                    return false;
                case FoxRunTransportProviderResolutionState.Conflicted:
                    failure = Error(FoxRunTransportSessionCaptureFailure.Conflict, id, "Provider ID is conflicted.");
                    return false;
                case FoxRunTransportProviderResolutionState.CapabilityMismatch:
                    failure = Error(
                        FoxRunTransportSessionCaptureFailure.CapabilityMismatch,
                        id,
                        "Provider does not support the selected direction.");
                    return false;
                case FoxRunTransportProviderResolutionState.Unavailable:
                    failure = Error(
                        FoxRunTransportSessionCaptureFailure.Unavailable,
                        id,
                        "Provider is unavailable.");
                    return false;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resolution));
            }
        }

        private static FoxRunTransportSessionCaptureError Error(
            FoxRunTransportSessionCaptureFailure code,
            FoxRunTransportId id,
            string reason)
            => new FoxRunTransportSessionCaptureError(code, id, reason);

        private static void AddUniqueByReference(
            ICollection<IFoxRunTransportProvider> destination,
            IEnumerable<IFoxRunTransportProvider> values)
        {
            foreach (var value in values)
            {
                if (destination.Any(existing => ReferenceEquals(existing, value)))
                    continue;
                destination.Add(value);
            }
        }

        private static void DisposeCaptured(IEnumerable<IFoxRunTransportSession> sessions)
        {
            foreach (var session in sessions.Reverse())
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                    // Preserve the original capture failure. Provider teardown
                    // diagnostics remain provider-owned.
                }
            }
        }

        private static void ValidateCapabilities(FoxRunTransportCapabilities capabilities)
        {
            var known = FoxRunTransportCapabilities.Publish
                        | FoxRunTransportCapabilities.Subscribe;
            if (capabilities == 0 || (capabilities & ~known) != 0)
                throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        private static void ValidateSingleCapability(
            FoxRunTransportCapabilities capability)
        {
            if (capability != FoxRunTransportCapabilities.Publish
                && capability != FoxRunTransportCapabilities.Subscribe)
                throw new ArgumentOutOfRangeException(nameof(capability));
        }

        private sealed class ReferenceIdentityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            internal static readonly ReferenceIdentityComparer<T> Instance =
                new ReferenceIdentityComparer<T>();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
