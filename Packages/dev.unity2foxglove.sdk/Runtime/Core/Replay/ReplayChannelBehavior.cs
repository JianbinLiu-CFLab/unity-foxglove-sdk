// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Classifies replay channels by the kind of scene behavior their messages can drive.

using System;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Core
{
    public enum ReplayChannelBehavior
    {
        NotLoaded = 0,
        Unclassified = 1,
        FrameTransformPose = 2,
        ScenePrimitivePose = 3,
        NonPose = 4
    }

    public static class ReplayChannelBehaviorClassifier
    {
        public static ReplayChannelBehavior ClassifyChannel(
            string messageEncoding,
            string schemaName,
            string schemaEncoding)
        {
            if (IsFrameTransformSchema(schemaName))
                return ReplayChannelBehavior.FrameTransformPose;

            if (IsSceneUpdateSchema(schemaName))
                return ReplayChannelBehavior.ScenePrimitivePose;

            if (string.Equals(messageEncoding, "json", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(messageEncoding))
                return ReplayChannelBehavior.Unclassified;

            return ReplayChannelBehavior.NonPose;
        }

        public static ReplayChannelBehavior ClassifyJsonPayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ReplayChannelBehavior.NonPose;

            try
            {
                return ClassifyJsonObject(JObject.Parse(json));
            }
            catch
            {
                return ReplayChannelBehavior.NonPose;
            }
        }

        public static ReplayChannelBehavior ClassifyJsonObject(JObject obj)
        {
            if (obj == null)
                return ReplayChannelBehavior.NonPose;

            if (obj["transforms"] is JArray)
                return ReplayChannelBehavior.FrameTransformPose;

            if (obj["child_frame_id"] != null && (obj["translation"] != null || obj["rotation"] != null))
                return ReplayChannelBehavior.FrameTransformPose;

            if (obj["entities"] is JArray)
                return ReplayChannelBehavior.ScenePrimitivePose;

            return ReplayChannelBehavior.NonPose;
        }

        private static bool IsFrameTransformSchema(string schemaName)
            => HasSchemaSuffix(schemaName, "FrameTransform") || HasSchemaSuffix(schemaName, "FrameTransforms");

        private static bool IsSceneUpdateSchema(string schemaName)
            => HasSchemaSuffix(schemaName, "SceneUpdate");

        private static bool HasSchemaSuffix(string schemaName, string suffix)
        {
            if (string.IsNullOrWhiteSpace(schemaName))
                return false;

            return string.Equals(schemaName, suffix, StringComparison.OrdinalIgnoreCase)
                   || schemaName.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
