// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates replay pose ownership arbitration without depending on UnityEngine.

using System;
using System.IO;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115DValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115D: Replay Pose Ownership Arbitration ===");
            _passed = 0;

            VerifyReplayMessageContext();
            VerifyBehaviorClassification();
            VerifyPoseArbiter();
            VerifyReplayContextForwardingSource();
            VerifyAdapterArbitrationBoundarySource();
            VerifyEvidenceNote();

            Console.WriteLine($"Phase 115D: {_passed} checks passed.");
        }

        private static void VerifyReplayMessageContext()
        {
            Check(typeof(ReplayMessageContext).IsValueType
                  && typeof(ReplayMessageContext).IsByRefLike == false
                  && typeof(ReplayMessageContext).IsPrimitive == false,
                "115D-A1: replay message context is a value type");

            var context = new ReplayMessageContext(
                channelId: 42,
                topic: "/renamed/frame_stream",
                messageEncoding: "protobuf",
                schemaName: "foxglove.FrameTransform",
                schemaEncoding: "protobuf",
                logTimeNs: 123,
                replayStartTimeNs: 100,
                payload: new byte[] { 1, 2, 3 });

            Check(context.ChannelId == 42
                  && context.Topic == "/renamed/frame_stream"
                  && context.MessageEncoding == "protobuf"
                  && context.SchemaName == "foxglove.FrameTransform"
                  && context.SchemaEncoding == "protobuf"
                  && context.LogTimeNs == 123
                  && context.ReplayStartTimeNs == 100
                  && context.Payload.Length == 3,
                "115D-A2: replay message context carries source id, schema, dispatch time, and payload");
        }

        private static void VerifyBehaviorClassification()
        {
            Check(ReplayChannelBehaviorClassifier.ClassifyChannel("protobuf", "foxglove.FrameTransform", "protobuf")
                  == ReplayChannelBehavior.FrameTransformPose,
                "115D-B1: protobuf FrameTransform channels classify as frame-transform pose sources");

            Check(ReplayChannelBehaviorClassifier.ClassifyChannel("protobuf", "foxglove.FrameTransforms", "protobuf")
                  == ReplayChannelBehavior.FrameTransformPose,
                "115D-B2: protobuf FrameTransforms channels classify as frame-transform pose sources");

            Check(ReplayChannelBehaviorClassifier.ClassifyChannel("protobuf", "foxglove.SceneUpdate", "protobuf")
                  == ReplayChannelBehavior.ScenePrimitivePose,
                "115D-B3: protobuf SceneUpdate channels classify as scene-primitive pose sources");

            Check(ReplayChannelBehaviorClassifier.ClassifyChannel("json", "", "")
                  == ReplayChannelBehavior.Unclassified,
                "115D-B4: schema-less JSON channels remain unclassified until payload shape is inspected");

            Check(ReplayChannelBehaviorClassifier.ClassifyJsonPayload("{\"child_frame_id\":\"Cube\",\"translation\":{\"x\":1},\"rotation\":{\"w\":1}}")
                  == ReplayChannelBehavior.FrameTransformPose,
                "115D-B5: JSON frame-transform shape classifies without relying on topic name");

            Check(ReplayChannelBehaviorClassifier.ClassifyJsonPayload("{\"entities\":[{\"id\":\"Cube\",\"cubes\":[{\"pose\":{}}]}]}")
                  == ReplayChannelBehavior.ScenePrimitivePose,
                "115D-B6: JSON scene-update shape classifies without relying on topic name");
        }

        private static void VerifyPoseArbiter()
        {
            var scenePose = ReplayPoseSample.CreatePosition(1, 2, 3);
            var framePose = ReplayPoseSample.CreatePosition(4, 5, 6);
            var arbiter = new ReplayPoseOwnershipArbiter();

            var held = arbiter.OfferPose(
                transformKey: 101,
                channelId: 20,
                behavior: ReplayChannelBehavior.ScenePrimitivePose,
                logTimeNs: 100,
                pose: scenePose);
            Check(held.Kind == ReplayPoseOwnershipDecisionKind.Hold && arbiter.IsDeferralActive,
                "115D-C1: scene pose is held during init deferral instead of applied");

            var frame = arbiter.OfferPose(
                transformKey: 101,
                channelId: 10,
                behavior: ReplayChannelBehavior.FrameTransformPose,
                logTimeNs: 100,
                pose: framePose);
            Check(frame.Kind == ReplayPoseOwnershipDecisionKind.Apply
                  && frame.OwnerChannelId == 10
                  && frame.Pose.PositionX == 4,
                "115D-C2: frame-transform pose immediately locks and applies for the same Transform");

            var skipped = arbiter.OfferPose(
                transformKey: 101,
                channelId: 20,
                behavior: ReplayChannelBehavior.ScenePrimitivePose,
                logTimeNs: 101,
                pose: scenePose);
            Check(skipped.Kind == ReplayPoseOwnershipDecisionKind.Skip
                  && skipped.OwnerChannelId == 10
                  && skipped.ShouldReportContention,
                "115D-C3: lower-priority scene pose is skipped after frame-transform ownership locks");

            var skippedAgain = arbiter.OfferPose(
                transformKey: 101,
                channelId: 20,
                behavior: ReplayChannelBehavior.ScenePrimitivePose,
                logTimeNs: 102,
                pose: scenePose);
            Check(skippedAgain.Kind == ReplayPoseOwnershipDecisionKind.Skip
                  && !skippedAgain.ShouldReportContention,
                "115D-C4: ownership contention is rate-limited per Transform/source pair");

            var sceneOnly = new ReplayPoseOwnershipArbiter();
            sceneOnly.OfferPose(202, 30, ReplayChannelBehavior.ScenePrimitivePose, 100, scenePose);
            var resolved = sceneOnly.EndInitDeferral();
            Check(resolved.Count == 1
                  && resolved[0].Kind == ReplayPoseOwnershipDecisionKind.Apply
                  && resolved[0].OwnerChannelId == 30
                  && resolved[0].Pose.PositionX == 1,
                "115D-C5: scene-only Transform applies one held pose when deferral ends");

            var laterScene = sceneOnly.OfferPose(202, 30, ReplayChannelBehavior.ScenePrimitivePose, 101, framePose);
            Check(laterScene.Kind == ReplayPoseOwnershipDecisionKind.Apply
                  && laterScene.OwnerChannelId == 30
                  && laterScene.Pose.PositionX == 4,
                "115D-C6: scene source continues to own pose when no frame-transform source exists");

            var twoFrameSources = new ReplayPoseOwnershipArbiter();
            var firstFrame = twoFrameSources.OfferPose(303, 50, ReplayChannelBehavior.FrameTransformPose, 100, scenePose);
            var secondFrame = twoFrameSources.OfferPose(303, 51, ReplayChannelBehavior.FrameTransformPose, 100, framePose);
            Check(firstFrame.Kind == ReplayPoseOwnershipDecisionKind.Apply
                  && secondFrame.Kind == ReplayPoseOwnershipDecisionKind.Skip
                  && secondFrame.OwnerChannelId == 50,
                "115D-C7: two frame-transform channels arbitrate by concrete source channel, not behavior class alone");

            twoFrameSources.Reset();
            var afterReset = twoFrameSources.OfferPose(303, 51, ReplayChannelBehavior.FrameTransformPose, 100, framePose);
            Check(afterReset.Kind == ReplayPoseOwnershipDecisionKind.Apply && afterReset.OwnerChannelId == 51,
                "115D-C8: arbiter reset clears per-session ownership");
        }

        private static void VerifyReplayContextForwardingSource()
        {
            var controller = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(controller.Contains("event Action<ReplayMessageContext> OnReplayMessageContext", StringComparison.Ordinal)
                  && runtime.Contains("event Action<ReplayMessageContext> OnReplayMessageContext", StringComparison.Ordinal)
                  && manager.Contains("Action<ReplayMessageContext> OnReplayMessageContext", StringComparison.Ordinal),
                "115D-D1: context-rich replay event is exposed without removing the legacy event");

            Check(controller.Contains("_channelBehaviorMap", StringComparison.Ordinal)
                  && controller.Contains("GetChannelBehavior(ushort channelId)", StringComparison.Ordinal)
                  && !controller.Contains("_replayTopicSet", StringComparison.Ordinal)
                  && !controller.Contains("HasTopic(string topic)", StringComparison.Ordinal),
                "115D-D2: replay behavior summary is keyed by channel id, not topic");

            Check(controller.Contains("CreateReplayMessageContext", StringComparison.Ordinal)
                  && controller.Contains("ForwardReplayMessageToScene", StringComparison.Ordinal)
                  && controller.Contains("logTimeNs:", StringComparison.Ordinal)
                  && controller.Contains("StartTimeNs", StringComparison.Ordinal),
                "115D-D3: tick and snapshot forwarding share context construction using replay log time");

            Check(runtime.Contains("_replayContextForwarder", StringComparison.Ordinal)
                  && server.Contains("_replayContextForwarder", StringComparison.Ordinal),
                "115D-D4: context replay events are wired and unwired through runtime and manager lifecycles");

            Check(runtime.Contains("OnReplayMessage", StringComparison.Ordinal)
                  && manager.Contains("OnReplayMessage", StringComparison.Ordinal),
                "115D-D5: legacy topic-only replay event remains for compatibility");
        }

        private static void VerifyAdapterArbitrationBoundarySource()
        {
            var adapter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Replay/FoxgloveReplayObjectAdapter.cs");

            Check(adapter.Contains("OnReplayMessage(ReplayMessageContext context)", StringComparison.Ordinal)
                  && adapter.Contains("ReplayPoseOwnershipArbiter", StringComparison.Ordinal)
                  && adapter.Contains("OfferPose", StringComparison.Ordinal)
                  && adapter.Contains("EndInitDeferral", StringComparison.Ordinal),
                "115D-E1: adapter routes replay pose writes through the behavior arbiter");

            Check(!adapter.Contains("ReplayHasTopic", StringComparison.Ordinal)
                  && !adapter.Contains("ReplayPoseOwnershipPolicy", StringComparison.Ordinal)
                  && !adapter.Contains("ShouldApplyScenePose", StringComparison.Ordinal),
                "115D-E2: obsolete topic-based ownership symbols are absent from the adapter");

            Check(adapter.Contains("ApplyPoseSample", StringComparison.Ordinal)
                  && adapter.Contains("ApplySceneVisuals", StringComparison.Ordinal)
                  && adapter.Contains("target.localScale", StringComparison.Ordinal)
                  && adapter.Contains("SetPropertyBlock", StringComparison.Ordinal),
                "115D-E3: pose arbitration is split from scene scale/color application");

            Check(adapter.Contains("ReplayPoseTraceEnabled = false", StringComparison.Ordinal)
                  && adapter.Contains("TracePoseWrite", StringComparison.Ordinal)
                  && adapter.Contains("channel=", StringComparison.Ordinal)
                  && !adapter.Contains("ReplayPoseTraceEnabled = true", StringComparison.Ordinal),
                "115D-E4: pose trace hook is present but disabled by default");

            Check(adapter.Contains("Pose ownership contention skipped", StringComparison.Ordinal)
                  && adapter.Contains("Debug.Log(", StringComparison.Ordinal)
                  && !adapter.Contains("Debug.LogWarning(\r\n                    $\"[Foxglove Replay] Pose ownership contention skipped", StringComparison.Ordinal)
                  && adapter.Contains("topic='{context.Topic}'", StringComparison.Ordinal)
                  && adapter.Contains("schema='{context.SchemaName}'", StringComparison.Ordinal),
                "115D-E5: pose contention diagnostics are informational and identify the skipped source");
        }

        private static void VerifyEvidenceNote()
        {
            var notePath = Path.Combine(RepoRoot, "Developer", "Phase115D_Replay_Pose_Ownership_Acceptance.md");
            if (!File.Exists(notePath))
            {
                Console.WriteLine("[INFO] 115D-F1: local Developer acceptance note is absent; automated behavior checks remain enforced.");
                return;
            }

            Check(true, "115D-F1: replay pose ownership acceptance note exists");

            var note = File.ReadAllText(notePath);
            Check(note.Contains("Root-Cause Trace", StringComparison.Ordinal)
                  && note.Contains("After-Fix Trace", StringComparison.Ordinal)
                  && note.Contains("Manual Unity Acceptance", StringComparison.Ordinal)
                  && note.Contains("FoxRun-only schema identity boundary", StringComparison.Ordinal),
                "115D-F2: acceptance note records trace evidence and schema boundary");
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(Path.Combine(dir, "Packages"))
                        && Directory.Exists(Path.Combine(dir, "Plan")))
                        return dir;
                    dir = Directory.GetParent(dir)?.FullName;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
