// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Raw camera image DDS binding for ROS2 For Unity.
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
        private sealed class RawImageBinding : BindingBase
        {
            private readonly FoxgloveCameraPublisher _source;
            private IPublisher<sensor_msgs.msg.Image> _publisher;
            private bool _subscribed;

            public RawImageBinding(Ros2ForUnityCameraNativeBridge owner, FoxgloveCameraPublisher source, string topic)
                : base(owner, topic)
            {
                _source = source;
            }

            public override void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.SensorRawImageReady += OnFrameReady;
                _subscribed = true;
            }

            public override bool IsStillEligible()
                => IsRawEligible(_source) && NormalizeTopic(_source.SensorCameraRawImageTopic) == Topic;

            public override void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.SensorRawImageReady -= OnFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameReady(SensorRawImageFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || Owner.IsShuttingDown)
                    return;

                if (!Owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    _publisher.Publish(Ros2ForUnityCameraMessageBuilder.BuildImage(frame));
                    WarnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera Image publish failed for " + Topic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(ROS2UnityComponent ros2Unity)
            {
                if (Owner.IsShuttingDown)
                    return false;

                if (Node != null && _publisher != null)
                    return true;

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        Node = ros2Unity.CreateNode(BuildNodeName(_source, "raw_image", attempt));
                        _publisher = Node.CreatePublisher<sensor_msgs.msg.Image>(Topic);
                        WarnedPublishFailure = false;
                        LogReadyOnce();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();

                        if (Owner.IsShuttingDown)
                            return false;
                    }
                }

                RecordPublishFailure(
                    "Unable to create ROS2 Camera raw Image publisher for " + Topic + ": "
                    + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (ReadyLogged)
                    return;

                ReadyLogged = true;
                Debug.Log("[Foxglove][R2FU] Camera Image DDS ready: topic=" + Topic + ".");
            }

            private void CleanupRos2()
            {
                if (Node != null && _publisher != null)
                {
                    try { Node.RemovePublisher<sensor_msgs.msg.Image>(_publisher); }
                    catch (Exception) { }
                }

                _publisher = null;
                CleanupNode();
            }
        }
    }
}
#endif
