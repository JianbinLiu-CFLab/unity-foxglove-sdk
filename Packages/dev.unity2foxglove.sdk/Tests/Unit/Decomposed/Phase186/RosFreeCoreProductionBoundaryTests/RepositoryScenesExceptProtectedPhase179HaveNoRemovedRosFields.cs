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


public sealed partial class RosFreeCoreProductionBoundaryTests
    {
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
