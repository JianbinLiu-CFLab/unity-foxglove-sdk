// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Interface implemented by generated declarative service sources.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Interface implemented by generated <c>[FoxService]</c> service sources.
    /// </summary>
    public interface IFoxgloveServiceSource
    {
        /// <summary>Generated service descriptors exposed by this source.</summary>
        IReadOnlyList<FoxgloveGeneratedServiceDescriptor> FoxgloveServices { get; }
    }
}
