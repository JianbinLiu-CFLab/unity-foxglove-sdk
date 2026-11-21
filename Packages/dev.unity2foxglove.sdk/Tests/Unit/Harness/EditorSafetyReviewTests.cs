// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Keep reviewed Editor process and path boundaries fail-closed.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Editor safety")]
    public sealed class EditorSafetyReviewTests
    {
        [Fact]
        public void TimedOutOpenH264ProbeWaitsForKilledProcessBeforeDrainingPipes()
        {
            var method = Method(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs",
                "Check");
            var source = method.ToFullString();
            var kill = source.IndexOf("TryKill(process);", StringComparison.Ordinal);
            var wait = source.IndexOf("process.WaitForExit(500);", StringComparison.Ordinal);
            var drain = source.IndexOf("WaitForStreamDrain(stdoutTask, stderrTask, 500)", StringComparison.Ordinal);

            Assert.True(kill >= 0);
            Assert.True(wait > kill);
            Assert.True(drain > wait);
        }

        [Fact]
        public void OpenH264PairPublisherRestoresExistingPairWhenHelperPublishFails()
        {
            var directory = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-pair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var finalDll = Path.Combine(directory, "openh264.dll");
            var finalHelper = Path.Combine(directory, "openh264-helper.exe");
            var stagedDll = finalDll + ".tmp";
            var stagedHelper = finalHelper + ".tmp";
            File.WriteAllText(finalDll, "old-dll");
            File.WriteAllText(finalHelper, "old-helper");
            File.WriteAllText(stagedDll, "new-dll");
            File.WriteAllText(stagedHelper, "new-helper");

            try
            {
                Action<string, string> move = (source, destination) =>
                {
                    if (string.Equals(source, stagedHelper, StringComparison.Ordinal))
                        throw new IOException("simulated helper publish failure");
                    File.Move(source, destination);
                };

                Assert.Throws<IOException>(() =>
                    OpenH264ArtifactPairPublisher.Publish(
                        stagedDll,
                        stagedHelper,
                        finalDll,
                        finalHelper,
                        move));

                Assert.Equal("old-dll", File.ReadAllText(finalDll));
                Assert.Equal("old-helper", File.ReadAllText(finalHelper));
                Assert.True(File.Exists(stagedHelper));
                Assert.False(File.Exists(finalDll + ".bak"));
                Assert.False(File.Exists(finalHelper + ".bak"));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void OpenH264PairPublisherStopsWhenDestinationValidationFails()
        {
            var directory = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-pair-validation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var finalDll = Path.Combine(directory, "openh264.dll");
            var finalHelper = Path.Combine(directory, "openh264-helper.exe");
            var stagedDll = finalDll + ".tmp";
            var stagedHelper = finalHelper + ".tmp";
            File.WriteAllText(stagedDll, "new-dll");
            File.WriteAllText(stagedHelper, "new-helper");

            try
            {
                Assert.Throws<IOException>(() =>
                    OpenH264ArtifactPairPublisher.Publish(
                        stagedDll,
                        stagedHelper,
                        finalDll,
                        finalHelper,
                        validateDestination: () => false));

                Assert.True(File.Exists(stagedDll));
                Assert.True(File.Exists(stagedHelper));
                Assert.False(File.Exists(finalDll));
                Assert.False(File.Exists(finalHelper));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void OpenH264PairPublisherDoesNotRollbackThroughAnInvalidatedDestination()
        {
            var directory = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-pair-invalidation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var finalDll = Path.Combine(directory, "openh264.dll");
            var finalHelper = Path.Combine(directory, "openh264-helper.exe");
            var stagedDll = finalDll + ".tmp";
            var stagedHelper = finalHelper + ".tmp";
            File.WriteAllText(finalDll, "old-dll");
            File.WriteAllText(finalHelper, "old-helper");
            File.WriteAllText(stagedDll, "new-dll");
            File.WriteAllText(stagedHelper, "new-helper");
            var validationCalls = 0;
            var invalidated = false;
            var operationsAfterInvalidation = 0;

            try
            {
                Action<string, string> move = (source, destination) =>
                {
                    if (invalidated)
                        operationsAfterInvalidation++;
                    File.Move(source, destination);
                };
                Action<string> delete = path =>
                {
                    if (invalidated)
                        operationsAfterInvalidation++;
                    File.Delete(path);
                };
                Func<bool> validate = () =>
                {
                    if (++validationCalls == 7)
                        invalidated = true;
                    return !invalidated;
                };

                Assert.Throws<IOException>(() =>
                    OpenH264ArtifactPairPublisher.Publish(
                        stagedDll,
                        stagedHelper,
                        finalDll,
                        finalHelper,
                        move,
                        delete,
                        validate));

                Assert.True(invalidated);
                Assert.Equal(0, operationsAfterInvalidation);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void OpenH264BatchPathsEscapePercentExpansion()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");

            Assert.Contains("Replace(\"%\", \"%%\")", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SchemaEvidenceContainmentUsesHostFilesystemCaseRules()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var sameOrChild = MethodFromSource(source, "IsSameOrChildPath").ToFullString();
            var equal = MethodFromSource(source, "PathsEqual").ToFullString();

            Assert.Contains("PathComparison", source, StringComparison.Ordinal);
            Assert.Contains("PathComparison", sameOrChild, StringComparison.Ordinal);
            Assert.Contains("PathComparison", equal, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax Method(string path, string name)
            => MethodFromSource(TestSources.Text(path), name);

        private static MethodDeclarationSyntax MethodFromSource(string source, string name)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Identifier.ValueText == name && method.Body != null)
                    return method;
            }

            throw new InvalidOperationException("Method not found: " + name);
        }
    }
}
