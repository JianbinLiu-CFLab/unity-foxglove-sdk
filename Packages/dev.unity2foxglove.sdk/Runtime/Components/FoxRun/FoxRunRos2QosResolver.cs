// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves portable FoxRun ROS2 QoS declarations against Manager policy.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Stable diagnostic codes returned by portable ROS2 QoS resolution.</summary>
    public enum FoxRunRos2QosDiagnosticCode
    {
        None = 0,
        InvalidDeclaredPreset = 1,
        InvalidManagerPreset = 2
    }

    /// <summary>Typed result of resolving one portable ROS2 QoS preset.</summary>
    public readonly struct FoxRunRos2QosResolution
    {
        internal FoxRunRos2QosResolution(
            bool success,
            FoxRunRos2QosPreset preset,
            FoxRunRos2QosDiagnosticCode diagnosticCode,
            string diagnosticMessage)
        {
            Success = success;
            Preset = preset;
            DiagnosticCode = diagnosticCode;
            DiagnosticMessage = diagnosticMessage ?? string.Empty;
        }

        public bool Success { get; }
        public FoxRunRos2QosPreset Preset { get; }
        public FoxRunRos2QosDiagnosticCode DiagnosticCode { get; }
        public string DiagnosticMessage { get; }
    }

    /// <summary>Pure resolver for portable ROS2 subscription QoS presets.</summary>
    public static class FoxRunRos2QosResolver
    {
        private const string InvalidDeclaredPresetMessage =
            "FoxRun ROS2 QoS declaration is invalid.";
        private const string InvalidManagerPresetMessage =
            "FoxRun Manager ROS2 QoS default is invalid.";

        /// <summary>Resolves a source declaration to one concrete QoS preset.</summary>
        public static FoxRunRos2QosResolution Resolve(
            FoxRunRos2QosPreset declaredPreset,
            FoxRunRos2QosPreset managerDefault)
        {
            switch (declaredPreset)
            {
                case FoxRunRos2QosPreset.Default:
                case FoxRunRos2QosPreset.Reliable:
                case FoxRunRos2QosPreset.SensorData:
                case FoxRunRos2QosPreset.TransientLocal:
                    return Success(declaredPreset);
                case FoxRunRos2QosPreset.Inherit:
                    if (!IsValidPreset(managerDefault))
                    {
                        return Failure(
                            FoxRunRos2QosDiagnosticCode.InvalidManagerPreset,
                            InvalidManagerPresetMessage);
                    }

                    return Success(NormalizeManagerDefault(managerDefault));
                default:
                    return Failure(
                        FoxRunRos2QosDiagnosticCode.InvalidDeclaredPreset,
                        InvalidDeclaredPresetMessage);
            }
        }

        /// <summary>Normalizes the Manager's source-only Inherit state to Default.</summary>
        public static FoxRunRos2QosPreset NormalizeManagerDefault(
            FoxRunRos2QosPreset managerDefault)
        {
            switch (managerDefault)
            {
                case FoxRunRos2QosPreset.Inherit:
                case FoxRunRos2QosPreset.Default:
                    return FoxRunRos2QosPreset.Default;
                case FoxRunRos2QosPreset.Reliable:
                case FoxRunRos2QosPreset.SensorData:
                case FoxRunRos2QosPreset.TransientLocal:
                    return managerDefault;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(managerDefault),
                        InvalidManagerPresetMessage);
            }
        }

        private static bool IsValidPreset(FoxRunRos2QosPreset preset)
            => preset == FoxRunRos2QosPreset.Inherit
               || preset == FoxRunRos2QosPreset.Default
               || preset == FoxRunRos2QosPreset.Reliable
               || preset == FoxRunRos2QosPreset.SensorData
               || preset == FoxRunRos2QosPreset.TransientLocal;

        private static FoxRunRos2QosResolution Success(FoxRunRos2QosPreset preset)
            => new(true, preset, FoxRunRos2QosDiagnosticCode.None, string.Empty);

        private static FoxRunRos2QosResolution Failure(
            FoxRunRos2QosDiagnosticCode diagnosticCode,
            string diagnosticMessage)
            => new(false, FoxRunRos2QosPreset.Inherit, diagnosticCode, diagnosticMessage);
    }
}
