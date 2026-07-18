// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Internal Player-safe custom typesupport readiness result.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal enum FoxRunRos2CustomTypesupportReadinessCode
    {
        Ready = 0,
        MissingCatalog = 1,
        DuplicateCatalog = 2,
        InvalidCatalog = 3,
        RuntimeMismatch = 4,
        DigestMismatch = 5,
        UnsupportedRmw = 6,
        NativeSessionStopped = 7
    }

    internal readonly struct FoxRunRos2CustomTypesupportReadiness
    {
        public FoxRunRos2CustomTypesupportReadiness(FoxRunRos2CustomTypesupportReadinessCode code)
        {
            Code = code;
        }

        public FoxRunRos2CustomTypesupportReadinessCode Code { get; }
        public bool IsReady => Code == FoxRunRos2CustomTypesupportReadinessCode.Ready;

        public static FoxRunRos2CustomTypesupportReadiness From(
            FoxRunRos2CustomTypesupportReadinessCode code)
        {
            return new FoxRunRos2CustomTypesupportReadiness(code);
        }
    }
}
#endif
