// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Public regression guards for Phase176 Subscribe Data and FoxRun Publish panel contracts.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase176Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 176 Tests ---");
            _passed = 0;

            VerifyDirectionalPolicyMigration();
            VerifySubscriptionCatalogAndLifecycle();
            VerifyInspectorWorkflow();
            VerifyPublishPanelWireDiscipline();
            VerifyRegistryEntry();

            Console.WriteLine("Phase 176: " + _passed + " checks passed.\n");
        }

        private static void VerifyDirectionalPolicyMigration()
        {
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPublishing.cs");
            var migration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var helper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunEncodingPolicyMigration.cs");
            var resolver = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunEncodingResolver.cs");

            Check(inbound.Contains("_defaultFoxRunEncoding", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                  && publishing.Contains("_defaultFoxRunPublishEncoding", StringComparison.Ordinal)
                  && migration.Contains("ISerializationCallbackReceiver", StringComparison.Ordinal)
                  && migration.Contains("OnAfterDeserialize", StringComparison.Ordinal)
                  && helper.Contains("CurrentSerializationVersion", StringComparison.Ordinal)
                  && helper.Contains("publishDefault = concrete", StringComparison.Ordinal)
                  && helper.Contains("subscriptionDefault = concrete", StringComparison.Ordinal),
                "176A-1: legacy one-default serialization migrates into directional publish and subscription defaults in player-safe deserialization");
            Check(resolver.Contains("FoxRunFlow.Publish", StringComparison.Ordinal)
                  && resolver.Contains("FoxRunFlow.Subscribe", StringComparison.Ordinal)
                  && resolver.Contains(
                      "Full-duplex omitted Encoding must be resolved per direction.",
                      StringComparison.Ordinal),
                "176A-2: inherited contracts resolve independently for each direction");
        }

        private static void VerifySubscriptionCatalogAndLifecycle()
        {
            var catalog = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSubscriptionCatalog.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionCatalog.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var catalogRegistrationIndex = server.IndexOf("RegisterFoxRunSubscriptionCatalogService();", StringComparison.Ordinal);
            var runtimeStartIndex = server.IndexOf("_runtime.Start(", StringComparison.Ordinal);

            Check(catalog.Contains("public const int Version = 2", StringComparison.Ordinal)
                  && catalog.Contains("[\"subscriptionsEnabled\"]", StringComparison.Ordinal)
                  && catalog.Contains("if (!subscriptionsEnabled || manifest == null)", StringComparison.Ordinal)
                  && catalog.Contains("string defaultSubscribeTransportId", StringComparison.Ordinal)
                  && catalog.Contains("var defaultProvider = new FoxRunTransportId(", StringComparison.Ordinal)
                  && catalog.Contains("[\"publishTransportIds\"]", StringComparison.Ordinal)
                  && catalog.Contains("[\"subscribeTransportId\"]", StringComparison.Ordinal)
                  && catalog.Contains("includeDescriptor", StringComparison.Ordinal)
                  && catalog.Contains("protobufDescriptorBase64", StringComparison.Ordinal),
                "176B-1: catalog returns a versioned disabled response, advertises neutral Provider IDs, and emits Protobuf descriptors only on demand");
            Check(manager.Contains("/foxrun/subscription-contracts", StringComparison.Ordinal)
                  && manager.Contains("IsFoxRunInboundAuthorized", StringComparison.Ordinal)
                  && !manager.Contains("_sharedToken", StringComparison.Ordinal)
                  && catalogRegistrationIndex >= 0
                  && runtimeStartIndex > catalogRegistrationIndex
                  && server.Contains("UnregisterFoxRunSubscriptionCatalogService();", StringComparison.Ordinal),
                "176B-2: Manager registers the catalog before opening the listener, keeps the existing authorization boundary, and clears it on shutdown without exposing tokens");
        }

        private static void VerifyInspectorWorkflow()
        {
            var main = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var subscribe = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs");
            var drawerRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxRunTransportProviderDrawerRegistry.cs");
            var transportId = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportId.cs");
            var r2fuDrawer = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs");
            var bridgeDrawer = ReadRepoText("Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2BridgeProviderDrawer.cs");
            var services = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.FoxServices.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");

            Check(main.Contains("DrawSection(\"Data Transport\"", StringComparison.Ordinal)
                  && !main.Contains("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Data Transport\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"FoxServices\"", StringComparison.Ordinal)
                  && editorSources.Contains("DrawDataTransportSubsection", StringComparison.Ordinal)
                  && editorSources.Contains("\"Publish Data\"", StringComparison.Ordinal)
                  && editorSources.Contains("\"Subscribe Data\"", StringComparison.Ordinal)
                  && !main.Contains("DrawSection(\"FoxRun\"", StringComparison.Ordinal)
                  && subscribe.Contains("\"_foxRunSubscribeTransportId\"", StringComparison.Ordinal)
                  && subscribe.Contains("\"Subscribe Source\"", StringComparison.Ordinal)
                  && subscribe.Contains("FoxgloveWebSocketTransport.Id", StringComparison.Ordinal)
                  && subscribe.Contains("ActiveFoxRunSubscriptionSessionPolicy", StringComparison.Ordinal)
                  && subscribe.Contains(".SubscriptionsEnabled", StringComparison.Ordinal)
                  && subscribe.Contains("Default Subscribe Rate Hz", StringComparison.Ordinal)
                  && subscribe.Contains("Maximum Subscribe Rate Hz (per Topic)", StringComparison.Ordinal)
                  && subscribe.IndexOf("Default Subscribe Rate Hz", StringComparison.Ordinal)
                     < subscribe.IndexOf("Maximum Subscribe Rate Hz (per Topic)", StringComparison.Ordinal)
                  && inbound.Contains("_foxRunDefaultSubscribeRateHz = 10", StringComparison.Ordinal)
                  && inbound.Contains("_foxRunInboundMaxMessagesPerSecondPerTopic", StringComparison.Ordinal)
                  && inbound.Contains("60;", StringComparison.Ordinal)
                  && !inbound.Contains("[Header(\"FoxRun Subscription Control\")]", StringComparison.Ordinal)
                  && publish.Contains("\"_foxRunPublishTransportIds\"", StringComparison.Ordinal)
                  && publish.Contains("\"Publish Destinations\"", StringComparison.Ordinal)
                  && publish.Contains("FoxgloveWebSocketTransport.Id", StringComparison.Ordinal)
                  && publish.Contains("Allow Component Publisher Override", StringComparison.Ordinal)
                  && drawerRegistry.Contains("string TransportId { get; }", StringComparison.Ordinal)
                  && drawerRegistry.Contains("string DisplayName { get; }", StringComparison.Ordinal)
                  && drawerRegistry.Contains("FoxRunTransportCapabilities Capabilities { get; }", StringComparison.Ordinal)
                  && drawerRegistry.Contains("OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)", StringComparison.Ordinal)
                  && transportId.Contains("public const string Id = \"foxglove.websocket\"", StringComparison.Ordinal)
                  && r2fuDrawer.Contains("FoxRunRos2TransportProvider.IdValue", StringComparison.Ordinal)
                  && r2fuDrawer.Contains("\"ROS 2 Native (R2FU)\"", StringComparison.Ordinal)
                  && r2fuDrawer.Contains("FoxRunTransportCapabilities.Publish", StringComparison.Ordinal)
                  && r2fuDrawer.Contains("FoxRunTransportCapabilities.Subscribe", StringComparison.Ordinal)
                  && bridgeDrawer.Contains("Ros2BridgeTransportProvider.ProviderId", StringComparison.Ordinal)
                  && bridgeDrawer.Contains("\"ROS 2 Bridge\"", StringComparison.Ordinal)
                  && bridgeDrawer.Contains("FoxRunTransportCapabilities.Publish;", StringComparison.Ordinal)
                  && !bridgeDrawer.Contains("FoxRunTransportCapabilities.Subscribe", StringComparison.Ordinal)
                  && !editorSources.Contains("FoxRunEndpointEditorLabels", StringComparison.Ordinal)
                  && !editorSources.Contains("_ros2NativeEnabled", StringComparison.Ordinal)
                  && !editorSources.Contains("_ros2BridgeEnabled", StringComparison.Ordinal),
                "176C-1: Inspector exposes one neutral publish-ID collection and one neutral subscribe-ID source while Provider drawers own display names and directional capabilities");
            Check(services.Contains("FoxRun Runtime Topics", StringComparison.Ordinal)
                  && services.Contains("DrawFoxRunTopicSummaryHeader", StringComparison.Ordinal)
                  && services.Contains("DrawFoxRunTopicSummaryRow", StringComparison.Ordinal)
                  && services.Contains("Publish Topics", StringComparison.Ordinal)
                  && services.Contains("Subscribe Topics", StringComparison.Ordinal)
                  && !services.Contains("Publish And Subscribe Topics", StringComparison.Ordinal)
                  && services.Contains("Wire schema: ", StringComparison.Ordinal)
                  && services.Contains("Logical schema: ", StringComparison.Ordinal)
                  && services.Contains("TopicSchemaStyle", StringComparison.Ordinal)
                  && services.Contains("wordWrap = true", StringComparison.Ordinal)
                  && services.Contains("GetTopicSchemaStyle", StringComparison.Ordinal)
                  && services.Contains("GetTopicSummaryColumns", StringComparison.Ordinal)
                  && services.Contains("GetTopicSchemaLayoutWidth", StringComparison.Ordinal)
                  && services.Contains("EditorGUILayout.GetControlRect", StringComparison.Ordinal)
                  && services.Contains("GUI.Button(copy, \"Copy\")", StringComparison.Ordinal)
                  && services.Contains("if (!summary.Available)", StringComparison.Ordinal),
                "176C-2: FoxServices groups full-duplex runtime topics into both directional views and places wire/logical schema plus availability beneath each topic");
        }

        private static void VerifyPublishPanelWireDiscipline()
        {
            var package = ReadRepoText("Tools/foxglove-extensions/foxrun-publish-panel/package.json");
            var panel = ReadRepoText("Tools/foxglove-extensions/foxrun-publish-panel/src/index.ts");
            var protocol = ReadRepoText("Tools/foxglove-extensions/foxrun-publish-panel/src/protocol.ts");
            var protobuf = ReadRepoText("Tools/foxglove-extensions/foxrun-publish-panel/src/protobuf.ts");
            var messagePack = ReadRepoText("Tools/foxglove-extensions/foxrun-publish-panel/src/msgpack.ts");
            var probe = ReadRepoText("Scripts/smoke/websocket/phase176_foxrun_publish_panel_probe.py");
            var manualProtobuf = ReadRepoText("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase175ProtobufManualAcceptance.cs");
            var manualJson = ReadRepoText("Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase175JsonManualAcceptance.cs");
            var saveState = Slice(panel, "function saveState()", "function selectedContract()");

            Check(package.Contains("\"name\": \"foxrun-publish-panel\"", StringComparison.Ordinal)
                  && panel.Contains("context.callService(CATALOG_SERVICE, {})", StringComparison.Ordinal)
                  && panel.Contains("if (contract.encoding === \"json\")", StringComparison.Ordinal)
                  && panel.Contains("renderFieldForm", StringComparison.Ordinal)
                  && panel.Contains("readFieldMessage", StringComparison.Ordinal)
                  && !panel.Contains("<textarea id=\"payload\"", StringComparison.Ordinal)
                  && panel.Contains("await directClient.publish", StringComparison.Ordinal)
                  && protocol.Contains("DirectFoxRunEncoding", StringComparison.Ordinal)
                  && protocol.Contains("encoding !== \"protobuf\" && encoding !== \"msgpack\"", StringComparison.Ordinal)
                  && protocol.Contains("MESSAGE_DATA_OPCODE = 1", StringComparison.Ordinal)
                  && protobuf.Contains("encodeProtobufMessage", StringComparison.Ordinal)
                  && messagePack.Contains("encodeMessagePackMessage", StringComparison.Ordinal)
                  && panel.Contains("encodeMessagePackMessage(contract.fields, message)", StringComparison.Ordinal)
                  && probe.Contains("phase175_main", StringComparison.Ordinal),
                "176D-1: FoxRun Publish loads Unity contracts and keeps JSON, Protobuf, and MessagePack wire paths explicit and separate");
            Check(!saveState.Contains("token:", StringComparison.Ordinal)
                  && panel.Contains("if (inFlight)", StringComparison.Ordinal)
                  && panel.Contains("Skipped repeat tick", StringComparison.Ordinal)
                  && panel.Contains("Fire-and-forget", StringComparison.Ordinal),
                "176D-2: panel keeps tokens out of persisted state and reports no-queue repeat behavior honestly");
            Check(panel.Contains("JsonTopicAdvertisementTracker", StringComparison.Ordinal)
                  && panel.Contains("ensureJsonAdvertisement", StringComparison.Ordinal)
                  && panel.Contains("releaseJsonAdvertisement", StringComparison.Ordinal)
                  && manualProtobuf.Contains("receivedTargetMessageCount", StringComparison.Ordinal)
                  && manualProtobuf.Contains("observedTargetRateHz", StringComparison.Ordinal)
                  && manualProtobuf.Contains("OnClientMessageWithEncoding", StringComparison.Ordinal)
                  && manualJson.Contains("receivedJsonMessageCount", StringComparison.Ordinal)
                  && manualJson.Contains("observedJsonRateHz", StringComparison.Ordinal)
                  && manualJson.Contains("LegacyJsonTopic", StringComparison.Ordinal)
                  && manualJson.Contains("OnClientMessageWithEncoding", StringComparison.Ordinal),
                "176D-3: repeated JSON reuses one advertised channel and both manual probes expose per-message receive rate");
        }

        private static void VerifyRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase176"
                                                           && item.Name == "Phase 176: FoxRun Subscribe Data and Publish panel"
                                                           && item.Evidence == ValidationEvidence.Structural),
                "176E-1: validation registry classifies the Subscribe Data and Publish panel source inspection as structural evidence");
        }

        private static string Slice(string source, string start, string end)
        {
            var startIndex = source.IndexOf(start, StringComparison.Ordinal);
            var endIndex = startIndex < 0 ? -1 : source.IndexOf(end, startIndex, StringComparison.Ordinal);
            return startIndex < 0 || endIndex < 0 ? string.Empty : source.Substring(startIndex, endIndex - startIndex);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
