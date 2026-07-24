// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves FoxRun declarations against frozen directional profiles.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Stable diagnostic codes returned by endpoint resolution.</summary>
    public enum FoxRunEndpointDiagnosticCode
    {
        None = 0,
        InvalidMode = 1,
        SourceNotAllowed = 2,
        TargetsNotAllowed = 3,
        InvalidSource = 4,
        InvalidTargets = 5,
        BridgeSubscribeUnsupported = 6,
        InvalidEncoding = 7,
        EncodingRequiresFoxglove = 8,
        InvalidProfileSource = 9,
        InvalidProfileTargets = 10,
        InvalidProfileEncoding = 11,
        QosRequiresRos2 = 12
    }

    /// <summary>Typed result for one endpoint-resolution attempt.</summary>
    public readonly struct FoxRunEndpointResolution
    {
        internal FoxRunEndpointResolution(
            bool success,
            FoxRunResolvedTopology topology,
            FoxRunEndpointDiagnosticCode diagnosticCode,
            string diagnosticMessage)
        {
            Success = success;
            Topology = topology;
            DiagnosticCode = diagnosticCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public bool Success { get; }
        public FoxRunResolvedTopology Topology { get; }
        public FoxRunEndpointDiagnosticCode DiagnosticCode { get; }
        public string DiagnosticMessage { get; }
    }

    /// <summary>
    /// Resolves one declaration without silently adding, removing, or rerouting
    /// endpoints. Explicit Targets replace the publish profile, while omitted
    /// values inherit the frozen profile for their own direction.
    /// </summary>
    public static class FoxRunEndpointResolver
    {
        private const FoxRunEndpoint AllPublishTargets =
            FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge;

        public static FoxRunEndpointResolution Resolve(
            FoxRunFlow mode,
            FoxRunEndpoint declaredSource,
            bool hasExplicitSource,
            FoxRunEndpoint declaredTargets,
            bool hasExplicitTargets,
            FoxRunEncoding declaredEncoding,
            bool hasExplicitEncoding,
            FoxRunEndpoint defaultSource,
            FoxRunEndpoint defaultTargets,
            FoxRunEncoding publishDefaultEncoding,
            FoxRunEncoding subscribeDefaultEncoding,
            bool hasExplicitQos = false)
        {
            if (!IsValidMode(mode))
                return Failure(mode, FoxRunEndpointDiagnosticCode.InvalidMode,
                    "FoxRun mode must be Publish, Subscribe, or PublishAndSubscribe.");

            var publishes = mode == FoxRunFlow.Publish
                            || mode == FoxRunFlow.PublishAndSubscribe;
            var subscribes = mode == FoxRunFlow.Subscribe
                             || mode == FoxRunFlow.PublishAndSubscribe;

            if (!subscribes && hasExplicitSource)
                return Failure(mode, FoxRunEndpointDiagnosticCode.SourceNotAllowed,
                    "FoxRun Source is valid only for a subscribe direction.");
            if (!publishes && hasExplicitTargets)
                return Failure(mode, FoxRunEndpointDiagnosticCode.TargetsNotAllowed,
                    "FoxRun Targets is valid only for a publish direction.");

            var source = (FoxRunEndpoint)0;
            if (subscribes)
            {
                source = hasExplicitSource ? declaredSource : defaultSource;
                if (source == FoxRunEndpoint.Ros2Bridge)
                {
                    return Failure(mode, FoxRunEndpointDiagnosticCode.BridgeSubscribeUnsupported,
                        "ROS 2 Bridge subscription is not supported.");
                }

                if (!IsValidSubscribeSource(source))
                {
                    return Failure(
                        mode,
                        hasExplicitSource
                            ? FoxRunEndpointDiagnosticCode.InvalidSource
                            : FoxRunEndpointDiagnosticCode.InvalidProfileSource,
                        hasExplicitSource
                            ? "FoxRun Source must select exactly Foxglove or Ros2Native."
                            : "FoxRun Subscribe Profile Source must select exactly Foxglove or Ros2Native.");
                }
            }

            var targets = (FoxRunEndpoint)0;
            if (publishes)
            {
                targets = hasExplicitTargets ? declaredTargets : defaultTargets;
                if (!IsValidPublishTargets(targets))
                {
                    return Failure(
                        mode,
                        hasExplicitTargets
                            ? FoxRunEndpointDiagnosticCode.InvalidTargets
                            : FoxRunEndpointDiagnosticCode.InvalidProfileTargets,
                        hasExplicitTargets
                            ? "FoxRun Targets must be a non-empty set of known publish endpoints."
                            : "FoxRun Publish Profile Targets must be a non-empty set of known publish endpoints.");
                }
            }

            if (hasExplicitEncoding && !IsValidEncoding(declaredEncoding))
            {
                return Failure(mode, FoxRunEndpointDiagnosticCode.InvalidEncoding,
                    "FoxRun Encoding must be Protobuf or JSON.");
            }

            var publishesToFoxglove = publishes
                                      && (targets & FoxRunEndpoint.Foxglove) != 0;
            var subscribesFromFoxglove = subscribes
                                         && source == FoxRunEndpoint.Foxglove;
            if (hasExplicitEncoding && !publishesToFoxglove && !subscribesFromFoxglove)
            {
                return Failure(mode, FoxRunEndpointDiagnosticCode.EncodingRequiresFoxglove,
                    "FoxRun Encoding requires at least one effective Foxglove direction.");
            }

            FoxRunEncoding publishEncoding = 0;
            if (publishesToFoxglove)
            {
                if (hasExplicitEncoding)
                {
                    publishEncoding = declaredEncoding;
                }
                else if (!TryValidateProfileEncoding(publishDefaultEncoding))
                {
                    return Failure(mode, FoxRunEndpointDiagnosticCode.InvalidProfileEncoding,
                        "FoxRun Publish Profile Encoding must be Protobuf or JSON.");
                }
                else
                {
                    publishEncoding = publishDefaultEncoding;
                }
            }

            FoxRunEncoding subscribeEncoding = 0;
            if (subscribesFromFoxglove)
            {
                if (hasExplicitEncoding)
                {
                    subscribeEncoding = declaredEncoding;
                }
                else if (!TryValidateProfileEncoding(subscribeDefaultEncoding))
                {
                    return Failure(mode, FoxRunEndpointDiagnosticCode.InvalidProfileEncoding,
                        "FoxRun Subscribe Profile Encoding must be Protobuf or JSON.");
                }
                else
                {
                    subscribeEncoding = subscribeDefaultEncoding;
                }
            }

            var hasRos2Direction =
                source == FoxRunEndpoint.Ros2Native
                || (targets & (FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge)) != 0;
            if (hasExplicitQos && !hasRos2Direction)
            {
                return Failure(
                    mode,
                    FoxRunEndpointDiagnosticCode.QosRequiresRos2,
                    "FoxRun QoS requires at least one resolved ROS 2 direction.");
            }

            return new FoxRunEndpointResolution(
                true,
                new FoxRunResolvedTopology(
                    mode,
                    source,
                    targets,
                    publishEncoding,
                    subscribeEncoding),
                FoxRunEndpointDiagnosticCode.None,
                string.Empty);
        }

        /// <summary>Validates a concrete Manager subscribe source.</summary>
        public static FoxRunEndpoint ValidateProfileSource(FoxRunEndpoint source)
        {
            if (IsValidSubscribeSource(source))
                return source;

            throw new System.ArgumentOutOfRangeException(
                nameof(source),
                "FoxRun Subscribe Profile Source must be Foxglove or Ros2Native.");
        }

        /// <summary>Validates concrete Manager publish targets.</summary>
        public static FoxRunEndpoint ValidateProfileTargets(FoxRunEndpoint targets)
        {
            if (IsValidPublishTargets(targets))
                return targets;

            throw new System.ArgumentOutOfRangeException(
                nameof(targets),
                "FoxRun Publish Profile Targets must be a non-empty set of known endpoints.");
        }

        private static bool IsValidMode(FoxRunFlow mode)
            => mode == FoxRunFlow.Publish
               || mode == FoxRunFlow.Subscribe
               || mode == FoxRunFlow.PublishAndSubscribe;

        private static bool IsValidSubscribeSource(FoxRunEndpoint source)
            => source == FoxRunEndpoint.Foxglove
               || source == FoxRunEndpoint.Ros2Native;

        private static bool IsValidPublishTargets(FoxRunEndpoint targets)
            => targets != 0 && (targets & ~AllPublishTargets) == 0;

        private static bool IsValidEncoding(FoxRunEncoding encoding)
            => encoding == FoxRunEncoding.Protobuf || encoding == FoxRunEncoding.JSON;

        private static bool TryValidateProfileEncoding(FoxRunEncoding encoding)
            => IsValidEncoding(encoding);

        private static FoxRunEndpointResolution Failure(
            FoxRunFlow mode,
            FoxRunEndpointDiagnosticCode diagnosticCode,
            string diagnosticMessage)
            => new(
                false,
                new FoxRunResolvedTopology(mode, 0, 0, 0, 0),
                diagnosticCode,
                diagnosticMessage);
    }
}
