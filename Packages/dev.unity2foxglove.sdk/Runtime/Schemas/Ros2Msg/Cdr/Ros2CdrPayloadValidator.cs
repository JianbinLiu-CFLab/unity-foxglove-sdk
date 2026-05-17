// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Cdr
// Purpose: Payload boundary checks for ROS 2 CDR publish helpers.

using System;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Validates the minimal CDR payload contract before publish.</summary>
    public static class Ros2CdrPayloadValidator
    {
        /// <summary>Validate that a payload is non-empty and has the little-endian CDR header.</summary>
        public static void Validate(byte[] payload, string parameterName = "payload")
        {
            if (payload == null)
                throw new ArgumentException("ROS 2 CDR payload must be non-null.", parameterName);
            if (payload.Length == 0)
                throw new ArgumentException("ROS 2 CDR payload must be non-empty.", parameterName);
            if (payload.Length < 4)
                throw new ArgumentException("ROS 2 CDR payload must include a 4-byte encapsulation header.", parameterName);
            if (payload[0] != 0x00 || payload[1] != 0x01 || payload[2] != 0x00 || payload[3] != 0x00)
                throw new ArgumentException("ROS 2 CDR payload must start with little-endian encapsulation header 00 01 00 00.", parameterName);
        }
    }
}
