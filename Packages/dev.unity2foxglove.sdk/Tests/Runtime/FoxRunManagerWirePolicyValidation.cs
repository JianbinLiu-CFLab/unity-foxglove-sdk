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
            var resolver = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunWireEncodingResolver.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var publishing = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPublishing.cs");
            var migration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(resolver.Contains("ValidateManagerDefault", StringComparison.Ordinal)
                  && resolver.Contains("PublishAndSubscribe requires an explicit", StringComparison.Ordinal),
                "175C-1: resolver accepts only concrete directional defaults and rejects ambiguous bidirectional inheritance");
            Check(inbound.Contains("_defaultFoxRunWireEncoding = FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunSubscriptionEncoding", StringComparison.Ordinal)
                  && publishing.Contains("_defaultFoxRunPublishEncoding", StringComparison.Ordinal)
                  && migration.Contains("ISerializationCallbackReceiver", StringComparison.Ordinal)
                  && migration.Contains("FoxRunWireEncodingPolicyMigration.Migrate", StringComparison.Ordinal),
                "175C-2: Manager retains the legacy policy source and migrates it into directional defaults in player-safe deserialization");
            var beginIndex = server.IndexOf("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal);
            var captureIndex = server.IndexOf("CaptureFoxRunPublishEncodingForServer();", StringComparison.Ordinal);
            var schemaRegistrationIndex = server.IndexOf("FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas", StringComparison.Ordinal);
            Check(beginIndex >= 0
                  && captureIndex > beginIndex
                  && schemaRegistrationIndex > captureIndex
                  && inbound.Contains("ActiveFoxRunSubscriptionSessionPolicy.WebSocketSubscriptionEncoding", StringComparison.Ordinal)
                  && publishing.Contains("_activeFoxRunPublishEncoding = DefaultFoxRunPublishEncoding;", StringComparison.Ordinal)
                  && publishing.Contains("_hasActiveFoxRunPublishEncoding", StringComparison.Ordinal)
                  && server.Contains("ClearFoxRunPublishEncodingForServer();", StringComparison.Ordinal),
                "175C-3: Manager freezes subscription policy independently and keeps publish encoding server-scoped");
        }

        private static void VerifyIndependentSubscriptionSessionLifecycleAndMigration()
        {
            var policy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSubscriptionSessionPolicy.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var inbound = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var migration = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var helper = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunWireEncodingPolicyMigration.cs");
            var copyBudgetPolicy = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunRos2NativeCopyBudgetPolicy.cs");

            Check(policy.Contains("public sealed class FoxRunSubscriptionSessionPolicy", StringComparison.Ordinal)
                  && policy.Contains("public ulong SessionGeneration { get; }", StringComparison.Ordinal)
                  && policy.Contains("public bool SubscriptionsEnabled { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxRunSubscriptionProvider DefaultProvider { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxRunWireEncoding WebSocketSubscriptionEncoding { get; }", StringComparison.Ordinal)
                  && policy.Contains("public FoxRunRos2QosPreset DefaultRos2Qos { get; }", StringComparison.Ordinal)
                  && policy.Contains("public int NativeCopyBudgetBytes { get; }", StringComparison.Ordinal)
                  && policy.Contains("public int MainThreadApplyRateLimitHz { get; }", StringComparison.Ordinal)
                  && policy.Contains("if (generation == ulong.MaxValue)", StringComparison.Ordinal)
                  && policy.Contains("throw new InvalidOperationException(", StringComparison.Ordinal)
                  && policy.Contains("var nextGeneration = generation + 1UL;", StringComparison.Ordinal)
                  && !policy.Contains("generation == ulong.MaxValue ? 1UL", StringComparison.Ordinal),
                "175C-3A: immutable subscription snapshots expose seven concrete fields and fail closed before generation reuse");

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
            var onDisableEndIndex = onDisable.IndexOf("EndFoxRunSubscriptionSession();", StringComparison.Ordinal);
            var onDisableStopIndex = onDisable.IndexOf("StopServer(", StringComparison.Ordinal);
            var onDestroyEndIndex = onDestroy.IndexOf("EndFoxRunSubscriptionSession();", StringComparison.Ordinal);
            var onDestroyStopIndex = onDestroy.IndexOf("StopServer(", StringComparison.Ordinal);
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
                  && onEnableBeginIndex < onEnableStartIndex
                  && update.Contains("SyncFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && onDisableEndIndex >= 0
                  && onDisableStopIndex >= 0
                  && onDisableEndIndex < onDisableStopIndex
                  && onDestroyEndIndex >= 0
                  && onDestroyStopIndex >= 0
                  && onDestroyEndIndex < onDestroyStopIndex,
                "175C-3B: subscription lifecycle isolates transition observers and preserves teardown ordering");

            var startServer = Slice(server, "public void StartServer()", "private void CleanupStartupAfterFailure()");
            var stopServer = Slice(server, "private void StopServer(bool restoreLivePublishers)", "private void DetachRuntimeForwarders");
            var startServerBeginIndex = startServer.IndexOf("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal);
            var outputDisabledIndex = startServer.IndexOf("if (!_foxgloveOutputEnabled)", StringComparison.Ordinal);
            var publishCaptureIndex = startServer.IndexOf("CaptureFoxRunPublishEncodingForServer();", StringComparison.Ordinal);
            var schemaRegistrationIndex = startServer.IndexOf("FoxRunSchemaInfoRegistry.RegisterGeneratedSchemas", StringComparison.Ordinal);
            Check(startServerBeginIndex >= 0
                  && outputDisabledIndex >= 0
                  && startServerBeginIndex < outputDisabledIndex
                  && publishCaptureIndex >= 0
                  && schemaRegistrationIndex >= 0
                  && publishCaptureIndex < schemaRegistrationIndex
                  && stopServer.Contains("ClearFoxRunPublishEncodingForServer();", StringComparison.Ordinal)
                  && !stopServer.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal),
                "175C-3C: output-off and server-stop paths preserve an enabled subscription session");

            var onValidate = Slice(manager, "private void OnValidate()", "private void OnEnable()");
            var compatibilitySetter = Slice(
                inbound,
                "public FoxRunWireEncoding DefaultFoxRunWireEncoding",
                "/// <summary>Compatibility alias for the former single active FoxRun default.</summary>");
            Check(inbound.Contains("_defaultFoxRunSubscriptionProvider = FoxRunSubscriptionProvider.FoxgloveWebSocket", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunRos2Qos = FoxRunRos2QosPreset.Default", StringComparison.Ordinal)
                  && inbound.Contains("_foxRunRos2NativeCopyBudgetBytes = FoxRunWireEncodingPolicyMigration.DefaultRos2NativeCopyBudgetBytes", StringComparison.Ordinal)
                  && inbound.Contains("_defaultFoxRunSubscriptionEncoding = FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && migration.Contains("ref _defaultFoxRunSubscriptionProvider", StringComparison.Ordinal)
                  && migration.Contains("ref _defaultFoxRunRos2Qos", StringComparison.Ordinal)
                  && migration.Contains("ref _foxRunRos2NativeCopyBudgetBytes", StringComparison.Ordinal)
                  && helper.Contains("CurrentSerializationVersion = 2", StringComparison.Ordinal)
                  && copyBudgetPolicy.Contains("public const int MinBytes = 1024", StringComparison.Ordinal)
                  && copyBudgetPolicy.Contains("public const int MaxBytes = 256 * 1024 * 1024", StringComparison.Ordinal)
                  && copyBudgetPolicy.Contains("public const int DefaultBytes = 4 * 1024 * 1024", StringComparison.Ordinal)
                  && helper.Contains("MinRos2NativeCopyBudgetBytes = FoxRunRos2NativeCopyBudgetPolicy.MinBytes", StringComparison.Ordinal)
                  && helper.Contains("MaxRos2NativeCopyBudgetBytes = FoxRunRos2NativeCopyBudgetPolicy.MaxBytes", StringComparison.Ordinal)
                  && helper.Contains("DefaultRos2NativeCopyBudgetBytes = FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes", StringComparison.Ordinal)
                  && helper.Contains("=> FoxRunRos2NativeCopyBudgetPolicy.NormalizeSerializedBytes(configuredBytes)", StringComparison.Ordinal)
                  && helper.Contains("FoxRunRos2QosResolver.NormalizeSerializedManagerDefault(qos)", StringComparison.Ordinal)
                  && compatibilitySetter.Contains("_foxRunPolicySerializationVersion = FoxRunWireEncodingPolicyMigration.CurrentSerializationVersion", StringComparison.Ordinal)
                  && !inbound.Contains("FormerlySerializedAs", StringComparison.Ordinal)
                  && !onValidate.Contains("FoxRunWireEncodingPolicyMigration.Migrate", StringComparison.Ordinal)
                  && !migration.Contains("BeginFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && !session.Contains("SerializeField", StringComparison.Ordinal),
                "175C-3D: additive deserialization migrates provider, QoS, and copy budget without changing the legacy field boundary");
        }

        private static void VerifyGeneratedInheritedDualCodecDispatch()
        {
            var input = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");

            Check(input.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal)
                  && input.Contains("Unsupported FoxRun inbound wire encoding", StringComparison.Ordinal),
                "175C-4: generated inbound dispatch preserves Inherit and supports both concrete encodings");
            Check(publish.Contains("mgr.ResolveFoxRunWireEncoding(FoxRunWireEncoding.Inherit, FoxRunMode.PublishOnly)", StringComparison.Ordinal),
                "175C-5: generated publish dispatch resolves inherited encoding through the publish policy");
            Check(router.Contains("DeclaredWireEncoding", StringComparison.Ordinal)
                  && router.Contains("DefaultSubscriptionWireEncoding", StringComparison.Ordinal),
                "175C-6: input router separates declared and effective subscription contracts");
        }

        private static void VerifyInspectorContract()
        {
            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var labels = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunEncodingEditorLabels.cs");
            var main = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");

            Check(main.Contains("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Publish Data\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"MCAP Record & Replay\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                  && main.IndexOf("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal)
                     < main.IndexOf("DrawSection(\"FoxServices\"", StringComparison.Ordinal),
                "175C-7: Subscribe Data Inspector section sits between MCAP and FoxServices");
            Check(labels.Contains("ManagerDefaultLabels = { \"Protobuf\", \"JSON\" }", StringComparison.Ordinal)
                  && labels.Contains("property.enumValueIndex == (int)FoxRunWireEncoding.Json ? 1 : 0", StringComparison.Ordinal)
                  && labels.Contains("property.enumValueIndex = selected == 0", StringComparison.Ordinal)
                  && labels.Contains("? (int)FoxRunWireEncoding.Protobuf", StringComparison.Ordinal)
                  && labels.Contains(": (int)FoxRunWireEncoding.Json", StringComparison.Ordinal)
                  && !labels.Contains("MsgPack", StringComparison.Ordinal)
                  && !labels.Contains("ROS2", StringComparison.Ordinal),
                "175C-8: Manager dropdown offers only Protobuf and JSON and cannot persist Inherit");
            Check(inspector.Contains("Default Subscription Protocol", StringComparison.Ordinal)
                  && inspector.Contains("Subscription Rate Limit Hz (per Topic)", StringComparison.Ordinal)
                  && inspector.Contains("Subscription-policy changes apply after subscriptions are re-enabled.", StringComparison.Ordinal)
                  && inspector.Contains("captured provider, WebSocket encoding, QoS, and copy budget", StringComparison.Ordinal),
                "175C-9: Inspector exposes subscription controls and the session re-enable boundary");
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
                  && source.Contains("Mode = FoxRunMode.SubscribeOnly, Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 1", StringComparison.Ordinal)
                  && source.Contains("Mode = FoxRunMode.PublishAndSubscribe, Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 1", StringComparison.Ordinal)
                  && source.Contains("#pragma warning disable FOXRUN400", StringComparison.Ordinal)
                  && source.Contains("remote-authoritative shared observation", StringComparison.Ordinal)
                  && source.Contains("#pragma warning restore FOXRUN400", StringComparison.Ordinal)
                  && !source.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal),
                "175C-10: manual acceptance pins Protobuf contracts and documents bidirectional authority");
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
                  && source.Contains("Mode = FoxRunMode.SubscribeOnly, Encoding = FoxRunWireEncoding.Json", StringComparison.Ordinal)
                  && source.Contains("requestedLegacyJsonState", StringComparison.Ordinal)
                  && source.Contains("Applied JSON legacy state", StringComparison.Ordinal)
                  && !source.Contains("FoxRunWireEncoding.Inherit", StringComparison.Ordinal),
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
