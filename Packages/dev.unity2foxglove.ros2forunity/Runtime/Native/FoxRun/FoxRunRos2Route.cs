// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: R2FU-local routing projection from neutral FoxRun Provider IDs.

using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    [Flags]
    public enum FoxRunRos2RouteEndpoint
    {
        WebSocket = 1 << 0,
        R2fu = 1 << 1
    }

    public enum FoxRunRos2RouteDiagnosticCode
    {
        None = 0,
        InvalidMode = 1,
        SourceNotAllowed = 2,
        TargetsNotAllowed = 3,
        InvalidSource = 4,
        InvalidTargets = 5,
        EncodingRequiresWebSocket = 6,
        QosRequiresR2fu = 7
    }

    public readonly struct FoxRunRos2ResolvedRoute
    {
        internal FoxRunRos2ResolvedRoute(
            FoxRunFlow mode,
            FoxRunRos2RouteEndpoint source,
            FoxRunRos2RouteEndpoint targets)
        {
            Mode = mode;
            Source = source;
            Targets = targets;
        }

        public FoxRunFlow Mode { get; }
        public FoxRunRos2RouteEndpoint Source { get; }
        public FoxRunRos2RouteEndpoint Targets { get; }
    }

    public readonly struct FoxRunRos2RouteResolution
    {
        internal FoxRunRos2RouteResolution(
            bool success,
            FoxRunRos2ResolvedRoute route,
            FoxRunRos2RouteDiagnosticCode diagnosticCode,
            string diagnosticMessage)
        {
            Success = success;
            Route = route;
            DiagnosticCode = diagnosticCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public bool Success { get; }
        public FoxRunRos2ResolvedRoute Route { get; }
        public FoxRunRos2RouteDiagnosticCode DiagnosticCode { get; }
        public string DiagnosticMessage { get; }
    }

    public static class FoxRunRos2RouteResolver
    {
        private const FoxRunRos2RouteEndpoint AllPublishTargets =
            FoxRunRos2RouteEndpoint.WebSocket
            | FoxRunRos2RouteEndpoint.R2fu;

        public static FoxRunRos2RouteResolution Resolve(
            FoxRunFlow mode,
            FoxRunRos2RouteEndpoint declaredSource,
            bool hasExplicitSource,
            FoxRunRos2RouteEndpoint declaredTargets,
            bool hasExplicitTargets,
            FoxRunRos2RouteEndpoint defaultSource,
            FoxRunRos2RouteEndpoint defaultTargets,
            bool hasExplicitWebSocketEncoding = false,
            bool hasExplicitQos = false)
        {
            var publishes =
                mode == FoxRunFlow.Publish
                || mode == FoxRunFlow.PublishAndSubscribe;
            var subscribes =
                mode == FoxRunFlow.Subscribe
                || mode == FoxRunFlow.PublishAndSubscribe;
            if (!publishes && !subscribes)
            {
                return Failure(
                    mode,
                    FoxRunRos2RouteDiagnosticCode.InvalidMode,
                    "FoxRun mode must be Publish, Subscribe, or PublishAndSubscribe.");
            }
            if (!subscribes && hasExplicitSource)
            {
                return Failure(
                    mode,
                    FoxRunRos2RouteDiagnosticCode.SourceNotAllowed,
                    "A subscribe Provider is valid only for a subscribe direction.");
            }
            if (!publishes && hasExplicitTargets)
            {
                return Failure(
                    mode,
                    FoxRunRos2RouteDiagnosticCode.TargetsNotAllowed,
                    "Publish Providers are valid only for a publish direction.");
            }

            var source = (FoxRunRos2RouteEndpoint)0;
            if (subscribes)
            {
                source = hasExplicitSource
                    ? declaredSource
                    : defaultSource;
                if (source != FoxRunRos2RouteEndpoint.WebSocket
                    && source != FoxRunRos2RouteEndpoint.R2fu)
                {
                    return Failure(
                        mode,
                        FoxRunRos2RouteDiagnosticCode.InvalidSource,
                        "The subscribe route must select exactly one known Provider.");
                }
            }

            var targets = (FoxRunRos2RouteEndpoint)0;
            if (publishes)
            {
                targets = hasExplicitTargets
                    ? declaredTargets
                    : defaultTargets;
                if (targets == 0
                    || (targets & ~AllPublishTargets) != 0)
                {
                    return Failure(
                        mode,
                        FoxRunRos2RouteDiagnosticCode.InvalidTargets,
                        "The publish route must select a non-empty set of known Providers.");
                }
            }

            var hasWebSocketDirection =
                source == FoxRunRos2RouteEndpoint.WebSocket
                || (targets & FoxRunRos2RouteEndpoint.WebSocket) != 0;
            if (hasExplicitWebSocketEncoding && !hasWebSocketDirection)
            {
                return Failure(
                    mode,
                    FoxRunRos2RouteDiagnosticCode.EncodingRequiresWebSocket,
                    "FoxRun Encoding requires a WebSocket direction.");
            }

            var hasR2fuDirection =
                source == FoxRunRos2RouteEndpoint.R2fu
                || (targets & FoxRunRos2RouteEndpoint.R2fu) != 0;
            if (hasExplicitQos && !hasR2fuDirection)
            {
                return Failure(
                    mode,
                    FoxRunRos2RouteDiagnosticCode.QosRequiresR2fu,
                    "R2FU QoS requires an R2FU direction.");
            }

            return new FoxRunRos2RouteResolution(
                true,
                new FoxRunRos2ResolvedRoute(mode, source, targets),
                FoxRunRos2RouteDiagnosticCode.None,
                string.Empty);
        }

        public static FoxRunRos2RouteEndpoint FromSubscribeProvider(
            FoxRunTransportId providerId)
            => providerId
               == new FoxRunTransportId(
                   FoxRunRos2TransportProvider.IdValue)
                ? FoxRunRos2RouteEndpoint.R2fu
                : FoxRunRos2RouteEndpoint.WebSocket;

        public static FoxRunRos2RouteEndpoint FromPublishProviders(
            System.Collections.Generic.IReadOnlyList<
                FoxRunTransportId> providerIds)
        {
            var result = (FoxRunRos2RouteEndpoint)0;
            if (providerIds != null)
            {
                for (var i = 0; i < providerIds.Count; i++)
                {
                    result |= providerIds[i]
                              == new FoxRunTransportId(
                                  FoxRunRos2TransportProvider
                                      .IdValue)
                        ? FoxRunRos2RouteEndpoint.R2fu
                        : FoxRunRos2RouteEndpoint.WebSocket;
                }
            }

            return result;
        }

        private static FoxRunRos2RouteResolution Failure(
            FoxRunFlow mode,
            FoxRunRos2RouteDiagnosticCode diagnosticCode,
            string diagnosticMessage)
            => new FoxRunRos2RouteResolution(
                false,
                new FoxRunRos2ResolvedRoute(mode, 0, 0),
                diagnosticCode,
                diagnosticMessage);
    }
}
