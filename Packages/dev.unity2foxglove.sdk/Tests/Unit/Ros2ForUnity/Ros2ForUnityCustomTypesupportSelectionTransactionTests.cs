// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Prove the Phase181 custom typesupport manifest transaction is atomic and fail-closed.

using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2ForUnity.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-C")]
    [Trait("Domain", "CustomTypesupportSelection")]
    public sealed partial class Ros2ForUnityCustomTypesupportSelectionTransactionTests
    {
        [Fact]
        public void ActionResolveOverloadReportsPendingInsteadOfSynchronousCompletion()
        {
            using var fixture = new SelectionFixture();
            fixture.WriteManifest(fixture.HumbleRuntimePackage);
            var resolveCalls = 0;
            Action resolve = () => resolveCalls++;

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                requestedAddOnPackage: null,
                resolve);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.ResolvePending, result.Code);
            Assert.Equal(1, resolveCalls);
        }

        [Fact]
        public void RestoreManifestRestoresTheExactOriginalText()
        {
            using var fixture = new SelectionFixture();
            fixture.WriteManifest(fixture.JazzyRuntimePackage);
            var original = File.ReadAllText(fixture.ManifestPath);
            fixture.WriteManifest(fixture.HumbleRuntimePackage);

            Ros2ForUnityCustomTypesupportSelectionTransaction.RestoreManifest(
                fixture.ProjectDirectory,
                original);

            Assert.Equal(original, File.ReadAllText(fixture.ManifestPath));
        }

        [Fact]
        public void BaseOnlyTransactionRemovesStaleCustomAddOnAndResolvesOnce()
        {
            using var fixture = new SelectionFixture();
            fixture.WriteAddOn("humble", valid: false);
            fixture.WriteManifest(
                fixture.HumbleRuntimePackage,
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64");
            var resolveCalls = 0;

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                requestedAddOnPackage: null,
                resolveAndConfirm: () => { resolveCalls++; return true; });

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly, result.Code);
            Assert.Equal(1, resolveCalls);
            Assert.Equal(new[] { fixture.HumbleRuntimePackage }, fixture.ManifestDependencyNames());
        }

        [Fact]
        public void EmptyDependenciesObjectAcceptsTheFirstRuntimeDependency()
        {
            using var fixture = new SelectionFixture();
            fixture.WriteEmptyManifest();
            var resolveCalls = 0;

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                requestedAddOnPackage: null,
                resolveAndConfirm: () => { resolveCalls++; return true; });

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly, result.Code);
            Assert.Equal(1, resolveCalls);
            Assert.Equal(new[] { fixture.HumbleRuntimePackage }, fixture.ManifestDependencyNames());
        }

        [Fact]
        public void FixtureScratchStaysInsideTheRepositoryBuildRoot()
        {
            using var fixture = new SelectionFixture();

            Assert.StartsWith(
                RepositoryBuildTestRoot() + Path.DirectorySeparatorChar,
                fixture.ScratchDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExactMatchingAddOnIsActivatedWithItsBaseRuntimeOnly()
        {
            using var fixture = new SelectionFixture();
            var addOn = fixture.WriteAddOn("humble", valid: true);
            fixture.WriteManifest(fixture.JazzyRuntimePackage);
            fixture.WritePackagesLock("Unity generated lock fixture");
            var originalLock = File.ReadAllText(fixture.PackagesLockPath);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                addOn,
                resolveAndConfirm: () => true);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.Ready, result.Code);
            Assert.Equal(addOn, result.ActiveAddOnPackage);
            Assert.Equal(
                new[] { fixture.StaticInterfacePackage, addOn, fixture.HumbleRuntimePackage },
                fixture.ManifestDependencyNames());
            Assert.Equal(originalLock, File.ReadAllText(fixture.PackagesLockPath));
        }

        [Fact]
        public void UnityLikeReadWriteHandleDoesNotInvalidateMatchingAddOn()
        {
            using var fixture = new SelectionFixture();
            var addOn = fixture.WriteAddOn("humble", valid: true);
            fixture.WriteManifest(fixture.HumbleRuntimePackage);

            // Unity may keep its loaded managed plugin open for read/write while
            // allowing readers. Selection must still verify its MVID and SHA-256
            // rather than converting that normal Editor state into a false stale
            // add-on result.
            using var unityLikeHandle = fixture.OpenRos2csCommonWithUnityLikeSharing(fixture.HumbleRuntimePackage);
            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                addOn,
                resolveAndConfirm: () => true);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.Ready, result.Code);
            Assert.Equal(addOn, result.ActiveAddOnPackage);
        }

        [Fact]
        public void ActiveManifestAddOnIsReevaluatedBeforeNativeSessionBinding()
        {
            using var fixture = new SelectionFixture();
            var addOn = fixture.WriteAddOn("humble", valid: true);
            fixture.WriteManifest(fixture.HumbleRuntimePackage, addOn);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.Ready, result.Code);
            Assert.Equal(addOn, result.ActiveAddOnPackage);
            Assert.NotEmpty(result.InterfaceDigest);
            Assert.NotEmpty(result.BaseRuntimeAbiDigest);
            Assert.EndsWith(
                "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
                result.NativePluginDirectory.Replace('\\', '/'));
        }

        [Fact]
        public void RevisionedStaticSourceLockSelectsItsMatchingV2AddOn()
        {
            using var fixture = new SelectionFixture();
            fixture.SetStaticSourceIdentity("unity2foxglove_foxrun_interfaces_v2", 2);
            var addOn = fixture.WriteAddOn("humble", valid: true);
            fixture.WriteManifest(fixture.HumbleRuntimePackage, addOn);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.Ready, result.Code);
            Assert.Equal(addOn, result.ActiveAddOnPackage);
        }

        [Fact]
        public void StaleRos2csCommonMvidFailsClosedBeforeNativeSessionBinding()
        {
            using var fixture = new SelectionFixture();
            var addOn = fixture.WriteAddOn("humble", valid: true);
            fixture.ReplaceRos2csMvid(addOn, Guid.NewGuid().ToString("D"));
            fixture.WriteManifest(fixture.HumbleRuntimePackage, addOn);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.EvaluateActive(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.RequestedCandidateNotReady, result.Code);
            Assert.Equal(
                Ros2ForUnityCustomTypesupportCandidateValidationCode.ManagedIdentity,
                result.CandidateValidationCode);
        }

        [Fact]
        public void InvalidOrAmbiguousCandidateFailsClosedToBaseOnly()
        {
            using var fixture = new SelectionFixture();
            fixture.WriteAddOn("humble", valid: true);
            fixture.WriteAddOn("humble-copy", valid: true, baseRuntime: fixture.HumbleRuntimePackage);
            fixture.WriteManifest(fixture.HumbleRuntimePackage);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                requestedAddOnPackage: null,
                resolveAndConfirm: () => true);

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly, result.Code);
            Assert.Equal(new[] { fixture.HumbleRuntimePackage }, fixture.ManifestDependencyNames());
        }

        [Fact]
        public void ResolveFailureRestoresTheOriginalManifestAtomically()
        {
            using var fixture = new SelectionFixture();
            var addOn = fixture.WriteAddOn("humble", valid: true);
            fixture.WriteManifest(fixture.JazzyRuntimePackage);
            var original = File.ReadAllText(fixture.ManifestPath);

            var result = Ros2ForUnityCustomTypesupportSelectionTransaction.Apply(
                fixture.ProjectDirectory,
                fixture.HumbleRuntimePackage,
                addOn,
                resolveAndConfirm: () => throw new InvalidOperationException("test resolve failure"));

            Assert.Equal(Ros2ForUnityCustomTypesupportSelectionCode.ResolveFailed, result.Code);
            Assert.Equal(original, File.ReadAllText(fixture.ManifestPath));
        }

        [Fact]
        public void LongWindowsPluginPathUsesExtendedReadFormOnlyAtTheVerificationSeam()
        {
            var ordinaryPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                new string('a', 260),
                "typesupport.dll");
            var normalized = Ros2ForUnityCustomTypesupportSelectionTransaction
                .NormalizeWindowsLongPathForRead(ordinaryPath);

            if (Path.DirectorySeparatorChar == '\\')
            {
                Assert.StartsWith(@"\\?\", normalized, StringComparison.Ordinal);
                Assert.EndsWith("typesupport.dll", normalized, StringComparison.Ordinal);

                var uncPath = @"\\phase184-server\typesupport-share\" +
                              new string('b', 260) +
                              @"\typesupport.dll";
                var normalizedUnc = Ros2ForUnityCustomTypesupportSelectionTransaction
                    .NormalizeWindowsLongPathForRead(uncPath);

                Assert.StartsWith(
                    @"\\?\UNC\phase184-server\typesupport-share\",
                    normalizedUnc,
                    StringComparison.Ordinal);
                Assert.EndsWith("typesupport.dll", normalizedUnc, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(Path.GetFullPath(ordinaryPath), normalized);
            }
        }

        [Fact]
        public void VerificationExistenceHandlesDeepPackagePaths()
        {
            var root = Path.Combine(
                RepositoryBuildTestRoot(),
                "u2f-phase181-exists-" + Guid.NewGuid().ToString("N"));
            var directory = root;
            while (directory.Length < 280)
                directory = Path.Combine(directory, "deep-typesupport-segment");
            var file = Path.Combine(directory, "custom-typesupport.dll");

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(file, "verified");

                Assert.True(
                    Ros2ForUnityCustomTypesupportSelectionTransaction.FileExistsForVerification(file));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

    }
}
