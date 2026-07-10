// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Owns FoxgloveManager environment-first secret resolution.

using System;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private string ResolveSharedToken()
            => ResolveSecretValue(SharedTokenEnvironmentVariable, _sharedToken);

        private string ResolveCertificatePassword()
            => ResolveSecretValue(CertificatePasswordEnvironmentVariable, _certificatePassword);

        private string ResolveRemoteMcapFileServerToken()
            => ResolveSecretValue(RemoteMcapFileServerTokenEnvironmentVariable, _remoteMcapFileServerToken).Trim();

        private string ResolveReplayCursorBridgeToken()
            => ResolveSecretValue(ReplayCursorBridgeTokenEnvironmentVariable, _replayCursorBridgeToken);

        private static string ResolveSecretValue(string environmentVariable, string serializedValue)
        {
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrEmpty(environmentValue)
                ? serializedValue ?? string.Empty
                : environmentValue;
        }
    }
}
