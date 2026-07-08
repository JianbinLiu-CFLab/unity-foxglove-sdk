// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 171 optional remote gateway package boundary checks.

using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "171")]
    [Trait("Domain", "Architecture")]
    public sealed class RemoteGatewayPackageShapeTests
    {
        private const string PackageRoot = "Packages/dev.unity2foxglove.remotegateway.win64";

        [Fact]
        public void RemoteGatewayOptionalPackageSkeletonIsPresentAndDefaultOff()
        {
            Assert.True(Directory.Exists(PathOf(PackageRoot)), PackageRoot + " should exist.");

            using var packageJson = JsonDocument.Parse(Text(PackageRoot + "/package.json"));
            var root = packageJson.RootElement;
            Assert.Equal("dev.unity2foxglove.remotegateway.win64", root.GetProperty("name").GetString());
            Assert.Equal("Apache-2.0", root.GetProperty("license").GetString());
            Assert.Contains("Windows x64", root.GetProperty("description").GetString(), StringComparison.Ordinal);

            var readme = Text(PackageRoot + "/README.md");
            Assert.Contains("default-off", readme, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Foxglove Cloud", readme, StringComparison.Ordinal);
            Assert.Contains("FOXGLOVE_DEVICE_TOKEN", readme, StringComparison.Ordinal);
            Assert.Contains("outbound-only", readme, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(PathOf(PackageRoot + "/LICENSE")));
            Assert.True(File.Exists(PathOf(PackageRoot + "/THIRD_PARTY_NOTICES.md")));
        }

        [Fact]
        public void RuntimeAssemblyReferencesCoreWithoutCreatingReverseDependency()
        {
            var asmdef = Text(PackageRoot + "/Runtime/Unity.FoxgloveSDK.RemoteGateway.asmdef");
            Assert.Contains("\"name\": \"Unity.FoxgloveSDK.RemoteGateway\"", asmdef, StringComparison.Ordinal);
            Assert.Contains("\"Unity.FoxgloveSDK\"", asmdef, StringComparison.Ordinal);
            Assert.Contains("\"WindowsStandalone64\"", asmdef, StringComparison.Ordinal);
            Assert.Contains("\"Editor\"", asmdef, StringComparison.Ordinal);

            var coreRuntime = Text("Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef");
            var coreEditor = Text("Packages/dev.unity2foxglove.sdk/Editor/Unity.FoxgloveSDK.Editor.asmdef");
            Assert.DoesNotContain("RemoteGateway", coreRuntime, StringComparison.Ordinal);
            Assert.DoesNotContain("RemoteGateway", coreEditor, StringComparison.Ordinal);
        }

        [Fact]
        public void NativeArtifactManifestRecordsProvenBuildGate()
        {
            using var manifest = JsonDocument.Parse(Text(PackageRoot + "/Runtime/Plugins/Windows/x86_64/foxglove-gateway-native-artifact.json"));
            var root = manifest.RootElement;
            Assert.Equal("foxglove.dll", root.GetProperty("artifact").GetString());
            Assert.Equal("windows-x64", root.GetProperty("platform").GetString());
            Assert.Contains("remote-access", root.GetProperty("features").GetString(), StringComparison.Ordinal);
            Assert.Contains("crt-static", root.GetProperty("rustflags").GetString(), StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("sha256").GetString());
            Assert.True(root.GetProperty("sizeBytes").GetInt64() > 40_000_000);
        }

        [Fact]
        public void BuildScriptKeepsNativeOutputsOutsidePackagesBeforeApprovedCopy()
        {
            var script = Text("Scripts/remotegateway/build_foxglove_c_win64.py");
            Assert.Contains("CARGO_TARGET_DIR", script, StringComparison.Ordinal);
            Assert.Contains("build/remotegateway", script.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("Runtime/Plugins/Windows/x86_64", script.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("AWS_LC_SYS_PREBUILT_NASM", script, StringComparison.Ordinal);
            Assert.Contains("target-feature=+crt-static", script, StringComparison.Ordinal);
            Assert.DoesNotContain("cargo build --target-dir Packages", script, StringComparison.Ordinal);
        }

        [Fact]
        public void CloudAcceptanceHelperBuildsNativeAndLaunchesUnityWithInheritedToken()
        {
            var script = Text("Scripts/remotegateway/run_cloud_acceptance.py");
            Assert.Contains("FOXGLOVE_DEVICE_TOKEN", script, StringComparison.Ordinal);
            Assert.Contains("build_foxglove_c_win64.py", script, StringComparison.Ordinal);
            Assert.Contains("--copy-to-package", script, StringComparison.Ordinal);
            Assert.Contains("-projectPath", script, StringComparison.Ordinal);
            Assert.Contains("Unity.exe", script, StringComparison.Ordinal);
            Assert.Contains("Remote gateway started. Publishing to Foxglove Cloud.", script, StringComparison.Ordinal);
            Assert.Contains("ClientPublish", script, StringComparison.Ordinal);
            Assert.Contains("subprocess.Popen", script, StringComparison.Ordinal);
            Assert.DoesNotContain("--device-token", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DeviceToken", script, StringComparison.Ordinal);
        }

        [Fact]
        public void ChannelRegistryKeepsNativeHandleAliveDuringPublish()
        {
            var source = Text(PackageRoot + "/Runtime/RemoteGatewayChannelRegistry.cs");
            var publish = MethodBody(source, "internal bool Publish");

            Assert.Contains("private readonly object _gate", source, StringComparison.Ordinal);
            Assert.Contains("private readonly ulong[] _logTimeScratch = new ulong[1];", source, StringComparison.Ordinal);
            Assert.Contains("lock (_gate)", publish, StringComparison.Ordinal);
            Assert.Contains("ChannelLog", publish, StringComparison.Ordinal);
            Assert.Contains("Keep the native channel handle live until ChannelLog has returned.", publish, StringComparison.Ordinal);
            Assert.True(
                publish.IndexOf("lock (_gate)", StringComparison.Ordinal)
                < publish.IndexOf("ChannelLog", StringComparison.Ordinal));
            Assert.DoesNotContain("new[] { logTimeNs }", publish, StringComparison.Ordinal);
        }

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot.Value, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string MethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "Missing method signature: " + signature);
            var open = source.IndexOf('{', start);
            Assert.True(open >= 0, "Missing method body: " + signature);

            var depth = 0;
            for (var i = open; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(open, i - open + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method body: " + signature);
        }

        private static readonly Lazy<string> RepoRoot = new Lazy<string>(FindRepoRoot);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Unity2Foxglove"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Packages"))
                    && File.Exists(Path.Combine(dir.FullName, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
