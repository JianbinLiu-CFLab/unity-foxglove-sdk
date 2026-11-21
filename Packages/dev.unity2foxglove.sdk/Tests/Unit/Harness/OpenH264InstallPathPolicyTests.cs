// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: OpenH264 install-path policy behavior coverage.

using System;
using System.IO;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "81")]
    [Trait("Domain", "OpenH264")]
    public sealed class OpenH264InstallPathPolicyTests
    {
        [Fact]
        public void RejectsProjectProtectedAndEditorRootsWhileAllowingSiblingCache()
        {
            var root = Path.Combine(Path.GetTempPath(), "openh264-install-policy");
            var projectRoot = Path.Combine(root, "project");
            var editorRoot = Path.Combine(root, "editor");
            var protectedRoot = Path.Combine(root, "program-files");
            var protectedRoots = new[] { protectedRoot };

            foreach (var projectChild in new[] { "Assets", "Packages", "ProjectSettings", ".git" })
            {
                Assert.False(OpenH264InstallPathPolicy.IsAllowedInstallRoot(
                    Path.Combine(projectRoot, projectChild),
                    "v2.6.0",
                    projectRoot,
                    editorRoot,
                    protectedRoots,
                    StringComparison.OrdinalIgnoreCase,
                    out _));
            }

            Assert.False(OpenH264InstallPathPolicy.IsAllowedInstallRoot(
                protectedRoot,
                "v2.6.0",
                projectRoot,
                editorRoot,
                protectedRoots,
                StringComparison.OrdinalIgnoreCase,
                out _));
            Assert.False(OpenH264InstallPathPolicy.IsAllowedInstallRoot(
                editorRoot.ToUpperInvariant(),
                "v2.6.0",
                projectRoot,
                editorRoot,
                protectedRoots,
                StringComparison.OrdinalIgnoreCase,
                out _));
            Assert.True(OpenH264InstallPathPolicy.IsAllowedInstallRoot(
                root + "-cache",
                "v2.6.0",
                projectRoot,
                editorRoot,
                protectedRoots,
                StringComparison.OrdinalIgnoreCase,
                out _));
        }
    }
}
