// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Owns explicit synthetic NavSatFix sample data for Phase132.

using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/ROS2 For Unity/Standard NavSatFix Source")]
public sealed class Phase132StandardNavSatFixSource : MonoBehaviour
{
    [Header("Synthetic WGS84 Fix")]
    [SerializeField] private string _frameId = "gps_link";
    [SerializeField] private double _latitudeDegrees = 37.7749d;
    [SerializeField] private double _longitudeDegrees = -122.4194d;
    [SerializeField] private double _altitudeMeters = 15.0d;

    [Header("Status")]
    [SerializeField] private string _statusMessage = "Ready.";
    [SerializeField] private int _publishedCount;
    [SerializeField] private string _lastError = string.Empty;

    public string FrameId => Phase132StandardMessagesCommon.CleanFrameId(_frameId, "gps_link");

    private void OnValidate()
    {
        _latitudeDegrees = Clamp(_latitudeDegrees, -90d, 90d);
        _longitudeDegrees = Clamp(_longitudeDegrees, -180d, 180d);
    }

    public void RecordPublished()
    {
        _publishedCount++;
        _lastError = string.Empty;
        _statusMessage = "Published NavSatFix=" + _publishedCount + ".";
    }

    public void RecordError(string error)
    {
        _lastError = error;
        _statusMessage = error;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return value < minimum ? minimum : value > maximum ? maximum : value;
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public sensor_msgs.msg.NavSatFix CreateNavSatFix(int sec, uint nanosec)
    {
        var covariance = new[] { 2.5d, 0d, 0d, 0d, 2.5d, 0d, 0d, 0d, 5.0d };
        Phase132StandardMessagesCommon.ValidateFixedArrayLength(covariance, 9, "NavSatFix position covariance double[9]");

        return new sensor_msgs.msg.NavSatFix
        {
            Header = Phase132StandardMessagesCommon.CreateHeader(FrameId, sec, nanosec),
            Status = new sensor_msgs.msg.NavSatStatus
            {
                Status = sensor_msgs.msg.NavSatStatus.STATUS_FIX,
                Service = sensor_msgs.msg.NavSatStatus.SERVICE_GPS
            },
            Latitude = _latitudeDegrees,
            Longitude = _longitudeDegrees,
            Altitude = _altitudeMeters,
            Position_covariance = covariance,
            Position_covariance_type = sensor_msgs.msg.NavSatFix.COVARIANCE_TYPE_DIAGONAL_KNOWN
        };
    }
#endif
}
