// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Compressed camera image DDS binding for ROS2 For Unity.
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
        private sealed class ImageBinding : BindingBase
        {
            private readonly FoxgloveCameraPublisher _source;
            private IPublisher<sensor_msgs.msg.CompressedImage> _publisher;

            internal override bool CleanupComplete
                => base.CleanupComplete && _publisher == null;
            private bool _subscribed;

            public ImageBinding(Ros2ForUnityCameraNativeBridge owner, FoxgloveCameraPublisher source, string topic)
                : base(owner, topic)
            {
                _source = source;
            }

            public override void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.SensorCompressedImageReady += OnFrameReady;
                _subscribed = true;
            }

            public override bool IsStillEligible()
                => IsEligible(_source) && NormalizeTopic(_source.SensorCameraImageTopic, DefaultCompressedImageTopic) == Topic;

            public override void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.SensorCompressedImageReady -= OnFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameReady(SensorCompressedImageFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || Owner.IsShuttingDown)
                    return;

                if (!Owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    _publisher.Publish(Ros2ForUnityCameraMessageBuilder.BuildCompressedImage(frame));
                    WarnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera CompressedImage publish failed for " + Topic + ": " + ex.Message);
                }
            }

            private bool TryEnsurePublisher(ROS2UnityComponent ros2Unity)
            {
                if (Owner.IsShuttingDown)
                    return false;

                if (Node != null && _publisher != null)
                    return true;

                if (Node != null || _publisher != null)
                {
                    CleanupRos2();
                    if (!CleanupComplete)
                        return false;
                }

                Exception lastException = null;
                for (var attempt = 0; attempt < MaxNodeCreateAttempts; attempt++)
                {
                    try
                    {
                        Node = ros2Unity.CreateNode(BuildNodeName(_source, "image", attempt));
                        _publisher = Node.CreatePublisher<sensor_msgs.msg.CompressedImage>(Topic);
                        WarnedPublishFailure = false;
                        LogReadyOnce();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();

                        if (!CleanupComplete)
                            return false;

                        if (Owner.IsShuttingDown)
                            return false;
                    }
                }

                RecordPublishFailure("Unable to create ROS2 Camera CompressedImage publisher for " + Topic + ": "
                                     + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (ReadyLogged)
                    return;

                ReadyLogged = true;
                Debug.Log("[Foxglove][R2FU] Camera CompressedImage DDS ready: topic=" + Topic + ".");
            }

            private void CleanupRos2()
            {
                if (_publisher != null)
                {
                    if (Node == null)
                        return;
                    else
                    {
                        try
                        {
                            if (!Node.RemovePublisher<sensor_msgs.msg.CompressedImage>(_publisher))
                                return;
                            _publisher = null;
                        }
                        catch (Exception)
                        {
                            return;
                        }
                    }
                }

                CleanupNode();
            }
        }
    }
}
#endif
