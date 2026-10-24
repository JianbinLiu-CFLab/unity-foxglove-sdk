// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Native publisher registration, admission, and provenance checks.

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
        private void AddBinding<TDto, TEnvelope>(
            MonoBehaviour source,
            FoxTopicBus bus,
            FoxRunResolvedQos inheritedQos,
            FoxRunRos2RouteEndpoint defaultSource,
            FoxRunRos2RouteEndpoint defaultTargets,
            FoxRunRos2CustomPublisherContract contract,
            Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> map,
            Action<TEnvelope> dispose)
            where TEnvelope : ROS2.Message, new()
        {
            if (source == null || contract == null)
                return;

            var identity = source.GetInstanceID() + "|" + contract.Id;
            _seen.Add(identity);
            if (_existing.Contains(identity))
            {
                _nativeDemand.Add(identity);
                return;
            }
            if (!contract.SupportsNativeOutput)
            {
                WarnOnce(identity + "|contract", "Custom native ROS2 publisher contract is invalid.");
                return;
            }

            var qosResolution = contract.ResolveQos(
                inheritedQos);
            if (!qosResolution.Success)
            {
                WarnOnce(
                    identity + "|qos|" + qosResolution.DiagnosticCode,
                    qosResolution.DiagnosticMessage);
                return;
            }

            if (!ShouldRegisterNativePublisher(
                    contract,
                    defaultSource,
                    defaultTargets,
                    out var topologyResolution))
            {
                if (!topologyResolution.Success)
                {
                    WarnOnce(
                        identity + "|topology|" + topologyResolution.DiagnosticCode,
                        topologyResolution.DiagnosticMessage);
                }
                return;
            }
            if (!ObserveBindingDemand(
                    identity,
                    _bindings.Count,
                    MaximumBindings,
                    _nativeDemand))
            {
                WarnOnce(
                    identity + "|capacity",
                    "Custom native ROS2 publisher capacity is exhausted.");
                return;
            }

            if (!TryGetAcceptedSourceOrigin(
                    source as IFoxgloveLogSource,
                    bus,
                    contract.Topic,
                    out var sourceOrigin))
            {
                // The main FoxRun hub is the sole topic-writer admission
                // authority.  A rejected duplicate must never create a native
                // endpoint merely because this independent scanner can see it.
                return;
            }

            var readiness = EvaluateReadiness(contract);
            if (!readiness.IsReady)
            {
                WarnOnce(
                    identity + "|" + readiness.Code,
                    FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.TypesupportUnavailable));
                return;
            }

            if (ShouldSuppressTerminalRegistrationFailure(
                    _terminalRegistrationFailures,
                    identity))
            {
                _nativeDemand.Add(identity);
                return;
            }

            if (!FoxRunRos2CustomNativeTransportHost.TryAcquirePublisherBackend(out var backend))
            {
                WarnOnce(
                    identity + "|runtime",
                    FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.RuntimeUnavailable));
                return;
            }

            var origin = FoxRunRos2CustomOriginRegistry.BeginPublisher(identity, sourceOrigin);
            FoxRunRos2CustomPublisherBinding<TDto, TEnvelope> binding = null;
            try
            {
                binding = new FoxRunRos2CustomPublisherBinding<TDto, TEnvelope>(
                    contract,
                    bus,
                    backend,
                    qosResolution.Qos,
                    map,
                    dispose,
                    origin,
                    new FoxRunRos2CustomSequenceSource(),
                    () => EvaluateReadiness(contract),
                    () => FoxRunRos2CustomOriginRegistry.EndPublisher(identity, origin));
                var result = binding.TryStart();
                if (!result.Succeeded)
                {
                    RecordTerminalRegistrationFailure(
                        _terminalRegistrationFailures,
                        identity,
                        result.Error);
                    binding.Stop();
                    TrackFailedBinding(
                        _bindings,
                        _existing,
                        identity,
                        new HostedBinding<TDto, TEnvelope>(
                            identity,
                            source.GetInstanceID(),
                            binding));
                    var failureKind = result.FailureKind;
                    WarnOnce(
                        identity + "|" + result.Error + "|" + failureKind,
                        string.IsNullOrEmpty(failureKind)
                            ? result.Diagnostic
                            : result.Diagnostic + " [failureKind=" + failureKind + "]");
                    return;
                }

                _bindings.Add(new HostedBinding<TDto, TEnvelope>(identity, source.GetInstanceID(), binding));
                _existing.Add(identity);
            }
            catch (Exception exception)
            {
                CleanupFailedStartupAndRethrow(
                    exception,
                    binding == null ? null : (Action)binding.Stop,
                    binding == null
                        ? () => FoxRunRos2CustomOriginRegistry.EndPublisher(
                            identity,
                            origin)
                        : null,
                    binding == null
                        ? () => backend.ReleaseNodeOwnership()
                        : null);
                throw;
            }
        }

        internal static bool ShouldTrackFailedBinding(bool cleanupPending)
            => cleanupPending;

        internal static bool ShouldSuppressTerminalRegistrationFailure(
            ISet<string> terminalFailures,
            string identity)
            => terminalFailures != null
               && !string.IsNullOrEmpty(identity)
               && terminalFailures.Contains(identity);

        internal static bool RecordTerminalRegistrationFailure(
            ISet<string> terminalFailures,
            string identity,
            FoxRunRos2RegistrationError error)
            => terminalFailures != null
               && !string.IsNullOrEmpty(identity)
               && error == FoxRunRos2RegistrationError.NativeRuntimeSurfaceUnavailable
               && terminalFailures.Add(identity);

        internal static bool TrackFailedBinding(
            IList<IFoxRunRos2CustomPublisherHostedBinding> bindings,
            ISet<string> existing,
            string identity,
            IFoxRunRos2CustomPublisherHostedBinding binding)
        {
            if (bindings == null || existing == null || binding == null
                || string.IsNullOrEmpty(identity)
                || !ShouldTrackFailedBinding(binding.CleanupPending))
                return false;
            bindings.Add(binding);
            existing.Add(identity);
            return true;
        }

        internal static bool ObserveBindingDemand(
            string identity,
            int bindingCount,
            int maximumBindings,
            ISet<string> nativeDemand)
        {
            if (string.IsNullOrEmpty(identity))
                throw new ArgumentException(
                    "A native publisher identity is required.",
                    nameof(identity));
            if (bindingCount < 0)
                throw new ArgumentOutOfRangeException(nameof(bindingCount));
            if (maximumBindings <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBindings));
            }
            if (nativeDemand == null)
                throw new ArgumentNullException(nameof(nativeDemand));

            nativeDemand.Add(identity);
            return bindingCount < maximumBindings;
        }

        private static FoxRunRos2CustomTypesupportReadiness EvaluateReadiness(
            FoxRunRos2CustomPublisherContract contract)
            => FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                contract.BaseRuntimePackageId,
                contract.InterfaceDigest,
                Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));

        internal static bool TryGetAcceptedSourceOrigin(
            IFoxgloveLogSource source,
            FoxTopicBus bus,
            string topic,
            out string origin)
        {
            origin = string.Empty;
            if (source == null
                || bus == null
                || string.IsNullOrWhiteSpace(topic)
                || source is not IFoxgloveTopicContractSource contractSource)
            {
                return false;
            }

            origin = contractSource.FoxgloveLog_Origin ?? string.Empty;
            if (string.IsNullOrWhiteSpace(origin))
                return false;
            for (var index = 0; index < source.FoxgloveLog_TopicCount; index++)
            {
                var candidate = contractSource.FoxgloveLog_GetContract(index);
                if (candidate != null
                    && string.Equals(candidate.Topic, topic, StringComparison.Ordinal)
                    && bus.IsRegistered(candidate, origin))
                {
                    return true;
                }
            }

            origin = string.Empty;
            return false;
        }

    }
}
#endif
