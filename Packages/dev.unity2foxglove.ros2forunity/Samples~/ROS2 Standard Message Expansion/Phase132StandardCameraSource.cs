// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Owns synthetic CameraInfo and raw Image sample data for Phase132.

using System;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Standard Camera Source")]
public sealed class Phase132StandardCameraSource : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private string _frameId = "camera_optical_frame";
    [SerializeField, Range(1, 128)] private int _width = 32;
    [SerializeField, Range(1, 128)] private int _height = 24;
    [SerializeField, Min(1f)] private float _focalLengthPixels = 24f;
    [SerializeField] private string _distortionModel = "plumb_bob";

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Ready.";
    [SerializeField] private int _publishedCameraInfoCount;
    [SerializeField] private int _publishedImageCount;
    [SerializeField] private string _lastError = string.Empty;

    public string FrameId => Phase132StandardMessagesCommon.CleanFrameId(_frameId, "camera_optical_frame");
    public int Width => Mathf.Clamp(_width, 1, 128);
    public int Height => Mathf.Clamp(_height, 1, 128);

    private void OnValidate()
    {
        _width = Mathf.Clamp(_width, 1, 128);
        _height = Mathf.Clamp(_height, 1, 128);
        _focalLengthPixels = Mathf.Max(1f, _focalLengthPixels);
    }

    public void RecordCameraInfoPublished()
    {
        _publishedCameraInfoCount++;
        _lastError = string.Empty;
        _statusMessage = "Published CameraInfo=" + _publishedCameraInfoCount + ".";
    }

    public void RecordImagePublished()
    {
        _publishedImageCount++;
        _lastError = string.Empty;
        _statusMessage = "Published Image=" + _publishedImageCount + ".";
    }

    public void RecordError(string error)
    {
        _lastError = error;
        _statusMessage = error;
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public sensor_msgs.msg.CameraInfo CreateCameraInfo(int sec, uint nanosec)
    {
        var width = Width;
        var height = Height;
        var cx = (width - 1) * 0.5d;
        var cy = (height - 1) * 0.5d;
        var focal = Math.Max(1d, _focalLengthPixels);
        var k = new[] { focal, 0d, cx, 0d, focal, cy, 0d, 0d, 1d };
        var r = new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d };
        var p = new[] { focal, 0d, cx, 0d, 0d, focal, cy, 0d, 0d, 0d, 1d, 0d };

        Phase132StandardMessagesCommon.ValidateFixedArrayLength(k, 9, "CameraInfo.k[9]");
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(r, 9, "CameraInfo.r[9]");
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(p, 12, "CameraInfo.p[12]");

        var message = new sensor_msgs.msg.CameraInfo
        {
            Header = Phase132StandardMessagesCommon.CreateHeader(FrameId, sec, nanosec),
            Height = checked((uint)height),
            Width = checked((uint)width),
            Distortion_model = string.IsNullOrWhiteSpace(_distortionModel) ? "plumb_bob" : _distortionModel.Trim(),
            D = Array.Empty<double>(),
            Binning_x = 0u,
            Binning_y = 0u,
            Roi = new sensor_msgs.msg.RegionOfInterest
            {
                X_offset = 0u,
                Y_offset = 0u,
                Height = 0u,
                Width = 0u,
                Do_rectify = false
            }
        };
        Array.Copy(k, message.K, k.Length);
        Array.Copy(r, message.R, r.Length);
        Array.Copy(p, message.P, p.Length);
        return message;
    }

    public sensor_msgs.msg.Image CreateImage(int sec, uint nanosec, int frameIndex)
    {
        var width = Width;
        var height = Height;
        var step = checked((uint)(width * 3));
        var expectedLength = checked(height * (int)step);
        var data = new byte[expectedLength];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = checked((y * width + x) * 3);
                data[offset] = (byte)((x * 8 + frameIndex * 7) & 0xff);
                data[offset + 1] = (byte)((y * 10 + frameIndex * 5) & 0xff);
                data[offset + 2] = (byte)(((x + y) * 5 + frameIndex * 3) & 0xff);
            }
        }

        if (data.Length != expectedLength)
            throw new InvalidOperationException("Image data.Length must equal height * step.");

        return new sensor_msgs.msg.Image
        {
            Header = Phase132StandardMessagesCommon.CreateHeader(FrameId, sec, nanosec),
            Height = checked((uint)height),
            Width = checked((uint)width),
            Encoding = "rgb8",
            Is_bigendian = 0,
            Step = step,
            Data = data
        };
    }
#endif
}
