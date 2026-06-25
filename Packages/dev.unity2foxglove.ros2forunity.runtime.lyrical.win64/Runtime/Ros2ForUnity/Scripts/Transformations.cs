// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using System.Runtime.CompilerServices;

namespace ROS2
{
/// <summary>
/// A set of transformation functions between coordinate systems of Unity and ROS
/// </summary>
public static class Transformations
{
    /// <summary>
    /// Constant Unity-to-ROS coordinate transform matrix.
    /// Unity uses x-right, y-up, z-forward; ROS uses x-forward, y-left, z-up.
    /// </summary>
    public static readonly Matrix4x4 Unity2RosMatrix = new Matrix4x4(
        new Vector4( 0.0f, 0.0f, 1.0f, 0.0f),
        new Vector4(-1.0f, 0.0f, 0.0f, 0.0f),
        new Vector4( 0.0f, 1.0f, 0.0f, 0.0f),
        new Vector4( 0.0f, 0.0f, 0.0f, 1.0f)
    ).transpose;

    /// <summary>
    /// Constant ROS-to-Unity coordinate transform matrix, inverse of Unity2RosMatrix.
    /// </summary>
    public static readonly Matrix4x4 Ros2UnityMatrix = Unity2RosMatrix.inverse;

    /// <summary>
    /// Converts a ROS vector (x-forward, y-left, z-up) into a Unity vector (x-right, y-up, z-forward).
    /// </summary>
    public static Vector3 Ros2Unity(this Vector3 vector3)
    {
        return new Vector3(-vector3.y, vector3.z, vector3.x);
    }

    /// <summary>
    /// Converts a Unity vector (x-right, y-up, z-forward) into a ROS vector (x-forward, y-left, z-up).
    /// </summary>
    public static Vector3 Unity2Ros(this Vector3 vector3)
    {
        return new Vector3(vector3.z, -vector3.x, vector3.y);
    }

    /// <summary>
    /// Converts ROS scale extents to Unity scale extents. Scale magnitudes do not carry handedness,
    /// so this only swaps axes and does not apply sign flips.
    /// </summary>
    public static Vector3 Ros2UnityScale(this Vector3 vector3)
    {
        return new Vector3(vector3.y, vector3.z, vector3.x);
    }

    /// <summary>
    /// Converts Unity scale extents to ROS scale extents. Scale magnitudes do not carry handedness,
    /// so this only swaps axes and does not apply sign flips.
    /// </summary>
    public static Vector3 Unity2RosScale(this Vector3 vector3)
    {
        return new Vector3(vector3.z, vector3.x, vector3.y);
    }

    /// <summary>
    /// Converts ROS rotation into Unity rotation, including the handedness flip between coordinate systems.
    /// </summary>
    public static Quaternion Ros2Unity(this Quaternion quaternion)
    {
        return new Quaternion(quaternion.y, -quaternion.z, -quaternion.x, quaternion.w);
    }

    /// <summary>
    /// Converts Unity rotation into ROS rotation, including the handedness flip between coordinate systems.
    /// </summary>
    public static Quaternion Unity2Ros(this Quaternion quaternion)
    {
        return new Quaternion(-quaternion.z, quaternion.x, -quaternion.y, quaternion.w);
    }

    /// <summary>
    /// Converts a Unity rotation to ROS in place.
    /// </summary>
    public static void Unity2Ros(ref Quaternion quaternion)
    {
        var z = quaternion.z;
        var x = quaternion.x;
        var y = quaternion.y;
        quaternion.x = -z;
        quaternion.y = x;
        quaternion.z = -y;
    }

    /// <summary>
    /// Converts a Unity vector to ROS in place.
    /// </summary>
    public static void Unity2Ros(ref Vector3 vector)
    {
        var z = vector.z;
        var x = vector.x;
        var y = vector.y;
        vector.x = z;
        vector.y = -x;
        vector.z = y;
    }

    /// <summary>
    /// Compatibility wrapper for callers that expect a method; prefer Unity2RosMatrix for new code.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 Unity2RosMatrix4x4()
    {
        return Unity2RosMatrix;
    }
}

}  // namespace ROS2
