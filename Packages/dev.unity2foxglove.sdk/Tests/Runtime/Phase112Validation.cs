// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 112 FoxRun canonical manifest and fingerprint validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase112Validation.
    /// </summary>
    public static class Phase112Validation
    {
        private const string ManifestDir = "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest";
        private const string ManifestWriterPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs";
        private const string PlayModeHookPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs";
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 112: FoxRun Canonical Manifest And Fingerprint ===");
            _passed = 0;

            VerifyCanonicalFixture();
            VerifyHashChangeBoundaries();
            VerifyStableOrderingAndTypeNormalization();
            VerifyReportAndEmptyManifest();
            VerifySourceGenerationBehaviorUnchanged();
            VerifyBoundaryAndWiring();
            VerifyDocs();

            Console.WriteLine($"Phase 112: {_passed} checks passed.");
        }

        private static void VerifyCanonicalFixture()
        {
            var manifest = FoxRunManifestBuilder.Build(FixtureMembers());
            var json = FoxRunManifestJsonWriter.WriteCanonical(manifest);

            const string expectedJson = "{\"manifestVersion\":1,\"package\":\"Unity2Foxglove\",\"generator\":{\"name\":\"FoxRun\",\"majorVersion\":1},\"sections\":{\"foxrun\":{\"manifestHash\":\"262502c3999d4140c8b809fc0110ea5ea2fa4898702a117743140e672502fcef\",\"types\":[{\"declaringType\":\"Demo.RobotState\",\"contracts\":[{\"topic\":\"/phase112/battery\",\"schemaName\":\"\",\"wireSchemaName\":\"\",\"logicalSchemaName\":\"Demo.RobotState\",\"encoding\":\"json\",\"availability\":{\"publishAvailable\":true,\"subscribeAvailable\":false,\"unavailableDiagnosticId\":\"\",\"unavailableReason\":\"\"},\"contractHash\":\"3a171385ef84247fd8fc3fd37a49619155bec770691804c04d879f7e70cf5207\",\"bindingHash\":\"dd4037ff4397dca2231b374e9972cce8838883482d0ace1d422132193fdf9f52\",\"policyHash\":\"86bde8645ea3d1246bb10dc5a648b52c2da83848b7c63e30931e30a9cdd4f20d\",\"fields\":[{\"jsonName\":\"batteryLevel\",\"memberName\":\"_batteryLevel\",\"memberKind\":\"field\",\"type\":\"float32\",\"nullable\":false,\"array\":false,\"normalizedSchedule\":{\"policy\":2,\"hasExplicitHz\":true,\"hz\":10,\"tolerance\":0.00100000005,\"onlyIf\":\"\",\"conditionMemberKind\":\"None\"}}],\"policy\":{\"mode\":\"Change\",\"hz\":10,\"tolerance\":0.00100000005}}]}]}},\"globalManifestHash\":\"898b97e6379e09221d3b251a41682f2062dd4ba236aa4e2dd5c9faa056faa8da\"}";
            const string expectedContractHash = "3a171385ef84247fd8fc3fd37a49619155bec770691804c04d879f7e70cf5207";
            const string expectedBindingHash = "dd4037ff4397dca2231b374e9972cce8838883482d0ace1d422132193fdf9f52";
            const string expectedPolicyHash = "86bde8645ea3d1246bb10dc5a648b52c2da83848b7c63e30931e30a9cdd4f20d";
            const string expectedManifestHash = "262502c3999d4140c8b809fc0110ea5ea2fa4898702a117743140e672502fcef";
            const string expectedGlobalManifestHash = "898b97e6379e09221d3b251a41682f2062dd4ba236aa4e2dd5c9faa056faa8da";

            var contract = manifest.Sections.FoxRun.Types[0].Contracts[0];
            Check(json == expectedJson, "112-A1: fixture canonical JSON is exact and compact");
            Check(contract.ContractHash == expectedContractHash, "112-A2: fixture contractHash is exact");
            Check(contract.BindingHash == expectedBindingHash, "112-A3: fixture bindingHash is exact");
            Check(contract.PolicyHash == expectedPolicyHash, "112-A4: fixture policyHash is exact");
            Check(manifest.Sections.FoxRun.ManifestHash == expectedManifestHash
                  && manifest.GlobalManifestHash == expectedGlobalManifestHash,
                "112-A5: fixture FoxRun section hash and global hash are exact");
            Check(FoxRunManifestHasher.IsLowercaseSha256Hex(expectedManifestHash)
                  && FoxRunManifestHasher.IsLowercaseSha256Hex(expectedGlobalManifestHash),
                "112-A6: fixture hashes are lowercase SHA-256 hex");
        }

        private static void VerifyHashChangeBoundaries()
        {
            var baseline = FoxRunManifestBuilder.Build(FixtureMembers());
            var baselineContract = baseline.Sections.FoxRun.Types[0].Contracts[0];

            var rateChanged = FoxRunManifestBuilder.Build(FixtureMembers(hz: 5f));
            var rateContract = rateChanged.Sections.FoxRun.Types[0].Contracts[0];
            Check(rateContract.PolicyHash != baselineContract.PolicyHash
                  && rateContract.ContractHash != baselineContract.ContractHash
                  && rateContract.BindingHash == baselineContract.BindingHash
                  && rateChanged.Sections.FoxRun.ManifestHash != baseline.Sections.FoxRun.ManifestHash,
                "112-B1: Hz changes policyHash, contractHash, and manifestHash while bindingHash stays stable");

            var typeChanged = FoxRunManifestBuilder.Build(FixtureMembers(typeName: "System.Double"));
            var typeContract = typeChanged.Sections.FoxRun.Types[0].Contracts[0];
            Check(typeContract.ContractHash != baselineContract.ContractHash
                  && typeChanged.Sections.FoxRun.ManifestHash != baseline.Sections.FoxRun.ManifestHash,
                "112-B2: field type changes contractHash and manifestHash");

            var topicChanged = FoxRunManifestBuilder.Build(FixtureMembers(topic: "/phase112/renamed"));
            var topicContract = topicChanged.Sections.FoxRun.Types[0].Contracts[0];
            Check(topicContract.BindingHash != baselineContract.BindingHash
                  && topicChanged.Sections.FoxRun.ManifestHash != baseline.Sections.FoxRun.ManifestHash,
                "112-B3: topic changes bindingHash and manifestHash");
        }

        private static void VerifyStableOrderingAndTypeNormalization()
        {
            var shuffled = new List<FoxRunManifestMember>
            {
                Member("Demo", "RobotState", "_temperatures", "field",
                    "System.Collections.Generic.List`1[[System.Single]]", true, true, "System.Single",
                    "/phase112/temperature", 1f, "", (int)FoxRunPolicy.FixedRate, 0f),
                Member("Demo", "RobotState", "_name", "field",
                    "System.String", false, false, "", "/phase112/name", 1f, "", (int)FoxRunPolicy.FixedRate, 0f),
                Member("Demo", "RobotState", "_batteryLevel", "field",
                    "System.Single", true, false, "", "/phase112/battery", -10f, "", (int)FoxRunPolicy.Change, -1f)
            };

            var sorted = shuffled.OrderBy(m => m.Topic, StringComparer.Ordinal).ToList();
            var shuffledJson = FoxRunManifestJsonWriter.WriteCanonical(FoxRunManifestBuilder.Build(shuffled));
            var sortedJson = FoxRunManifestJsonWriter.WriteCanonical(FoxRunManifestBuilder.Build(sorted));
            Check(shuffledJson == sortedJson, "112-C1: canonical JSON is stable for differently ordered input");

            var manifest = FoxRunManifestBuilder.Build(shuffled);
            var contracts = manifest.Sections.FoxRun.Types[0].Contracts;
            var battery = contracts.First(c => c.Topic == "/phase112/battery");
            var name = contracts.First(c => c.Topic == "/phase112/name");
            var temperatures = contracts.First(c => c.Topic == "/phase112/temperature");

            Check(battery.Fields[0].Type == "float32"
                  && !battery.Fields[0].Nullable
                  && !battery.Fields[0].Array
                  && battery.Policy.Hz == 10f
                  && battery.Policy.Tolerance == 0f,
                "112-C2: scalar value types and unspecified or negative policy knobs are normalized");
            Check(name.Fields[0].Type == "string" && name.Fields[0].Nullable,
                "112-C3: string fields are nullable scalar manifest fields");
            Check(temperatures.Fields[0].Type == "float32"
                  && temperatures.Fields[0].Array
                  && temperatures.Fields[0].Nullable,
                "112-C4: list fields normalize to array element type");
        }

        private static void VerifyReportAndEmptyManifest()
        {
            var empty = FoxRunManifestBuilder.Build(
                Array.Empty<FoxRunManifestMember>(),
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var emptyJson = FoxRunManifestJsonWriter.WriteCanonical(empty);
            var emptyAgainJson = FoxRunManifestJsonWriter.WriteCanonical(
                FoxRunManifestBuilder.Build(
                    Array.Empty<FoxRunManifestMember>(),
                    manifestVersion: FoxrunManifestWriter.CurrentManifestVersion));
            Check(emptyJson == emptyAgainJson
                  && empty.Sections.FoxRun.Types.Count == 0
                  && FoxRunManifestHasher.IsLowercaseSha256Hex(empty.GlobalManifestHash),
                "112-D1: empty manifest is valid and deterministic");

            var report = FoxRunManifestJsonWriter.WriteReport(
                empty,
                "2026-05-20T00:00:00.0000000Z",
                new[] { "report-only warning" });
            Check(!emptyJson.Contains("generatedAtUtc", StringComparison.Ordinal)
                  && report.Contains("generatedAtUtc", StringComparison.Ordinal)
                  && report.Contains("report-only warning", StringComparison.Ordinal),
                "112-D2: generatedAtUtc and warnings are report-only");

            var tempRoot = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase112-" + Guid.NewGuid().ToString("N"));
            var manifestPath = Path.Combine(tempRoot, "foxrun.manifest.json");
            var hashPath = Path.Combine(tempRoot, "foxrun.manifest.hash");
            try
            {
                Directory.CreateDirectory(tempRoot);
                File.WriteAllText(manifestPath, "{\"stale\":true}");
                FoxrunManifestWriter.WriteManifestFiles(tempRoot, Array.Empty<FoxRunManifestMember>(), "2026-05-20T00:00:00.0000000Z");
                var rewritten = File.ReadAllText(manifestPath);
                Check(File.Exists(hashPath),
                    "112-D4a: manifest hash sidecar is written before content validation");
                var hashBytes = File.ReadAllBytes(hashPath);
                var hashText = Encoding.ASCII.GetString(hashBytes);
                var expectedHashBytes = Encoding.ASCII.GetBytes(empty.GlobalManifestHash + "\n");
                Check(rewritten == emptyJson,
                    "112-D3: empty manifest generation overwrites stale non-empty content");
                Check(hashText == empty.GlobalManifestHash + "\n"
                      && !hashText.Contains("\r", StringComparison.Ordinal)
                      && hashBytes.SequenceEqual(expectedHashBytes),
                    "112-D4: manifest hash sidecar uses stable LF newline and no BOM");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    TryDeleteDirectory(tempRoot);
            }
        }

        private static void VerifySourceGenerationBehaviorUnchanged()
        {
            var source = FoxgloveSourceEmitter.EmitClass("", "Phase112Source", new[]
            {
                new FoxgloveSourceEmitter.TopicMember(
                    "_batteryLevel",
                    "System.Single",
                    "/phase112/battery",
                    10f,
                    "",
                    (int)FoxRunPolicy.Change,
                    0.01f)
            });

            Check(source.Contains("partial class", StringComparison.Ordinal)
                  && source.Contains("FoxRunPolicy.Change", StringComparison.Ordinal)
                  && !source.Contains("foxrun.manifest", StringComparison.Ordinal),
                "112-E1: existing _FoxRun.g.cs source behavior remains unchanged");
        }

        private static void VerifyBoundaryAndWiring()
        {
            foreach (var path in new[]
            {
                ManifestDir + "/FoxRunManifestModel.cs",
                ManifestDir + "/FoxRunManifestBuilder.cs",
                ManifestDir + "/FoxRunManifestJsonWriter.cs",
                ManifestDir + "/FoxRunManifestHasher.cs",
                ManifestWriterPath,
                PlayModeHookPath
            })
            {
                Check(RepoFileExists(path), "112-F1: manifest source file exists: " + path);
            }

            var manifestCode = string.Join("\n", TextFiles(ManifestDir)
                .Concat(new[] { RepoPath(ManifestWriterPath), RepoPath(PlayModeHookPath) })
                .Select(File.ReadAllText));
            foreach (var token in new[] { "typed publisher", "MCAP", "replay" })
                Check(!manifestCode.Contains(token, StringComparison.OrdinalIgnoreCase),
                    "112-F2: manifest code avoids out-of-scope token: " + token);
            Check(FoxrunManifestWriter.CurrentManifestVersion == 3
                  && manifestCode.Contains("subscriptions", StringComparison.Ordinal)
                  && manifestCode.Contains("SupportsRos2Native", StringComparison.Ordinal)
                  && manifestCode.Contains("QosReliability", StringComparison.Ordinal)
                  && manifestCode.Contains("QosDurability", StringComparison.Ordinal)
                  && manifestCode.Contains("QosHistory", StringComparison.Ordinal)
                  && manifestCode.Contains("QosDepth", StringComparison.Ordinal),
                "112-F2a: manifest v3 owns the native ROS2 subscription and portable QoS contract");

            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase112Validation.cs", StringComparison.Ordinal)
                  && project.Contains("Editor/Shared/FoxRunManifest", StringComparison.Ordinal),
                "112-F3: test project explicitly compiles Phase112 validation and manifest shared code");

            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("--phase112", StringComparison.Ordinal)
                  && registry.Contains("Phase112Validation.Validate", StringComparison.Ordinal),
                "112-F4: registry wires --phase112");

            var gitignore = ReadRepoText(".gitignore");
            Check(gitignore.Contains("Unity2Foxglove/Assets/Generated/FoxRun/", StringComparison.Ordinal)
                  && gitignore.Contains("Unity2Foxglove/Assets/Generated.meta", StringComparison.Ordinal),
                "112-F5: generated manifest artifacts and Unity meta files are ignored");

            var generator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var memberData = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunMemberData.cs");
            Check(generator.Contains("FoxrunManifestWriter.WriteManifestFiles", StringComparison.Ordinal)
                  && generator.Contains("CollectManifestMembers", StringComparison.Ordinal)
                  && generator.Contains("GenerateManifestFilesOnly", StringComparison.Ordinal)
                  && memberData.Contains("ToManifestMember", StringComparison.Ordinal),
                "112-F6: build-time FoxRun generator writes manifest from resolved member scan");

            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");
            Check(writer.Contains("new UTF8Encoding(false)", StringComparison.Ordinal),
                "112-F7: manifest writer uses explicit UTF-8 without BOM for Unity compatibility");

            var hook = ReadRepoText(PlayModeHookPath);
            Check(hook.Contains("EditorApplication.playModeStateChanged", StringComparison.Ordinal)
                  && hook.Contains("PlayModeStateChange.ExitingEditMode", StringComparison.Ordinal)
                  && hook.Contains("GenerateManifestFilesOnly", StringComparison.Ordinal),
                "112-F8: Editor Play Mode entry refreshes manifest artifacts");
            Check(!hook.Contains("GenerateSourceFiles(", StringComparison.Ordinal)
                  && !hook.Contains("Scripts/Generated", StringComparison.Ordinal),
                "112-F9: Play Mode manifest hook does not write physical _FoxRun.g.cs fallback files");
        }

        private static void VerifyDocs()
        {
            var en = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md");
            var zh = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md");
            var research = ReadRepoText("docs/research-shared-emitter-architecture.md");

            foreach (var doc in new[] { en, zh, research })
            {
                Check(doc.Contains("canonical manifest", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("governance", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("timestamps", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("machine-local", StringComparison.OrdinalIgnoreCase),
                    "112-G1: docs describe canonical manifest governance and ignored local state");
            }
        }

        private static IReadOnlyList<FoxRunManifestMember> FixtureMembers(
            string typeName = "System.Single",
            string topic = "/phase112/battery",
            float hz = 10f)
        {
            return new[]
            {
                Member("Demo", "RobotState", "_batteryLevel", "field",
                    typeName, true, false, "", topic, hz, "", (int)FoxRunPolicy.Change, 0.001f)
            };
        }

        private static FoxRunManifestMember Member(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string typeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            float hz,
            string schemaName,
            int policy,
            float tolerance)
        {
            return new FoxRunManifestMember(
                ns,
                className,
                memberName,
                memberKind,
                typeName,
                isValueType,
                isArray,
                elementTypeName,
                topic,
                hz,
                schemaName,
                policy,
                tolerance);
        }

        private static IEnumerable<string> TextFiles(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!Directory.Exists(path))
                return Array.Empty<string>();

            return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(IsTextFile);
        }

        private static bool IsTextFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase112 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase112 validation.");
            return root;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (IOException ex)
            {
                Console.WriteLine("[WARN] Failed to delete temporary directory '" + path + "': " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("[WARN] Failed to delete temporary directory '" + path + "': " + ex.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
