// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Optional generated FoxRun conditional publish gate contract.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Optional interface for FoxRun sources that have per-topic conditional
    /// publish gates from <c>FoxRunAttribute.When</c> or <c>Unless</c>.
    /// </summary>
    public interface IFoxgloveLogConditionSource
    {
        /// <summary>Return true when the topic is currently allowed to publish.</summary>
        bool FoxgloveLog_CanPublish(int topicIndex);
    }
}
