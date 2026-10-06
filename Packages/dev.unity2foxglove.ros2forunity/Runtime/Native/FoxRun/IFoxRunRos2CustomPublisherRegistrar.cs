// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Closed-generic custom DTO publisher registration seam.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Receives closed DTO/envelope mappings from generated code. The optional
    /// host owns the bus subscription, endpoint token, and all stop ordering.
    /// </summary>
    public interface IFoxRunRos2CustomPublisherRegistrar
    {
        void Register<TDto, TEnvelope>(
            FoxRunRos2CustomPublisherContract contract,
            Func<TDto, string, ulong, ulong, FoxRunRos2CustomOutboundMappingContext, TEnvelope> map,
            Action<TEnvelope> dispose)
            where TEnvelope : ROS2.Message, new();
    }
}
#endif
