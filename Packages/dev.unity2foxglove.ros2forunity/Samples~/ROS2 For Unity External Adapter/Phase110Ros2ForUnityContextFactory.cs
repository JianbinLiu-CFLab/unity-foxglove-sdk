// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Creates the external ROS2 For Unity facade context for imported samples.

using UnityEngine;

public static class Phase110Ros2ForUnityContextFactory
{
    public static Phase110Ros2ForUnityContext Create(GameObject host)
    {
        return new Phase110Ros2ForUnityContext(host);
    }
}
