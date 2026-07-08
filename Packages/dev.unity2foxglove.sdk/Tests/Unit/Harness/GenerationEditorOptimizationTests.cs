// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-68/69/70/71/72/73 generation and editor optimization checks.

using System;
using System.IO;
using System.Text.Json;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-68")]
    [Trait("Domain", "Harness")]
    public sealed class FoxRunSharedEmitterOptimizationTests
    {
        [Fact]
        public void SharedEmitterHotPathsAvoidRedundantWork()
        {
            var validator = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            var model = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var formatter = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunEmissionTypeNameFormatter.cs");
            var comparer = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorComparer.cs");
            var reconciler = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGeneratedSourceReconciler.cs");
            var writer = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorJsonWriter.cs");

            Assert.Contains("private static readonly string[] UnityNativeContainerPrefixes", validator, StringComparison.Ordinal);
            Assert.DoesNotContain("new[]", TestSources.ExtractMethod(validator, "private static bool IsUnityNativeContainerTypeName"), StringComparison.Ordinal);
            Assert.Contains("return ElementTypeName;", TestSources.ExtractMethod(model, "private string SelectCanonicalSourceType()"), StringComparison.Ordinal);
            Assert.DoesNotContain("NormalizeCSharpTypeName(ElementTypeName)", TestSources.ExtractMethod(model, "private string SelectCanonicalSourceType()"), StringComparison.Ordinal);
            Assert.Contains("IndexOf(\"global::\", StringComparison.Ordinal)", formatter, StringComparison.Ordinal);
            Assert.Contains("IndexOf('+')", formatter, StringComparison.Ordinal);
            Assert.Contains("CompareSortedMemberKeys", comparer, StringComparison.Ordinal);
            Assert.DoesNotContain(".Except(", comparer, StringComparison.Ordinal);
            Assert.DoesNotContain(".Intersect(", comparer, StringComparison.Ordinal);
            Assert.Contains("AsReadOnly()", comparer, StringComparison.Ordinal);
            Assert.Contains("copyInputs: false", comparer, StringComparison.Ordinal);
            Assert.Contains("StreamReader", reconciler, StringComparison.Ordinal);
            Assert.Contains("ReadLine()", reconciler, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllText(path)", reconciler, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Linq", reconciler, StringComparison.Ordinal);
            Assert.Contains("new StringBuilder(EstimateCapacity(model))", writer, StringComparison.Ordinal);
        }

        [Fact]
        public void SharedEmitterOutputSemanticsArePreserved()
        {
            var model = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType(
                    "Demo",
                    "Car",
                    new[]
                    {
                        new FoxRunGenerationMember(
                            "Demo",
                            "Car",
                            "speed",
                            "field",
                            "System.Single",
                            "float",
                            "float",
                            true,
                            false,
                            "",
                            "/vehicle/speed",
                            30f,
                            "unity2foxglove.Float32",
                            3,
                            0.001f,
                            1f,
                            "reflection",
                            7,
                            "UNITY_EDITOR"),
                        new FoxRunGenerationMember(
                            "Demo",
                            "Car",
                            "samples",
                            "property",
                            "System.Single[]",
                            "float[]",
                            "",
                            false,
                            true,
                            "global::System.Single",
                            "/vehicle/samples",
                            0f,
                            "unity2foxglove.Float32",
                            3,
                            0f,
                            0f,
                            "roslyn",
                            8,
                            "")
                    })
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);
            var comparison = FoxRunGenerationDescriptorComparer.Compare(model, model);

            Assert.Contains("\"descriptorVersion\":1", json, StringComparison.Ordinal);
            Assert.Contains("\"elementTypeName\":\"float\"", json, StringComparison.Ordinal);
            Assert.Contains("\"topic\":\"/vehicle/samples\"", json, StringComparison.Ordinal);
            Assert.True(comparison.IsSemanticEqual);
            Assert.True(comparison.IsProvenanceEqual);
        }

        [Fact]
        public void DescriptorWriterRejectsLoneSurrogatesAndEscapesPairs()
        {
            var lone = CreateDescriptorModel("\uD800");
            Assert.Throws<InvalidOperationException>(() => FoxRunGenerationDescriptorJsonWriter.Write(lone));

            var paired = CreateDescriptorModel("face\U0001F600");
            var json = FoxRunGenerationDescriptorJsonWriter.Write(paired);

            Assert.Contains("\\ud83d\\ude00", json, StringComparison.Ordinal);
            using var _ = JsonDocument.Parse(json);
        }

        [Fact]
        public void GeneratedSourceOwnershipStillUsesHeaderSentinels()
        {
            var temp = Path.Combine(Path.GetTempPath(), "u2f_phase140_68_" + Guid.NewGuid().ToString("N") + ".g.cs");
            try
            {
                File.WriteAllText(
                    temp,
                    "// <auto-generated/>\n// " + FoxRunGeneratedSourceReconciler.GeneratedSourceSentinel + "\npublic class A {}\n");
                Assert.True(FoxRunGeneratedSourceReconciler.IsOwnedGeneratedSourceFile(temp));
                File.WriteAllText(temp, "public class B {}\n");
                Assert.False(FoxRunGeneratedSourceReconciler.IsOwnedGeneratedSourceFile(temp));
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        [Fact]
        public void Phase14068MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_68Validation.cs", "--phase140-68", "Phase140_68Validation.Validate");

        private static FoxRunGenerationModel CreateDescriptorModel(string jsonFieldName)
            => new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType(
                    "Demo",
                    "Text",
                    new[]
                    {
                        new FoxRunGenerationMember(
                            "Demo",
                            "Text",
                            "value",
                            "field",
                            "System.String",
                            "string",
                            "string",
                            false,
                            false,
                            "",
                            "/demo/text",
                            0f,
                            "unity2foxglove.String",
                            1,
                            0f,
                            0f,
                            "reflection",
                            1,
                            "",
                            jsonFieldName: jsonFieldName)
                    })
            });

        [Fact]
        public void TestSourceSlicesNormalizeLineEndings()
        {
            var source = "alpha\r\nbeta\r\ngamma";

            var slice = TestSources.Slice(source, "alpha\n", "\ngamma");

            Assert.Equal("alpha\nbeta", slice);
        }
    }

    [Trait("Phase", "140-69")]
    [Trait("Domain", "Harness")]
    public sealed class FoxRunGenerationHostOptimizationTests
    {
        [Fact]
        public void RoslynGeneratorHotPathsAvoidPerCandidateLinq()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var hasFoxRunAttr = TestSources.Slice(source, "private static bool HasFoxRunAttr", "private static MemberData ExtractMember");
            var extractMember = TestSources.Slice(source, "private static MemberData ExtractMember", "private static bool TryReadFloatConstant");
            var generate = TestSources.Slice(source, "private static void Generate", "private static void EmitClass");
            var toRoslynMembers = TestSources.Slice(source, "public IReadOnlyList<FoxRunRoslynGenerationMember> ToRoslynMembers", "        }\r\n\r\n        /// <summary>\r\n        /// Immutable tuple");

            Assert.Contains("AttrAttributeName", hasFoxRunAttr, StringComparison.Ordinal);
            Assert.Contains("AttrQualifiedNameSuffix", hasFoxRunAttr, StringComparison.Ordinal);
            Assert.Contains("AttrQualifiedAttributeNameSuffix", hasFoxRunAttr, StringComparison.Ordinal);
            Assert.DoesNotContain("AttrShortName +", hasFoxRunAttr, StringComparison.Ordinal);
            Assert.DoesNotContain(".Where(a => a.AttributeClass?.ToDisplayString() == AttrFullName)", extractMember, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToList()", extractMember, StringComparison.Ordinal);
            Assert.Contains("AppendRoslynMembers", generate, StringComparison.Ordinal);
            Assert.DoesNotContain("items.Where", generate, StringComparison.Ordinal);
            Assert.DoesNotContain("SelectMany(m => m.ToRoslynMembers()).ToList()", generate, StringComparison.Ordinal);
            Assert.DoesNotContain(".GroupBy(m => (m.Ns, m.ClassName))", generate, StringComparison.Ordinal);
            Assert.Contains("AppendRoslynMembers", toRoslynMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("Topics.Select", toRoslynMembers, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToList()", toRoslynMembers, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorReviewFixesStayPinned()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var generate = TestSources.Slice(source, "private static void Generate", "private static string DiagnosticDeclaringType");
            var services = TestSources.Slice(source, "private static void GenerateServices", "        /// <summary>\r\n        /// Emits the generated partial class");
            var locationFor = TestSources.ExtractMethod(source, "private static Location LocationFor");
            var chunkedDescriptor = TestSources.ExtractMethod(source, "private static string ChunkedDescriptorCarrierSource");
            var escape = TestSources.ExtractMethod(source, "private static string EscapeStringLiteral");

            Assert.Contains("catch (Exception ex) when (ex is OverflowException || ex is InvalidCastException || ex is FormatException)", source, StringComparison.Ordinal);
            Assert.Contains("roslynMemberCapacity", generate, StringComparison.Ordinal);
            Assert.Contains("item.Topics?.Length ?? 0", generate, StringComparison.Ordinal);
            Assert.Contains("var servicesByName = new Dictionary<string, List<ServiceMethodData>>(StringComparer.Ordinal);", services, StringComparison.Ordinal);
            Assert.DoesNotContain(".GroupBy(item => item.ServiceName", services, StringComparison.Ordinal);
            Assert.Contains("diagnostic.Target ?? string.Empty", locationFor, StringComparison.Ordinal);
            Assert.Contains("public static readonly string DescriptorJson = string.Concat(", chunkedDescriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("public static string DescriptorJson => string.Concat(", chunkedDescriptor, StringComparison.Ordinal);
            Assert.Contains("char.IsHighSurrogate(ch)", escape, StringComparison.Ordinal);
            Assert.Contains("char.IsLowSurrogate(ch)", escape, StringComparison.Ordinal);
        }

        [Fact]
        public void EditorGeneratorPreservesLoadedAssemblyDiscovery()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var scanner = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");
            var collectTypes = TestSources.Slice(source, "public static List<(string AsmName, string Ns, string ClassName)> CollectFoxRunTypes()", "        /// <summary>\r\n        /// Generate an IL2CPP link.xml snippet");
            var scanMembers = TestSources.Slice(scanner, "private static FoxRunScanResult ScanFoxRunMembers", "        /// <summary>\r\n        /// Checks whether a type was declared");
            var validate = TestSources.Slice(source, "private static void ValidateGenerationModel", "private static string GetManifestOutputDirectory");
            var emitSourceFile = TestSources.Slice(source, "public static string EmitSourceFile(MemberData[] members)", "public static string EmitSourceFile(FoxRunGenerationType type)");

            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", source, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", source, StringComparison.Ordinal);
            Assert.Contains("ReflectionTypeLoadException", source, StringComparison.Ordinal);
            Assert.DoesNotContain("TypeCache.GetTypesDerivedFrom<MonoBehaviour>()", source, StringComparison.Ordinal);
            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", collectTypes, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", collectTypes, StringComparison.Ordinal);
            Assert.Contains("ReflectionTypeLoadException", scanner, StringComparison.Ordinal);
            Assert.Contains("AppDomain.CurrentDomain.GetAssemblies()", scanMembers, StringComparison.Ordinal);
            Assert.Contains("typeof(MonoBehaviour).IsAssignableFrom(type)", scanMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("members.Select(member => member.ToManifestMember())", scanMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("members.Select(member => member.ToReflectionMember())", scanMembers, StringComparison.Ordinal);
            Assert.DoesNotContain("diagnostics.Where", validate, StringComparison.Ordinal);
            Assert.DoesNotContain("members.Select(member => member.ToReflectionMember()).ToList()", emitSourceFile, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14069MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_69Validation.cs", "--phase140-69", "Phase140_69Validation.Validate");
    }

    [Trait("Phase", "140-70")]
    [Trait("Domain", "Harness")]
    public sealed class InspectorEditorOptimizationTests
    {
        [Fact]
        public void CameraAndBasePublisherEditorsCacheRepaintState()
        {
            var camera = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var cameraInspector = TestSources.Slice(camera, "public override void OnInspectorGUI()", "        private static string[] BuildCameraOutputModeLabels");
            var publisherBase = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs");
            var baseInspector = TestSources.Slice(publisherBase, "public override void OnInspectorGUI()", "        private void CacheDefaultProperties()");

            Assert.Contains("private SerializedProperty _manager;", camera, StringComparison.Ordinal);
            Assert.Contains("private void OnEnable()", camera, StringComparison.Ordinal);
            Assert.DoesNotContain("serializedObject.FindProperty(\"_manager\")", cameraInspector, StringComparison.Ordinal);
            Assert.DoesNotContain("serializedObject.FindProperty(\"_publishRateSource\")", cameraInspector, StringComparison.Ordinal);
            Assert.DoesNotContain("serializedObject.FindProperty(\"_ros2BridgeOutput\")", cameraInspector, StringComparison.Ordinal);
            Assert.Contains("private static GUIContent Label(string text)", camera, StringComparison.Ordinal);
            Assert.DoesNotContain("new GUIContent(\"", cameraInspector, StringComparison.Ordinal);
            Assert.Contains("private readonly System.Collections.Generic.List<SerializedProperty> _defaultProperties", publisherBase, StringComparison.Ordinal);
            Assert.Contains("CacheDefaultProperties()", publisherBase, StringComparison.Ordinal);
            Assert.DoesNotContain("serializedObject.GetIterator()", baseInspector, StringComparison.Ordinal);
            Assert.DoesNotContain("NextVisible", baseInspector, StringComparison.Ordinal);
            Assert.Contains("private static GUIContent Label(string text)", publisherBase, StringComparison.Ordinal);
            Assert.DoesNotContain("new GUIContent(\"", baseInspector, StringComparison.Ordinal);
        }

        [Fact]
        public void ManagerEditorAndCameraInfoCachesStayHoisted()
        {
            var manager = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var transport = TestSources.Slice(manager, "private void DrawTransportModeProperty()", "private void DrawFloatProperty");
            var mcap = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var replayAutoPlay = TestSources.Slice(mcap, "private void DrawReplayAutoPlayControl()", "private void DrawRemoteFileAccessSection");
            var cameraInfo = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");
            var getObjectFieldType = TestSources.Slice(cameraInfo, "private static System.Type GetObjectFieldType", "    }\r\n}");

            Assert.Contains("private static readonly string[] TransportModeLabels", manager, StringComparison.Ordinal);
            Assert.DoesNotContain("new[]", transport, StringComparison.Ordinal);
            Assert.Contains("var remoteFileServerEnabled", replayAutoPlay, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(replayAutoPlay, "GetBool(\"_enableRemoteMcapFileServer\")"));
            Assert.Contains("_cachedRootCaFingerprint", manager, StringComparison.Ordinal);
            Assert.Contains("GetCachedRootCaFingerprint", manager, StringComparison.Ordinal);
            Assert.Contains("ObjectFieldTypeCache", cameraInfo, StringComparison.Ordinal);
            Assert.Contains("ObjectFieldTypeCache.TryGetValue", getObjectFieldType, StringComparison.Ordinal);
            Assert.Contains("ObjectFieldTypeCache[typeName]", getObjectFieldType, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14070MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_70Validation.cs", "--phase140-70", "Phase140_70Validation.Validate");
    }

    [Trait("Phase", "140-71")]
    [Trait("Domain", "Harness")]
    public sealed class SchemaEvidenceManifestOptimizationTests
    {
        [Fact]
        public void SchemaEvidencePathsAndSettingsCacheProjectState()
        {
            var paths = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var projectRoot = TestSources.Slice(paths, "private static string ProjectRoot", "        private static string ResolveProjectRoot()");
            var settings = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            var drawSettings = TestSources.Slice(settings, "private static void DrawSettings()", "        private static void SaveAndSync()");

            Assert.Contains("private static readonly Lazy<string> CachedProjectRoot = new Lazy<string>(ResolveProjectRoot);", paths, StringComparison.Ordinal);
            Assert.Contains("private static string ProjectRoot => CachedProjectRoot.Value;", paths, StringComparison.Ordinal);
            Assert.DoesNotContain("Application.dataPath", projectRoot, StringComparison.Ordinal);
            Assert.DoesNotContain("Directory.GetParent", projectRoot, StringComparison.Ordinal);
            Assert.Contains("var resolvedRoot = ResolveCurrentEvidenceRootCached();", drawSettings, StringComparison.Ordinal);
            Assert.Contains("TryNormalizeAssetsRootCached(root", drawSettings, StringComparison.Ordinal);
            Assert.Contains("private static string s_resolvedRootCacheKey;", settings, StringComparison.Ordinal);
            Assert.Contains("private static bool TryNormalizeAssetsRootCached", settings, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(settings, "Unity2FoxgloveSchemaEvidencePaths.ResolveCurrentEvidenceRoot()"));
            Assert.Contains("Directory.CreateDirectory(resolvedRoot)", drawSettings, StringComparison.Ordinal);
            Assert.Contains("EditorUtility.RevealInFinder(resolvedRoot)", drawSettings, StringComparison.Ordinal);
        }

        [Fact]
        public void SchemaManifestBuilderAvoidsRepeatedSortAndAllocation()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            var buildFoxRun = TestSources.Slice(source, "private static Unity2FoxgloveFoxRunSummarySection BuildFoxRunSection", "        private static Unity2FoxgloveProtobufRegistrySection BuildProtobufRegistrySection()");
            var buildSdk = TestSources.Slice(source, "private static Unity2FoxgloveSdkTypedPublishersSection BuildSdkTypedPublishersSection()", "        private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> GetSortedSdkTypedPublisherEntries()");
            var getSorted = TestSources.Slice(source, "private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> GetSortedSdkTypedPublisherEntries()", "        private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> BuildSortedSdkTypedPublisherEntries()");
            var buildSorted = TestSources.Slice(source, "private static IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry> BuildSortedSdkTypedPublisherEntries()", "        private static void ValidatePublisherCatalog");

            Assert.Contains("foreach (var type in types)", buildFoxRun, StringComparison.Ordinal);
            Assert.Contains("contracts += type.Contracts.Count;", buildFoxRun, StringComparison.Ordinal);
            Assert.Contains("fields += contract.Fields.Count;", buildFoxRun, StringComparison.Ordinal);
            Assert.DoesNotContain(".Sum(", buildFoxRun, StringComparison.Ordinal);
            Assert.DoesNotContain("private static string Sha256Hex(byte[] bytes)", source, StringComparison.Ordinal);
            Assert.Contains("FoxRunManifestHasher.Sha256Hex", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<IReadOnlyList<Unity2FoxgloveSdkTypedPublisherEntry>> SortedSdkTypedPublisherEntries", source, StringComparison.Ordinal);
            Assert.Contains("var entries = GetSortedSdkTypedPublisherEntries();", buildSdk, StringComparison.Ordinal);
            Assert.Contains("entries.Count", buildSdk, StringComparison.Ordinal);
            Assert.DoesNotContain("OrderBy", buildSdk, StringComparison.Ordinal);
            Assert.Contains("return SortedSdkTypedPublisherEntries.Value;", getSorted, StringComparison.Ordinal);
            Assert.Contains("ValidatePublisherCatalog(entries)", buildSorted, StringComparison.Ordinal);

            var hasher = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestHasher.cs");
            Assert.Contains("using var sha = SHA256.Create();", hasher, StringComparison.Ordinal);
            Assert.DoesNotContain("ThreadLocal<SHA256>", hasher, StringComparison.Ordinal);

            var memberData = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunMemberData.cs");
            Assert.Contains("LooksLikeArrayType(rawType)", memberData, StringComparison.Ordinal);
            Assert.Contains("Type-based MemberData constructor", memberData, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14071MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_71Validation.cs", "--phase140-71", "Phase140_71Validation.Validate");
    }

    [Trait("Phase", "140-72")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2ForUnityAdapterOptimizationTests
    {
        [Fact]
        public void NativeBridgesReuseScanCollections()
        {
            foreach (var file in new[]
            {
                "Ros2ForUnityCameraNativeBridge.cs",
                "Ros2ForUnityImuNativeBridge.cs",
                "Ros2ForUnityPointCloud2NativeBridge.cs",
                "Ros2ForUnityTransformNativeBridge.cs"
            })
            {
                var source = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/" + file);
                Assert.Contains("readonly HashSet<int>", source, StringComparison.Ordinal);
                Assert.Contains("readonly List<int>", source, StringComparison.Ordinal);
                Assert.Contains(".Clear();", source, StringComparison.Ordinal);
                Assert.DoesNotContain("var seen = new HashSet<int>();", source, StringComparison.Ordinal);
                Assert.DoesNotContain("var stale = new List<int>();", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CameraBridgeDiscoversPublishersOnceAndKeepsOwnershipDeferred()
        {
            var cameraBridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            var refreshBindings = TestSources.Slice(cameraBridge, "private void RefreshBindings()", "        private void RefreshRawImageBindings");
            var cameraInfo = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraInfoBinding.cs");
            var pointCloudBridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2NativeBridge.cs");
            var transformBridge = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityTransformNativeBridge.cs");
            var builder = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityPointCloud2MessageBuilder.cs");

            Assert.Contains("var cameraPublishers = FindObjectsByType<FoxgloveCameraPublisher>", refreshBindings, StringComparison.Ordinal);
            Assert.Contains("RefreshImageBindings(cameraPublishers)", refreshBindings, StringComparison.Ordinal);
            Assert.Contains("RefreshRawImageBindings(cameraPublishers)", refreshBindings, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(cameraBridge, "FindObjectsByType<FoxgloveCameraPublisher>"));
            Assert.Contains("Transforms = new[]", cameraInfo, StringComparison.Ordinal);
            Assert.Contains("Transforms = new[]", pointCloudBridge, StringComparison.Ordinal);
            Assert.Contains("Transforms = new[]", transformBridge, StringComparison.Ordinal);
            Assert.Contains("new sensor_msgs.msg.PointField[packedFields.Count]", builder, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14072MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_72Validation.cs", "--phase140-72", "Phase140_72Validation.Validate");
    }

    [Trait("Phase", "140-73")]
    [Trait("Domain", "Harness")]
    public sealed class JazzyRuntimeWrapperOptimizationTests
    {
        [Fact]
        public void SensorAndTransformCachesPreserveMutableState()
        {
            var sensor = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Sensor.cs");
            var frameName = TestSources.Slice(sensor, "public override string frameName()", "    /// <summary>\r\n    /// Visualises");
            var transformations = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/Transformations.cs");
            var method = TestSources.Slice(transformations, "public static Matrix4x4 Unity2RosMatrix4x4()", "}");

            Assert.Contains("private string cachedFrameName;", sensor, StringComparison.Ordinal);
            Assert.DoesNotContain("private string cachedFrameNameOwner;", sensor, StringComparison.Ordinal);
            Assert.DoesNotContain("private string cachedFrameNameFrameId;", sensor, StringComparison.Ordinal);
            Assert.Contains("cachedFrameName = String.IsNullOrEmpty(ownerAgentName) ? frameID : ownerAgentName + \"/\" + frameID;", sensor, StringComparison.Ordinal);
            Assert.Contains("if (cachedFrameName != null)", frameName, StringComparison.Ordinal);
            Assert.Contains("return cachedFrameName;", frameName, StringComparison.Ordinal);
            Assert.Contains("static readonly Matrix4x4 Unity2RosMatrix", transformations, StringComparison.Ordinal);
            Assert.Contains("static readonly Matrix4x4 Ros2UnityMatrix", transformations, StringComparison.Ordinal);
            Assert.Contains("Unity2RosMatrix.inverse", transformations, StringComparison.Ordinal);
            Assert.Contains("return Unity2RosMatrix;", method, StringComparison.Ordinal);
            Assert.DoesNotContain("new Matrix4x4", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".transpose", method, StringComparison.Ordinal);
        }

        [Fact]
        public void RosSupportAndExecutorSpinUsesReusableSnapshots()
        {
            var ros2 = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs");
            var checkSupport = TestSources.Slice(ros2, "private void CheckROSSupport(string ros2Codename)", "    private void CheckRmwImplementation()");
            var component = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
            var core = TestSources.Text("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs");

            Assert.Contains("private static readonly string[] SupportedRosVersions", ros2, StringComparison.Ordinal);
            Assert.Contains("private static readonly string SupportedRosVersionsString", ros2, StringComparison.Ordinal);
            Assert.DoesNotContain("new List<string>()", checkSupport, StringComparison.Ordinal);
            Assert.Contains("SupportedRosVersionsString", checkSupport, StringComparison.Ordinal);
            Assert.Contains("Array.IndexOf(SupportedRosVersions, ros2Codename)", checkSupport, StringComparison.Ordinal);
            Assert.Contains("nodesSnapshot.AddRange(ros2csNodes)", component, StringComparison.Ordinal);
            Assert.Contains("Ros2cs.SpinOnce(nodesSnapshot", component, StringComparison.Ordinal);
            Assert.Contains("nodesSnapshot.AddRange(ros2csNodes)", core, StringComparison.Ordinal);
            Assert.Contains("Ros2cs.SpinOnce(nodesSnapshot", core, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14073MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_73Validation.cs", "--phase140-73", "Phase140_73Validation.Validate");
    }
}
