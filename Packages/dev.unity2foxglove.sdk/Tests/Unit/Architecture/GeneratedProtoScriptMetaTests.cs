// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Unity import-shape checks for generated protobuf runtime scripts.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "165")]
    [Trait("Domain", "Architecture")]
    public sealed class GeneratedProtoScriptMetaTests
    {
        private const int ExpectedGeneratedProtoScriptMetaCount = 47;

        [Fact]
        public void GeneratedProtoScriptsHaveUnityMonoImporterMetas()
        {
            var root = PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Generated");
            var metas = Directory.GetFiles(root, "*.cs.meta", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(ExpectedGeneratedProtoScriptMetaCount, metas.Length);
            foreach (var metaPath in metas)
            {
                var meta = Text(Relative(metaPath));
                Assert.True(HasValidUnityGuid(meta), Relative(metaPath) + " should have a valid Unity GUID.");
                Assert.Contains("MonoImporter:", meta, StringComparison.Ordinal);
                Assert.True(meta.EndsWith("\n", StringComparison.Ordinal), Relative(metaPath) + " should end with a newline for Unity YAML import.");
            }
        }

        [Fact]
        public void ReviewedRuntimeAndDemoScriptsHaveUnityMonoImporterMetas()
        {
            var metas = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionPlaybackHandler.cs.meta",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Builders/PointCloudMessageBuilder.cs.meta",
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/ProtobufSchemaRegistry.cs.meta",
                "Unity2Foxglove/Assets/Editor/FoxgloveBuild.cs.meta"
            };

            foreach (var metaPath in metas)
            {
                var meta = Text(metaPath);
                Assert.True(HasValidUnityGuid(meta), metaPath + " should have a valid Unity GUID.");
                Assert.Contains("MonoImporter:", meta, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Ros2CdrGeneratedCodeLivesInLeafAssemblyOutsideRuntimeAndProtoCycles()
        {
            var protoGeneratedAsmdefPath = "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Generated/Unity.FoxgloveSDK.Proto.Generated.asmdef";
            var protoGeneratedAsmdef = Text(protoGeneratedAsmdefPath);
            var protoGeneratedAsmdefMeta = Text(protoGeneratedAsmdefPath + ".meta");
            var generatedAsmdefPath = "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Unity2Foxglove.Ros2Bridge.Generated.asmdef";
            var generatedAsmdefMetaPath = generatedAsmdefPath + ".meta";
            var generatedAsmdef = Text(generatedAsmdefPath);
            var generatedAsmdefMeta = Text(generatedAsmdefMetaPath);
            var runtimeAsmdef = Text("Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef");
            var runtimeAssemblyInfo = Text("Packages/dev.unity2foxglove.sdk/Runtime/AssemblyInfo.cs");
            var protoAsmdef = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Unity.FoxgloveSDK.Proto.asmdef");
            var registry = Text("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs");
            var ros2Generated = Text("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedDeserializers.g.cs");
            var ros2TypedFactoryMeta = Text("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/McapRos2CdrTypedDecoderFactory.cs.meta");
            var ros2BridgePublisherMeta = Text("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgePublisher.cs.meta");

            Assert.Contains("\"name\": \"Unity.FoxgloveSDK.Proto.Generated\"", protoGeneratedAsmdef, StringComparison.Ordinal);
            Assert.Contains("\"references\": []", protoGeneratedAsmdef, StringComparison.Ordinal);
            Assert.Contains("AssemblyDefinitionImporter:", protoGeneratedAsmdefMeta, StringComparison.Ordinal);
            Assert.Contains("\"name\": \"Unity2Foxglove.Ros2Bridge.Generated\"", generatedAsmdef, StringComparison.Ordinal);
            Assert.Contains("\"Unity.FoxgloveSDK\"", generatedAsmdef, StringComparison.Ordinal);
            Assert.Contains("\"Unity.FoxgloveSDK.Proto.Generated\"", generatedAsmdef, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Unity.FoxgloveSDK.Proto\"", generatedAsmdef, StringComparison.Ordinal);
            Assert.Contains("AssemblyDefinitionImporter:", generatedAsmdefMeta, StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2", runtimeAssemblyInfo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"Unity.FoxgloveSDK.Proto\"", runtimeAsmdef, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Unity.FoxgloveSDK.Proto.Messages\"", runtimeAsmdef, StringComparison.Ordinal);
            Assert.Contains("\"Unity.FoxgloveSDK\"", protoAsmdef, StringComparison.Ordinal);
            Assert.Contains("\"Unity.FoxgloveSDK.Proto.Generated\"", protoAsmdef, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Unity2Foxglove.Ros2Bridge.Generated\"", protoAsmdef, StringComparison.Ordinal);
            Assert.Contains("global::Foxglove.", ros2Generated, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", ros2TypedFactoryMeta, StringComparison.Ordinal);
            Assert.Contains("MonoImporter:", ros2BridgePublisherMeta, StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2", registry, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(PathOf("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/McapRos2CdrTypedDecoderFactory.cs")));
            Assert.True(File.Exists(PathOf("Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgePublisher.cs")));
            Assert.False(File.Exists(PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Generated/Messages/Unity.FoxgloveSDK.Proto.Messages.asmdef")));
            Assert.False(File.Exists(PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/DataLoader/McapRos2CdrTypedDecoderFactory.cs")));
            Assert.False(File.Exists(PathOf("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Ros2Bridge/Ros2BridgePublisher.cs")));
        }

        [Fact]
        public void FoxgloveManagerEditorPartialClassHasOneUnityLifecycleOwner()
        {
            var dir = PathOf("Packages/dev.unity2foxglove.sdk/Editor/Manager");
            var files = Directory.GetFiles(dir, "FoxgloveManagerEditor*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var onDisableOwners = files
                .Where(path => CountOccurrences(File.ReadAllText(path), "void OnDisable(") > 0)
                .Select(Relative)
                .ToArray();
            var main = Text("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");

            Assert.Equal(new[] { "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs" }, onDisableOwners);
            Assert.Contains("_mcapReplayPreflight.Dispose();", main, StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2Bridge", main, StringComparison.Ordinal);
        }

        [Fact]
        public void UnityEditorAssemblyCanCompileGeneratedSchemaAndCameraInspectorFoldouts()
        {
            var editorAsmdef = Text("Packages/dev.unity2foxglove.sdk/Editor/Unity.FoxgloveSDK.Editor.asmdef");
            var cameraEditor = Text("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Assert.Contains("\"Unity.FoxgloveSDK.Proto.Generated\"", editorAsmdef, StringComparison.Ordinal);
            Assert.Contains("\"Provider Payload\"", cameraEditor, StringComparison.Ordinal);
            Assert.DoesNotContain("Ros2", cameraEditor, StringComparison.OrdinalIgnoreCase);
        }

        private static string PathOf(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string Text(string relativePath)
        {
            var path = PathOf(relativePath);
            Assert.True(File.Exists(path), relativePath + " not found.");
            return File.ReadAllText(path);
        }

        private static string Relative(string absolutePath)
            => Path.GetRelativePath(RepoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

        private static bool HasValidUnityGuid(string meta)
        {
            const string prefix = "guid:";
            foreach (var rawLine in meta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var guid = line.Substring(prefix.Length).Trim();
                return guid.Length == 32 && guid.All(IsHex);
            }

            return false;
        }

        private static bool IsHex(char c)
            => (c >= '0' && c <= '9')
               || (c >= 'a' && c <= 'f')
               || (c >= 'A' && c <= 'F');

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                        || File.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
