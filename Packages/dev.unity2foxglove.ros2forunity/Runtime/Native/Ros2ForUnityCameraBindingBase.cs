// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Shared lifecycle base for camera native DDS binding objects.
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)
using System;
using ROS2;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Camera;
using UnityEngine;
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class Ros2ForUnityCameraNativeBridge
    {
        private abstract class BindingBase : IDisposable
        {
            protected readonly Ros2ForUnityCameraNativeBridge Owner;
            protected ROS2Node Node;
            protected bool WarnedPublishFailure;
            protected bool ReadyLogged;
            private int _publishFailureCount;

            protected BindingBase(Ros2ForUnityCameraNativeBridge owner, string topic)
            {
                Owner = owner;
                Topic = topic;
            }

            public string Topic { get; }

            public abstract void Subscribe();
            public abstract bool IsStillEligible();
            public abstract void Dispose();

            protected void RecordPublishFailure(string message)
            {
                _publishFailureCount++;
                if (WarnedPublishFailure && _publishFailureCount % WarningIntervalFrames != 0)
                    return;

                WarnedPublishFailure = true;
                Debug.LogWarning("[Foxglove][R2FU] " + message);
            }

            protected bool CleanupNode()
            {
                if (Node == null || Owner._ros2Unity == null)
                    return Node == null;

                try
                {
                    if (!Owner._ros2Unity.TryRemoveNode(Node))
                        return false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera node cleanup failed for " + Topic + ": " + ex.Message);
                    return false;
                }

                Node = null;
                return true;
            }

            internal virtual bool CleanupComplete => Node == null;

            internal bool TryDispose()
            {
                try
                {
                    Dispose();
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera binding cleanup failed for " + Topic + ": " + ex.Message);
                    return false;
                }

                return CleanupComplete;
            }
        }
    }
}
#endif
