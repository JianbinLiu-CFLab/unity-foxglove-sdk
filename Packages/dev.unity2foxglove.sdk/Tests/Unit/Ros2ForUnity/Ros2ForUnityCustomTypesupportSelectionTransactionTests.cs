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
    public sealed class Ros2ForUnityCustomTypesupportSelectionTransactionTests
    {
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
                resolve: () => resolveCalls++);

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
                resolve: () => { });

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
                () => { });

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
                resolve: () => { });

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
                resolve: () => throw new InvalidOperationException("test resolve failure"));

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

        private sealed class SelectionFixture : IDisposable
        {
            private readonly string _root;

            public SelectionFixture()
            {
                _root = Path.Combine(
                    RepositoryBuildTestRoot(),
                    "u2f-phase181-" + Guid.NewGuid().ToString("N"));
                ProjectDirectory = Path.Combine(_root, "Unity2Foxglove");
                PackagesDirectory = Path.Combine(_root, "Packages");
                Directory.CreateDirectory(Path.Combine(ProjectDirectory, "Packages"));
                Directory.CreateDirectory(PackagesDirectory);

                WriteStaticSourceLock();
                WriteBaseRuntime("humble");
                WriteBaseRuntime("jazzy");
            }

            public string ProjectDirectory { get; }
            public string PackagesDirectory { get; }
            public string ScratchDirectory => _root;
            public string ManifestPath => Path.Combine(ProjectDirectory, "Packages", "manifest.json");
            public string PackagesLockPath => Path.Combine(ProjectDirectory, "Packages", "packages-lock.json");
            public string HumbleRuntimePackage => "dev.unity2foxglove.ros2forunity.runtime.humble.win64";
            public string JazzyRuntimePackage => "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
            public string StaticInterfacePackage => "dev.unity2foxglove.foxrun.ros2.interfaces";
            private string StaticRosPackageName { get; set; } = "unity2foxglove_foxrun_interfaces_v1";
            private int StaticInterfaceRevision { get; set; } = 1;

            public void SetStaticSourceIdentity(string rosPackageName, int interfaceRevision)
            {
                StaticRosPackageName = rosPackageName;
                StaticInterfaceRevision = interfaceRevision;
                WriteStaticSourceLock();
            }

            public string WriteAddOn(string suffix, bool valid, string baseRuntime = null)
            {
                var packageName = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + suffix + ".win64";
                var runtime = baseRuntime ?? HumbleRuntimePackage;
                var directory = Path.Combine(PackagesDirectory, packageName);
                Directory.CreateDirectory(Path.Combine(directory, "RuntimeSupport"));
                Directory.CreateDirectory(Path.Combine(directory, "Runtime", "Ros2ForUnity", "Plugins", "Windows", "x86_64"));
                var sourceDigest = StaticInterfaceDigest();
                var runtimeManifest = ReadJson(Path.Combine(PackagesDirectory, runtime, "RuntimeSupport", "runtime-manifest.json"));
                var nativePath = "Runtime/Ros2ForUnity/Plugins/Windows/x86_64/custom.dll";
                var nativeFile = Path.Combine(directory, nativePath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(nativeFile, "native");
                File.WriteAllText(
                    Path.Combine(directory, "package.json"),
                    new JObject
                    {
                        ["name"] = packageName,
                        ["unity2foxgloveFoxRunCustomTypesupportAddOn"] = valid,
                        ["dependencies"] = new JObject
                        {
                            ["dev.unity2foxglove.ros2forunity"] = "0.1.0-preview.1",
                            [runtime] = "0.1.0-preview.1"
                        }
                    }.ToString(Formatting.Indented));
                var manifest = new JObject
                {
                    ["schemaVersion"] = 1,
                    ["source"] = new JObject
                    {
                        ["upmPackageId"] = "dev.unity2foxglove.foxrun.ros2.interfaces",
                        ["rosPackageName"] = StaticRosPackageName,
                        ["interfaceRevision"] = StaticInterfaceRevision,
                        ["interfaceDigest"] = sourceDigest,
                        ["generatorSchemaVersion"] = 1
                    },
                    ["distro"] = runtime == HumbleRuntimePackage ? "humble" : "jazzy",
                    ["baseRuntime"] = new JObject
                    {
                        ["packageId"] = runtime,
                        ["runtimeManifestVersion"] = 1,
                        ["runtimeManifestSha256"] = NormalizedJsonSha256(runtimeManifest)
                    },
                    ["platform"] = "win64",
                    ["architecture"] = "x86_64",
                    ["supportedRmwImplementations"] = new JArray("rmw_fastrtps_cpp"),
                    ["managed"] = new JObject
                    {
                        ["ros2Message"] = new JObject
                        {
                            ["assemblyName"] = "ros2cs_common",
                            ["version"] = "0.0.0.0",
                            ["publicKeyToken"] = "",
                            ["mvid"] = Ros2csMvid(runtime),
                            ["sha256"] = FileSha256(Path.Combine(PackagesDirectory, runtime, "Runtime", "Ros2ForUnity", "Plugins", "ros2cs_common.dll"))
                        }
                    },
                    ["nativeLibraries"] = new JArray
                    {
                        new JObject
                        {
                            ["path"] = nativePath,
                            ["sha256"] = FileSha256(nativeFile),
                            ["classification"] = "direct"
                        }
                    },
                    ["rmwClosures"] = new JObject
                    {
                        ["rmw_fastrtps_cpp"] = new JObject
                        {
                            ["baseRuntimeLibraries"] = new JArray("rmw_fastrtps_cpp.dll"),
                            ["addOnLibraries"] = new JArray(nativePath)
                        }
                    }
                };
                File.WriteAllText(
                    Path.Combine(directory, "RuntimeSupport", "typesupport-manifest.json"),
                    manifest.ToString(Formatting.Indented));
                File.WriteAllText(
                    Path.Combine(directory, "RuntimeSupport", "typesupport-inventory.json"),
                    new JObject
                    {
                        ["schemaVersion"] = 1,
                        ["entries"] = new JArray
                        {
                            new JObject
                            {
                                ["path"] = nativePath,
                                ["byteLength"] = new FileInfo(nativeFile).Length,
                                ["sha256"] = FileSha256(nativeFile),
                                ["role"] = "native",
                                ["classification"] = "direct"
                            }
                        }
                    }.ToString(Formatting.Indented));
                return packageName;
            }

            public FileStream OpenRos2csCommonWithUnityLikeSharing(string runtime)
            {
                var assemblyPath = Path.Combine(
                    PackagesDirectory,
                    runtime,
                    "Runtime",
                    "Ros2ForUnity",
                    "Plugins",
                    "ros2cs_common.dll");
                return new FileStream(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            }

            public void ReplaceRos2csMvid(string packageName, string mvid)
            {
                var path = Path.Combine(
                    PackagesDirectory,
                    packageName,
                    "RuntimeSupport",
                    "typesupport-manifest.json");
                var manifest = ReadJson(path);
                ((JObject)manifest["managed"]!["ros2Message"]!)["mvid"] = mvid;
                File.WriteAllText(path, manifest.ToString(Formatting.Indented));
            }

            public void WriteManifest(params string[] packageNames)
            {
                var dependencies = new JObject { ["dev.unity2foxglove.sdk"] = "file:../../Packages/dev.unity2foxglove.sdk" };
                foreach (var packageName in packageNames)
                    dependencies[packageName] = "file:../../Packages/" + packageName;
                File.WriteAllText(ManifestPath, new JObject { ["dependencies"] = dependencies }.ToString(Formatting.Indented) + "\n");
            }

            public void WritePackagesLock(string text)
            {
                File.WriteAllText(PackagesLockPath, text);
            }

            public string[] ManifestDependencyNames()
            {
                var dependencies = ReadJson(ManifestPath)["dependencies"] as JObject;
                return dependencies.Properties()
                    .Select(property => property.Name)
                    .Where(name => name.StartsWith("dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal)
                                   || name.StartsWith("dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.", StringComparison.Ordinal)
                                   || string.Equals(name, StaticInterfacePackage, StringComparison.Ordinal))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
            }

            public void Dispose()
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }

            private void WriteStaticSourceLock()
            {
                var support = Path.Combine(PackagesDirectory, "dev.unity2foxglove.foxrun.ros2.interfaces", "RuntimeSupport");
                Directory.CreateDirectory(support);
                File.WriteAllText(
                    Path.Combine(support, "foxrun-ros2-interface-lock.json"),
                    new JObject
                    {
                        ["unityPackageId"] = "dev.unity2foxglove.foxrun.ros2.interfaces",
                        ["rosPackageName"] = StaticRosPackageName,
                        ["interfaceRevision"] = StaticInterfaceRevision,
                        ["interfaceDigest"] = StaticInterfaceDigest()
                    }.ToString(Formatting.Indented));
            }

            private void WriteBaseRuntime(string distro)
            {
                var packageName = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64";
                var support = Path.Combine(PackagesDirectory, packageName, "RuntimeSupport");
                var native = Path.Combine(PackagesDirectory, packageName, "Runtime", "Ros2ForUnity", "Plugins", "Windows", "x86_64");
                var managed = Path.Combine(PackagesDirectory, packageName, "Runtime", "Ros2ForUnity", "Plugins");
                Directory.CreateDirectory(support);
                Directory.CreateDirectory(native);
                Directory.CreateDirectory(managed);
                File.Copy(
                    typeof(SelectionFixture).Assembly.Location,
                    Path.Combine(managed, "ros2cs_common.dll"),
                    overwrite: true);
                File.WriteAllText(Path.Combine(native, "rmw_fastrtps_cpp.dll"), "rmw fixture");
                File.WriteAllText(
                    Path.Combine(support, "runtime-manifest.json"),
                    new JObject
                    {
                        ["schemaVersion"] = 1,
                        ["packageName"] = packageName,
                        ["packageVersion"] = "0.1.0-preview.1",
                        ["rosDistro"] = distro,
                        ["platform"] = "win64",
                        ["architecture"] = "x86_64"
                    }.ToString(Formatting.Indented));
            }

            private static JObject ReadJson(string path)
                => JObject.Parse(File.ReadAllText(path));

            private static string StaticInterfaceDigest()
                => new string('a', 64);

            private static string FileSha256(string path)
            {
                using var sha = SHA256.Create();
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();
            }

            private static string NormalizedJsonSha256(JToken token)
            {
                using var sha = SHA256.Create();
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalJson(token))))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }

            private string Ros2csMvid(string runtime)
            {
                var assemblyPath = Path.Combine(
                    PackagesDirectory,
                    runtime,
                    "Runtime",
                    "Ros2ForUnity",
                    "Plugins",
                    "ros2cs_common.dll");
                using var stream = File.OpenRead(assemblyPath);
                using var reader = new PEReader(stream);
                var metadata = reader.GetMetadataReader();
                return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D");
            }

            private static string CanonicalJson(JToken token)
            {
                if (token is JObject obj)
                {
                    return "{" + string.Join(",", obj.Properties()
                        .OrderBy(property => property.Name, StringComparer.Ordinal)
                        .Select(property => CanonicalJson(new JValue(property.Name)) + ":" + CanonicalJson(property.Value))) + "}";
                }

                if (token is JArray array)
                    return "[" + string.Join(",", array.Select(CanonicalJson)) + "]";

                return token.ToString(Formatting.None);
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
