// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Selection transaction model records..

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityCustomTypesupportSelectionTransaction
    {
        private sealed class BaseRuntime
        {
            public BaseRuntime(string packageId, string packageVersion, string distro, string manifestDigest, string directory)
            {
                PackageId = packageId;
                PackageVersion = packageVersion;
                Distro = distro;
                ManifestDigest = manifestDigest;
                Directory = directory;
            }

            public string PackageId { get; }
            public string PackageVersion { get; }
            public string Distro { get; }
            public string ManifestDigest { get; }
            public string Directory { get; }
        }

        private sealed class Candidate
        {
            public Candidate(string packageId, string interfaceDigest, string nativePluginDirectory)
            {
                PackageId = packageId;
                InterfaceDigest = interfaceDigest;
                NativePluginDirectory = nativePluginDirectory;
            }

            public string PackageId { get; }
            public string InterfaceDigest { get; }
            public string NativePluginDirectory { get; }
        }

        private sealed class StaticSourceLock
        {
            public StaticSourceLock(string rosPackageName, int interfaceRevision, string interfaceDigest)
            {
                RosPackageName = rosPackageName;
                InterfaceRevision = interfaceRevision;
                InterfaceDigest = interfaceDigest;
            }

            public string RosPackageName { get; }
            public int InterfaceRevision { get; }
            public string InterfaceDigest { get; }
        }
    }
}
