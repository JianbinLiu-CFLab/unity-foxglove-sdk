// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Creates the Phase109 local ROS2 For Unity facade context for manual smoke tests.

using UnityEngine;

public static class Phase109Ros2ForUnityContextFactory
{
    public static Phase109Ros2ForUnityContext Create(GameObject host)
    {
        return new Phase109Ros2ForUnityContext(host);
    }
}
