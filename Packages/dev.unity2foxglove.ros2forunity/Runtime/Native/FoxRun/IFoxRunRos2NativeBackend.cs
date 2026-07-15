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
        ApplyFailure = 10
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
            Diagnostic = Bound(diagnostic);
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

        private static string Bound(string diagnostic)
        {
            if (string.IsNullOrEmpty(diagnostic))
                return string.Empty;
            return diagnostic.Length <= MaximumDiagnosticLength
                ? diagnostic
                : diagnostic.Substring(0, MaximumDiagnosticLength);
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
}
#endif
