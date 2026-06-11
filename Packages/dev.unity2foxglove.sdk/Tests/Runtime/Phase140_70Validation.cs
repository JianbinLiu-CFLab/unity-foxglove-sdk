// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 140-70 source-shape regression coverage for Inspector editor repaint-path optimizations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase140_70Validation.
    /// </summary>
    public static class Phase140_70Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-70: Inspector Manager and Publisher Editors Optimization ===");
            _passed = 0;

            VerifyCameraPublisherEditorHotPaths();
            VerifyManagerEditorHotPaths();
            VerifyPublisherBaseEditorHotPaths();
            VerifyAlreadyCachedFindingsStayCached();
            VerifyRegistration();

            Console.WriteLine($"Phase 140-70: {_passed} checks passed.");
        }

        private static void VerifyCameraPublisherEditorHotPaths()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var onInspector = Slice(source, "public override void OnInspectorGUI()", "        private static string[] BuildCameraOutputModeLabels");
            Check(source.Contains("private SerializedProperty _manager;", StringComparison.Ordinal)
                  && source.Contains("private void OnEnable()", StringComparison.Ordinal)
                  && !onInspector.Contains("serializedObject.FindProperty(\"_manager\")", StringComparison.Ordinal)
                  && !onInspector.Contains("serializedObject.FindProperty(\"_publishRateSource\")", StringComparison.Ordinal)
                  && !onInspector.Contains("serializedObject.FindProperty(\"_ros2BridgeOutput\")", StringComparison.Ordinal),
                "140-70A-1: camera publisher editor caches serialized properties in OnEnable");

            Check(source.Contains("private static GUIContent Label(string text)", StringComparison.Ordinal)
                  && !onInspector.Contains("new GUIContent(\"", StringComparison.Ordinal),
                "140-70A-2: camera publisher repaint path reuses GUIContent labels");
        }

        private static void VerifyManagerEditorHotPaths()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var transport = Slice(manager, "private void DrawTransportModeProperty()", "private void DrawFloatProperty");
            Check(manager.Contains("private static readonly string[] TransportModeLabels", StringComparison.Ordinal)
                  && !transport.Contains("new[]", StringComparison.Ordinal),
                "140-70B-1: manager transport mode labels are hoisted out of repaint");

            var mcap = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var replayAutoPlay = Slice(mcap, "private void DrawReplayAutoPlayControl()", "private void DrawRemoteFileAccessSection");
            Check(replayAutoPlay.Contains("var remoteFileServerEnabled", StringComparison.Ordinal)
                  && CountOccurrences(replayAutoPlay, "GetBool(\"_enableRemoteMcapFileServer\")") == 1,
                "140-70B-2: replay auto-play control resolves remote-file state once per repaint");
        }

        private static void VerifyPublisherBaseEditorHotPaths()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs");
            var onInspector = Slice(source, "public override void OnInspectorGUI()", "        private void CacheDefaultProperties()");
            Check(source.Contains("private readonly System.Collections.Generic.List<SerializedProperty> _defaultProperties", StringComparison.Ordinal)
                  && source.Contains("private void OnEnable()", StringComparison.Ordinal)
                  && source.Contains("CacheDefaultProperties()", StringComparison.Ordinal)
                  && !onInspector.Contains("serializedObject.GetIterator()", StringComparison.Ordinal)
                  && !onInspector.Contains("NextVisible", StringComparison.Ordinal),
                "140-70C-1: base publisher editor caches default serialized-property iterator results");

            Check(source.Contains("private static GUIContent Label(string text)", StringComparison.Ordinal)
                  && !onInspector.Contains("new GUIContent(\"", StringComparison.Ordinal),
                "140-70C-2: base publisher repaint path reuses GUIContent labels");
        }

        private static void VerifyAlreadyCachedFindingsStayCached()
        {
            var manager = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            Check(manager.Contains("_cachedRootCaFingerprint", StringComparison.Ordinal)
                  && manager.Contains("GetCachedRootCaFingerprint", StringComparison.Ordinal),
                "140-70D-1: root CA fingerprint remains cached by path");

            var cameraInfo = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");
            var getObjectFieldType = Slice(cameraInfo, "private static System.Type GetObjectFieldType", "    }\r\n}");
            Check(cameraInfo.Contains("ObjectFieldTypeCache", StringComparison.Ordinal)
                  && getObjectFieldType.Contains("ObjectFieldTypeCache.TryGetValue", StringComparison.Ordinal)
                  && getObjectFieldType.Contains("ObjectFieldTypeCache[typeName]", StringComparison.Ordinal),
                "140-70D-2: CameraInfo object field type fallback remains cached after first miss");
        }

        private static void VerifyRegistration()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(project.Contains("Phase140_70Validation.cs", StringComparison.Ordinal),
                "140-70E-1: test project compiles Phase140_70Validation");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("\"--phase140-70\"", StringComparison.Ordinal)
                  && registry.Contains("Phase140_70Validation.Validate", StringComparison.Ordinal),
                "140-70E-2: validation registry exposes --phase140-70");
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        private static string RepoRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(Path.Combine(directory, ".git")))
                    return directory;
                directory = Directory.GetParent(directory)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private static string Slice(string source, string startText, string endText)
        {
            var start = source.IndexOf(startText, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Could not locate source slice start: " + startText);
            var end = source.IndexOf(endText, start + startText.Length, StringComparison.Ordinal);
            if (end < 0)
                end = source.Length;
            return source.Substring(start, end - start);
        }

        private static int CountOccurrences(string source, string text)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(text, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += text.Length;
            }
            return count;
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
