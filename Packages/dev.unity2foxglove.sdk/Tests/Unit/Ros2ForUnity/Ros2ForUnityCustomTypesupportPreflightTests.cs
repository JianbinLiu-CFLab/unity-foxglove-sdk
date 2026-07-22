// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pin Phase181-E metadata-only custom typesupport preflight behavior.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2ForUnity.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "181-E")]
    [Trait("Domain", "CustomTypesupportPreflight")]
    public sealed class Ros2ForUnityCustomTypesupportPreflightTests
    {
        [Fact]
        public void NoCustomNativeContractIsNotRequiredEvenWhenCandidatesExist()
        {
            using var fixture = new PreflightFixture();
            var result = fixture.Evaluate(required: false);

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.NotRequired, result.Code);
            Assert.Empty(result.ActiveAddOnPackage);
            Assert.Empty(result.CandidateAddOnPackages);
        }

        [Fact]
        public void FixtureScratchStaysInsideTheRepositoryBuildRoot()
        {
            using var fixture = new PreflightFixture();

            Assert.StartsWith(
                RepositoryBuildTestRoot() + Path.DirectorySeparatorChar,
                fixture.Root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MissingStaticSourcePackageIsReportedWithoutExposingPaths()
        {
            using var fixture = new PreflightFixture();
            fixture.DeleteStaticSourceLock();

            var result = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.MissingSource, result.Code);
            Assert.DoesNotContain(fixture.Root, result.Action, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.Root, result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void StaleStaticSourceLockIsReportedBeforeAddOnActivation()
        {
            using var fixture = new PreflightFixture();
            fixture.WriteStaticLock(interfaceDigest: new string('b', 64));

            var result = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.StaleSource, result.Code);
        }

        [Fact]
        public void MissingAndMultipleResolvedAddOnsRemainDistinct()
        {
            using var fixture = new PreflightFixture();

            var missing = fixture.Evaluate(activeAddOns: Array.Empty<string>());
            var multiple = fixture.Evaluate(activeAddOns: new[] { fixture.AddOnPackage, fixture.OtherAddOnPackage });

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.MissingAddOn, missing.Code);
            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.MultipleAddOns, multiple.Code);
        }

        [Fact]
        public void DistributionAndDigestMismatchAreFailClosedAndDistinct()
        {
            using var fixture = new PreflightFixture();
            fixture.WriteAddOnManifest(distro: "jazzy");
            var distribution = fixture.Evaluate();

            fixture.WriteAddOnManifest(distro: "humble", interfaceDigest: new string('b', 64));
            var digest = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.DistributionMismatch, distribution.Code);
            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.DigestMismatch, digest.Code);
        }

        [Fact]
        public void InvalidMetadataAndInventoryRemainDistinct()
        {
            using var fixture = new PreflightFixture();
            File.WriteAllText(fixture.TypesupportManifestPath, "{");
            var invalidManifest = fixture.Evaluate();

            fixture.WriteAddOnManifest();
            File.WriteAllText(fixture.InventoryPath, "{");
            var invalidInventory = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.InvalidManifest, invalidManifest.Code);
            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.InvalidInventory, invalidInventory.Code);
        }

        [Fact]
        public void TypeMapAndCatalogFailuresArePrecise()
        {
            using var fixture = new PreflightFixture();
            fixture.WriteAddOnManifest(includeExpectedManagedType: false);
            var missingManagedType = fixture.Evaluate();

            fixture.WriteAddOnManifest();
            fixture.WriteInventory(catalogCount: 0);
            var missingCatalog = fixture.Evaluate();

            fixture.WriteInventory(catalogCount: 2);
            var duplicateCatalog = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.MissingManagedType, missingManagedType.Code);
            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.MissingCatalog, missingCatalog.Code);
            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.DuplicateCatalog, duplicateCatalog.Code);
        }

        [Fact]
        public void UnsupportedRmwAndCompileSettlingPreventReady()
        {
            using var fixture = new PreflightFixture();
            var unsupportedRmw = fixture.Evaluate(rmw: "rmw_zenoh_cpp");
            var settling = fixture.Evaluate(customCompileSymbolDefined: false);

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.UnsupportedRmw, unsupportedRmw.Code);
            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.Settling, settling.Code);
        }

        [Fact]
        public void PackageResolutionInProgressReportsSettlingBeforeTemporaryAddOnMetadataFailure()
        {
            using var fixture = new PreflightFixture();
            File.WriteAllText(fixture.TypesupportManifestPath, "{");

            var result = fixture.Evaluate(editorReloadSettled: false);

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.Settling, result.Code);
        }

        [Fact]
        public void ReadyResultCarriesOnlySafeCompactIdentityAndContractPresentation()
        {
            using var fixture = new PreflightFixture();
            var result = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.Ready, result.Code);
            Assert.Equal(fixture.AddOnPackage, result.ActiveAddOnPackage);
            Assert.Equal("humble", result.Distribution);
            Assert.Equal("rmw_fastrtps_cpp", result.RmwImplementation);
            Assert.Equal(12, result.ShortInterfaceDigest.Length);
            var contract = Assert.Single(result.Contracts);
            Assert.Equal(fixture.CanonicalEnvelopeType, contract.CanonicalEnvelopeType);
            Assert.Equal("Inbound / Sensor Data", contract.DirectionalPolicy);
            Assert.DoesNotContain(fixture.Root, result.ToDisplaySummary(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OneRosEnvelopeSharedByDistinctContractsRemainsReady()
        {
            using var fixture = new PreflightFixture();
            fixture.WriteStaticLock(sharedEnvelopeAcrossContracts: true);

            var result = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.Ready, result.Code);
            Assert.Equal(fixture.CanonicalEnvelopeType, Assert.Single(result.Contracts).CanonicalEnvelopeType);
        }

        [Fact]
        public void DiscoveryCacheRefreshesOnlyAfterAnExplicitInvalidation()
        {
            using var fixture = new PreflightFixture();
            var initial = Ros2ForUnityCustomTypesupportDiscovery.Discover(
                fixture.ProjectDirectory,
                fixture.AddOnPackage);

            fixture.WriteAddOnManifest(distro: "jazzy", invalidateDiscoveryCache: false);
            var cached = Ros2ForUnityCustomTypesupportDiscovery.Discover(
                fixture.ProjectDirectory,
                fixture.AddOnPackage);
            Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            var refreshed = Ros2ForUnityCustomTypesupportDiscovery.Discover(
                fixture.ProjectDirectory,
                fixture.AddOnPackage);

            Assert.Equal("humble", initial.AddOn.Distribution);
            Assert.Equal("humble", cached.AddOn.Distribution);
            Assert.Equal("jazzy", refreshed.AddOn.Distribution);
        }

        [Fact]
        public void ActiveManifestReferenceIsUsedInsteadOfTheRepositoryCandidateDirectory()
        {
            using var fixture = new PreflightFixture();
            fixture.WriteResolvedAddOnOutsideRepository();
            File.WriteAllText(fixture.TypesupportManifestPath, "{");

            var result = fixture.Evaluate();

            Assert.Equal(Ros2ForUnityCustomTypesupportPreflightCode.Ready, result.Code);
        }

        private sealed class PreflightFixture : IDisposable
        {
            private const string StaticPackageId = "dev.unity2foxglove.foxrun.ros2.interfaces";
            private const string RosPackageName = "unity2foxglove_foxrun_interfaces_v1";
            private const string InterfaceDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

            public PreflightFixture()
            {
                Root = Path.Combine(
                    RepositoryBuildTestRoot(),
                    "u2f-phase181-e-" + Guid.NewGuid().ToString("N"));
                ProjectDirectory = Path.Combine(Root, "Unity2Foxglove");
                PackagesDirectory = Path.Combine(Root, "Packages");
                Directory.CreateDirectory(Path.Combine(ProjectDirectory, "Packages"));
                Directory.CreateDirectory(PackagesDirectory);
                AddOnPackage = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64";
                OtherAddOnPackage = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64";
                CanonicalEnvelopeType = RosPackageName + "/msg/StateEnvelope";
                WriteStaticLock();
                WriteAddOnManifest();
                WriteInventory();
                WriteProjectManifest("file:../../Packages/" + AddOnPackage);
            }

            public string Root { get; }
            public string ProjectDirectory { get; }
            public string PackagesDirectory { get; }
            public string AddOnPackage { get; }
            public string OtherAddOnPackage { get; }
            public string CanonicalEnvelopeType { get; }
            public string TypesupportManifestPath => Path.Combine(
                PackagesDirectory,
                AddOnPackage,
                "RuntimeSupport",
                "typesupport-manifest.json");
            public string InventoryPath => Path.Combine(
                PackagesDirectory,
                AddOnPackage,
                "RuntimeSupport",
                "typesupport-inventory.json");

            public void WriteResolvedAddOnOutsideRepository()
            {
                var directory = Path.Combine(Root, "resolved", AddOnPackage);
                WriteAddOnManifestAt(directory);
                WriteInventoryAt(directory);
                WriteProjectManifest("file:../../resolved/" + AddOnPackage);
                Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            }

            public Ros2ForUnityCustomTypesupportPreflightResult Evaluate(
                bool required = true,
                IReadOnlyList<string> activeAddOns = null,
                string rmw = "rmw_fastrtps_cpp",
                bool customCompileSymbolDefined = true,
                bool editorReloadSettled = true)
            {
                return Ros2ForUnityCustomTypesupportPreflight.Evaluate(
                    new Ros2ForUnityCustomTypesupportPreflightInput(
                        ProjectDirectory,
                        required,
                        "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
                        "humble",
                        rmw,
                        editorReloadSettled,
                        customCompileSymbolDefined,
                        new Ros2ForUnityCustomTypesupportSelectionResult(
                            Ros2ForUnityCustomTypesupportSelectionCode.Ready,
                            AddOnPackage,
                            InterfaceDigest,
                            "runtime-manifest-sha",
                            string.Empty),
                        activeAddOns ?? new[] { AddOnPackage },
                        new[] { AddOnPackage, OtherAddOnPackage },
                        new[]
                        {
                            new Ros2ForUnityCustomTypesupportContract(
                                CanonicalEnvelopeType,
                                "Inbound / Sensor Data")
                        }));
            }

            public void DeleteStaticSourceLock()
            {
                var path = Path.Combine(
                    PackagesDirectory,
                    StaticPackageId,
                    "RuntimeSupport",
                    "foxrun-ros2-interface-lock.json");
                File.Delete(path);
                Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            }

            public void WriteStaticLock(
                string interfaceDigest = InterfaceDigest,
                bool sharedEnvelopeAcrossContracts = false)
            {
                var directory = Path.Combine(PackagesDirectory, StaticPackageId, "RuntimeSupport");
                Directory.CreateDirectory(directory);
                var contracts = new JArray
                {
                    new JObject { ["envelopeMessageName"] = "StateEnvelope" }
                };
                if (sharedEnvelopeAcrossContracts)
                    contracts.Add(new JObject { ["envelopeMessageName"] = "StateEnvelope" });
                File.WriteAllText(
                    Path.Combine(directory, "foxrun-ros2-interface-lock.json"),
                    new JObject
                    {
                        ["lockSchemaVersion"] = 1,
                        ["interfaceSchemaVersion"] = 1,
                        ["unityPackageId"] = StaticPackageId,
                        ["rosPackageName"] = RosPackageName,
                        ["interfaceRevision"] = 1,
                        ["interfaceDigest"] = interfaceDigest,
                        ["contracts"] = contracts
                    }.ToString(Formatting.Indented));
                Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            }

            public void WriteAddOnManifest(
                string distro = "humble",
                string interfaceDigest = InterfaceDigest,
                bool includeExpectedManagedType = true,
                bool invalidateDiscoveryCache = true)
            {
                WriteAddOnManifestAt(
                    Path.Combine(PackagesDirectory, AddOnPackage),
                    distro,
                    interfaceDigest,
                    includeExpectedManagedType);
                if (invalidateDiscoveryCache)
                    Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            }

            private void WriteAddOnManifestAt(
                string packageDirectory,
                string distro = "humble",
                string interfaceDigest = InterfaceDigest,
                bool includeExpectedManagedType = true)
            {
                var runtimeSupportDirectory = Path.Combine(packageDirectory, "RuntimeSupport");
                Directory.CreateDirectory(runtimeSupportDirectory);
                File.WriteAllText(
                    Path.Combine(packageDirectory, "package.json"),
                    new JObject
                    {
                        ["name"] = AddOnPackage,
                        ["unity2foxgloveFoxRunCustomTypesupportAddOn"] = true,
                        ["dependencies"] = new JObject
                        {
                            ["dev.unity2foxglove.ros2forunity"] = "0.1.0-preview.1"
                        }
                    }.ToString(Formatting.Indented));
                var typeMap = new JArray();
                if (includeExpectedManagedType)
                {
                    typeMap.Add(new JObject
                    {
                        ["canonicalRosType"] = CanonicalEnvelopeType,
                        ["managedType"] = RosPackageName + ".msg.StateEnvelope"
                    });
                }

                File.WriteAllText(
                    Path.Combine(runtimeSupportDirectory, "typesupport-manifest.json"),
                    new JObject
                    {
                        ["schemaVersion"] = 1,
                        ["source"] = new JObject
                        {
                            ["upmPackageId"] = StaticPackageId,
                            ["rosPackageName"] = RosPackageName,
                            ["interfaceRevision"] = 1,
                            ["interfaceDigest"] = interfaceDigest
                        },
                        ["distro"] = distro,
                        ["baseRuntime"] = new JObject
                        {
                            ["packageId"] = "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
                        },
                        ["platform"] = "win64",
                        ["architecture"] = "x86_64",
                        ["supportedRmwImplementations"] = new JArray("rmw_fastrtps_cpp"),
                        ["managed"] = new JObject
                        {
                            ["assembly"] = new JObject { ["name"] = RosPackageName + "_assembly" },
                            ["typeMap"] = typeMap
                        }
                    }.ToString(Formatting.Indented));
            }

            public void WriteInventory(int catalogCount = 1)
            {
                WriteInventoryAt(Path.Combine(PackagesDirectory, AddOnPackage), catalogCount);
                Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            }

            private void WriteInventoryAt(string packageDirectory, int catalogCount = 1)
            {
                var entries = new JArray();
                for (var index = 0; index < catalogCount; index++)
                {
                    entries.Add(new JObject
                    {
                        ["path"] = "Runtime/FoxRun/Generated/FoxRunCustomTypesupportCatalog" + index + ".g.cs",
                        ["sha256"] = InterfaceDigest,
                        ["role"] = "catalog"
                    });
                }

                File.WriteAllText(
                    Path.Combine(packageDirectory, "RuntimeSupport", "typesupport-inventory.json"),
                    new JObject
                    {
                        ["schemaVersion"] = 1,
                        ["entries"] = entries
                    }.ToString(Formatting.Indented));
            }

            private void WriteProjectManifest(string addOnReference)
            {
                File.WriteAllText(
                    Path.Combine(ProjectDirectory, "Packages", "manifest.json"),
                    new JObject
                    {
                        ["dependencies"] = new JObject
                        {
                            [AddOnPackage] = addOnReference
                        }
                    }.ToString(Formatting.Indented));
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
        }

        private static string RepositoryBuildTestRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                {
                    return Path.Combine(directory.FullName, "build", "Tests", "Phase181");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
