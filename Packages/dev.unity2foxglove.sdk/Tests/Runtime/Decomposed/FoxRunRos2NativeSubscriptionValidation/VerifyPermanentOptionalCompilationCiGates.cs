// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards the FoxRun native subscription policy and dependency boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class FoxRunRos2NativeSubscriptionValidation
    {
        private static void VerifyPermanentOptionalCompilationCiGates()
        {
            var localCi = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Scripts/release/run_ci.py");
            var workflow = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                ".github/workflows/dotnet-tests.yml");
            const string inboundAcceptanceRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_inbound_acceptance";
            const string playerHostRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_player_host";
            const string matrixProfilesRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_matrix_profiles";
            const string zenohTopologyRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_zenoh_topology";

            Check(localCi.Contains("UNIT_ADAPTER_TEST_PROPS", StringComparison.Ordinal)
                  && localCi.Contains("UNIT_NATIVE_TEST_PROPS", StringComparison.Ordinal)
                  && localCi.Contains("-p:IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal)
                  && localCi.Contains("-p:IncludeRos2ForUnityNative=true", StringComparison.Ordinal),
                "local CI runs dedicated optional adapter and Native compile/test lanes");

            Check(workflow.Contains("optional-ros2-adapter:", StringComparison.Ordinal)
                  && workflow.Contains("name: optional ROS2 adapter gate", StringComparison.Ordinal)
                  && workflow.Contains("optional-ros2-native:", StringComparison.Ordinal)
                  && workflow.Contains("name: optional ROS2 Native gate", StringComparison.Ordinal)
                  && workflow.Contains("-p:IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal)
                  && workflow.Contains("-p:IncludeRos2ForUnityNative=true", StringComparison.Ordinal),
                "GitHub Actions keeps the default test check and names permanent optional adapter and Native gates");

            Check(localCi.Contains("phase179-ros2-regression", StringComparison.Ordinal)
                  && localCi.Contains(inboundAcceptanceRegression, StringComparison.Ordinal)
                  && localCi.Contains(playerHostRegression, StringComparison.Ordinal)
                  && localCi.Contains(matrixProfilesRegression, StringComparison.Ordinal)
                  && localCi.Contains(zenohTopologyRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + inboundAcceptanceRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + playerHostRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + matrixProfilesRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + zenohTopologyRegression, StringComparison.Ordinal),
                "local and GitHub CI run the pure Phase179 Linux, Windows, profile, and Zenoh helper regression modules");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var entries = PhaseValidationRegistry.All
                .Where(item => string.Equals(item.Flag, "--phase179", StringComparison.Ordinal))
                .ToArray();
            Check(entries.Length == 1
                  && entries[0].Name == "FoxRun R2FU subscription Provider boundary"
                  && entries[0].Category == ValidationCategory.CiSafe
                  && entries[0].Evidence == (ValidationEvidence.Behavior | ValidationEvidence.Structural)
                  && entries[0].IncludeInDefault
                  && entries[0].Run.Method.DeclaringType == typeof(FoxRunRos2NativeSubscriptionValidation),
                "native subscription registry entry is singular, behavior-and-structural, CI-safe, and included in default");

            var registry = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var meta = PhaseValidationSourceHelpers.ReadRequiredRepoText(ValidationMeta);
            var guidLine = meta
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .SingleOrDefault(line => line.StartsWith("guid: ", StringComparison.Ordinal));
            var guid = guidLine?.Substring("guid: ".Length) ?? string.Empty;
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var numericValidationPath = Path.Combine(
                repoRoot,
                CorePackageRoot.Replace('/', Path.DirectorySeparatorChar),
                "Tests",
                "Runtime",
                "Phase179Validation.cs");

            Check(CountOccurrences(registry, "\"--phase179\"") == 1
                  && CountOccurrences(
                      project,
                      "<Compile Include=\"FoxRunRos2NativeSubscriptionValidation.cs\"") == 1
                  && project.Contains("FoxRunRos2NativeSubscriptionValidation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase179Validation.cs", StringComparison.Ordinal)
                  && !File.Exists(numericValidationPath)
                  && guid.Length == 32
                  && guid.All(Uri.IsHexDigit)
                  && meta.Contains("MonoImporter:", StringComparison.Ordinal),
                "descriptive validator source, project wiring, and Unity metadata remain singular and valid");
        }

        private static IEnumerable<string> ReadAsmdefReferences(string path)
        {
            return ReadAsmdefReferences(JObject.Parse(File.ReadAllText(path)));
        }

        private static IEnumerable<string> ReadAsmdefReferences(JObject root)
        {
            return new[] { "references", "precompiledReferences" }
                .SelectMany(property => (root[property] as JArray ?? new JArray()).Values<string>())
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .ToArray();
        }

        private static IEnumerable<string> ReadProjectDependencyReferences(string path)
        {
            return ReadProjectDependencyReferences(XDocument.Load(path));
        }

        private static IEnumerable<string> ReadProjectDependencyReferences(XDocument document)
        {
            return document
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference"
                                  || element.Name.LocalName == "PackageReference"
                                  || element.Name.LocalName == "Reference")
                .Select(element => (string)element.Attribute("Include"))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .ToArray();
        }

        private static IEnumerable<string> ReadPackageDependencyReferences(JObject package)
        {
            if (!(package["dependencies"] is JObject dependencies))
                return Array.Empty<string>();

            var references = new List<string>();
            foreach (var property in dependencies.Properties())
            {
                references.Add(property.Name);
                if (property.Value.Type == JTokenType.String)
                    references.Add(property.Value.Value<string>());
            }

            return references;
        }

        private static IEnumerable<string> EnumerateSourceDefinitionFiles(
            string root,
            string searchPattern)
        {
            return Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
                .Where(path => !IsBuildArtifactPath(path));
        }

        private static bool IsBuildArtifactPath(string path)
        {
            return path
                .Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsTestProjectPath(string coreRoot, string path)
        {
            var relative = Path.GetRelativePath(coreRoot, path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return relative.StartsWith("Tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, AsmdefIdentity> BuildAsmdefGuidIndex(
            string repoRoot)
        {
            var packagesRoot = Path.Combine(repoRoot, "Packages");
            var index = new Dictionary<string, AsmdefIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var asmdefPath in EnumerateSourceDefinitionFiles(packagesRoot, "*.asmdef")
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var metaPath = asmdefPath + ".meta";
                if (!File.Exists(metaPath))
                    continue;

                var guid = ReadUnityAssetGuid(metaPath);
                if (string.IsNullOrEmpty(guid))
                    continue;

                var root = JObject.Parse(File.ReadAllText(asmdefPath));
                var identity = new AsmdefIdentity(
                    Path.GetRelativePath(repoRoot, asmdefPath).Replace('\\', '/'),
                    root.Value<string>("name") ?? string.Empty);
                if (index.TryGetValue(guid, out var existing))
                {
                    throw new InvalidOperationException(
                        "Duplicate asmdef GUID " + guid + " maps to both "
                        + existing.RelativePath + " and " + identity.RelativePath + ".");
                }

                index.Add(guid, identity);
            }

            return index;
        }

        private static string ReadUnityAssetGuid(string metaPath)
        {
            var prefix = "guid: ";
            var line = File.ReadLines(metaPath)
                .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            var guid = line?.Substring(prefix.Length).Trim() ?? string.Empty;
            return guid.Length == 32 && guid.All(Uri.IsHexDigit) ? guid : string.Empty;
        }

        private static bool HasDiagnosticDescriptorId(string source, string descriptorName, string id)
        {
            var marker = "public static readonly DiagnosticDescriptor " + descriptorName
                         + " = new DiagnosticDescriptor(";
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return false;

            var next = source.IndexOf(
                "public static readonly DiagnosticDescriptor ",
                start + marker.Length,
                StringComparison.Ordinal);
            var descriptor = next < 0 ? source.Substring(start) : source.Substring(start, next - start);
            return descriptor.Contains("\"" + id + "\"", StringComparison.Ordinal);
        }

        private static bool HasReleaseTrackingRow(string releaseTracking, string id, string note)
        {
            return releaseTracking
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Any(line => line.StartsWith(id + " | FoxRun |", StringComparison.Ordinal)
                             && line.Contains(note, StringComparison.Ordinal));
        }

        private static bool HasReservedBeforeReleaseRow(string releaseTracking, string id, string note)
        {
            return releaseTracking
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Any(line => line.StartsWith("; " + id + " | FoxRun |", StringComparison.Ordinal)
                             && line.Contains(note, StringComparison.Ordinal));
        }

        private static bool HasCompleteUnityScriptMetadata(string metaPath)
        {
            if (!File.Exists(metaPath))
                return false;

            var metadata = File.ReadAllText(metaPath).Replace("\r\n", "\n");
            return metadata.EndsWith("\n", StringComparison.Ordinal)
                   && metadata.Contains(
                       "MonoImporter:\n"
                       + "  externalObjects: {}\n"
                       + "  serializedVersion: 2\n"
                       + "  defaultReferences: []\n"
                       + "  executionOrder: 0\n"
                       + "  icon: {instanceID: 0}\n"
                       + "  userData:\n"
                       + "  assetBundleName:\n"
                       + "  assetBundleVariant:\n",
                       StringComparison.Ordinal);
        }

        private static int CountUnityAssetGuidOccurrences(string repoRoot, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return 0;

            return new[]
                {
                    Path.Combine(repoRoot, "Packages"),
                    Path.Combine(repoRoot, "Unity2Foxglove", "Assets"),
                }
                .Where(Directory.Exists)
                .SelectMany(root => EnumerateSourceDefinitionFiles(root, "*.meta"))
                .Count(path => string.Equals(
                    ReadUnityAssetGuid(path),
                    guid,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOptionalAsmdefReference(
            string reference,
            IReadOnlyDictionary<string, AsmdefIdentity> guidIndex)
        {
            if (IsOptionalNativeDependency(reference))
                return true;

            return TryResolveAsmdefReference(reference, guidIndex, out var target)
                   && (IsOptionalNativeDependency(target.RelativePath)
                       || IsOptionalNativeDependency(target.Name));
        }

        private static bool TryResolveAsmdefReference(
            string reference,
            IReadOnlyDictionary<string, AsmdefIdentity> guidIndex,
            out AsmdefIdentity identity)
        {
            identity = null;
            const string prefix = "GUID:";
            if (string.IsNullOrWhiteSpace(reference)
                || !reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var guid = reference.Substring(prefix.Length).Trim();
            return guid.Length == 32
                   && guid.All(Uri.IsHexDigit)
                   && guidIndex.TryGetValue(guid, out identity);
        }

        private static bool IsOptionalNativeDependency(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return false;

            var normalized = reference.Replace('\\', '/').Trim();
            return normalized.Contains("Unity2Foxglove.Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("dev.unity2foxglove.ros2forunity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("ros2-for-unity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("r2fu", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "ROS2", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("ROS2.", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("ROS2,", StringComparison.OrdinalIgnoreCase);
        }

        private static bool OccursBefore(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static string RequiredRos2ForUnityMethod(
            string source,
            string signature,
            string fileName)
        {
            var method = PhaseValidationSourceHelpers.SourceMethodWithPreprocessorSymbols(
                source,
                signature,
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                "UNITY_EDITOR_WIN");
            if (method.Length == 0)
                throw new InvalidOperationException("[FAIL] missing active R2FU method in " + fileName + ": " + signature);
            return method;
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }

        private sealed class AsmdefIdentity
        {
            public AsmdefIdentity(string relativePath, string name)
            {
                RelativePath = relativePath;
                Name = name;
            }

            public string RelativePath { get; }
            public string Name { get; }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
