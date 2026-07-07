// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-29 package-sample asset boundary checks.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "140-29")]
    [Trait("Domain", "Architecture")]
    public sealed class UnityDemoSamplesAssetsTests
    {
        private static readonly Lazy<string> RepoRoot = new Lazy<string>(FindRepoRoot);

        private static readonly string[] VolumeProfilePaths =
        {
            "Unity2Foxglove/Assets/Settings/DefaultVolumeProfile.asset",
            "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Settings/DefaultVolumeProfile.asset"
        };

        private static readonly string[] PcRenderPipelineAssetPaths =
        {
            "Unity2Foxglove/Assets/Settings/PC_RPAsset.asset",
            "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Settings/PC_RPAsset.asset"
        };

        private static readonly string[] UrpGlobalSettingsPaths =
        {
            "Unity2Foxglove/Assets/Settings/UniversalRenderPipelineGlobalSettings.asset",
            "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Settings/UniversalRenderPipelineGlobalSettings.asset"
        };

        private static readonly string[] MazeDemoRoots =
        {
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo",
            "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo"
        };

        [Fact]
        public void FoxRunLinkXmlEmitterGroupsTypesBeforeWritingAssemblies()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");

            Assert.Contains(".GroupBy(", source, StringComparison.Ordinal);
            Assert.Contains(".AsmName", source, StringComparison.Ordinal);
            Assert.Contains("foreach (var type in group", source, StringComparison.Ordinal);
            Assert.DoesNotContain("foreach (var (asm, ns, cn) in types)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FullDemoPackageSampleDoesNotShipDefaultInputActionsAsset()
        {
            Assert.False(File.Exists(Path("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/InputSystem_Actions.inputactions")));
            Assert.False(File.Exists(Path("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/InputSystem_Actions.inputactions.meta")));

            var sync = Text("Scripts/samples/sync_full_demo.py");
            Assert.DoesNotContain("InputSystem_Actions.inputactions", sync, StringComparison.Ordinal);

            var validator = Text("Scripts/package/validate_unity_package.py");
            var requiredFullStart = validator.IndexOf("required_full = [", StringComparison.Ordinal);
            var requiredFullEnd = validator.IndexOf("missing = [rel(p) for p in required_full", StringComparison.Ordinal);
            Assert.True(requiredFullStart >= 0 && requiredFullEnd > requiredFullStart);
            var requiredFull = validator.Substring(requiredFullStart, requiredFullEnd - requiredFullStart);
            Assert.DoesNotContain("InputSystem_Actions.inputactions", requiredFull, StringComparison.Ordinal);
            Assert.Contains("FullDemo avoids project-level input action assets", validator, StringComparison.Ordinal);

            var readme = Text("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/README.md");
            Assert.DoesNotContain("InputSystem_Actions.inputactions", readme, StringComparison.Ordinal);
        }

        [Fact]
        public void FullDemoDefaultVolumeProfilesContainOnlyPortableUrpComponents()
        {
            foreach (var relativePath in VolumeProfilePaths)
            {
                var profile = Text(relativePath);

                Assert.DoesNotContain("TestAnimationCurveVolumeComponent", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("TestVolume", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("OutlineVolumeComponent", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("OasisFogVolumeComponent", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("guid: 0fd9ee276a1023e439cf7a9c393195fa", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("guid: 74955a4b0b4243bc87231e8b59ed9140", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("guid: 60f3b30c03e6ba64d9a27dc9dba8f28d", profile, StringComparison.Ordinal);
                Assert.DoesNotContain("guid: 5a00a63fdd6bd2a45ab1f2d869305ffd", profile, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void PcRenderPipelineAssetsDisableAdaptivePerformance()
        {
            foreach (var relativePath in PcRenderPipelineAssetPaths)
                Assert.Contains("m_UseAdaptivePerformance: 0", Text(relativePath), StringComparison.Ordinal);
        }

        [Fact]
        public void UrpGlobalSettingsEnableNamedRenderingLayers()
        {
            foreach (var relativePath in UrpGlobalSettingsPaths)
                Assert.DoesNotContain("m_ValidRenderingLayers: 0", Text(relativePath), StringComparison.Ordinal);
        }

        [Fact]
        public void MazeDemoStoresEachInternalWallOnce()
        {
            foreach (var root in MazeDemoRoots)
            {
                var source = Text($"{root}/Phase138MazeBuilder.cs");

                Assert.Contains("Store each internal wall once", source, StringComparison.Ordinal);
                Assert.Contains("walls.Add((x, z, 0))", source, StringComparison.Ordinal);
                Assert.Contains("walls.Add((x, z, 2))", source, StringComparison.Ordinal);
                Assert.DoesNotContain("walls.Add((x, z, 1))", source, StringComparison.Ordinal);
                Assert.DoesNotContain("walls.Add((x, z, 3))", source, StringComparison.Ordinal);
                Assert.Contains("walls.Remove((nx2, nz2, 0))", source, StringComparison.Ordinal);
                Assert.Contains("walls.Remove((nx2, nz2, 2))", source, StringComparison.Ordinal);
                Assert.DoesNotContain("cellWorldX - cellSize * 0.5f", source, StringComparison.Ordinal);
                Assert.DoesNotContain("cellWorldZ - cellSize * 0.5f", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void MazeDemoPrimitiveColoringDoesNotCloneMaterials()
        {
            foreach (var root in MazeDemoRoots)
            {
                foreach (var fileName in new[] { "Phase138MazeBuilder.cs", "Phase138LidarVehicleController.cs" })
                {
                    var source = Text($"{root}/{fileName}");

                    Assert.Contains("new MaterialPropertyBlock()", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("new Material(renderer.sharedMaterial)", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("renderer.sharedMaterial = ", source, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void MazeDemoVehicleAutoWanderDoesNotRaycastAgainstItself()
        {
            foreach (var root in MazeDemoRoots)
            {
                var source = Text($"{root}/Phase138LidarVehicleController.cs");

                Assert.Contains("Physics.DefaultRaycastLayers", source, StringComparison.Ordinal);
                Assert.DoesNotContain("~0, QueryTriggerInteraction.Ignore", source, StringComparison.Ordinal);
                Assert.Contains("_suppressJitterUntil", source, StringComparison.Ordinal);
                Assert.Contains("SetWanderDirection(Quaternion.Euler", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void MazeDemoOverviewCameraPublisherHasExplicitManager()
        {
            foreach (var root in MazeDemoRoots)
            {
                var bootstrap = Text($"{root}/Phase138MazeDemoBootstrap.cs");
                Assert.Contains("SetPrivateField(demoCameraPublisher, \"_manager\", manager);", bootstrap, StringComparison.Ordinal);

                var sceneBuilder = Text($"{root}/Editor/Phase138MazeDemoSceneBuilder.cs");
                Assert.Contains("SetField(demoCameraPublisher, \"_manager\", manager);", sceneBuilder, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void MazeDemoBootstrapAndSceneBuilderUseTheSameSensorFieldOverrides()
        {
            var lidarFields = new[]
            {
                "_manager",
                "_sensorUnitProfile",
                "_frameId",
                "_pointCloudPublisher",
                "_columnStep",
                "_maxRaysPerScan",
                "_layerMask",
                "_maxRaycastCommandsPerFixedUpdate",
                "_publishEmptyFrames",
                "_drawDebugRays"
            };
            var imuFields = new[]
            {
                "_manager",
                "_rigidbody",
                "_frameId",
                "_topic",
                "_publishOnStart",
                "_includeOrientation",
                "_globalPhysicsRateHzOverride",
                "_enableNoise",
                "_accelNoiseStdDev",
                "_gyroNoiseStdDev"
            };

            foreach (var root in MazeDemoRoots)
            {
                var bootstrap = Text($"{root}/Phase138MazeDemoBootstrap.cs");
                var sceneBuilder = Text($"{root}/Editor/Phase138MazeDemoSceneBuilder.cs");

                foreach (var field in lidarFields)
                {
                    Assert.Contains($"SetPrivateField(lidar, \"{field}\"", bootstrap, StringComparison.Ordinal);
                    Assert.Contains($"SetField(lidar, \"{field}\"", sceneBuilder, StringComparison.Ordinal);
                }

                foreach (var field in imuFields)
                {
                    Assert.Contains($"SetPrivateField(imu, \"{field}\"", bootstrap, StringComparison.Ordinal);
                    Assert.Contains($"SetField(imu, \"{field}\"", sceneBuilder, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void MazeDemoReflectionHelpersFailClosedOnMissingFields()
        {
            foreach (var root in MazeDemoRoots)
            {
                foreach (var fileName in new[] { "Phase138MazeDemoBootstrap.cs", "Editor/Phase138MazeDemoSceneBuilder.cs" })
                {
                    var source = Text($"{root}/{fileName}");

                    Assert.Contains("throw new System.MissingFieldException", source, StringComparison.Ordinal);
                    Assert.DoesNotContain("Failed to set private field", source, StringComparison.Ordinal);
                }
            }
        }

        private static string Text(string relativePath)
        {
            return File.ReadAllText(Path(relativePath));
        }

        private static string Path(string relativePath)
        {
            return System.IO.Path.Combine(RepoRoot.Value, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "README.md"))
                    && Directory.Exists(System.IO.Path.Combine(dir.FullName, "Unity2Foxglove"))
                    && Directory.Exists(System.IO.Path.Combine(dir.FullName, "Packages")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
