// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: CameraInfo and TF anchor DDS binding for ROS2 For Unity.
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
        private sealed class InfoBinding : BindingBase
        {
            private readonly FoxgloveCameraInfoPublisher _source;
            private IPublisher<sensor_msgs.msg.CameraInfo> _publisher;
            private IPublisher<tf2_msgs.msg.TFMessage> _tfAnchorPublisher;
            private bool _subscribed;

            public InfoBinding(Ros2ForUnityCameraNativeBridge owner, FoxgloveCameraInfoPublisher source, string topic)
                : base(owner, topic)
            {
                _source = source;
            }

            public override void Subscribe()
            {
                if (_subscribed || _source == null)
                    return;

                _source.SensorCameraInfoReady += OnFrameReady;
                _subscribed = true;
            }

            public override bool IsStillEligible()
                => IsEligible(_source) && NormalizeTopic(_source.SensorCameraInfoTopic) == Topic;

            public override void Dispose()
            {
                if (_subscribed && _source != null)
                    _source.SensorCameraInfoReady -= OnFrameReady;

                _subscribed = false;
                CleanupRos2();
            }

            private void OnFrameReady(SensorCameraInfoFrame frame)
            {
                if (frame == null || !Ros2NativeOutputPolicy.Enabled || Owner.IsShuttingDown)
                    return;

                if (!Owner.TryGetRos2Unity(out var ros2Unity))
                    return;

                if (!TryEnsurePublisher(ros2Unity))
                    return;

                try
                {
                    PublishTfAnchor(frame);
                    _publisher.Publish(Ros2ForUnityCameraMessageBuilder.BuildCameraInfo(frame));
                    WarnedPublishFailure = false;
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 CameraInfo publish failed for " + Topic + ": " + ex.Message);
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
                        Node = ros2Unity.CreateNode(BuildNodeName(_source, "info", attempt));
                        _publisher = Node.CreatePublisher<sensor_msgs.msg.CameraInfo>(Topic);
                        WarnedPublishFailure = false;
                        LogReadyOnce();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        CleanupRos2();
                    }
                }

                RecordPublishFailure("Unable to create ROS2 CameraInfo publisher for " + Topic + ": "
                                     + (lastException == null ? "unknown failure" : lastException.Message));
                return false;
            }

            private void LogReadyOnce()
            {
                if (ReadyLogged)
                    return;

                ReadyLogged = true;
                Debug.Log("[Foxglove][R2FU] CameraInfo DDS ready: topic=" + Topic + " tf=" + DescribeTfAnchor() + ".");
            }

            private string DescribeTfAnchor()
            {
                if (!_source.PublishCameraTfAnchor)
                    return "disabled";

                var parentFrame = _source.CameraTfParentFrame;
                var childFrame = _source.CameraTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return "skipped parent=" + parentFrame + " child=" + childFrame;
                }

                return TfAnchorTopic + " " + parentFrame + "->" + childFrame;
            }

            private void PublishTfAnchor(SensorCameraInfoFrame frame)
            {
                if (!_source.PublishCameraTfAnchor || Node == null)
                    return;

                var parentFrame = _source.CameraTfParentFrame;
                var childFrame = _source.CameraTfChildFrame;
                if (string.IsNullOrWhiteSpace(parentFrame)
                    || string.IsNullOrWhiteSpace(childFrame)
                    || string.Equals(parentFrame, childFrame, StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    _tfAnchorPublisher ??= Node.CreatePublisher<tf2_msgs.msg.TFMessage>(TfAnchorTopic);
                    _tfAnchorPublisher.Publish(BuildTfAnchorMessage(frame, parentFrame, childFrame));
                }
                catch (Exception ex)
                {
                    RecordPublishFailure("ROS2 Camera TF anchor publish failed for " + childFrame + ": " + ex.Message);
                }
            }

            private tf2_msgs.msg.TFMessage BuildTfAnchorMessage(
                SensorCameraInfoFrame frame,
                string parentFrame,
                string childFrame)
            {
                var translation = _source.CameraTfTranslation;
                var rotation = _source.CameraTfRotation;

                return new tf2_msgs.msg.TFMessage
                {
                    Transforms = new[]
                    {
                        new geometry_msgs.msg.TransformStamped
                        {
                            Header = new std_msgs.msg.Header
                            {
                                Stamp = new builtin_interfaces.msg.Time
                                {
                                    Sec = (int)(frame.UnixNs / 1_000_000_000UL),
                                    Nanosec = (uint)(frame.UnixNs % 1_000_000_000UL)
                                },
                                Frame_id = parentFrame
                            },
                            Child_frame_id = childFrame,
                            Transform = new geometry_msgs.msg.Transform
                            {
                                Translation = new geometry_msgs.msg.Vector3
                                {
                                    X = translation.X,
                                    Y = translation.Y,
                                    Z = translation.Z
                                },
                                Rotation = new geometry_msgs.msg.Quaternion
                                {
                                    X = rotation.X,
                                    Y = rotation.Y,
                                    Z = rotation.Z,
                                    W = rotation.W
                                }
                            }
                        }
                    }
                };
            }

            private void CleanupRos2()
            {
                if (Node != null && _publisher != null)
                {
                    try { Node.RemovePublisher<sensor_msgs.msg.CameraInfo>(_publisher); }
                    catch (Exception) { }
                }

                if (Node != null && _tfAnchorPublisher != null)
                {
                    try { Node.RemovePublisher<tf2_msgs.msg.TFMessage>(_tfAnchorPublisher); }
                    catch (Exception) { }
                }

                _publisher = null;
                _tfAnchorPublisher = null;
                CleanupNode();
            }
        }
    }
}
#endif
