// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Shared lifecycle base for camera native DDS binding objects.
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
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

            protected void CleanupNode()
            {
                if (Owner._ros2Unity != null && Node != null)
                {
                    try { Owner._ros2Unity.RemoveNode(Node); }
                    catch (Exception) { }
                }

                Node = null;
            }
        }
    }
}
#endif
