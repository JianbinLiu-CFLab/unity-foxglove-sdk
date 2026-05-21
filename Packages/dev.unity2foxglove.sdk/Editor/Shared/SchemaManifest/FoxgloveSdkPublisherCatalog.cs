// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/SchemaManifest
// Purpose: Explicit SDK-owned publisher coverage catalog for schema evidence.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxgloveSdkPublisherCatalog
    {
        private static readonly Unity2FoxgloveSdkTypedPublisherEntry[] EntriesArray =
        {
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveTransformPublisher",
                "dedicatedPublisher",
                "/tf",
                "foxglove.FrameTransform",
                Ros2PublisherSchemaNames.FrameTransform,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true,
                "Transform publisher for TF-style frame transforms."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveSceneCubePublisher",
                "dedicatedPublisher",
                "/scene",
                "foxglove.SceneUpdate",
                Ros2PublisherSchemaNames.SceneUpdate,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true,
                "Scene cube smoke publisher for 3D visualization."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveCameraCalibrationPublisher",
                "dedicatedPublisher",
                "/unity/camera/calibration",
                "foxglove.CameraCalibration",
                Ros2PublisherSchemaNames.CameraCalibration,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true,
                "Camera calibration publisher for Unity Camera intrinsics."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveLaserScanPublisher",
                "dedicatedPublisher",
                "/unity/laser_scan",
                "foxglove.LaserScan",
                Ros2PublisherSchemaNames.LaserScan,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true,
                "LaserScan publisher with JSON, protobuf, and ROS2 CDR paths."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxglovePointCloudPublisher",
                "dedicatedPublisher",
                PointCloudOutputModeDefaults.RawTopic,
                PointCloudOutputModeDefaults.RawSchema,
                Ros2PublisherSchemaNames.PointCloud,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true,
                "Raw point-cloud profile."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxglovePointCloudPublisher",
                "dedicatedPublisher",
                PointCloudOutputModeDefaults.DracoTopic,
                PointCloudOutputModeDefaults.DracoSchema,
                Ros2PublisherSchemaNames.CompressedPointCloud,
                supportsJson: false,
                supportsProtobuf: true,
                supportsRos2: true,
                "Draco compressed point-cloud profile."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveCameraPublisher",
                "dedicatedPublisher",
                CameraOutputModeDefaults.JpegTopic,
                CameraOutputModeDefaults.JpegSchema,
                Ros2PublisherSchemaNames.CompressedImage,
                supportsJson: true,
                supportsProtobuf: true,
                supportsRos2: true,
                "JPEG camera profile."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveCameraPublisher",
                "dedicatedPublisher",
                CameraOutputModeDefaults.H264Topic,
                CameraOutputModeDefaults.H264Schema,
                "",
                supportsJson: false,
                supportsProtobuf: true,
                supportsRos2: false,
                "Compressed video camera profile."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveCompressedVideoCameraPublisher",
                "dedicatedPublisher",
                "/unity/camera/video",
                "foxglove.CompressedVideo",
                "",
                supportsJson: false,
                supportsProtobuf: true,
                supportsRos2: false,
                "Legacy standalone compressed video publisher."),
            Concrete(
                "Unity.FoxgloveSDK.Components.FoxgloveCompressedPointCloudPublisher",
                "dedicatedPublisher",
                PointCloudOutputModeDefaults.DracoTopic,
                PointCloudOutputModeDefaults.DracoSchema,
                "",
                supportsJson: false,
                supportsProtobuf: true,
                supportsRos2: false,
                "Legacy standalone Draco point-cloud publisher."),
            Template(
                "Unity.FoxgloveSDK.Components.FoxglovePublisher<TMessage>",
                "jsonTyped",
                supportsJson: true,
                supportsProtobuf: false,
                supportsRos2: false,
                "Generic JSON typed publisher template; concrete schema is supplied by TMessage."),
            Template(
                "Foxglove.Components.ProtobufPublisher<T>",
                "protobufTyped",
                supportsJson: false,
                supportsProtobuf: true,
                supportsRos2: false,
                "Generic protobuf typed publisher template; concrete schema is supplied by T.")
        };

        public static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> Entries { get; } = Array.AsReadOnly(EntriesArray);

        private static Unity2FoxgloveSdkTypedPublisherEntry Concrete(
            string publisherTypeFullName,
            string publisherFamily,
            string defaultTopic,
            string foxgloveSchemaName,
            string ros2SchemaName,
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2,
            string productNote)
        {
            return new Unity2FoxgloveSdkTypedPublisherEntry(
                publisherTypeFullName,
                "concretePublisher",
                publisherFamily,
                defaultTopic,
                foxgloveSchemaName,
                ros2SchemaName,
                supportsJson,
                supportsProtobuf,
                supportsRos2,
                isTemplate: false,
                productNote);
        }

        private static Unity2FoxgloveSdkTypedPublisherEntry Template(
            string publisherTypeFullName,
            string publisherFamily,
            bool supportsJson,
            bool supportsProtobuf,
            bool supportsRos2,
            string productNote)
        {
            return new Unity2FoxgloveSdkTypedPublisherEntry(
                publisherTypeFullName,
                "genericTemplate",
                publisherFamily,
                "",
                "",
                "",
                supportsJson,
                supportsProtobuf,
                supportsRos2,
                isTemplate: true,
                productNote);
        }
    }
}
