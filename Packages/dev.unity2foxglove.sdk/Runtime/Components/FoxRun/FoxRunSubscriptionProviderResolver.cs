// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves FoxRun subscription-provider declarations and capabilities.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Stable diagnostic codes returned by subscription-provider resolution.</summary>
    public enum FoxRunSubscriptionProviderDiagnosticCode
    {
        None = 0,
        InvalidDeclaredProvider = 1,
        InvalidManagerProvider = 2,
        InvalidMode = 3,
        InvalidDeclaredEncoding = 4,
        NativeEncodingConflict = 5,
        NativeRequiresSubscribe = 6,
        Unsupported = 7
    }

    /// <summary>Typed result of resolving one FoxRun subscription provider.</summary>
    public readonly struct FoxRunSubscriptionProviderResolution
    {
        internal FoxRunSubscriptionProviderResolution(
            bool success,
            FoxRunSubscriptionProvider provider,
            FoxRunSubscriptionProviderDiagnosticCode diagnosticCode,
            string diagnosticMessage)
        {
            Success = success;
            Provider = provider;
            DiagnosticCode = diagnosticCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public bool Success { get; }
        public FoxRunSubscriptionProvider Provider { get; }
        public FoxRunSubscriptionProviderDiagnosticCode DiagnosticCode { get; }
        public string DiagnosticMessage { get; }
    }

    /// <summary>Pure provider-policy resolver shared by Manager and generated FoxRun routing.</summary>
    public static class FoxRunSubscriptionProviderResolver
    {
        private const string InvalidDeclaredProviderMessage =
            "FoxRun subscription provider declaration is invalid.";
        private const string InvalidManagerProviderMessage =
            "FoxRun Manager subscription provider is invalid.";
        private const string InvalidModeMessage =
            "FoxRun subscription mode is invalid.";
        private const string InvalidDeclaredEncodingMessage =
            "FoxRun wire encoding declaration is invalid.";
        private const string NativeEncodingConflictMessage =
            "Explicit Ros2Native subscriptions cannot declare a WebSocket Json or Protobuf encoding.";
        private const string NativeRequiresSubscribeMessage =
            "Ros2Native subscriptions require Subscribe mode.";
        private const string UnsupportedMessage =
            "The resolved FoxRun subscription provider is unsupported for this type.";

        /// <summary>
        /// Resolves a provider declaration without treating native ROS2 as a wire encoding.
        /// The declared encoding is used only to reject an explicitly contradictory pair.
        /// A generated complete custom interface may opt into native
        /// PublishAndSubscribe; in that one case Json/Protobuf remains an
        /// outbound WebSocket policy and native remains the sole input provider.
        /// </summary>
        public static FoxRunSubscriptionProviderResolution Resolve(
            FoxRunSubscriptionProvider declaredProvider,
            FoxRunSubscriptionProvider managerDefault,
            FoxRunFlow mode,
            FoxRunWireEncoding declaredEncoding,
            bool supportsWebSocket,
            bool supportsRos2Native,
            bool allowsNativePublishAndSubscribe = false)
        {
            if (!IsValidProvider(declaredProvider))
            {
                return Failure(
                    FoxRunSubscriptionProvider.Inherit,
                    FoxRunSubscriptionProviderDiagnosticCode.InvalidDeclaredProvider,
                    InvalidDeclaredProviderMessage);
            }

            FoxRunSubscriptionProvider provider;
            if (declaredProvider == FoxRunSubscriptionProvider.Inherit)
            {
                if (!IsValidProvider(managerDefault))
                {
                    return Failure(
                        FoxRunSubscriptionProvider.Inherit,
                        FoxRunSubscriptionProviderDiagnosticCode.InvalidManagerProvider,
                        InvalidManagerProviderMessage);
                }

                provider = NormalizeManagerDefault(managerDefault);
            }
            else
            {
                provider = declaredProvider;
            }

            if (!IsValidMode(mode))
            {
                return Failure(
                    provider,
                    FoxRunSubscriptionProviderDiagnosticCode.InvalidMode,
                    InvalidModeMessage);
            }

            if (!IsValidEncoding(declaredEncoding))
            {
                return Failure(
                    provider,
                    FoxRunSubscriptionProviderDiagnosticCode.InvalidDeclaredEncoding,
                    InvalidDeclaredEncodingMessage);
            }

            var customNativeBidirectional = provider == FoxRunSubscriptionProvider.Ros2Native
                                            && mode == FoxRunFlow.PublishAndSubscribe
                                            && allowsNativePublishAndSubscribe;

            if (declaredProvider == FoxRunSubscriptionProvider.Ros2Native
                && declaredEncoding != FoxRunWireEncoding.Inherit
                && !customNativeBidirectional)
            {
                return Failure(
                    provider,
                    FoxRunSubscriptionProviderDiagnosticCode.NativeEncodingConflict,
                    NativeEncodingConflictMessage);
            }

            if (provider == FoxRunSubscriptionProvider.Ros2Native
                && mode != FoxRunFlow.Subscribe
                && !customNativeBidirectional)
            {
                return Failure(
                    provider,
                    FoxRunSubscriptionProviderDiagnosticCode.NativeRequiresSubscribe,
                    NativeRequiresSubscribeMessage);
            }

            var supported = provider == FoxRunSubscriptionProvider.FoxgloveWebSocket
                ? supportsWebSocket
                : supportsRos2Native;
            if (!supported)
            {
                return Failure(
                    provider,
                    FoxRunSubscriptionProviderDiagnosticCode.Unsupported,
                    UnsupportedMessage);
            }

            return new FoxRunSubscriptionProviderResolution(
                true,
                provider,
                FoxRunSubscriptionProviderDiagnosticCode.None,
                string.Empty);
        }

        /// <summary>Normalizes the Manager's source-only Inherit state to WebSocket.</summary>
        public static FoxRunSubscriptionProvider NormalizeManagerDefault(
            FoxRunSubscriptionProvider managerDefault)
        {
            switch (managerDefault)
            {
                case FoxRunSubscriptionProvider.Inherit:
                    return FoxRunSubscriptionProvider.FoxgloveWebSocket;
                case FoxRunSubscriptionProvider.FoxgloveWebSocket:
                case FoxRunSubscriptionProvider.Ros2Native:
                    return managerDefault;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(managerDefault),
                        InvalidManagerProviderMessage);
            }
        }

        private static bool IsValidProvider(FoxRunSubscriptionProvider provider)
            => provider == FoxRunSubscriptionProvider.Inherit
               || provider == FoxRunSubscriptionProvider.FoxgloveWebSocket
               || provider == FoxRunSubscriptionProvider.Ros2Native;

        private static bool IsValidMode(FoxRunFlow mode)
            => mode == FoxRunFlow.Publish
               || mode == FoxRunFlow.Subscribe
               || mode == FoxRunFlow.PublishAndSubscribe;

        private static bool IsValidEncoding(FoxRunWireEncoding encoding)
            => encoding == FoxRunWireEncoding.Inherit
               || encoding == FoxRunWireEncoding.Protobuf
               || encoding == FoxRunWireEncoding.Json;

        private static FoxRunSubscriptionProviderResolution Failure(
            FoxRunSubscriptionProvider provider,
            FoxRunSubscriptionProviderDiagnosticCode diagnosticCode,
            string diagnosticMessage)
            => new(false, provider, diagnosticCode, diagnosticMessage);
    }
}
