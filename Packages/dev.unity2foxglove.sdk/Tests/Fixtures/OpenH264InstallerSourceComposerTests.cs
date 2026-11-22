// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Fixtures
// Purpose: Verify OpenH264 installer source composition boundaries.

using System;
using System.IO;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class OpenH264InstallerSourceComposerTests
    {
        private const string SourceRoot = "Packages/dev.unity2foxglove.sdk/Editor/Publishers";
        private const string FragmentRoot = SourceRoot + "/Decomposed/OpenH264OfficialBinaryInstaller";

        [Fact]
        public void ComposeUsesExplicitRepositoryRootForFacadeFallback()
        {
            var root = CreateFixture();
            try
            {
                Write(root, "OpenH264OfficialBinaryInstaller.cs", "facade");
                Assert.Equal("facade", OpenH264InstallerSourceComposer.Compose(root));
            }
            finally
            {
                DeleteFixture(root);
            }
        }

        [Fact]
        public void ComposeCombinesRegisteredFragmentsWithStableNewlines()
        {
            var root = CreateFixture();
            try
            {
                Write(root, "OpenH264OfficialBinaryInstaller.cs", "facade");
                Write(root, FragmentRoot, "OpenH264OfficialBinaryInstaller.Download.cs", "download");
                Write(root, FragmentRoot, "OpenH264OfficialBinaryInstaller.Helper.cs", "helper");

                Assert.Equal(
                    "facade\ndownload\nhelper",
                    OpenH264InstallerSourceComposer.Compose(root));
            }
            finally
            {
                DeleteFixture(root);
            }
        }

        [Fact]
        public void ComposeRejectsIncompleteFragmentSet()
        {
            var root = CreateFixture();
            try
            {
                Write(root, "OpenH264OfficialBinaryInstaller.cs", "facade");
                Write(root, FragmentRoot, "OpenH264OfficialBinaryInstaller.Download.cs", "download");

                Assert.Throws<InvalidDataException>(() => OpenH264InstallerSourceComposer.Compose(root));
            }
            finally
            {
                DeleteFixture(root);
            }
        }

        [Fact]
        public void ComposeRejectsUnexpectedFragment()
        {
            var root = CreateFixture();
            try
            {
                Write(root, "OpenH264OfficialBinaryInstaller.cs", "facade");
                Write(root, FragmentRoot, "OpenH264OfficialBinaryInstaller.Download.cs", "download");
                Write(root, FragmentRoot, "OpenH264OfficialBinaryInstaller.Helper.cs", "helper");
                Write(root, FragmentRoot, "OpenH264OfficialBinaryInstaller.Cleanup.cs", "cleanup");

                Assert.Throws<InvalidDataException>(() => OpenH264InstallerSourceComposer.Compose(root));
            }
            finally
            {
                DeleteFixture(root);
            }
        }

        private static string CreateFixture()
        {
            var root = Path.Combine(
                PhaseValidationSourceHelpers.FindRequiredRepoRoot(),
                "build",
                "tmp",
                "phase207-openh264-composer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, SourceRoot));
            return root;
        }

        private static void Write(string root, string fileName, string contents)
            => Write(root, SourceRoot, fileName, contents);

        private static void Write(string root, string relativeDirectory, string fileName, string contents)
        {
            var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), contents);
        }

        private static void DeleteFixture(string root)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
