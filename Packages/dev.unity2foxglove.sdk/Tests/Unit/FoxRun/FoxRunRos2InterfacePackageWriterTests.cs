// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxRunRos2InterfacePackageWriterTests
    {
        [Fact]
        public void RendererProducesDeterministicPayloadEnvelopeAndPortableRosPackageFiles()
        {
            var rendered = FoxRunRos2InterfacePackageRenderer.Render(BuildModel(typeof(Phase181State)));

            Assert.True(rendered.HasCustomContracts);
            Assert.Equal(FoxRunRos2InterfaceIdentity.DefaultRosPackageName, rendered.Lock.RosPackageName);
            Assert.Contains(rendered.Files, file => file.RelativePath == "package.json");
            Assert.Contains(rendered.Files, file => file.RelativePath == "Ros2Package~/package.xml");
            Assert.Contains(rendered.Files, file => file.RelativePath == "Ros2Package~/CMakeLists.txt");
            var payload = Assert.Single(rendered.Files, file => file.Text.Contains("int32 count", StringComparison.Ordinal));
            var envelope = Assert.Single(rendered.Files, file => file.RelativePath.EndsWith("Envelope.msg", StringComparison.Ordinal));
            Assert.Contains("int32 count", payload.Text, StringComparison.Ordinal);
            Assert.Contains("bool foxrun_has_message", payload.Text, StringComparison.Ordinal);
            Assert.Contains("builtin_interfaces/Time foxrun_stamp", envelope.Text, StringComparison.Ordinal);
            Assert.Contains("foxrun_origin_id", envelope.Text, StringComparison.Ordinal);
            Assert.Contains("rosidl_default_generators", rendered.GetText("Ros2Package~/CMakeLists.txt"), StringComparison.Ordinal);
            Assert.Contains("ament_cmake", rendered.GetText("Ros2Package~/package.xml"), StringComparison.Ordinal);
            Assert.Contains(
                "\"ros2CustomEnvelopeMessageName\":\"Phase181State48D288ED82F1Envelope\"",
                FoxRunGenerationDescriptorJsonWriter.Write(BuildModel(typeof(Phase181State))),
                StringComparison.Ordinal);
        }

        [Fact]
        public void RendererSharesOneEnvelopeFileAcrossContractsUsingTheSameDto()
        {
            var rendered = FoxRunRos2InterfacePackageRenderer.Render(BuildModelWithSharedDtoContracts(typeof(Phase181State)));
            var envelopePath = "Ros2Package~/msg/Phase181State48D288ED82F1Envelope.msg";

            Assert.Equal(3, rendered.Lock.Contracts.Count);
            Assert.Single(rendered.Files, file => string.Equals(file.RelativePath, envelopePath, StringComparison.Ordinal));
            Assert.Single(rendered.Lock.Contracts.Select(contract => contract.EnvelopeMessageName).Distinct(StringComparer.Ordinal));
            Assert.Single(rendered.Lock.Contracts.Select(contract => contract.EnvelopeDigest).Distinct(StringComparer.Ordinal));
        }

        [Fact]
        public void RendererAndCheckedInStaticPackageMatchTheExactGoldenUtf8LfFiles()
        {
            var rendered = FoxRunRos2InterfacePackageRenderer.Render(BuildModel(typeof(Phase181State)));
            var repoRoot = FindRepoRoot();
            AssertGoldenEquals(
                Path.Combine(repoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Unit", "FoxRun", "Fixtures", "Phase181InterfaceGolden"),
                rendered.Files);
            AssertGoldenEquals(
                Path.Combine(repoRoot, "Packages", FoxRunRos2InterfaceIdentity.UnityPackageId),
                rendered.Files);
        }

        [Fact]
        public void IdenticalGenerationPreservesExistingMetadataAndProducesNoDiff()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                var model = BuildModel(typeof(Phase181State));
                var first = FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, model);
                var retainedMeta = Path.Combine(packageRoot, "RuntimeSupport", "retained.asset.meta");
                Directory.CreateDirectory(Path.GetDirectoryName(retainedMeta));
                File.WriteAllText(retainedMeta, "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");
                var before = Snapshot(packageRoot);

                var second = FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, model);

                Assert.True(first.Changed);
                Assert.False(second.Changed);
                Assert.Equal(before, Snapshot(packageRoot));
                Assert.Contains("0123456789abcdef", File.ReadAllText(retainedMeta), StringComparison.Ordinal);
            });
        }

        [Fact]
        public void SchemaChangeRequiresExplicitMonotonicRevisionBeforeAnyReplacement()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildModel(typeof(Phase181State)));
                var before = Snapshot(packageRoot);

                Assert.Throws<FoxRunRos2InterfaceRevisionRequiredException>(() =>
                    FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildModel(typeof(Phase181StateV2))));
                Assert.Equal(before, Snapshot(packageRoot));

                var result = FoxRunRos2InterfacePackageWriter.Generate(
                    repoRoot,
                    packageRoot,
                    BuildModel(typeof(Phase181StateV2)),
                    nextRevision: "unity2foxglove_foxrun_interfaces_v2");
                Assert.True(result.Changed);
                Assert.Equal(2, result.Lock.InterfaceRevision);
            });
        }

        [Fact]
        public void InitialOperatorPackageNameIsFrozenAsTheOnlyAllowedRevisionStem()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                var first = FoxRunRos2InterfacePackageWriter.Generate(
                    repoRoot,
                    packageRoot,
                    BuildModel(typeof(Phase181State)),
                    nextRevision: "project_interfaces_v1");
                Assert.Equal("project_interfaces_v1", first.Lock.RosPackageName);

                Assert.Throws<FoxRunRos2InterfaceRevisionRequiredException>(() =>
                    FoxRunRos2InterfacePackageWriter.Generate(
                        repoRoot,
                        packageRoot,
                        BuildModel(typeof(Phase181StateV2)),
                        nextRevision: "other_interfaces_v2"));

                var second = FoxRunRos2InterfacePackageWriter.Generate(
                    repoRoot,
                    packageRoot,
                    BuildModel(typeof(Phase181StateV2)),
                    nextRevision: "project_interfaces_v2");
                Assert.Equal("project_interfaces_v2", second.Lock.RosPackageName);
            });
        }

        [Fact]
        public void InvalidAndMalformedInputsFailClosedWithoutChangingExistingPackage()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildModel(typeof(Phase181State)));
                var before = Snapshot(packageRoot);
                File.WriteAllText(
                    Path.Combine(packageRoot, "RuntimeSupport", "foxrun-ros2-interface-lock.json"),
                    "{ bad lock");
                var malformed = Snapshot(packageRoot);

                Assert.Throws<FoxRunRos2InterfaceInvalidLockException>(() =>
                    FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildModel(typeof(Phase181State))));
                Assert.Equal(malformed, Snapshot(packageRoot));

                Directory.Delete(packageRoot, recursive: true);
                Assert.Throws<FoxRunRos2InterfaceRenderException>(() =>
                    FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildInvalidModel()));
                Assert.False(Directory.Exists(packageRoot));
                Assert.NotEmpty(before);
            });
        }

        [Fact]
        public void CancellationAndInterruptionLeaveThePreviousPackageByteIdentical()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                var model = BuildModel(typeof(Phase181State));
                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, model);
                var before = Snapshot(packageRoot);

                Assert.Throws<OperationCanceledException>(() => FoxRunRos2InterfacePackageWriter.Generate(
                    repoRoot,
                    packageRoot,
                    BuildModel(typeof(Phase181StateV2)),
                    nextRevision: "unity2foxglove_foxrun_interfaces_v2",
                    isCancellationRequested: () => true));
                Assert.Equal(before, Snapshot(packageRoot));

                Assert.Throws<IOException>(() => FoxRunRos2InterfacePackageWriter.Generate(
                    repoRoot,
                    packageRoot,
                    BuildModel(typeof(Phase181StateV2)),
                    nextRevision: "unity2foxglove_foxrun_interfaces_v2",
                    beforeCommit: () => throw new IOException("simulated interruption")));
                Assert.Equal(before, Snapshot(packageRoot));
            });
        }

        private static FoxRunGenerationModel BuildModel(Type dtoType)
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(dtoType);
            return FoxRunGenerationModel.FromMembers(new[]
            {
                CreateMember(shape, dtoType, "State", "/phase181/custom_state", rawMemberOrder: 0)
            });
        }

        private static FoxRunGenerationModel BuildModelWithSharedDtoContracts(Type dtoType)
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(dtoType);
            return FoxRunGenerationModel.FromMembers(new[]
            {
                CreateMember(shape, dtoType, "NativeInput", "/phase181/native_input", rawMemberOrder: 0),
                CreateMember(shape, dtoType, "NativeOutput", "/phase181/native_output", rawMemberOrder: 1),
                CreateMember(shape, dtoType, "NativeInputWebSocketOutput", "/phase181/native_input_websocket_output", rawMemberOrder: 2)
            });
        }

        private static FoxRunGenerationMember CreateMember(
            FoxRunRos2CustomDtoShape shape,
            Type dtoType,
            string memberName,
            string topic,
            int rawMemberOrder)
        {
            return new FoxRunGenerationMember(
                typeof(Phase181CustomInterfaceFixture).Namespace,
                nameof(Phase181CustomInterfaceFixture),
                memberName,
                "property",
                dtoType.FullName,
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic: topic,
                rateHz: 10f,
                schemaName: string.Empty,
                publishMode: 0,
                changeEpsilon: 0f,
                forceIntervalSeconds: 0f,
                hostKind: "fixture",
                rawMemberOrder: rawMemberOrder,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunMode.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                ros2Qos: FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);
        }

        private static FoxRunGenerationModel BuildInvalidModel()
        {
            var invalidShape = new FoxRunRos2CustomDtoShape(
                "Fixture.Invalid",
                "invalid",
                "InvalidPayload",
                hasPublicParameterlessConstructor: true,
                isSupported: false,
                members: Array.Empty<FoxRunRos2CustomDtoMemberShape>(),
                diagnostics: new[] { "FOXRUN606|fixture" });
            var member = new FoxRunGenerationMember(
                "Fixture",
                "Invalid",
                "State",
                "property",
                "Fixture.Invalid",
                false,
                false,
                string.Empty,
                "/phase181/invalid",
                10f,
                string.Empty,
                0,
                0f,
                0f,
                "fixture",
                0,
                string.Empty,
                mode: (int)FoxRunMode.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                subscriptionProvider: FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                ros2Qos: FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                generatesRos2NativeRegistration: true,
                ros2CustomDtoShape: invalidShape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);
            return FoxRunGenerationModel.FromMembers(new[] { member });
        }

        private static IReadOnlyDictionary<string, byte[]> Snapshot(string root)
            => Directory.Exists(root)
                ? Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .ToDictionary(
                        path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                        File.ReadAllBytes,
                        StringComparer.Ordinal)
                : new Dictionary<string, byte[]>(StringComparer.Ordinal);

        private static void AssertGoldenEquals(string root, IReadOnlyList<FoxRunRos2InterfaceRenderedFile> files)
        {
            // Unity may generate importer sidecars for fixture/package files.
            // They are intentionally retained by the writer, but are outside
            // the source-only ROS interface payload rendered by this test.
            var expected = Snapshot(root)
                .Where(pair => !pair.Key.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var actual = files.ToDictionary(
                file => file.RelativePath,
                file => FoxRunRos2InterfaceDigest.EncodeText(file.Text),
                StringComparer.Ordinal);
            Assert.Equal(expected.Keys.OrderBy(value => value, StringComparer.Ordinal), actual.Keys.OrderBy(value => value, StringComparer.Ordinal));
            foreach (var pair in expected)
                Assert.Equal(pair.Value, actual[pair.Key]);
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ".gitattributes"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages", "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root for the Phase181 golden package.");
        }

        private static void WithTempPackage(Action<string, string> action)
        {
            var root = Path.Combine(
                FindRepoRoot(),
                "build",
                "Tests",
                "Phase181",
                "writer-" + Guid.NewGuid().ToString("N"));
            var packageRoot = Path.Combine(root, "Packages", FoxRunRos2InterfaceIdentity.UnityPackageId);
            try
            {
                action(root, packageRoot);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void TemporaryPackageFixtureStaysInsideTheRepositoryBuildRoot()
        {
            WithTempPackage((root, _) =>
            {
                var expectedRoot = Path.Combine(FindRepoRoot(), "build", "Tests", "Phase181");
                Assert.StartsWith(expectedRoot, root, StringComparison.OrdinalIgnoreCase);
            });
        }
    }
}
