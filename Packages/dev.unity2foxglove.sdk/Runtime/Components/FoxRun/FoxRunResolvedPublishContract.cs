// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Immutable, session-resolved publish topology and QoS contract.

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunResolvedPublishContract
    {
        private FoxRunResolvedPublishContract(
            FoxRunEndpoint targets,
            FoxRunEncoding foxgloveEncoding,
            FoxRunResolvedQos nativeQos,
            FoxRunResolvedQos bridgeQos)
        {
            Targets = targets;
            FoxgloveEncoding = foxgloveEncoding;
            NativeQos = nativeQos;
            BridgeQos = bridgeQos;
        }

        public FoxRunEndpoint Targets { get; }
        public FoxRunEncoding FoxgloveEncoding { get; }
        public FoxRunResolvedQos NativeQos { get; }
        public FoxRunResolvedQos BridgeQos { get; }

        public bool Selects(FoxRunEndpoint target)
            => target != 0 && (Targets & target) == target;

        internal static bool TryResolve(
            FoxgloveLogTopicInfo info,
            FoxRunEndpoint defaultTargets,
            FoxRunEncoding publishDefaultEncoding,
            FoxRunResolvedQos nativeDefaultQos,
            FoxRunResolvedQos bridgeDefaultQos,
            FoxRunEndpoint defaultSource,
            FoxRunEncoding subscribeDefaultEncoding,
            out FoxRunResolvedPublishContract contract,
            out string diagnostic)
            => TryResolveForDeclaringType(
                info,
                string.Empty,
                defaultTargets,
                publishDefaultEncoding,
                nativeDefaultQos,
                bridgeDefaultQos,
                defaultSource,
                subscribeDefaultEncoding,
                out contract,
                out diagnostic);

        internal static bool TryResolveForDeclaringType(
            FoxgloveLogTopicInfo info,
            string declaringType,
            FoxRunEndpoint defaultTargets,
            FoxRunEncoding publishDefaultEncoding,
            FoxRunResolvedQos nativeDefaultQos,
            FoxRunResolvedQos bridgeDefaultQos,
            FoxRunEndpoint defaultSource,
            FoxRunEncoding subscribeDefaultEncoding,
            out FoxRunResolvedPublishContract contract,
            out string diagnostic)
        {
            contract = null;
            diagnostic = string.Empty;

            var topology = FoxRunEndpointResolver.Resolve(
                info.Flow,
                info.DeclaredSource,
                info.HasExplicitSource,
                info.DeclaredTargets,
                info.HasExplicitTargets,
                info.DeclaredEncoding,
                info.HasExplicitEncoding,
                defaultSource,
                defaultTargets,
                publishDefaultEncoding,
                subscribeDefaultEncoding,
                info.HasExplicitQos);
            if (!topology.Success)
            {
                diagnostic = topology.DiagnosticMessage;
                return false;
            }

            if ((topology.Topology.Targets & FoxRunEndpoint.Foxglove) != 0
                && !FoxRunSchemaInfoRegistry.TryResolveSessionContract(
                    declaringType,
                    info.Topic,
                    FoxRunFlow.Publish,
                    topology.Topology.PublishEncoding,
                    out _,
                    out diagnostic))
            {
                return false;
            }

            var native = ResolveQos(info, nativeDefaultQos);
            if (!native.Success)
            {
                diagnostic = native.DiagnosticMessage;
                return false;
            }

            var bridge = ResolveQos(info, bridgeDefaultQos);
            if (!bridge.Success)
            {
                diagnostic = bridge.DiagnosticMessage;
                return false;
            }

            contract = new FoxRunResolvedPublishContract(
                topology.Topology.Targets,
                topology.Topology.PublishEncoding,
                native.Qos,
                bridge.Qos);
            return true;
        }

        private static FoxRunQosResolution ResolveQos(
            FoxgloveLogTopicInfo info,
            FoxRunResolvedQos inherited)
            => FoxRunRos2QosProfileResolver.Resolve(
                info.QosProfile,
                info.HasExplicitQosProfile,
                info.QosReliability,
                info.HasExplicitReliability,
                info.QosDurability,
                info.HasExplicitDurability,
                info.QosHistory,
                info.HasExplicitHistory,
                info.QosDepth,
                info.HasExplicitDepth,
                inherited);
    }
}
