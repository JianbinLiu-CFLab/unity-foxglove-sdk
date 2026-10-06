// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Player-safe metadata seam implemented by validated custom typesupport add-ons.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System.Collections.Generic;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Static metadata supplied by one validated custom ROS2 typesupport
    /// add-on. Implementations must not initialize ROS2 or load native plugins.
    /// </summary>
    public interface IFoxRunRos2CustomTypesupportCatalog
    {
        string SourcePackageId { get; }
        string RosPackageName { get; }
        int InterfaceRevision { get; }
        string InterfaceDigest { get; }
        string BaseRuntimePackageId { get; }
        string Platform { get; }
        IReadOnlyList<string> SupportedRmwImplementations { get; }
        IReadOnlyList<FoxRunRos2CustomTypesupportTypeMapEntry> TypeMap { get; }
    }

    /// <summary>One canonical ROS type to generated managed type identity.</summary>
    public readonly struct FoxRunRos2CustomTypesupportTypeMapEntry
    {
        public FoxRunRos2CustomTypesupportTypeMapEntry(string canonicalRosType, string managedTypeName)
        {
            CanonicalRosType = canonicalRosType;
            ManagedTypeName = managedTypeName;
        }

        public string CanonicalRosType { get; }
        public string ManagedTypeName { get; }
    }
}
#endif
