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
            var transport = TestSources.ExtractMethod(manager, "private void DrawTransportModeProperty");
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
}
