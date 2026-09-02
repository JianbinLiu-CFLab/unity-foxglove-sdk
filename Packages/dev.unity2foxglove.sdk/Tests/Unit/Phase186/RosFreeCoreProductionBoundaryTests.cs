// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Phase186
// Purpose: Enforces the Phase186 ROS-free SDK production boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Phase186
{
    [Trait("Phase", "186A")]
    [Trait("Domain", "Architecture")]
    public sealed class RosFreeCoreProductionBoundaryTests
    {
        private static readonly string[] ProductionRoots =
        {
            "Packages/dev.unity2foxglove.sdk/Runtime",
            "Packages/dev.unity2foxglove.sdk/Editor"
        };

        private static readonly string[] CheckedInGeneratedProductionFiles =
        {
            "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs"
        };

        private static readonly string[] TextExtensions =
        {
            ".asmdef",
            ".c",
            ".cc",
            ".cs",
            ".csproj",
            ".cpp",
            ".cxx",
            ".h",
            ".hpp",
            ".json",
            ".props",
            ".proto",
            ".targets",
            ".xml"
        };

        private static readonly Regex ForbiddenSymbol = new Regex(
            @"(?:"
            + @"Ros2[A-Z_]|"
            + @"\bros2\b|"
            + @"ROS\s*2|"
            + @"\bR2FU\b|"
            + @"\brclcpp\b|"
            + @"\brmw_|"
            + @"\bU2R2\b|"
            + @"Schemas[./\\]Ros2Msg|"
            + @"\bCdr[A-Z_]|"
            + @"(?<![A-Za-z0-9_])cdr(?![A-Za-z0-9_])|"
            + @"(?:sensor|foxglove)_msgs/msg/|"
            + @"PointCloud2Native"
            + @")",
            RegexOptions.CultureInvariant);

        [Fact]
        public void SdkRuntimeEditorAndGeneratorsOwnNoRosTransportConcepts()
        {
            var violations = new List<string>();
            foreach (var file in EnumerateProductionFiles())
            {
                var relative = Relative(file);
                if (ContainsForbiddenPathSegment(relative))
                {
                    violations.Add(relative + ": forbidden production path");
                    continue;
                }

                if (!TextExtensions.Contains(
                        Path.GetExtension(file),
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = StripSerializationMigrationAttributes(
                    File.ReadAllText(file));
                var match = ForbiddenSymbol.Match(source);
                if (match.Success)
                {
                    var line = 1 + source.Take(match.Index).Count(c => c == '\n');
                    violations.Add(
                        relative
                        + ":"
                        + line
                        + ": forbidden symbol '"
                        + match.Value
                        + "'");
                }
            }

            Assert.True(
                violations.Count == 0,
                "SDK production boundary still owns ROS transport concepts:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void CheckedInGeneratedCoreSourceIsPartOfTheProductionScan()
        {
            const string generatedSource =
                "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs";
            var scanned = EnumerateProductionFiles()
                .Select(Relative)
                .ToArray();

            Assert.Equal(
                new[] { generatedSource },
                CheckedInGeneratedProductionFiles);
            Assert.Contains(generatedSource, scanned);
            Assert.True(File.Exists(PathOf(generatedSource)));
        }

        [Fact]
        public void SdkReadmeRoutesRosTransportsToCompanionPackages()
        {
            var readme = File.ReadAllText(
                PathOf("Packages/dev.unity2foxglove.sdk/README.md"));

            Assert.Contains(
                "`dev.unity2foxglove.ros2bridge`",
                readme,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "already covers"
                + Environment.NewLine
                + "Foxglove WebSocket streaming, FoxRun over WebSocket, MCAP recording/replay,"
                + Environment.NewLine
                + "sensors, services, and the optional ROS2 Bridge sidecar",
                readme,
                StringComparison.Ordinal);
        }

        [Fact]
        public void MaintainedSamplesDoNotReflectRemovedRosFields()
        {
            var paths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs",
                "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs"
            };
            foreach (var path in paths)
            {
                var source = File.ReadAllText(PathOf(path));
                Assert.DoesNotContain(
                    "_publishStandardRos2CompressedImage",
                    source,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "_publishStandardRos2RawImage",
                    source,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RenamedPointCloudFieldsPreserveSerializedUserData()
        {
            var source = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs"));
            var migrations = new Dictionary<string, string>
            {
                ["_publishPackedPointCloudTfAnchor"] =
                    "_publishPointCloud2NativeTfAnchor",
                ["_packedPointCloudTfParentFrame"] =
                    "_pointCloud2NativeTfParentFrame",
                ["_packedPointCloudTfChildFrame"] =
                    "_pointCloud2NativeTfChildFrame",
                ["_packedPointCloudTfTranslation"] =
                    "_pointCloud2NativeTfTranslation",
                ["_packedPointCloudTfRotationEuler"] =
                    "_pointCloud2NativeTfRotationEuler",
                ["_deskewedPackedPointCloudTopic"] =
                    "_deskewedPointCloud2NativeTopic",
                ["_deskewedPackedPointCloudMaxPublishRateHz"] =
                    "_deskewedPointCloud2NativeMaxPublishRateHz"
            };

            foreach (var migration in migrations)
            {
                Assert.Matches(
                    @"\[FormerlySerializedAs\("""
                    + Regex.Escape(migration.Value)
                    + @"""\)\]\s*\r?\n\s*\[[^\]]*SerializeField[^\]]*\]"
                    + @"[^\r\n]*\b"
                    + Regex.Escape(migration.Key)
                    + @"\b",
                    source);
            }
        }

        [Fact]
        public void ManagerLifecycleCannotBypassFailedTransportCapture()
        {
            var server = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs"));
            var subscriptions = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs"));
            var providers = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs"));

            Assert.Matches(
                @"public void StartServer\(\)\s*\{\s*"
                + @"(?:if \(!IsRunning && HasRetainedRuntimeForwarders\(\)\)\s*"
                + @"throw new InvalidOperationException\([\s\S]*?\);\s*)?"
                + @"if \(!BeginFoxRunTransportSessionIfNeeded\(\)\)\s*"
                + @"\{\s*_startServerAfterTransportCapture = true;\s*"
                + @"return;\s*\}",
                server);
            Assert.Matches(
                @"private void SyncFoxRunSubscriptionSession\(\)\s*\{\s*"
                + @"if \(_activeFoxRunTransportSession == null\s*"
                + @"&& !BeginFoxRunTransportSessionIfNeeded\(\)\)\s*"
                + @"\{\s*EndFoxRunSubscriptionSession\(\);\s*return;\s*\}",
                subscriptions);
            Assert.Contains(
                "if (!components[i].isActiveAndEnabled)",
                providers,
                StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerSelectionUsesLoadedSceneDeclarationsNotGlobalManifest()
        {
            var providers = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs"));
            var schemaRegistry = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs"));
            var loadedSceneProbe = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunLoadedSceneContractProbe.cs"));

            Assert.DoesNotContain(
                "GetExplicitPublishTransportIds",
                providers,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "GetExplicitPublishTransportIds",
                schemaRegistry,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunLoadedSceneContractProbe.CaptureLoadedScenes()",
                providers,
                StringComparison.Ordinal);
            Assert.Contains(
                "loaded.ExplicitPublishTransportIds",
                providers,
                StringComparison.Ordinal);
            Assert.Contains(
                "internal IEnumerable<string> ExplicitPublishTransportIds",
                loadedSceneProbe,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FoxRunSchemaInfoRegistry",
                providers);
        }

        [Fact]
        public void R2fuProviderDoesNotRegisterWhileDisabled()
        {
            var source = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs"));
            var activeGuard = source.IndexOf(
                "if (!isActiveAndEnabled)",
                StringComparison.Ordinal);
            var registration = source.IndexOf(
                "Interlocked.Exchange(ref _registered, 1)",
                StringComparison.Ordinal);

            Assert.True(activeGuard >= 0);
            Assert.True(registration > activeGuard);
        }

        [Fact]
        public void Phase162SetupUsesR2fuProviderInsteadOfRemovedManagerField()
        {
            var source = File.ReadAllText(
                PathOf("Unity2Foxglove/Assets/Editor/Phase162LocalZenohPlaySetup.cs"));

            Assert.Contains(
                "AddComponent<FoxRunRos2TransportProvider>()",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxRunRos2TransportProvider.IdValue",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"_ros2NativeEnabled\"",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Phase162SetupRestoresProcessEnvironmentWhenConfigurationFails()
        {
            var source = File.ReadAllText(
                PathOf("Unity2Foxglove/Assets/Editor/Phase162LocalZenohPlaySetup.cs"));
            var configure = Harness.TestSources.ExtractMethod(
                source,
                "private static void TryConfigureAndPlay()");

            Assert.Contains("catch", configure, StringComparison.Ordinal);
            Assert.Contains(
                "RestoreEnvironmentAfterOverride();",
                configure,
                StringComparison.Ordinal);
            Assert.Contains("throw;", configure, StringComparison.Ordinal);
        }

        [Fact]
        public void BridgeSampleUsesProviderSerialization()
        {
            var scene = File.ReadAllText(
                PathOf(
                    "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity"));

            Assert.Contains(
                "guid: 6e1c973bf0174cf5ae0ec73f69d8a242",
                scene,
                StringComparison.Ordinal);
            Assert.Matches(
                @"(?m)^  _foxRunPublishTransportIds:\r?\n"
                + @"  - unity2foxglove\.ros2bridge$",
                scene);

            const string controllerType =
                "m_EditorClassIdentifier: Assembly-CSharp::Ros2BridgeSampleController";
            var controllerStart = scene.IndexOf(controllerType, StringComparison.Ordinal);
            Assert.True(controllerStart >= 0, "Bridge sample controller was not serialized.");
            var controllerEnd = scene.IndexOf("--- !u!", controllerStart, StringComparison.Ordinal);
            Assert.True(controllerEnd > controllerStart, "Bridge sample controller block was not terminated.");
            var controllerBlock = scene.Substring(controllerStart, controllerEnd - controllerStart);
            Assert.Contains("_provider: {fileID:", controllerBlock, StringComparison.Ordinal);
            Assert.DoesNotContain("_manager: {fileID:", controllerBlock, StringComparison.Ordinal);
        }

        [Fact]
        public void RepositoryScenesExceptProtectedPhase179HaveNoRemovedRosFields()
        {
            var legacyField = new Regex(
                @"(?m)^\s+_(?:"
                + @"ros2NativeEnabled|"
                + @"ros2BridgeEnabled|"
                + @"ros2BridgeHost|"
                + @"ros2BridgePort|"
                + @"ros2BridgeNamespace|"
                + @"defaultRos2BridgeOutputEnabled|"
                + @"ros2BridgeOutput|"
                + @"ros2BridgeTopicOverride|"
                + @"publishStandardRos2CompressedImage|"
                + @"publishStandardRos2RawImage"
                + @"):",
                RegexOptions.CultureInvariant);
            const string protectedScene =
                "Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity";
            var violations = new List<string>();
            foreach (var root in new[] { "Packages", "Unity2Foxglove/Assets" })
            {
                foreach (var file in Directory.EnumerateFiles(
                             PathOf(root),
                             "*.unity",
                             SearchOption.AllDirectories))
                {
                    var relative = Relative(file);
                    if (string.Equals(
                            relative,
                            protectedScene,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var match = legacyField.Match(File.ReadAllText(file));
                    if (match.Success)
                        violations.Add(relative + ": " + match.Value.Trim());
                }
            }

            Assert.True(
                violations.Count == 0,
                "Repository scenes still serialize removed ROS fields:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void MaintainedFoxRunGuidesUseProviderContracts()
        {
            var paths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md",
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun Custom ROS2 Interface/README.md",
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/README.md"
            };
            foreach (var path in paths)
            {
                var source = File.ReadAllText(PathOf(path));
                Assert.DoesNotContain(
                    "FoxRunEndpoint",
                    source,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "Source =",
                    source,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "Targets =",
                    source,
                    StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CoreGuidesDoNotClaimRosTransportOwnership()
        {
            var paths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Documentation~/README.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/03_Samples_and_Demo_Project.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/10_Architecture.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/11_Troubleshooting.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/12_Inspector_Reference.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/14_Typed_Sensor_Publishers.md"
            };
            var forbiddenClaims = new[]
            {
                "Runtime/Schemas/Ros2Msg",
                "PublisherEffectiveEncoding.Ros2",
                "Publisher Encoding` to `ROS2`",
                "Inspector `ROS2` encoding option",
                "SDK registers the official Foxglove ROS 2",
                "with ROS2 Bridge enabled",
                "(en/16_ROS2_Bridge_Sample.md)"
            };

            foreach (var path in paths)
            {
                var source = File.ReadAllText(PathOf(path));
                foreach (var forbiddenClaim in forbiddenClaims)
                {
                    Assert.DoesNotContain(
                        forbiddenClaim,
                        source,
                        StringComparison.Ordinal);
                }
            }
        }

        private static bool ContainsForbiddenPathSegment(string relative)
            => relative.IndexOf("Ros2", StringComparison.OrdinalIgnoreCase) >= 0
               || relative.IndexOf("R2fu", StringComparison.OrdinalIgnoreCase) >= 0
               || relative.IndexOf("Cdr", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string StripSerializationMigrationAttributes(
            string source)
            => Regex.Replace(
                source,
                @"\[FormerlySerializedAs\(""[^""]+""\)\]",
                string.Empty,
                RegexOptions.CultureInvariant);

        private static IEnumerable<string> EnumerateProductionFiles()
        {
            var files = ProductionRoots
                .SelectMany(relativeRoot =>
                    Directory.EnumerateFiles(
                        PathOf(relativeRoot),
                        "*",
                        SearchOption.AllDirectories))
                .Concat(
                    CheckedInGeneratedProductionFiles.Select(PathOf));
            return files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal);
        }

        private static string PathOf(string relative)
            => Path.Combine(
                RepoRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));

        private static string Relative(string absolute)
            => Path.GetRelativePath(RepoRoot, absolute)
                .Replace(Path.DirectorySeparatorChar, '/');

        private static string RepoRoot
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                        || File.Exists(Path.Combine(directory.FullName, ".git")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }

                throw new DirectoryNotFoundException(
                    "Could not locate repository root from "
                    + AppContext.BaseDirectory);
            }
        }
    }
}
