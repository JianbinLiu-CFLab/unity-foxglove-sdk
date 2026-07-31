// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Provider-neutral generation token for full-duplex origin ownership.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Provider-neutral ownership token for generated full-duplex members.
    /// A transport can clear only the generation it previously marked.
    /// </summary>
    public interface IFoxRunRemoteOwnershipSource
    {
        void FoxRunOrigin_MarkRemoteApplied(
            int topicIndex,
            string transportId,
            ulong generation);

        void FoxRunOrigin_ClearRemoteApplied(
            int topicIndex,
            string transportId,
            ulong generation);

        bool FoxRunOrigin_TryGetRemoteApplied(
            int topicIndex,
            out string transportId,
            out ulong generation);
    }
}
