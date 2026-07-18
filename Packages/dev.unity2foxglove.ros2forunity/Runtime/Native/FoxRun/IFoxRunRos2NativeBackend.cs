// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Small typed transport seam for native subscription lifecycle tests.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>Stable failure classes exposed by native subscription registration.</summary>
    public enum FoxRunRos2RegistrationError
    {
        None = 0,
        RuntimeUnavailable = 1,
        UnsupportedMessageType = 2,
        UnsupportedQos = 3,
        RegistrationRejected = 4,
        InvalidSubscriptionToken = 5,
        BackendFailure = 6,
        StaleGeneration = 7,
        Stopped = 8,
        TeardownFailure = 9,
        ApplyFailure = 10,
        InvalidPublisherToken = 11,
        PublisherBackendFailure = 12,
        PublishFailure = 13,
        TypesupportUnavailable = 14
    }

    /// <summary>
    /// Converts internal backend outcomes into public diagnostic text. Backend
    /// and exception messages can contain middleware configuration, so public
    /// diagnostics deliberately expose only a stable error-class explanation.
    /// </summary>
    internal static class FoxRunRos2PublicDiagnostic
    {
        internal static string Describe(FoxRunRos2RegistrationError error)
        {
            switch (error)
            {
                case FoxRunRos2RegistrationError.None:
                    return string.Empty;
                case FoxRunRos2RegistrationError.RuntimeUnavailable:
                    return "The native ROS2 runtime is unavailable or not ready.";
                case FoxRunRos2RegistrationError.UnsupportedMessageType:
                    return "The requested ROS2 message type is not supported by the active runtime.";
                case FoxRunRos2RegistrationError.UnsupportedQos:
                    return "The requested ROS2 QoS preset is not supported by the active runtime.";
                case FoxRunRos2RegistrationError.RegistrationRejected:
                    return "The native ROS2 subscription registration was rejected.";
                case FoxRunRos2RegistrationError.InvalidSubscriptionToken:
                    return "The native ROS2 subscription returned an invalid ownership token.";
                case FoxRunRos2RegistrationError.BackendFailure:
                    return "The native ROS2 backend failed while operating the subscription.";
                case FoxRunRos2RegistrationError.StaleGeneration:
                    return "The native ROS2 subscription belongs to an inactive session.";
                case FoxRunRos2RegistrationError.Stopped:
                    return "The native ROS2 subscription binding is stopped.";
                case FoxRunRos2RegistrationError.TeardownFailure:
                    return "The native ROS2 subscription did not complete teardown.";
                case FoxRunRos2RegistrationError.ApplyFailure:
                    return "The native ROS2 subscription could not apply the copied message.";
                case FoxRunRos2RegistrationError.InvalidPublisherToken:
                    return "The native ROS2 publisher returned an invalid ownership token.";
                case FoxRunRos2RegistrationError.PublisherBackendFailure:
                    return "The native ROS2 backend failed while operating the publisher.";
                case FoxRunRos2RegistrationError.PublishFailure:
                    return "The native ROS2 publisher could not publish the mapped message.";
                case FoxRunRos2RegistrationError.TypesupportUnavailable:
                    return "The selected custom ROS2 typesupport add-on is not ready.";
                default:
                    return "The native ROS2 subscription failed.";
            }
        }
    }

    /// <summary>Bounded, transport-independent registration outcome.</summary>
    public readonly struct FoxRunRos2RegistrationResult
    {
        public const int MaximumDiagnosticLength = 512;

        private FoxRunRos2RegistrationResult(
            bool succeeded,
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            Succeeded = succeeded;
            Error = error;
            Diagnostic = FoxRunRos2PublicDiagnostic.Describe(error);
        }

        public bool Succeeded { get; }
        public FoxRunRos2RegistrationError Error { get; }
        public string Diagnostic { get; }

        public static FoxRunRos2RegistrationResult Success()
            => new FoxRunRos2RegistrationResult(true, FoxRunRos2RegistrationError.None, string.Empty);

        public static FoxRunRos2RegistrationResult Failure(
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            if (error == FoxRunRos2RegistrationError.None)
                error = FoxRunRos2RegistrationError.BackendFailure;
            return new FoxRunRos2RegistrationResult(false, error, diagnostic);
        }

    }

    /// <summary>
    /// Opaque transport subscription ownership. A backend must never advertise
    /// a null or unusable token as successful registration.
    /// </summary>
    internal interface IFoxRunRos2NativeSubscriptionToken
    {
        bool IsUsable { get; }
    }

    /// <summary>
    /// Opaque transport publisher ownership. The custom typed publisher host
    /// never exposes a raw R2FU publisher to generated DTO code.
    /// </summary>
    internal interface IFoxRunRos2NativePublisherToken
    {
        bool IsUsable { get; }
    }

    /// <summary>Backend-only result that carries the transport ownership token.</summary>
    internal readonly struct FoxRunRos2NativeBackendRegistration
    {
        private FoxRunRos2NativeBackendRegistration(
            bool succeeded,
            IFoxRunRos2NativeSubscriptionToken token,
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            Succeeded = succeeded;
            Token = token;
            Error = error;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IFoxRunRos2NativeSubscriptionToken Token { get; }
        public FoxRunRos2RegistrationError Error { get; }
        public string Diagnostic { get; }

        public static FoxRunRos2NativeBackendRegistration Success(
            IFoxRunRos2NativeSubscriptionToken token)
            => new FoxRunRos2NativeBackendRegistration(
                true,
                token,
                FoxRunRos2RegistrationError.None,
                string.Empty);

        public static FoxRunRos2NativeBackendRegistration Failure(
            FoxRunRos2RegistrationError error,
            string diagnostic)
            => new FoxRunRos2NativeBackendRegistration(false, null, error, diagnostic);
    }

    /// <summary>Backend-only result that carries a typed publisher token.</summary>
    internal readonly struct FoxRunRos2NativePublisherRegistration
    {
        private FoxRunRos2NativePublisherRegistration(
            bool succeeded,
            IFoxRunRos2NativePublisherToken token,
            FoxRunRos2RegistrationError error,
            string diagnostic)
        {
            Succeeded = succeeded;
            Token = token;
            Error = error;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IFoxRunRos2NativePublisherToken Token { get; }
        public FoxRunRos2RegistrationError Error { get; }
        public string Diagnostic { get; }

        public static FoxRunRos2NativePublisherRegistration Success(
            IFoxRunRos2NativePublisherToken token)
            => new FoxRunRos2NativePublisherRegistration(
                true,
                token,
                FoxRunRos2RegistrationError.None,
                string.Empty);

        public static FoxRunRos2NativePublisherRegistration Failure(
            FoxRunRos2RegistrationError error,
            string diagnostic)
            => new FoxRunRos2NativePublisherRegistration(false, null, error, diagnostic);
    }

    /// <summary>
    /// Typed R2FU endpoint seam. Production code adapts an already-owned node;
    /// tests can inject a managed backend without creating a live ROS graph.
    /// Before throwing or returning a failure, an implementation must roll back
    /// every partially created endpoint and node lease. A successful result must
    /// carry the only token needed to detach that endpoint.
    /// The supplied QoS profile is borrowed and remains valid only for this
    /// synchronous call. R2FU endpoint creation copies its policies before
    /// returning; implementations must not retain the wrapper or native profile.
    /// </summary>
    internal interface IFoxRunRos2NativeBackend
    {
        FoxRunRos2NativeBackendRegistration Register<T>(
            FoxRunRos2GeneratedContract contract,
            IFoxRunRos2NativeQosProfile qosProfile,
            Action<T> callback)
            where T : ROS2.Message, new();

        void RemoveSubscription(IFoxRunRos2NativeSubscriptionToken token);

        void ReleaseNodeOwnership();
    }

    /// <summary>
    /// Typed R2FU publisher seam for Phase181 custom ROS2 interfaces. Endpoint
    /// creation is closed-generic; no byte serialization or runtime type lookup
    /// is permitted on this path.
    /// </summary>
    internal interface IFoxRunRos2NativePublisherBackend
    {
        FoxRunRos2NativePublisherRegistration Register<T>(
            FoxRunRos2CustomPublisherContract contract)
            where T : ROS2.Message, new();

        bool TryPublish<T>(IFoxRunRos2NativePublisherToken token, T message)
            where T : ROS2.Message, new();

        void RemovePublisher(IFoxRunRos2NativePublisherToken token);

        void ReleaseNodeOwnership();
    }
}
#endif
