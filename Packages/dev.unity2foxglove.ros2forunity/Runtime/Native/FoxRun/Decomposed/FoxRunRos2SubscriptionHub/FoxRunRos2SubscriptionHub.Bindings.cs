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
        private sealed class CollectingRegistrar : IFoxRunRos2SubscriptionRegistrar
        {
            private readonly FoxRunRos2SubscriptionHub _hub;
            private readonly SourceCandidate _source;

            internal CollectingRegistrar(FoxRunRos2SubscriptionHub hub, SourceCandidate source)
            {
                _hub = hub;
                _source = source;
            }

            public void Register<T>(FoxRunRos2GeneratedContract contract,
                Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose,
                Action<T> apply, Func<T, bool> clearIfOwned,
                Func<T, T, bool> valuesEqual, Func<bool> consumeTrigger,
                Func<bool> canApply)
                where T : ROS2.Message, new()
                => FoxRunRos2RegistrationIsolation.TryRun(
                    () => _hub.AddBinding(
                        _source,
                        contract,
                        copy,
                        dispose,
                        apply,
                        clearIfOwned,
                        valuesEqual,
                        consumeTrigger,
                        canApply),
                    exception => _hub.RecordFailed(
                        _source.InstanceId + "|" + contract.Id,
                        contract,
                        exception));

            public void RegisterStream<TTransport, TSample>(
                FoxRunRos2GeneratedContract contract,
                Func<bool> tryAdmitInput,
                Func<TTransport, FoxRunRos2CopyContext, TSample> materializeOwned,
                Action<TSample> transferOwned,
                Action clearOwned,
                Func<bool> cancelAdmissionCredit = null)
                where TTransport : ROS2.Message, new()
                => FoxRunRos2RegistrationIsolation.TryRun(
                    () => _hub.AddStreamBinding(
                        _source,
                        contract,
                        tryAdmitInput,
                        cancelAdmissionCredit,
                        materializeOwned,
                        transferOwned,
                        clearOwned),
                    exception => _hub.RecordFailed(
                        _source.InstanceId + "|" + contract.Id,
                        contract,
                        exception));
        }

        internal static bool IsSelfOrigin(
            string endpointIdentity,
            string candidateOrigin,
            string generatedSourceOrigin)
        {
            if (string.IsNullOrWhiteSpace(candidateOrigin))
                return false;

            return (!string.IsNullOrWhiteSpace(generatedSourceOrigin)
                    && string.Equals(
                        candidateOrigin,
                        generatedSourceOrigin,
                        StringComparison.Ordinal))
                   || FoxRunRos2CustomOriginRegistry.IsCurrentOrigin(
                       endpointIdentity,
                       candidateOrigin);
        }

        private readonly struct SourceCandidate
        {
            internal SourceCandidate(
                MonoBehaviour behaviour,
                IFoxRunRos2SubscriptionSource native,
                IFoxRunRos2CustomSubscriptionSource custom)
            {
                Behaviour = behaviour;
                Native = native;
                Custom = custom;
                TypeName = behaviour.GetType().FullName ?? string.Empty;
                InstanceId = behaviour.GetInstanceID();
                Key = new FoxRunRos2DiscoveryKey(TypeName, InstanceId, string.Empty, string.Empty);
            }

            internal MonoBehaviour Behaviour { get; }
            internal IFoxRunRos2SubscriptionSource Native { get; }
            internal IFoxRunRos2CustomSubscriptionSource Custom { get; }
            internal string TypeName { get; }
            internal int InstanceId { get; }
            internal FoxRunRos2DiscoveryKey Key { get; }
        }

        private sealed class HostedBinding : IFoxRunRos2SubscriptionHostedCleanup
        {
            private readonly MonoBehaviour _source;
            private readonly int _sourceInstanceId;
            private readonly FoxRunRos2ApplyRateGate _rateGate;

            internal HostedBinding(MonoBehaviour source, int sourceInstanceId, string identity,
                FoxRunRos2DiscoveryKey key, IFoxRunRos2HostBinding binding, double rateLimitHz)
            {
                _source = source;
                _sourceInstanceId = sourceInstanceId;
                Identity = identity;
                Key = key;
                Binding = binding;
                _rateGate = new FoxRunRos2ApplyRateGate(Math.Max(1d, rateLimitHz));
            }

            internal string Identity { get; }
            internal int SourceInstanceId => _sourceInstanceId;
            internal FoxRunRos2DiscoveryKey Key { get; }
            internal IFoxRunRos2HostBinding Binding { get; }

            internal bool IsCurrent(
                ulong generation,
                HashSet<int> seenSources,
                HashSet<string> seenEndpoints)
                => _source != null
                   && _source.isActiveAndEnabled
                   && seenSources.Contains(_sourceInstanceId)
                   && seenEndpoints.Contains(Identity)
                   && generation <= long.MaxValue
                   && Binding.SessionGeneration == (long)generation;

            internal bool TryDrain(double nowSeconds, long generation, out Exception failure)
            {
                failure = null;
                if (!_rateGate.IsAllowed(nowSeconds))
                    return false;
                if (!FoxRunRos2ApplyIsolation.TryRun(Binding, generation, nowSeconds, out failure))
                    return false;
                _rateGate.MarkApplied(nowSeconds);
                return true;
            }

            internal void Stop() => Binding.Stop();

            internal bool CleanupComplete
                => !(Binding is IFoxRunRos2DeferredCleanupStatus deferred)
                   || deferred.CleanupComplete;

            bool IFoxRunRos2SubscriptionHostedCleanup.CleanupComplete
                => CleanupComplete;

            void IFoxRunRos2SubscriptionHostedCleanup.Stop()
                => Stop();
        }
    }
}
#endif
