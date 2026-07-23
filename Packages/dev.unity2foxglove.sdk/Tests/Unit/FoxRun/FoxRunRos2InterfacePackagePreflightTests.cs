// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxRunRos2InterfacePackagePreflightTests
    {
        [Fact]
        public void NoCustomContractIsExplicitlyNotRequired()
        {
            var result = FoxRunRos2InterfacePackagePreflight.Evaluate(
                Path.Combine(
                    FindRepoRoot(),
                    "build",
                    "Tests",
                    "Phase181",
                    "preflight-missing-" + Guid.NewGuid().ToString("N")),
                FoxRunGenerationModel.FromMembers(Array.Empty<FoxRunGenerationMember>()));

            Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.NotRequired, result.State);
            Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.NoCustomContracts, result.DiagnosticCode);
            Assert.True(result.IsReady);
        }

        [Fact]
        public void MissingSourceAndMissingLockFailWithTypedActions()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                var missing = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, BuildModel(typeof(Phase181State)));
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.MissingSource, missing.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourcePackageMissing, missing.DiagnosticCode);
                Assert.NotEmpty(missing.Contracts);

                Directory.CreateDirectory(packageRoot);
                var missingLock = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, BuildModel(typeof(Phase181State)));
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.InvalidSource, missingLock.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceLockMissing, missingLock.DiagnosticCode);
            });
        }

        [Fact]
        public void LockedCurrentSourceIsReadyForSeparateTypesupportBuild()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                var model = BuildModel(typeof(Phase181State));
                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, model);

                var result = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, model);

                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.ReadyForBuild, result.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.None, result.DiagnosticCode);
                Assert.True(result.IsReady);
                Assert.Equal(FoxRunRos2InterfaceIdentity.DefaultRosPackageName, result.RosPackageName);
                Assert.Equal(12, result.ShortDigest.Length);
            });
        }

        [Fact]
        public void CheckedInStaticSourcePackagePassesTheSamePreflightAsACleanCheckout()
        {
            var repoRoot = FindRepoRoot();
            var packageRoot = Path.Combine(repoRoot, "Packages", FoxRunRos2InterfaceIdentity.UnityPackageId);

            var result = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, BuildModel(typeof(Phase181State)));

            Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.ReadyForBuild, result.State);
            Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.None, result.DiagnosticCode);
        }

        [Fact]
        public void AdditionalContractsReusingTheLockedDtoDoNotInvalidateStaticSourceFiles()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildModel(typeof(Phase181State)));

                var result = FoxRunRos2InterfacePackagePreflight.Evaluate(
                    packageRoot,
                    BuildModelWithSharedDtoContracts(typeof(Phase181State)));

                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.ReadyForBuild, result.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.None, result.DiagnosticCode);
            });
        }

        [Fact]
        public void MissingMessageAndTamperedDigestAreDistinguished()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                var model = BuildModel(typeof(Phase181State));
                var generated = FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, model);
                var payload = Path.Combine(packageRoot, "Ros2Package~", "msg", generated.Lock.Contracts[0].PayloadMessageName + ".msg");
                File.Delete(payload);

                var missing = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, model);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.StaleSource, missing.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceFileMissing, missing.DiagnosticCode);

                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, model);
                File.AppendAllText(payload, "# accidental edit\n");
                var tampered = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, model);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.InvalidSource, tampered.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.SourceLockInvalid, tampered.DiagnosticCode);
            });
        }

        [Fact]
        public void SchemaChangeRequiresAnExplicitRevisionRatherThanStaleOverwrite()
        {
            WithTempPackage((repoRoot, packageRoot) =>
            {
                FoxRunRos2InterfacePackageWriter.Generate(repoRoot, packageRoot, BuildModel(typeof(Phase181State)));

                var result = FoxRunRos2InterfacePackagePreflight.Evaluate(packageRoot, BuildModel(typeof(Phase181StateV2)));

                Assert.Equal(FoxRunRos2InterfaceSourcePreflightState.RevisionRequired, result.State);
                Assert.Equal(FoxRunRos2InterfaceSourcePreflightDiagnosticCode.LockedSchemaChanged, result.DiagnosticCode);
                Assert.Contains("_vN", result.Action, StringComparison.Ordinal);
            });
        }

        private static FoxRunGenerationModel BuildModel(Type dtoType)
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(dtoType);
            return FoxRunGenerationModel.FromMembers(new[]
            {
                CreateMember(shape, "State", "/phase181/custom_state", 0)
            });
        }

        private static FoxRunGenerationModel BuildModelWithSharedDtoContracts(Type dtoType)
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(dtoType);
            return FoxRunGenerationModel.FromMembers(new[]
            {
                CreateMember(shape, "NativePublish", "/phase181/custom/publish", 0),
                CreateMember(shape, "NativeSubscribe", "/phase181/custom/subscribe", 1),
                CreateMember(shape, "NativeBidirectional", "/phase181/custom/bidirectional", 2),
            });
        }

        private static FoxRunGenerationMember CreateMember(
            FoxRunRos2CustomDtoShape shape,
            string memberName,
            string topic,
            int rawMemberOrder)
            => new FoxRunGenerationMember(
                typeof(Phase181CustomInterfaceFixture).Namespace,
                nameof(Phase181CustomInterfaceFixture),
                memberName,
                "property",
                shape.FullyQualifiedTypeName,
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic,
                hz: 10f,
                schemaName: string.Empty,
                policy: 0,
                tolerance: 0f,
                hostKind: "fixture",
                rawMemberOrder,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                ros2Qos: FoxRunGenerationDescriptorConstants.ReliableRos2Qos,
                generatesWebSocketCodec: true,
                generatesRos2NativeRegistration: true,
                ros2CustomDtoShape: shape,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);

        private static void WithTempPackage(Action<string, string> action)
        {
            var root = Path.Combine(
                FindRepoRoot(),
                "build",
                "Tests",
                "Phase181",
                "preflight-" + Guid.NewGuid().ToString("N"));
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

            throw new DirectoryNotFoundException("Could not locate the repository root for the Phase181 source-package preflight.");
        }
    }
}
