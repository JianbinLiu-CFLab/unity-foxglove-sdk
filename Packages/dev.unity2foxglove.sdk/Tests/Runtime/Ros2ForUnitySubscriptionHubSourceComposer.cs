// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source composition for the ROS2 For Unity subscription hub.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Ros2ForUnitySubscriptionHubSourceComposer
    {
        private const string SourceFileName = "FoxRunRos2SubscriptionHub.cs";

        public static string Compose(string repositoryRoot)
        {
            var path = Path.Combine(
                repositoryRoot,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Native",
                "FoxRun",
                SourceFileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing ROS2 for Unity subscription hub source: " + path, path);
            return File.ReadAllText(path);
        }

    }
}
