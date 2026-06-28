// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: Maps SDK camera DTOs to generated ROS2 sensor_msgs messages.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Schemas.Camera;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal static class Ros2ForUnityCameraMessageBuilder
    {
        public static sensor_msgs.msg.CompressedImage BuildCompressedImage(SensorCompressedImageFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            return new sensor_msgs.msg.CompressedImage
            {
                Header = CreateHeader(
                    frame.FrameId,
                    (int)(frame.UnixNs / 1_000_000_000UL),
                    (uint)(frame.UnixNs % 1_000_000_000UL)),
                Format = frame.Format,
                Data = frame.Data
            };
        }

        public static sensor_msgs.msg.CameraInfo BuildCameraInfo(SensorCameraInfoFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var message = new sensor_msgs.msg.CameraInfo
            {
                Header = CreateHeader(
                    frame.FrameId,
                    (int)(frame.UnixNs / 1_000_000_000UL),
                    (uint)(frame.UnixNs % 1_000_000_000UL)),
                Height = frame.Height,
                Width = frame.Width,
                Distortion_model = frame.DistortionModel,
                D = Copy(frame.D),
                Binning_x = 0U,
                Binning_y = 0U,
                Roi = new sensor_msgs.msg.RegionOfInterest
                {
                    X_offset = 0U,
                    Y_offset = 0U,
                    Height = 0U,
                    Width = 0U,
                    Do_rectify = false
                }
            };

            CopyInto(frame.K, message.K);
            CopyInto(frame.R, message.R);
            CopyInto(frame.P, message.P);
            return message;
        }

        public static sensor_msgs.msg.Image BuildImage(SensorRawImageFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            var message = new sensor_msgs.msg.Image
            {
                Header = CreateHeader(
                    frame.FrameId,
                    (int)(frame.UnixNs / 1_000_000_000UL),
                    (uint)(frame.UnixNs % 1_000_000_000UL)),
                Height = checked((uint)frame.Height),
                Width = checked((uint)frame.Width),
                Encoding = frame.Encoding,
                Is_bigendian = frame.IsBigendian,
                Step = checked((uint)frame.Step),
                Data = frame.Data
            };
            return message;
        }

        private static std_msgs.msg.Header CreateHeader(string frameId, int sec, uint nanosec)
        {
            return new std_msgs.msg.Header
            {
                Stamp = new builtin_interfaces.msg.Time
                {
                    Sec = sec,
                    Nanosec = nanosec
                },
                Frame_id = frameId
            };
        }

        private static double[] Copy(System.Collections.Generic.IReadOnlyList<double> values)
        {
            if (values == null)
                return Array.Empty<double>();

            var output = new double[values.Count];
            for (var i = 0; i < output.Length; i++)
                output[i] = values[i];
            return output;
        }

        private static double[] Copy(System.Collections.Generic.IReadOnlyList<double> values, int length)
        {
            var output = new double[length];
            if (values == null)
                return output;

            for (var i = 0; i < output.Length && i < values.Count; i++)
                output[i] = values[i];
            return output;
        }

        private static void CopyInto(System.Collections.Generic.IReadOnlyList<double> source, double[] destination)
        {
            if (source == null || destination == null)
                return;

            var count = Math.Min(source.Count, destination.Length);
            for (var i = 0; i < count; i++)
                destination[i] = source[i];
        }
    }
}
#endif
