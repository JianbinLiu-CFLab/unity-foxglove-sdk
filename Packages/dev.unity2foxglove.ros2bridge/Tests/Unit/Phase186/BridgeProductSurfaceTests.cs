// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Phase186
{
    public sealed class BridgeProductSurfaceTests
    {
        private const string SampleRoot =
            "Packages/dev.unity2foxglove.ros2bridge/Samples~/Ros2BridgeSample";

        [Fact]
        public void SampleDeclaresPublishSubscribeAndFullDuplexContracts()
        {
            var path = PathOf(SampleRoot + "/Scripts/Ros2BridgeSampleDuplex.cs");
            Assert.True(File.Exists(path), path);
            var source = File.ReadAllText(path);

            Assert.Contains("Mode = FoxRunFlow.Publish,", source, StringComparison.Ordinal);
            Assert.Contains("Mode = FoxRunFlow.Subscribe,", source, StringComparison.Ordinal);
            Assert.Contains(
                "Mode = FoxRunFlow.PublishAndSubscribe,",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "Policy = FoxRunPolicy.Change,",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "private Foxglove.Log _incoming",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "private Foxglove.Log _subscribe",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "Ros2BridgeTransportProvider.ProviderId",
                source,
                StringComparison.Ordinal);
            Assert.Contains("Foxglove.Log", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SampleSceneHasAUnityOwnedBuilderAndDuplexComponent()
        {
            var builderPath = PathOf(
                SampleRoot + "/Editor/Ros2BridgeSampleSceneBuilder.cs");
            Assert.True(File.Exists(builderPath), builderPath);
            var builder = File.ReadAllText(builderPath);
            Assert.Contains("EditorSceneManager.SaveScene", builder, StringComparison.Ordinal);
            Assert.Contains("new SerializedObject(manager)", builder, StringComparison.Ordinal);
            Assert.Contains("_foxRunPublishTransportIds", builder, StringComparison.Ordinal);
            Assert.Contains("_foxRunSubscribeTransportId", builder, StringComparison.Ordinal);
            Assert.Contains("_enableFoxRunInbound", builder, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteAllText", builder, StringComparison.Ordinal);

            var duplexMeta = File.ReadAllText(PathOf(
                SampleRoot + "/Scripts/Ros2BridgeSampleDuplex.cs.meta"));
            var guidLine = Array.Find(
                duplexMeta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith("guid: ", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(guidLine));
            var scene = File.ReadAllText(PathOf(
                SampleRoot + "/Scenes/Ros2BridgeSample.unity"));
            Assert.Contains(guidLine, scene, StringComparison.Ordinal);
            Assert.Contains(
                "_foxRunSubscribeTransportId: unity2foxglove.ros2bridge",
                scene,
                StringComparison.Ordinal);
            Assert.Contains("_enableFoxRunInbound: 1", scene, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "/foxrun/phase186/p186h_",
                scene,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "phase186h-",
                scene,
                StringComparison.Ordinal);
        }

        [Fact]
        public void BridgeDocsLockProductBoundariesAndBreakingMigration()
        {
            var guide = File.ReadAllText(PathOf(
                "Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/16_ROS2_Bridge_Sample.md"));
            foreach (var required in new[]
                     {
                         "foxglove.websocket",
                         "unity2foxglove.r2fu",
                         "unity2foxglove.ros2bridge",
                         "zero or more",
                         "exactly one",
                         "no fallback",
                         "127.0.0.1",
                         "Starting",
                         "Reconnecting",
                         "PublishAndSubscribe"
                     })
            {
                Assert.Contains(required, guide, StringComparison.Ordinal);
            }
            Assert.Contains(
                "Arbitrary remote hosts, wildcard/LAN/public peers, TLS, ROS services, ROS actions, and ROS parameters are outside this product boundary.",
                guide.Replace("\r\n", "\n").Replace("\n", " "),
                StringComparison.Ordinal);
            foreach (var forbidden in new[]
                     {
                         "automatically installs",
                         "automatically starts",
                         "supports remote ROS",
                         "supports TLS",
                         "supports ROS services",
                         "supports ROS actions",
                         "supports ROS parameters"
                     })
            {
                Assert.DoesNotContain(forbidden, guide, StringComparison.OrdinalIgnoreCase);
            }

            var upgrade = File.ReadAllText(PathOf(
                "Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/PHASE186_BREAKING_UPGRADE.md"));
            foreach (var removed in new[]
                     {
                         "FoxRunEndpoint",
                         "Source",
                         "Targets",
                         "FoxRunQosProfile",
                         "_ros2NativeEnabled",
                         "_ros2BridgeEnabled",
                         "_ros2BridgeHost",
                         "_ros2BridgePort",
                         "_ros2BridgeAutoConnect",
                         "_defaultRos2BridgeOutputEnabled",
                         "_allowPublisherRos2BridgeOverride",
                         "_ros2BridgeNamespace",
                         "_ros2BridgeOutput",
                         "_ros2BridgeTopicOverride",
                         "_defaultFoxRunSubscriptionSource",
                         "_foxRunRos2NativeCopyBudgetBytes",
                         "_initialRosPackageName",
                         "_publishStandardRos2CompressedImage",
                         "_publishStandardRos2RawImage",
                         "FoxRunEndpointResolution",
                         "FoxRunRos2InterfacePackageWriter",
                         "Ros2BridgeRuntime",
                         "Ros2CdrWriter",
                         "FoxgloveRuntime.RegisterRos2MsgSchemaChannel",
                         "FoxglovePublisherBase.Ros2BridgeOutput"
                     })
            {
                Assert.Contains(removed, upgrade, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void RepositoryReadmeUsesProviderPackageAndDirectionalVocabulary()
        {
            var readme = File.ReadAllText(PathOf("README.md"));
            Assert.Contains(
                "dev.unity2foxglove.ros2bridge",
                readme,
                StringComparison.Ordinal);
            Assert.Contains(
                "PublishTransportIds",
                readme,
                StringComparison.Ordinal);
            Assert.Contains(
                "SubscribeTransportId",
                readme,
                StringComparison.Ordinal);
            Assert.DoesNotContain("FoxRunEndpoint", readme, StringComparison.Ordinal);
            Assert.DoesNotContain("Bridge is publish-only", readme, StringComparison.Ordinal);

            var protocol = File.ReadAllText(PathOf(
                "Packages/dev.unity2foxglove.ros2bridge/Documentation~/en/U2R2_PROTOCOL.md"));
            Assert.Contains(
                "production Bridge runtime consumes this v2 session",
                protocol,
                StringComparison.Ordinal);
            Assert.DoesNotContain("not yet wired", protocol, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase186AcceptanceHarnessIsUnityOwnedAndFailClosed()
        {
            const string runtimeRelative =
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186Ros2BridgeAcceptance.cs";
            const string interactionRelative =
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs";
            const string builderRelative =
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186Ros2BridgeAcceptanceBuilder.cs";
            const string probeRelative =
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186BatchModeRos2BridgeProbe.cs";
            const string sceneRelative =
                "Unity2Foxglove/Assets/Scenes/ManualAcceptance/Phase186Ros2BridgeAcceptance.unity";

            foreach (var relative in new[]
                     {
                         runtimeRelative,
                         interactionRelative,
                         builderRelative,
                         probeRelative,
                         sceneRelative,
                     })
            {
                Assert.True(File.Exists(PathOf(relative)), relative);
            }

            var runtime = File.ReadAllText(PathOf(runtimeRelative));
            var interaction = File.ReadAllText(PathOf(interactionRelative));
            Assert.DoesNotContain("UnityEngine", interaction, StringComparison.Ordinal);
            Assert.Contains("Phase186ManualStep", interaction, StringComparison.Ordinal);
            Assert.Contains("ObserveGeneratedTick", interaction, StringComparison.Ordinal);
            Assert.Contains("ResetForRun", interaction, StringComparison.Ordinal);
            Assert.Contains("TryRequestLocalB", interaction, StringComparison.Ordinal);
            Assert.Contains("TryRequestComplete", interaction, StringComparison.Ordinal);
            Assert.Contains("partial void Phase186Generated_Describe", runtime, StringComparison.Ordinal);
            Assert.Contains("partial void Phase186Generated_Tick", runtime, StringComparison.Ordinal);
            Assert.Contains("PHASE186_MANUAL_COMPLETE", runtime, StringComparison.Ordinal);
            Assert.Contains("PHASE186_ACCEPTANCE_PASS", runtime, StringComparison.Ordinal);
            Assert.Contains("PHASE186_ACCEPTANCE_PROGRESS", runtime, StringComparison.Ordinal);
            Assert.Contains("ProgressFingerprint", runtime, StringComparison.Ordinal);
            Assert.Contains("tokenHash=", runtime, StringComparison.Ordinal);
            Assert.Contains("head=", runtime, StringComparison.Ordinal);
            Assert.Contains("CaptureFoxRunTransportStatuses", runtime, StringComparison.Ordinal);
            Assert.Contains("PublishLocalMutation", runtime, StringComparison.Ordinal);
            Assert.Contains("slowMainThread", runtime, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ProviderDirectionsReady", runtime, StringComparison.Ordinal);
            Assert.Contains("EvaluateManualInteraction", runtime, StringComparison.Ordinal);
            Assert.Contains("_manualInteraction.TryRequestLocalB", runtime, StringComparison.Ordinal);
            Assert.Contains("_manualInteraction.TryRequestComplete", runtime, StringComparison.Ordinal);
            Assert.Contains(
                "var appliedBeforeTick = _generatedEvidence.Applied;\n"
                + "            Phase186Generated_Tick(ref _generatedEvidence);\n"
                + "            _manualInteraction.ObserveGeneratedTick(\n"
                + "                _manual,\n"
                + "                appliedBeforeTick,\n"
                + "                _generatedEvidence.Applied,\n"
                + "                HasObservedExternalInput());",
                runtime.Replace("\r\n", "\n"),
                StringComparison.Ordinal);
            Assert.True(
                runtime.IndexOf("Phase186Generated_Tick(ref _generatedEvidence);", StringComparison.Ordinal)
                < runtime.IndexOf("_manualInteraction.ObserveGeneratedTick(", StringComparison.Ordinal));
            Assert.True(
                runtime.IndexOf("_manualInteraction.ResetForRun();", StringComparison.Ordinal)
                < runtime.IndexOf("_runId = runId;", StringComparison.Ordinal));
            Assert.Contains(
                "tokenHash=\" + _tokenHash\n                    + \" head=\" + _featureHead",
                runtime.Replace("\r\n", "\n"),
                StringComparison.Ordinal);
            Assert.Contains(
                "+ \" externalA=\" + (_manualInteraction.ExternalAObserved ? \"true\" : \"false\")",
                runtime,
                StringComparison.Ordinal);
            Assert.Contains(
                "+ (_manualInteraction.ExternalAObserved ? \"1\" : \"0\")",
                runtime,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (!_hasObservedConnection && stats.Connected)",
                runtime,
                StringComparison.Ordinal);
            Assert.Contains("PHASE186_FANOUT_FAILURE_INJECTED", runtime, StringComparison.Ordinal);
            Assert.Contains("unity-exercise-gate.json", runtime, StringComparison.Ordinal);

            var automaticProgress = Slice(
                runtime,
                "private void EmitAutomaticProgress(bool caseSpecific)",
                "private string ProgressFingerprint(bool caseSpecific)");
            foreach (var required in new[]
                     {
                         "PHASE186_ACCEPTANCE_PROGRESS run=",
                         "+ \" case=\" + _caseId",
                         "+ \" ready=\" + (_ready ? \"true\" : \"false\")",
                         "+ \" external=\" + (_externalGateReady ? \"true\" : \"false\")",
                         "+ \" generated=\" + (_generatedEvidence.CanComplete ? \"true\" : \"false\")",
                         "+ \" caseSpecific=\" + (caseSpecific ? \"true\" : \"false\")",
                         "+ \" connected=\" + (_connected ? \"true\" : \"false\")",
                         "+ \" publish=\" + _publishState",
                         "+ \" subscribe=\" + _subscribeState",
                         "+ \" received=\" + _generatedEvidence.Received.ToString(",
                         "+ \" applied=\" + _generatedEvidence.Applied.ToString(",
                         "+ \" diagnostic=\" + Phase186Bound(_lastDiagnostic)"
                     })
            {
                Assert.Contains(required, automaticProgress, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("tokenHash=", automaticProgress, StringComparison.Ordinal);
            Assert.DoesNotContain("head=", automaticProgress, StringComparison.Ordinal);
            Assert.DoesNotContain("externalA=", automaticProgress, StringComparison.Ordinal);

            var automaticFingerprint = Slice(
                runtime,
                "private string ProgressFingerprint(bool caseSpecific)",
                "private string ManualProgressFingerprint(bool caseSpecific)");
            Assert.Contains(
                "=> (_ready ? \"1\" : \"0\")\n"
                + "               + (_externalGateReady ? \"1\" : \"0\")\n"
                + "               + (_generatedEvidence.CanComplete ? \"1\" : \"0\")\n"
                + "               + (caseSpecific ? \"1\" : \"0\")\n"
                + "               + (_connected ? \"1\" : \"0\")\n"
                + "               + \":\" + _publishState\n"
                + "               + \":\" + _subscribeState\n"
                + "               + \":\" + Phase186Bound(_lastDiagnostic);",
                automaticFingerprint.Replace("\r\n", "\n"),
                StringComparison.Ordinal);

            var builder = File.ReadAllText(PathOf(builderRelative));
            Assert.Contains("EditorSceneManager.SaveScene", builder, StringComparison.Ordinal);
            Assert.Contains("EnsureFanoutProvider", builder, StringComparison.Ordinal);
            Assert.Contains("unity2foxglove.r2fu", builder, StringComparison.Ordinal);
            Assert.Contains("Ros2BridgeTransportProvider", builder, StringComparison.Ordinal);
            Assert.Contains("_foxRunPublishTransportIds", builder, StringComparison.Ordinal);
            Assert.Contains("_foxRunSubscribeTransportId", builder, StringComparison.Ordinal);
            Assert.Contains("_enableFoxRunInbound", builder, StringComparison.Ordinal);
            Assert.DoesNotContain("WriteAllText", builder, StringComparison.Ordinal);

            var probe = File.ReadAllText(PathOf(probeRelative));
            Assert.Contains("-phase186RunConfig", probe, StringComparison.Ordinal);
            Assert.Contains("Application.logMessageReceived", probe, StringComparison.Ordinal);
            Assert.Contains("SessionState", probe, StringComparison.Ordinal);
            Assert.Contains("PHASE186_ACCEPTANCE_PASS", probe, StringComparison.Ordinal);
            Assert.Contains("PHASE186_MANUAL_COMPLETE", probe, StringComparison.Ordinal);
            Assert.Contains(
                "PHASE186_MANUAL_SCENE_PREPARE_FAIL run=",
                probe,
                StringComparison.Ordinal);
            Assert.Contains("EditorApplication.EnterPlaymode", probe, StringComparison.Ordinal);
            var manualPrepare = Slice(
                probe,
                "public static void PrepareCurrentManualRun()",
                "private static void ResumePendingManualPreparation()");
            Assert.Contains(
                "SessionState.SetBool(Key(\"manual-prepare-pending\"), true);",
                manualPrepare,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "PHASE186_MANUAL_SCENE_READY",
                manualPrepare,
                StringComparison.Ordinal);

            var manualStabilization = Slice(
                probe,
                "private static void ContinueManualPreparation()",
                "private static void CompleteManualPreparation(");
            Assert.Contains(
                "EditorApplication.isCompiling || EditorApplication.isUpdating",
                manualStabilization,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxrunCodeGenerator.GenerateManifestFilesOnlyWithResult()",
                manualStabilization,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (manifestRefresh.SchemaInfoChanged)",
                manualStabilization,
                StringComparison.Ordinal);

            var manualCompletion = Slice(
                probe,
                "private static void CompleteManualPreparation(",
                "private static void ValidateManualPreparationIdentity(");
            Assert.Contains(
                "PHASE186_MANUAL_SCENE_READY",
                manualCompletion,
                StringComparison.Ordinal);
            Assert.Contains(
                "schemaInfoChanged=false",
                manualCompletion,
                StringComparison.Ordinal);
            Assert.Contains(
                "AssetDatabase.Refresh(",
                probe,
                StringComparison.Ordinal);
            Assert.Contains(
                "ImportAssetOptions.ForceSynchronousImport",
                probe,
                StringComparison.Ordinal);

            var runtimeMeta = File.ReadAllText(PathOf(runtimeRelative + ".meta"));
            var guidLine = Array.Find(
                runtimeMeta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                line => line.StartsWith("guid: ", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(guidLine));
            var scene = File.ReadAllText(PathOf(sceneRelative));
            Assert.Contains(guidLine, scene, StringComparison.Ordinal);
            Assert.Contains(
                "_foxRunSubscribeTransportId: unity2foxglove.ros2bridge",
                scene,
                StringComparison.Ordinal);
            Assert.Contains("_enableFoxRunInbound: 1", scene, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase186TopicGuardUsesCurrentRunPrefixInsteadOfHistoricalSubstrings()
        {
            var probe = File.ReadAllText(PathOf(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase186BatchModeRos2BridgeProbe.cs"));
            var runtime = File.ReadAllText(PathOf(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase186Ros2BridgeAcceptance.cs"));

            Assert.Contains(
                "\"/foxrun/phase186/\" + token + \"/\"",
                probe,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "topic.IndexOf(\"phase181\"",
                probe,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "topic.IndexOf(\"phase184\"",
                probe,
                StringComparison.Ordinal);
            Assert.Contains(
                "topic.StartsWith(\"/foxrun/phase186/p186h_\"",
                runtime,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "topic.IndexOf(\"phase181\"",
                runtime,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "topic.IndexOf(\"phase184\"",
                runtime,
                StringComparison.Ordinal);
        }

        private static string PathOf(string relative)
            => Path.Combine(
                RepoRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));

        private static string Slice(string source, string start, string end)
        {
            var startIndex = source.IndexOf(start, StringComparison.Ordinal);
            var endIndex = source.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            Assert.True(startIndex >= 0, start);
            Assert.True(endIndex > startIndex, end);
            return source.Substring(startIndex, endIndex - startIndex);
        }

        private static string RepoRoot
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null)
                {
                    if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                        || File.Exists(Path.Combine(directory.FullName, ".git")))
                    {
                        return directory.FullName;
                    }
                    directory = directory.Parent;
                }

                throw new DirectoryNotFoundException(
                    "Could not locate the Unity2Foxglove repository root.");
            }
        }
    }
}
