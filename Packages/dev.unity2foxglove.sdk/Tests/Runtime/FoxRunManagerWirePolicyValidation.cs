// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards Phase175C Manager wire-policy migration and Inspector contracts.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase175CValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 175C Tests ---");
            _passed = 0;

            VerifyPolicyResolverAndFrozenSessionState();
            VerifyIndependentSubscriptionSessionLifecycleAndMigration();
            VerifyGeneratedInheritedDualCodecDispatch();
            VerifyInspectorContract();
            VerifyExplicitProtobufManualAcceptance();
            VerifyExplicitJsonManualAcceptance();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 175C: " + _passed + " checks passed.\n");
        }

        private static void VerifyPolicyResolverAndFrozenSessionState()
        {
            var resolver = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunEncodingResolver.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPublishing.cs");
            var migration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");

            Check(resolver.Contains("ValidateProfileDefault", StringComparison.Ordinal)
                  && resolver.Contains("Full-duplex omitted Encoding must be resolved per direction.", StringComparison.Ordinal),
                "175C-1: resolver accepts only concrete directional defaults and rejects ambiguous bidirectional inheritance");
            Check(inbound.Contains("_defaultFoxRunEncoding =", StringComparison.Ordinal)
                  && inbound.Contains("FoxRunEncoding.Protobuf", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                  && publishing.Contains("_defaultFoxRunPublishEncoding", StringComparison.Ordinal)
                  && migration.Contains("ISerializationCallbackReceiver", StringComparison.Ordinal)
                  && migration.Contains("FoxRunEncodingPolicyMigration.Migrate", StringComparison.Ordinal),
                "175C-2: Manager retains the legacy policy source and migrates it into directional defaults in player-safe deserialization");
            var onEnable = Slice(manager, "private void OnEnable()", "private void Update()");
            var publishBeginIndex = onEnable.IndexOf("BeginFoxRunPublishSessionIfNeeded();", StringComparison.Ordinal);
            var subscriptionBeginIndex = onEnable.IndexOf("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal);
            var serverStartIndex = onEnable.IndexOf("StartServer();", StringComparison.Ordinal);
            Check(publishBeginIndex >= 0
                  && subscriptionBeginIndex > publishBeginIndex
                  && serverStartIndex > subscriptionBeginIndex
                  && inbound.Contains("ActiveFoxRunSubscriptionSessionPolicy", StringComparison.Ordinal)
                  && inbound.Contains(".WebSocketEncoding", StringComparison.Ordinal)
                  && publishing.Contains("FoxRunPublishSessionState", StringComparison.Ordinal)
                  && publishing.Contains("_foxRunPublishSessionState.BeginIfNeeded(", StringComparison.Ordinal)
                  && manager.Contains("EndFoxRunPublishSession,", StringComparison.Ordinal),
                "175C-3: Manager freezes directional policies before transports and releases publish policy with the Manager lifetime");
        }

        private static void VerifyIndependentSubscriptionSessionLifecycleAndMigration()
        {
            var policy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSubscriptionSessionPolicy.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var migration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var helper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunEncodingPolicyMigration.cs");
            var providers = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");

            Check(policy.Contains("public sealed class FoxRunSubscriptionSessionPolicy", StringComparison.Ordinal)
                  && policy.Contains("public ulong SessionGeneration { get; }", StringComparison.Ordinal)
                  && policy.Contains("public bool SubscriptionsEnabled { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxRunTransportId DefaultProvider { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxRunEncoding WebSocketEncoding { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxRunDeliveryPolicy DefaultDeliveryPolicy { get; }", StringComparison.Ordinal)
                  && policy.Contains("public int TransportAdmissionRateLimitHz { get; }", StringComparison.Ordinal)
                  && policy.Contains("public int DefaultSubscribeRateHz { get; }", StringComparison.Ordinal)
                  && policy.Contains("public int MaxPayloadBytes { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxgloveMsgPackReadLimits MessagePackReadLimits { get; }", StringComparison.Ordinal)
                  && policy.Contains("if (Current.SessionGeneration == ulong.MaxValue)", StringComparison.Ordinal)
                  && policy.Contains("throw new InvalidOperationException(", StringComparison.Ordinal)
                  && policy.Contains("Current.SessionGeneration + 1UL", StringComparison.Ordinal)
                  && !policy.Contains("ulong.MaxValue ? 1UL", StringComparison.Ordinal),
                "175C-3A: immutable subscription snapshots expose Provider-neutral wire, delivery, and input limits and fail closed before generation reuse");

            var onEnable = Slice(manager, "private void OnEnable()", "private void Update()");
            var update = Slice(manager, "private void Update()", "private void OnDisable()");
            var onDisable = Slice(manager, "private void OnDisable()", "private void OnDestroy()");
            var onDestroy = Slice(manager, "private void OnDestroy()", "private static string ProjectRoot");
            var beginSession = Slice(
                session,
                "internal void BeginFoxRunSubscriptionSessionIfNeeded()",
                "internal void EndFoxRunSubscriptionSession()");
            var endSession = Slice(
                session,
                "internal void EndFoxRunSubscriptionSession()",
                "private void SyncFoxRunSubscriptionSession()");
            var notifySession = Slice(
                session,
                "private void NotifyFoxRunSubscriptionSessionChanged(",
                "private void SyncFoxRunSubscriptionSession()");
            var onEnableBeginIndex = onEnable.IndexOf("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal);
            var onEnableStartIndex = onEnable.IndexOf("StartServer();", StringComparison.Ordinal);
            var onEnablePublishIndex = onEnable.IndexOf("BeginFoxRunPublishSessionIfNeeded();", StringComparison.Ordinal);
            var onDisableEndIndex = onDisable.IndexOf("EndFoxRunSubscriptionSession,", StringComparison.Ordinal);
            var onDisableStopIndex = onDisable.IndexOf("() => StopServer(", StringComparison.Ordinal);
            var onDisablePublishIndex = onDisable.IndexOf("EndFoxRunPublishSession,", StringComparison.Ordinal);
            var onDestroyEndIndex = onDestroy.IndexOf("EndFoxRunSubscriptionSession,", StringComparison.Ordinal);
            var onDestroyStopIndex = onDestroy.IndexOf("() => StopServer(", StringComparison.Ordinal);
            var onDestroyPublishIndex = onDestroy.IndexOf("EndFoxRunPublishSession,", StringComparison.Ordinal);
            var beginStateIndex = beginSession.IndexOf("_foxRunSubscriptionSessionState.BeginIfNeeded(", StringComparison.Ordinal);
            var beginNotifyIndex = beginSession.IndexOf("NotifyFoxRunSubscriptionSessionChanged(policy);", StringComparison.Ordinal);
            var endStateIndex = endSession.IndexOf("_foxRunSubscriptionSessionState.End();", StringComparison.Ordinal);
            var endNotifyIndex = endSession.IndexOf("NotifyFoxRunSubscriptionSessionChanged(policy);", StringComparison.Ordinal);
            Check(session.Contains("public event Action<FoxRunSubscriptionSessionPolicy> FoxRunSubscriptionSessionChanged", StringComparison.Ordinal)
                  && session.Contains("BeginFoxRunSubscriptionSessionIfNeeded", StringComparison.Ordinal)
                  && session.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && notifySession.Contains("var handlers = FoxRunSubscriptionSessionChanged;", StringComparison.Ordinal)
                  && notifySession.Contains("foreach (var subscriber in handlers.GetInvocationList())", StringComparison.Ordinal)
                  && notifySession.Contains("((Action<FoxRunSubscriptionSessionPolicy>)subscriber)(policy);", StringComparison.Ordinal)
                  && notifySession.Contains("catch (Exception ex)", StringComparison.Ordinal)
                  && notifySession.Contains("Debug.LogException(ex);", StringComparison.Ordinal)
                  && session.Contains("Callbacks run on the Unity main thread after the current snapshot has been updated.", StringComparison.Ordinal)
                  && session.Contains("Late subscribers must read ActiveFoxRunSubscriptionSessionPolicy immediately after attaching.", StringComparison.Ordinal)
                  && !session.Contains("FoxRunSubscriptionSessionChanged?.Invoke", StringComparison.Ordinal)
                  && beginStateIndex >= 0
                  && beginNotifyIndex >= 0
                  && beginStateIndex < beginNotifyIndex
                  && endStateIndex >= 0
                  && endNotifyIndex >= 0
                  && endStateIndex < endNotifyIndex
                  && onEnableBeginIndex >= 0
                  && onEnableStartIndex >= 0
                  && onEnablePublishIndex >= 0
                  && onEnablePublishIndex < onEnableBeginIndex
                  && onEnableBeginIndex < onEnableStartIndex
                  && update.Contains("SyncFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && onDisableEndIndex >= 0
                  && onDisableStopIndex >= 0
                  && onDisableEndIndex < onDisableStopIndex
                  && onDisablePublishIndex > onDisableStopIndex
                  && onDestroyEndIndex >= 0
                  && onDestroyStopIndex >= 0
                  && onDestroyEndIndex < onDestroyStopIndex
                  && onDestroyPublishIndex > onDestroyStopIndex,
                "175C-3B: subscription lifecycle isolates transition observers and preserves teardown ordering");

            var startServer = Slice(server, "public void StartServer()", "private void CleanupStartupAfterFailure()");
            var stopServer = Slice(server, "private void StopServer(bool restoreLivePublishers)", "private void DetachRuntimeForwarders");
            var startServerBeginIndex = startServer.IndexOf("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal);
            var outputDisabledIndex = startServer.IndexOf("if (!_foxgloveOutputEnabled)", StringComparison.Ordinal);
            var schemaRegistrationIndex = startServer.IndexOf("FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas", StringComparison.Ordinal);
            Check(startServerBeginIndex >= 0
                  && outputDisabledIndex >= 0
                  && startServerBeginIndex < outputDisabledIndex
                  && schemaRegistrationIndex >= 0
                  && !startServer.Contains("BeginFoxRunPublishSessionIfNeeded", StringComparison.Ordinal)
                  && !stopServer.Contains("EndFoxRunPublishSession", StringComparison.Ordinal)
                  && !stopServer.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal),
                "175C-3C: individual server restarts preserve both Manager-lifetime directional session snapshots");

            var onValidate = Slice(manager, "private void OnValidate()", "private void OnEnable()");
            Check(inbound.Contains("_defaultFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                  && inbound.Contains("FoxRunEncoding.Protobuf", StringComparison.Ordinal)
                  && inbound.Contains("_foxRunInboundMaxPayloadBytes", StringComparison.Ordinal)
                  && migration.Contains("ref _defaultFoxRunPublishEncoding", StringComparison.Ordinal)
                  && migration.Contains("ref _defaultFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                  && helper.Contains("DirectionalSerializationVersion = 1", StringComparison.Ordinal)
                  && providers.Contains("_foxRunPublishTransportIds", StringComparison.Ordinal)
                  && providers.Contains("_foxRunSubscribeTransportId", StringComparison.Ordinal)
                  && providers.Contains("ConfigureFoxRunTransports(", StringComparison.Ordinal)
                  && providers.Contains("TryCaptureSession(", StringComparison.Ordinal)
                  && providers.Contains("FoxgloveWebSocketTransport.Id", StringComparison.Ordinal)
                  && !inbound.Contains("public FoxRunEncoding DefaultFoxRunEncoding", StringComparison.Ordinal)
                  && !inbound.Contains("public FoxRunEncoding ActiveFoxRunDefaultWireEncoding", StringComparison.Ordinal)
                  && !inbound.Contains("public FoxRunEncoding ResolveFoxRunEncoding(FoxRunEncoding declaredEncoding)", StringComparison.Ordinal)
                  && inbound.Contains("_foxRunDefaultApplyRateHz", StringComparison.Ordinal)
                  && !inbound.Contains("Ros2", StringComparison.Ordinal)
                  && !providers.Contains("Ros2", StringComparison.Ordinal)
                  && !onValidate.Contains("FoxRunEncodingPolicyMigration.Migrate", StringComparison.Ordinal)
                  && !migration.Contains("BeginFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && !session.Contains("SerializeField", StringComparison.Ordinal),
                "175C-3D: deserialization keeps directional WebSocket migration while Provider selection and input limits remain ROS-free");
        }

        private static void VerifyGeneratedInheritedDualCodecDispatch()
        {
            var input = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");

            Check(input.Contains("(FoxRunEncoding)0", StringComparison.Ordinal)
                  && input.Contains("Unsupported FoxRun inbound wire encoding", StringComparison.Ordinal),
                "175C-4: generated inbound dispatch preserves Inherit and supports every concrete wire encoding");
            Check(hub.Contains("ResolveWebSocketEncoding(", StringComparison.Ordinal)
                  && hub.Contains("_manager.ActiveFoxRunPublishEncoding", StringComparison.Ordinal)
                  && hub.Contains("manager.ActiveFoxRunPublishSessionPolicy", StringComparison.Ordinal)
                  && hub.Contains("active.PublishTransportIds", StringComparison.Ordinal)
                  && hub.Contains("IFoxRunWebSocketCaptureSource", StringComparison.Ordinal)
                  && publish.Contains("__foxRunCaptureEncoding_", StringComparison.Ordinal)
                  && publish.Contains("FoxgloveLog_SetWebSocketEncoding", StringComparison.Ordinal),
                "175C-5: generated publish dispatch freezes inherited WebSocket encoding and Provider IDs through the active policy");
            Check(router.Contains("DeclaredEncoding", StringComparison.Ordinal)
                  && router.Contains("DefaultSubscriptionEncoding", StringComparison.Ordinal),
                "175C-6: input router separates declared and effective subscription contracts");
        }

        private static void VerifyInspectorContract()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var labels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEncodingEditorLabels.cs");
            var main = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();

            Check(main.Contains("DrawSection(\"Data Transport\"", StringComparison.Ordinal)
                  && !main.Contains("DrawSection(\"Publish Data\"", StringComparison.Ordinal)
                  && !main.Contains("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Data Transport\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"FoxServices\"", StringComparison.Ordinal)
                  && editorSources.Contains("DrawDataTransportSubsection", StringComparison.Ordinal)
                  && editorSources.Contains("\"Publish Data\"", StringComparison.Ordinal)
                  && editorSources.Contains("\"Subscribe Data\"", StringComparison.Ordinal),
                "175C-7: Data Transport contains Publish and Subscribe before sibling MCAP and FoxServices");
            Check(labels.Contains("ManagerDefaultLabels = { \"Protobuf\", \"JSON\", \"MessagePack\" }", StringComparison.Ordinal)
                  && labels.Contains("var selected = property.intValue switch", StringComparison.Ordinal)
                  && labels.Contains("(int)FoxRunEncoding.Protobuf => 0", StringComparison.Ordinal)
                  && labels.Contains("(int)FoxRunEncoding.JSON => 1", StringComparison.Ordinal)
                  && labels.Contains("(int)FoxRunEncoding.MessagePack => 2", StringComparison.Ordinal)
                  && labels.Contains("property.intValue = selected switch", StringComparison.Ordinal)
                  && labels.Contains("0 => (int)FoxRunEncoding.Protobuf", StringComparison.Ordinal)
                  && labels.Contains("1 => (int)FoxRunEncoding.JSON", StringComparison.Ordinal)
                  && labels.Contains("2 => (int)FoxRunEncoding.MessagePack", StringComparison.Ordinal)
                  && !labels.Contains("property.enumValueIndex", StringComparison.Ordinal)
                  && !labels.Contains("ROS2", StringComparison.Ordinal),
                "175C-8: Manager dropdown maps popup indices through serialized enum values without using enumValueIndex as the wire enum");
            Check(inspector.Contains("Subscription Control", StringComparison.Ordinal)
                  && inspector.Contains("Subscribe Source", StringComparison.Ordinal)
                  && inspector.Contains("WebSocket Encoding", StringComparison.Ordinal)
                  && inspector.Contains("Maximum Payload Bytes", StringComparison.Ordinal)
                  && inspector.Contains("Default Subscribe Rate Hz", StringComparison.Ordinal)
                  && inspector.Contains("Maximum Subscribe Rate Hz (per Topic)", StringComparison.Ordinal)
                  && inspector.IndexOf("Default Subscribe Rate Hz", StringComparison.Ordinal)
                     < inspector.IndexOf("Maximum Subscribe Rate Hz (per Topic)", StringComparison.Ordinal)
                  && inspector.Contains(
                      "Subscription profile changes apply after subscriptions are disabled and re-enabled.",
                      StringComparison.Ordinal)
                  && inspector.Contains(
                      "captured Provider, encoding, rate, and payload bounds",
                      StringComparison.Ordinal),
                "175C-9: Inspector exposes Provider-neutral subscription controls and the session re-enable boundary");
        }

        private static void VerifyExplicitProtobufManualAcceptance()
        {
            const string relativePath = "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase175ProtobufManualAcceptance.cs";
            var root = Phase16Validation.FindRepoRoot();
            var fullPath = root == null
                ? string.Empty
                : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

            Check(source.Contains("class Phase175ProtobufManualAcceptance", StringComparison.Ordinal)
                  && source.Contains("/phase175/protobuf/target-value", StringComparison.Ordinal)
                  && source.Contains("/phase175/protobuf/shared-state", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf, ProtobufFieldNumber = 1", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunEncoding.Protobuf, ProtobufFieldNumber = 1", StringComparison.Ordinal)
                  && source.Contains("remote-authoritative shared observation", StringComparison.Ordinal)
                  && !source.Contains("FOXRUN400", StringComparison.Ordinal)
                  && !source.Contains("(FoxRunEncoding)0", StringComparison.Ordinal),
                "175C-10: manual acceptance pins Protobuf contracts, documents bidirectional authority, and needs no warning suppression");
        }

        private static void VerifyExplicitJsonManualAcceptance()
        {
            const string relativePath = "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase175JsonManualAcceptance.cs";
            var root = Phase16Validation.FindRepoRoot();
            var fullPath = root == null
                ? string.Empty
                : Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var source = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

            Check(source.Contains("class Phase175JsonManualAcceptance", StringComparison.Ordinal)
                  && source.Contains("/phase175/json/legacy-state", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON", StringComparison.Ordinal)
                  && source.Contains("requestedLegacyJsonState", StringComparison.Ordinal)
                  && source.Contains("Applied JSON legacy state", StringComparison.Ordinal)
                  && !source.Contains("(FoxRunEncoding)0", StringComparison.Ordinal),
                "175C-11: manual acceptance pins an explicit JSON legacy contract");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase175c"
                                                           && item.Name == "Phase 175C: FoxRun Manager wire policy and migration"),
                "175C-12: validation registry exposes a descriptive Manager policy gate");
        }

        private static string Slice(string source, string start, string end)
        {
            var startIndex = source.IndexOf(start, StringComparison.Ordinal);
            var endIndex = startIndex < 0 ? -1 : source.IndexOf(end, startIndex, StringComparison.Ordinal);
            return startIndex < 0 || endIndex < 0
                ? string.Empty
                : source.Substring(startIndex, endIndex - startIndex);
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
