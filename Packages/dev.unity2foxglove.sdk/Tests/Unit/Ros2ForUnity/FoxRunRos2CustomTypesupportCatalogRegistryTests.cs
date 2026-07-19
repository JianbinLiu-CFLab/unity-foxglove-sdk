// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Player-safe fail-closed catalog readiness tests without AssetDatabase.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System.Collections.Generic;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-C")]
    [Trait("Domain", "CustomTypesupportCatalog")]
    public sealed class FoxRunRos2CustomTypesupportCatalogRegistryTests
    {
        private const string Runtime = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
        private const string Digest = "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd";

        [Fact]
        public void OneExactCatalogIsReady()
        {
            Reset();
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(ValidCatalog());

            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.Ready,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_fastrtps_cpp").Code);
        }

        [Fact]
        public void RevisionedCatalogUsesItsOwnVersionedRosPackageName()
        {
            Reset();
            const string rosPackageName = "unity2foxglove_foxrun_interfaces_v2";
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(new Catalog(
                sourcePackageId: "dev.unity2foxglove.foxrun.ros2.interfaces",
                rosPackageName: rosPackageName,
                interfaceRevision: 2,
                interfaceDigest: Digest,
                baseRuntime: Runtime,
                platform: "win64",
                rmws: new[] { "rmw_fastrtps_cpp" },
                typeMap: new[]
                {
                    new FoxRunRos2CustomTypesupportTypeMapEntry(
                        rosPackageName + "/msg/Phase181State48D288ED82F1Envelope",
                        rosPackageName + ".msg.Phase181State48D288ED82F1Envelope")
                }));

            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.Ready,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_fastrtps_cpp").Code);
        }

        [Fact]
        public void MissingDuplicateMismatchAndMalformedCatalogsFailClosed()
        {
            Reset();
            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.MissingCatalog,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_fastrtps_cpp").Code);

            FoxRunRos2CustomTypesupportCatalogRegistry.Register(ValidCatalog());
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(ValidCatalog());
            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.DuplicateCatalog,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_fastrtps_cpp").Code);

            Reset();
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(ValidCatalog());
            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.RuntimeMismatch,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate("other.runtime", Digest, "rmw_fastrtps_cpp").Code);
            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.DigestMismatch,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, "other", "rmw_fastrtps_cpp").Code);
            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.UnsupportedRmw,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_zenoh_cpp").Code);

            Reset();
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(new Catalog(
                sourcePackageId: "",
                rosPackageName: "unity2foxglove_foxrun_interfaces_v1",
                interfaceRevision: 1,
                interfaceDigest: Digest,
                baseRuntime: Runtime,
                platform: "win64",
                rmws: new[] { "rmw_fastrtps_cpp" },
                typeMap: new[] { new FoxRunRos2CustomTypesupportTypeMapEntry("", "") }));
            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.InvalidCatalog,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_fastrtps_cpp").Code);
        }

        [Fact]
        public void RegistrationAfterNativeSessionStopRemainsNonReady()
        {
            Reset();
            FoxRunRos2CustomTypesupportCatalogRegistry.MarkNativeSessionStopped();
            FoxRunRos2CustomTypesupportCatalogRegistry.Register(ValidCatalog());

            Assert.Equal(
                FoxRunRos2CustomTypesupportReadinessCode.NativeSessionStopped,
                FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(Runtime, Digest, "rmw_fastrtps_cpp").Code);
        }

        private static void Reset()
        {
            FoxRunRos2CustomTypesupportCatalogRegistry.ResetForTests();
        }

        private static IFoxRunRos2CustomTypesupportCatalog ValidCatalog()
        {
            return new Catalog(
                sourcePackageId: "dev.unity2foxglove.foxrun.ros2.interfaces",
                rosPackageName: "unity2foxglove_foxrun_interfaces_v1",
                interfaceRevision: 1,
                interfaceDigest: Digest,
                baseRuntime: Runtime,
                platform: "win64",
                rmws: new[] { "rmw_fastrtps_cpp" },
                typeMap: new[]
                {
                    new FoxRunRos2CustomTypesupportTypeMapEntry(
                        "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
                        "unity2foxglove_foxrun_interfaces_v1.msg.Phase181State48D288ED82F1Envelope")
                });
        }

        private sealed class Catalog : IFoxRunRos2CustomTypesupportCatalog
        {
            public Catalog(
                string sourcePackageId,
                string rosPackageName,
                int interfaceRevision,
                string interfaceDigest,
                string baseRuntime,
                string platform,
                IReadOnlyList<string> rmws,
                IReadOnlyList<FoxRunRos2CustomTypesupportTypeMapEntry> typeMap)
            {
                SourcePackageId = sourcePackageId;
                RosPackageName = rosPackageName;
                InterfaceRevision = interfaceRevision;
                InterfaceDigest = interfaceDigest;
                BaseRuntimePackageId = baseRuntime;
                Platform = platform;
                SupportedRmwImplementations = rmws;
                TypeMap = typeMap;
            }

            public string SourcePackageId { get; }
            public string RosPackageName { get; }
            public int InterfaceRevision { get; }
            public string InterfaceDigest { get; }
            public string BaseRuntimePackageId { get; }
            public string Platform { get; }
            public IReadOnlyList<string> SupportedRmwImplementations { get; }
            public IReadOnlyList<FoxRunRos2CustomTypesupportTypeMapEntry> TypeMap { get; }
        }
    }
}
#endif
