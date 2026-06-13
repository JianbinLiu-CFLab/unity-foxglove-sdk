// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140I2 FoxgloveManager structure checks.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140I2")]
    [Trait("Domain", "Harness")]
    public sealed class FoxgloveManagerStructureTests
    {
        [Fact]
        public void ServiceAndParameterFacadeLivesInFocusedPartial()
        {
            const string managerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";
            const string servicesPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Services.cs";
            const string servicesMetaPath = servicesPath + ".meta";

            Assert.True(File.Exists(PathOf(servicesPath)), servicesPath + " should exist.");
            Assert.True(File.Exists(PathOf(servicesMetaPath)), servicesMetaPath + " should exist.");

            var manager = Text(managerPath);
            var services = Text(servicesPath);
            var meta = Text(servicesMetaPath);

            Assert.Contains("public partial class FoxgloveManager", services, StringComparison.Ordinal);
            Assert.Contains("// Module: Runtime/Components/Manager", services, StringComparison.Ordinal);
            Assert.Contains("// Purpose:", services, StringComparison.Ordinal);
            Assert.Contains("public void RegisterParameter(", services, StringComparison.Ordinal);
            Assert.Contains("public bool UnregisterParameter(", services, StringComparison.Ordinal);
            Assert.Contains("public uint RegisterService(", services, StringComparison.Ordinal);
            Assert.Contains("public bool UnregisterService(", services, StringComparison.Ordinal);

            Assert.DoesNotContain("public void RegisterParameter(", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("public bool UnregisterParameter(", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("public uint RegisterService(", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("public bool UnregisterService(", manager, StringComparison.Ordinal);

            Assert.True(HasValidUnityGuid(meta), servicesMetaPath + " should have a valid Unity GUID.");
            Assert.Contains("MonoImporter:", meta, StringComparison.Ordinal);
        }

        private static string Text(string relativePath)
            => File.ReadAllText(PathOf(relativePath));

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static bool HasValidUnityGuid(string meta)
        {
            const string prefix = "guid:";
            foreach (var rawLine in meta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var guid = line.Substring(prefix.Length).Trim();
                if (guid.Length != 32)
                    return false;

                for (var i = 0; i < guid.Length; i++)
                {
                    var c = guid[i];
                    var isHex = (c >= '0' && c <= '9')
                                || (c >= 'a' && c <= 'f')
                                || (c >= 'A' && c <= 'F');
                    if (!isHex)
                        return false;
                }

                return true;
            }

            return false;
        }

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
