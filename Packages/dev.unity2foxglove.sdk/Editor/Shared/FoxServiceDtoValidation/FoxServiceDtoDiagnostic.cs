// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceDtoValidation
// Purpose: Shared DTO validation diagnostic model for declarative FoxService analyzers.

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxServiceDtoDiagnostic
    {
        public FoxServiceDtoDiagnostic(
            string id,
            bool isWarning,
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            string serviceName = null)
        {
            Id = id ?? string.Empty;
            IsWarning = isWarning;
            Side = side ?? string.Empty;
            RootType = rootType ?? string.Empty;
            Path = path ?? string.Empty;
            OffendingType = offendingType ?? string.Empty;
            Reason = reason ?? string.Empty;
            ServiceName = serviceName ?? string.Empty;
        }

        public string Id { get; }
        public bool IsWarning { get; }
        public string ServiceName { get; }
        public string Side { get; }
        public string RootType { get; }
        public string Path { get; }
        public string OffendingType { get; }
        public string Reason { get; }
        public string Target => FormatTarget(ServiceName);

        public string FormatTarget(string serviceName)
            => "FoxService '" + (!string.IsNullOrEmpty(serviceName) ? serviceName : ServiceName) + "' "
               + Side + " DTO '" + RootType + "' member '" + Path
               + "' uses '" + OffendingType + "': " + Reason;
    }
}
