// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.PackageMatrix
{
    internal static class PackageMatrixNeutralProbe
    {
        internal static FoxRunTransportId ParseProviderId(string value)
            => new FoxRunTransportId(value);

        internal static FoxRunTransportCapabilities PublishCapability =>
            FoxRunTransportCapabilities.Publish;
    }
}
