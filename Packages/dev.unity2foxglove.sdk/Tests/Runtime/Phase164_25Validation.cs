using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_25Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-25 Tests ---");
            _passed = 0;

            VerifyManagerEditorCachesSerializedPropertiesAndUrls();
            VerifyManagerEditorCachesRuntimeSnapshotsPerRepaint();
            VerifyR2fuProviderOwnsSelectorDirectly();
            VerifyMcapInspectorUsesCachedPropertiesAndUrls();
            VerifyPointCloudEditorCachesSerializedProperties();
            VerifyRegistry();

            Console.WriteLine("Phase 164-25: " + _passed + " checks passed.\n");
        }

        private static void VerifyManagerEditorCachesSerializedPropertiesAndUrls()
        {
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var onEnable = PhaseValidationSourceHelpers.SourceMethod(editor, "private void OnEnable");
            var cache = PhaseValidationSourceHelpers.SourceMethod(editor, "private void CacheSerializedProperties");
            var findCached = PhaseValidationSourceHelpers.SourceMethod(editor, "private SerializedProperty FindCachedProperty");
            var compactStatus = PhaseValidationSourceHelpers.SourceMethod(editor, "private void DrawCompactStatus");
            var webUrlCache = PhaseValidationSourceHelpers.SourceMethod(editor, "private void RefreshWebUrlCache");

            Check(onEnable.Contains("CacheSerializedProperties();", StringComparison.Ordinal)
                  && onEnable.Contains("InvalidateUrlCaches();", StringComparison.Ordinal),
                "164-25A-1: manager editor initializes serialized-property and URL caches once per inspector lifetime");
            Check(cache.Contains("_hostProperty = serializedObject.FindProperty(\"_host\");", StringComparison.Ordinal)
                  && cache.Contains("_portProperty = serializedObject.FindProperty(\"_port\");", StringComparison.Ordinal)
                  && cache.Contains("_sharedTokenProperty = serializedObject.FindProperty(\"_sharedToken\");", StringComparison.Ordinal)
                  && cache.Contains("_schemaEvidenceRootProperty = serializedObject.FindProperty(\"_schemaEvidenceRoot\");", StringComparison.Ordinal),
                "164-25A-2: manager editor caches common serialized properties instead of rediscovering them while drawing");
            Check(findCached.Contains("case \"_host\": return _hostProperty;", StringComparison.Ordinal)
                  && findCached.Contains("case \"_remoteMcapFileServerSourceId\": return _remoteMcapFileServerSourceIdProperty;", StringComparison.Ordinal)
                  && findCached.Contains("default: return serializedObject.FindProperty(propertyName);", StringComparison.Ordinal),
                "164-25A-3: manager editor routes hot properties through cached handles while preserving fallback drawing");
            Check(compactStatus.Contains("RefreshWebUrlCache(host, port, isSecure, token);", StringComparison.Ordinal)
                  && compactStatus.Contains("_cachedEndpoint", StringComparison.Ordinal)
                  && compactStatus.Contains("_cachedRedactedFoxgloveWebUrl", StringComparison.Ordinal)
                  && compactStatus.Contains("_cachedFoxgloveWebUrl", StringComparison.Ordinal)
                  && !compactStatus.Contains("BuildWebSocketEndpoint", StringComparison.Ordinal)
                  && !compactStatus.Contains("BuildHostedWebSocketUrl", StringComparison.Ordinal),
                "164-25A-4: compact status reuses cached endpoint and Foxglove Web URL strings");
            Check(webUrlCache.Contains("BuildWebSocketEndpoint", StringComparison.Ordinal)
                  && webUrlCache.Contains("BuildHostedWebSocketUrl", StringComparison.Ordinal)
                  && webUrlCache.Contains("_cachedEndpointHost", StringComparison.Ordinal)
                  && webUrlCache.Contains("_cachedEndpointToken", StringComparison.Ordinal),
                "164-25A-5: URL cache rebuilds only when endpoint inputs change");
        }

        private static void VerifyManagerEditorCachesRuntimeSnapshotsPerRepaint()
        {
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Diagnostics.cs");
            var compactStatus = PhaseValidationSourceHelpers.SourceMethod(editor, "private void DrawCompactStatus");
            var services = PhaseValidationSourceHelpers.SourceMethod(editorSources, "private void DrawFoxServicesSection");
            var refreshStats = PhaseValidationSourceHelpers.SourceMethod(editorSources, "private void RefreshTransportStatsForRepaint");
            var getStats = PhaseValidationSourceHelpers.SourceMethod(editorSources, "private TransportStatsSnapshot GetTransportStatsForRepaint");
            var getServices = PhaseValidationSourceHelpers.SourceMethod(editorSources, "private System.Collections.Generic.IReadOnlyList<Components.FoxgloveRegisteredServiceSnapshot> GetServiceSnapshotsForRepaint");
            var drawHealth = PhaseValidationSourceHelpers.SourceMethod(diagnostics, "private void DrawTransportHealth");

            Check(refreshStats.Contains("_transportStatsFrame = Time.frameCount;", StringComparison.Ordinal)
                  && refreshStats.Contains("manager.GetTransportStatsSnapshot()", StringComparison.Ordinal),
                "164-25B-1: manager editor captures transport stats once for the current repaint frame");
            Check(getStats.Contains("if (_transportStatsFrame != Time.frameCount)", StringComparison.Ordinal)
                  && getStats.Contains("RefreshTransportStatsForRepaint();", StringComparison.Ordinal),
                "164-25B-2: transport stats getter reuses the cached repaint snapshot");
            Check(compactStatus.Contains("var stats = GetTransportStatsForRepaint();", StringComparison.Ordinal)
                  && drawHealth.Contains("var stats = GetTransportStatsForRepaint();", StringComparison.Ordinal)
                  && !compactStatus.Contains("GetTransportStatsSnapshot()", StringComparison.Ordinal)
                  && !drawHealth.Contains("GetTransportStatsSnapshot()", StringComparison.Ordinal),
                "164-25B-3: status and diagnostics panels share one transport snapshot per repaint");
            Check(services.Contains("var snapshots = GetServiceSnapshotsForRepaint(hub);", StringComparison.Ordinal)
                  && getServices.Contains("_cachedServiceSnapshotFrame = frame;", StringComparison.Ordinal)
                  && getServices.Contains("hub.GetRegisteredServiceSnapshots();", StringComparison.Ordinal),
                "164-25B-4: FoxService inspector snapshots are cached per hub and repaint frame");
        }

        private static void VerifyR2fuProviderOwnsSelectorDirectly()
        {
            var source = Read(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityManagerSetupDrawer.cs");
            var providerDrawer = Read(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs");
            var asmdef = Read(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Unity2Foxglove.Ros2ForUnity.Editor.asmdef");
            var manager = Read(
                "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var coreManagerSources =
                PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();

            Check(source.Contains("IFoxRunManagerSetupDrawer", StringComparison.Ordinal)
                  && PhaseValidationSourceHelpers
                      .QualifiedInvocationCount(
                          source,
                          "FoxRunManagerSetupDrawerRegistry",
                          "Register") == 1
                  && PhaseValidationSourceHelpers
                      .QualifiedInvocationCountInMethod(
                          manager,
                          "DrawFoxRunTransportProviderExtensions",
                          "FoxRunManagerSetupDrawerRegistry",
                          "Capture") == 1
                  && PhaseValidationSourceHelpers
                      .QualifiedInvocationCountInMethod(
                          manager,
                          "DrawFoxRunTransportProviderExtensions",
                          "setupDrawer",
                          "Draw") == 1,
                "164-25C-1: the R2FU selector is hosted by one domain-registered Manager setup drawer");
            Check(PhaseValidationSourceHelpers
                      .InvocationCountInMethod(
                          source,
                          "Draw",
                          "DrawActiveRuntimeSelector") == 1
                  && PhaseValidationSourceHelpers
                      .InvocationCount(
                          source,
                          "DrawActiveRuntimeSelector") == 1
                  && PhaseValidationSourceHelpers
                      .InvocationCount(
                          providerDrawer,
                          "DrawActiveRuntimeSelector") == 0
                  && !source.Contains("Type.GetType", StringComparison.Ordinal)
                  && !source.Contains("GetMethod", StringComparison.Ordinal),
                "164-25C-2: the unconstrained setup drawer invokes the R2FU selector exactly once with no native-drawer duplicate");
            Check(asmdef.Contains(
                      "\"name\": \"Unity2Foxglove.Ros2ForUnity.Editor\"",
                      StringComparison.Ordinal)
                  && asmdef.Contains(
                      "\"Unity.FoxgloveSDK.Editor\"",
                      StringComparison.Ordinal)
                  && asmdef.Contains(
                      "\"defineConstraints\": []",
                      StringComparison.Ordinal),
                "164-25C-3: the unconstrained R2FU Editor assembly declares its generic Manager-editor dependency");
            Check(!coreManagerSources.Contains(
                      "Unity2Foxglove.Ros2ForUnity",
                      StringComparison.Ordinal)
                  && !coreManagerSources.Contains(
                      "_r2fuRuntimeSelectorDrawMethod",
                      StringComparison.Ordinal)
                  && !coreManagerSources.Contains(
                      "ResolveR2fuRuntimeSelectorDrawMethod",
                      StringComparison.Ordinal),
                "164-25C-4: the core Manager editor owns no optional R2FU reflection cache after Provider extraction");
        }

        private static void VerifyMcapInspectorUsesCachedPropertiesAndUrls()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var manager = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var section = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrawMcapSection");
            var replay = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrawReplayAutoPlayControl");
            var directUrl = PhaseValidationSourceHelpers.SourceMethod(source, "private string BuildRemoteMcapDirectFileUrl");
            var schema = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrawSchemaEvidenceSection");
            var remoteCache = PhaseValidationSourceHelpers.SourceMethod(manager, "private void RefreshRemoteMcapUrlCache");

            Check(section.Contains("FindCachedProperty(\"_recordingDirectory\")", StringComparison.Ordinal)
                  && section.Contains("FindCachedProperty(\"_replayFilePath\")", StringComparison.Ordinal)
                  && replay.Contains("FindCachedProperty(\"_replayAutoPlay\")", StringComparison.Ordinal),
                "164-25D-1: MCAP inspector uses cached handles for recording and replay controls");
            Check(directUrl.Contains("RefreshRemoteMcapUrlCache", StringComparison.Ordinal)
                  && directUrl.Contains("return _cachedRemoteDirectFileUrl;", StringComparison.Ordinal)
                  && remoteCache.Contains("System.Uri.EscapeDataString(sourceId)", StringComparison.Ordinal),
                "164-25D-2: remote MCAP URL builders reuse cached base and direct URLs");
            Check(schema.Contains("FindCachedProperty(\"_identityModeSource\")", StringComparison.Ordinal)
                  && schema.Contains("FindCachedProperty(\"_identityModeOverride\")", StringComparison.Ordinal)
                  && schema.Contains("FindCachedProperty(\"_projectSettingsIdentityMode\")", StringComparison.Ordinal)
                  && schema.Contains("FindCachedProperty(\"_schemaEvidenceRoot\")", StringComparison.Ordinal),
                "164-25D-3: schema evidence inspector uses cached serialized-property handles");
        }

        private static void VerifyPointCloudEditorCachesSerializedProperties()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxglovePointCloudPublisherEditor.cs");
            var onEnable = PhaseValidationSourceHelpers.SourceMethod(source, "private void OnEnable");
            var gui = PhaseValidationSourceHelpers.SourceMethod(source, "public override void OnInspectorGUI");
            var script = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrawScriptField");
            var general = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrawGeneralSection");
            var drawProperty = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrawProperty(SerializedProperty property, string label)");

            Check(onEnable.Contains("_topic = serializedObject.FindProperty(\"_topic\");", StringComparison.Ordinal)
                  && onEnable.Contains("_publishRateSource = serializedObject.FindProperty(\"_publishRateSource\");", StringComparison.Ordinal)
                  && onEnable.Contains("_encodingOverride = serializedObject.FindProperty(\"_encodingOverride\");", StringComparison.Ordinal)
                  && !source.Contains("_ros2BridgeOutput", StringComparison.Ordinal),
                "164-25E-1: point-cloud editor caches provider-neutral serialized properties in OnEnable");
            Check(gui.Contains("DrawOutputModeSection(_outputMode, _topic);", StringComparison.Ordinal)
                  && general.Contains("DrawProperty(_manager, \"Manager\");", StringComparison.Ordinal)
                  && drawProperty.Contains("if (property != null)", StringComparison.Ordinal),
                "164-25E-2: point-cloud editor draw path uses cached SerializedProperty references");
            Check(script.Contains("if (_script != null)", StringComparison.Ordinal),
                "164-25E-3: point-cloud editor handles missing script property defensively");
            Check(Count(source, "serializedObject.FindProperty(") == Count(onEnable, "serializedObject.FindProperty("),
                "164-25E-4: point-cloud editor keeps FindProperty calls out of repaint drawing methods");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-25\"", StringComparison.Ordinal), "164-25F-1: validation registry exposes Phase164-25");
            Check(project.Contains("Phase164_25Validation.cs", StringComparison.Ordinal), "164-25F-2: runtime validation project compiles Phase164-25");
        }

        private static int Count(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }

            return count;
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
