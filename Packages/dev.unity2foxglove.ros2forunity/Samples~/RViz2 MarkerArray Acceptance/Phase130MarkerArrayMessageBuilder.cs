// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Sample
// Purpose: Maps deterministic Unity scene markers to ROS2 MarkerArray messages.

using System;
using System.Text;
using UnityEngine;

public static class Phase130MarkerArrayMessageBuilder
{
    public const string DefaultFrameId = "map";
    public const string DefaultNamespace = "unity2foxglove";

    private const uint FnvOffsetBasis = 2166136261u;
    private const uint FnvPrime = 16777619u;

    public static int CreateDeterministicId(string stableName)
    {
        if (string.IsNullOrWhiteSpace(stableName))
            throw new ArgumentException("Marker stable name must not be empty.", nameof(stableName));

        var hash = FnvOffsetBasis;
        var bytes = Encoding.UTF8.GetBytes(stableName);
        for (var i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= FnvPrime;
        }

        return unchecked((int)(hash & 0x7fffffffu));
    }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
    public static visualization_msgs.msg.MarkerArray BuildAddOrModify(
        string stableName,
        Vector3 position,
        Vector3 scale,
        Color color,
        int sec,
        uint nanosec)
    {
        return new visualization_msgs.msg.MarkerArray
        {
            Markers = new[]
            {
                CreateBaseMarker(stableName, sec, nanosec, visualization_msgs.msg.Marker.ADD, visualization_msgs.msg.Marker.CUBE)
                    .WithPose(position)
                    .WithScale(scale)
                    .WithColor(color)
            }
        };
    }

    public static visualization_msgs.msg.MarkerArray BuildDelete(string stableName, int sec, uint nanosec)
    {
        return new visualization_msgs.msg.MarkerArray
        {
            Markers = new[]
            {
                CreateBaseMarker(stableName, sec, nanosec, visualization_msgs.msg.Marker.DELETE, visualization_msgs.msg.Marker.CUBE)
            }
        };
    }

    public static visualization_msgs.msg.MarkerArray BuildDeleteAll(int sec, uint nanosec)
    {
        return new visualization_msgs.msg.MarkerArray
        {
            Markers = new[]
            {
                CreateBaseMarker("delete_all", sec, nanosec, visualization_msgs.msg.Marker.DELETEALL, visualization_msgs.msg.Marker.CUBE)
            }
        };
    }

    private static visualization_msgs.msg.Marker CreateBaseMarker(string stableName, int sec, uint nanosec, int action, int type)
    {
        return new visualization_msgs.msg.Marker
        {
            Header = CreateHeader(sec, nanosec),
            Ns = DefaultNamespace,
            Id = CreateDeterministicId(stableName),
            Type = type,
            Action = action,
            Pose = IdentityPose(),
            Scale = OneScale(),
            Color = WhiteColor(),
            Lifetime = new builtin_interfaces.msg.Duration
            {
                Sec = 0,
                Nanosec = 0u
            },
            Frame_locked = false
        };
    }

    private static visualization_msgs.msg.Marker WithPose(this visualization_msgs.msg.Marker marker, Vector3 position)
    {
        marker.Pose = new geometry_msgs.msg.Pose
        {
            Position = new geometry_msgs.msg.Point
            {
                X = position.x,
                Y = position.y,
                Z = position.z
            },
            Orientation = IdentityRotation()
        };
        return marker;
    }

    private static visualization_msgs.msg.Marker WithScale(this visualization_msgs.msg.Marker marker, Vector3 scale)
    {
        marker.Scale = new geometry_msgs.msg.Vector3
        {
            X = Math.Max(0.001d, scale.x),
            Y = Math.Max(0.001d, scale.y),
            Z = Math.Max(0.001d, scale.z)
        };
        return marker;
    }

    private static visualization_msgs.msg.Marker WithColor(this visualization_msgs.msg.Marker marker, Color color)
    {
        marker.Color = new std_msgs.msg.ColorRGBA
        {
            R = color.r,
            G = color.g,
            B = color.b,
            A = Mathf.Clamp01(color.a)
        };
        return marker;
    }

    private static std_msgs.msg.Header CreateHeader(int sec, uint nanosec)
    {
        return new std_msgs.msg.Header
        {
            Stamp = new builtin_interfaces.msg.Time
            {
                Sec = sec,
                Nanosec = nanosec
            },
            Frame_id = DefaultFrameId
        };
    }

    private static geometry_msgs.msg.Pose IdentityPose()
    {
        return new geometry_msgs.msg.Pose
        {
            Position = new geometry_msgs.msg.Point
            {
                X = 0.0,
                Y = 0.0,
                Z = 0.0
            },
            Orientation = IdentityRotation()
        };
    }

    private static geometry_msgs.msg.Vector3 OneScale()
    {
        return new geometry_msgs.msg.Vector3
        {
            X = 1.0,
            Y = 1.0,
            Z = 1.0
        };
    }

    private static geometry_msgs.msg.Quaternion IdentityRotation()
    {
        return new geometry_msgs.msg.Quaternion
        {
            X = 0.0,
            Y = 0.0,
            Z = 0.0,
            W = 1.0
        };
    }

    private static std_msgs.msg.ColorRGBA WhiteColor()
    {
        return new std_msgs.msg.ColorRGBA
        {
            R = 1f,
            G = 1f,
            B = 1f,
            A = 1f
        };
    }
#endif
}
