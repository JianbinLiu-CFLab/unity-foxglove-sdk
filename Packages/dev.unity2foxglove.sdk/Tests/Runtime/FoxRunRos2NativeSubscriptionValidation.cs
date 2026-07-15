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

namespace Unity.FoxgloveSDK.Tests
{
    internal static class FoxRunRos2NativeSubscriptionValidation
    {
        private const string CorePackageRoot = "Packages/dev.unity2foxglove.sdk";
        private const string OptionalRuntimeAsmdefGuid = "f8feed905315b394d8d0f92bf2441283";
        private const string OptionalRuntimeAsmdefPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2Foxglove.Ros2ForUnity.asmdef";
        private const string ValidationSource =
            CorePackageRoot + "/Tests/Runtime/FoxRunRos2NativeSubscriptionValidation.cs";
        private const string ValidationMeta = ValidationSource + ".meta";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun Native ROS2 Subscription Boundary ---");
            _passed = 0;

            VerifyWireEncodingAndProviderAxes();
            VerifyDependencyInspectionCoverage();
            VerifyCoreDependencyBoundary();
            VerifyIndependentManagerSubscriptionSession();
            VerifyLegacyProviderMigrationDefaultsToWebSocket();
            VerifyExistingR2fuSinkRemainsOutboundOnly();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine(
                "FoxRun native ROS2 subscription boundary: " + _passed + " checks passed.\n");
        }

        private static void VerifyWireEncodingAndProviderAxes()
        {
            var wireNames = Enum.GetNames(typeof(FoxRunWireEncoding));
            Check(wireNames.SequenceEqual(new[] { "Inherit", "Protobuf", "Json" })
                  && (int)FoxRunWireEncoding.Inherit == 0
                  && (int)FoxRunWireEncoding.Protobuf == 1
                  && (int)FoxRunWireEncoding.Json == 2,
                "FoxRun wire encoding remains limited to Inherit, Protobuf, and Json");

            var providerNames = Enum.GetNames(typeof(FoxRunSubscriptionProvider));
            var providerProperty = typeof(FoxRunAttribute).GetProperty(
                nameof(FoxRunAttribute.SubscriptionProvider));
            Check(providerNames.SequenceEqual(
                      new[] { "Inherit", "FoxgloveWebSocket", "Ros2Native" })
                  && (int)FoxRunSubscriptionProvider.Inherit == 0
                  && (int)FoxRunSubscriptionProvider.FoxgloveWebSocket == 1
                  && (int)FoxRunSubscriptionProvider.Ros2Native == 2
                  && providerProperty?.PropertyType == typeof(FoxRunSubscriptionProvider)
                  && typeof(FoxRunSubscriptionProvider) != typeof(FoxRunWireEncoding),
                "FoxRun subscription provider remains a separate typed policy axis with stable values");
        }

        private static void VerifyDependencyInspectionCoverage()
        {
            var asmdefGuidFixture = JObject.Parse(
                "{\"references\":[\"GUID:" + OptionalRuntimeAsmdefGuid + "\"]}");
            var precompiledAsmdefFixture = JObject.Parse(
                "{\"precompiledReferences\":[\"ROS2.dll\"]}");
            var projectFixture = XDocument.Parse(
                "<Project><ItemGroup><Reference Include=\"ROS2ForUnity, Version=1.0.0.0\" /></ItemGroup></Project>");
            var packageFixture = JObject.Parse(
                "{\"dependencies\":{\"optional-adapter\":\"file:../dev.unity2foxglove.ros2forunity\"}}");
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var guidIndex = BuildAsmdefGuidIndex(repoRoot);
            var guidReference = ReadAsmdefReferences(asmdefGuidFixture).Single();
            var resolved = TryResolveAsmdefReference(guidReference, guidIndex, out var target);

            Check(resolved
                  && target.RelativePath == OptionalRuntimeAsmdefPath
                  && target.Name == "Unity2Foxglove.Ros2ForUnity"
                  && IsOptionalAsmdefReference(guidReference, guidIndex)
                  && !IsOptionalAsmdefReference(
                      "GUID:00000000000000000000000000000000",
                      guidIndex)
                  && ReadAsmdefReferences(precompiledAsmdefFixture).Any(IsOptionalNativeDependency)
                  && ReadProjectDependencyReferences(projectFixture).Any(IsOptionalNativeDependency)
                  && ReadPackageDependencyReferences(packageFixture).Any(IsOptionalNativeDependency)
                  && IsBuildArtifactPath(Path.Combine("source", "obj", "generated.csproj"))
                  && !IsBuildArtifactPath(Path.Combine("source", "Runtime", "source.csproj")),
                "dependency inspection resolves tracked asmdef GUIDs, covers direct and precompiled references, and excludes build artifacts");
        }

        private static void VerifyCoreDependencyBoundary()
        {
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var coreRoot = Path.Combine(
                repoRoot,
                CorePackageRoot.Replace('/', Path.DirectorySeparatorChar));
            var guidIndex = BuildAsmdefGuidIndex(repoRoot);

            var forbiddenAsmdefReferences = EnumerateSourceDefinitionFiles(coreRoot, "*.asmdef")
                .SelectMany(ReadAsmdefReferences)
                .Where(reference => IsOptionalAsmdefReference(reference, guidIndex))
                .ToArray();
            Check(forbiddenAsmdefReferences.Length == 0,
                "core asmdef references remain free of optional ROS2 For Unity dependencies");

            var forbiddenProjectReferences = EnumerateSourceDefinitionFiles(coreRoot, "*.csproj")
                .SelectMany(ReadProjectDependencyReferences)
                .Where(IsOptionalNativeDependency)
                .ToArray();
            var package = JObject.Parse(PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/package.json"));
            var packageReferences = ReadPackageDependencyReferences(package);
            Check(forbiddenProjectReferences.Length == 0
                  && !packageReferences.Any(IsOptionalNativeDependency),
                "core project and package references remain free of optional ROS2 For Unity dependencies");
        }

        private static void VerifyIndependentManagerSubscriptionSession()
        {
            var manager = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var session = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs");
            var onEnable = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnEnable()");
            var update = PhaseValidationSourceHelpers.SourceMethod(manager, "private void Update()");
            var onDisable = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnDisable()");
            var onDestroy = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnDestroy()");
            var sync = PhaseValidationSourceHelpers.SourceMethod(
                session,
                "private void SyncFoxRunSubscriptionSession()");

            Check(OccursBefore(
                      onEnable,
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "StartServer();")
                  && update.Contains("SyncFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && sync.Contains("if (_enableFoxRunInbound)", StringComparison.Ordinal)
                  && sync.Contains("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal)
                  && sync.Contains("EndFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && OccursBefore(
                      onDisable,
                      "EndFoxRunSubscriptionSession();",
                      "StopServer(restoreLivePublishers: true);")
                  && OccursBefore(
                      onDestroy,
                      "EndFoxRunSubscriptionSession();",
                      "StopServer(restoreLivePublishers: true);"),
                "Manager enable, update, and teardown own the subscription session lifecycle");

            var startServer = PhaseValidationSourceHelpers.SourceMethod(
                server,
                "public void StartServer()");
            var stopServer = PhaseValidationSourceHelpers.SourceMethod(
                server,
                "private void StopServer(bool restoreLivePublishers)");
            Check(OccursBefore(
                      startServer,
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "if (!_foxgloveOutputEnabled)")
                  && !stopServer.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && stopServer.Contains("ClearFoxRunPublishEncodingForServer();", StringComparison.Ordinal),
                "WebSocket start only ensures the session while WebSocket stop preserves it");
        }

        private static void VerifyLegacyProviderMigrationDefaultsToWebSocket()
        {
            var inbound = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var managerMigration = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var migration = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunWireEncodingPolicyMigration.cs");
            var legacyBranch = migration.IndexOf(
                "if (serializationVersion < CurrentSerializationVersion)",
                StringComparison.Ordinal);
            var legacyWebSocket = migration.IndexOf(
                "providerDefault = FoxRunSubscriptionProvider.FoxgloveWebSocket;",
                legacyBranch < 0 ? 0 : legacyBranch,
                StringComparison.Ordinal);
            var normalizeCall = migration.IndexOf(
                "providerDefault = NormalizeSubscriptionProvider(providerDefault);",
                legacyBranch < 0 ? 0 : legacyBranch,
                StringComparison.Ordinal);
            var normalizeDefinition = migration.IndexOf(
                "private static FoxRunSubscriptionProvider NormalizeSubscriptionProvider",
                StringComparison.Ordinal);
            var preserveNative = migration.IndexOf(
                "? FoxRunSubscriptionProvider.Ros2Native",
                normalizeDefinition < 0 ? 0 : normalizeDefinition,
                StringComparison.Ordinal);
            var fallbackWebSocket = migration.IndexOf(
                ": FoxRunSubscriptionProvider.FoxgloveWebSocket",
                normalizeDefinition < 0 ? 0 : normalizeDefinition,
                StringComparison.Ordinal);

            Check(inbound.Contains(
                      "_defaultFoxRunSubscriptionProvider = FoxRunSubscriptionProvider.FoxgloveWebSocket",
                      StringComparison.Ordinal)
                  && managerMigration.Contains("ISerializationCallbackReceiver.OnAfterDeserialize()", StringComparison.Ordinal)
                  && managerMigration.Contains("ref _defaultFoxRunSubscriptionProvider", StringComparison.Ordinal)
                  && legacyBranch >= 0
                  && legacyWebSocket > legacyBranch
                  && normalizeCall > legacyWebSocket
                  && normalizeDefinition > normalizeCall
                  && preserveNative > normalizeDefinition
                  && fallbackWebSocket > preserveNative,
                "serialized provider migration defaults legacy or invalid values to WebSocket and preserves native");
        }

        private static void VerifyExistingR2fuSinkRemainsOutboundOnly()
        {
            var sink = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Ros2R2FUTopicSink.cs");
            Check(sink.Contains(
                      "public sealed class Ros2R2FUTopicSink : IFoxTopicSink",
                      StringComparison.Ordinal)
                  && sink.Contains("IRos2TopicPublisherFactory", StringComparison.Ordinal)
                  && sink.Contains("publisher.TryPublish(", StringComparison.Ordinal)
                  && !sink.Contains("CreateSubscription", StringComparison.Ordinal)
                  && !sink.Contains("IUnity2FoxgloveRos2Subscription", StringComparison.Ordinal)
                  && !sink.Contains("IRos2TopicSubscriber", StringComparison.Ordinal),
                "existing R2FU topic sink remains outbound-only without subscription ownership");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var entries = PhaseValidationRegistry.All
                .Where(item => string.Equals(item.Flag, "--phase179", StringComparison.Ordinal))
                .ToArray();
            Check(entries.Length == 1
                  && entries[0].Name == "FoxRun native ROS2 subscription boundary"
                  && entries[0].Category == ValidationCategory.CiSafe
                  && entries[0].Evidence == ValidationEvidence.Structural
                  && !entries[0].IncludeInDefault
                  && entries[0].Run.Method.DeclaringType == typeof(FoxRunRos2NativeSubscriptionValidation),
                "native subscription registry entry is singular, structural, CI-safe, and excluded from default");

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
                      "<Compile Include=\"FoxRunRos2NativeSubscriptionValidation.cs\" />") == 1
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
