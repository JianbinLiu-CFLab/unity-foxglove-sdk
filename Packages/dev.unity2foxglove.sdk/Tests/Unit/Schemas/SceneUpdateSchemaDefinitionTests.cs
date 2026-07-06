// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: SceneUpdate JSON schema embedding guard.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Foxglove.Schemas;
using Unity.FoxgloveSDK.Schemas;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-001")]
    [Trait("Domain", "Schemas")]
    public sealed class SceneUpdateSchemaDefinitionTests
    {
        [Fact]
        public void SceneUpdateSourceJsonMatchesEmbeddedRuntimeSchema()
        {
            var repoRoot = FindRepoRoot();
            var schemaPath = Path.Combine(
                repoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Json",
                "SceneUpdate.json");
            var definitionsPath = Path.Combine(
                repoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Registry",
                "FoxgloveSchemaDefinitions.cs");

            var fileBytes = File.ReadAllBytes(schemaPath);
            var fileText = Encoding.UTF8.GetString(fileBytes);
            var expectedHashPrefix = ComputeSha256Hex(fileBytes).Substring(0, 16);
            var definitionsSource = File.ReadAllText(definitionsPath);

            Assert.Equal(fileText, FoxgloveSchemaDefinitions.SceneUpdateSchema);
            Assert.Contains("SceneUpdate.json sha256=" + expectedHashPrefix, definitionsSource);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Packages", "dev.unity2foxglove.sdk"))
                    && Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
