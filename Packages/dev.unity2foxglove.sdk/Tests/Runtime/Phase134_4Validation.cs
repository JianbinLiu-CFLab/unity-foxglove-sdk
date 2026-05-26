// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-4 publisher topic guardrails.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_4Validation
    {
        private const string PublisherBasePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs";
        private const string ManagerPublishingPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs";
        private const string ManagerPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs";
        private const string PublisherEditorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 134-4: Publisher topic guardrails ===");
            _passed = 0;

            VerifyTopicPredicate();
            VerifyPublisherBaseRejectsInvalidTopics();
            VerifyManagerRejectsInvalidTopicsBeforeRegistration();
            VerifyInspectorWarnsForInvalidTopics();

            Console.WriteLine($"Phase 134-4: {_passed} checks passed.");
        }

        private static void VerifyTopicPredicate()
        {
            var source = ReadRepoText(PublisherBasePath);
            Check(source.Contains("public static bool HasValidPublisherTopic(string topic)", StringComparison.Ordinal),
                "134-4A-1: publisher base exposes a reusable topic validator");
            Check(source.Contains("!string.IsNullOrWhiteSpace(topic)", StringComparison.Ordinal),
                "134-4A-2: publisher topic validator rejects null, empty, and whitespace topics");
            Check(source.Contains("public bool HasValidTopic => HasValidPublisherTopic(_topic)", StringComparison.Ordinal),
                "134-4A-3: publisher instances expose configured topic validity");
            Check(source.Contains("public string Topic => _topic", StringComparison.Ordinal),
                "134-4A-4: publisher instances expose their configured topic");
        }

        private static void VerifyPublisherBaseRejectsInvalidTopics()
        {
            var source = ReadRepoText(PublisherBasePath);
            Check(source.Contains("public string Topic => _topic", StringComparison.Ordinal)
                  && source.Contains("public bool HasValidTopic => HasValidPublisherTopic(_topic)", StringComparison.Ordinal),
                "134-4B-1: publisher base exposes topic validity for Inspector and tests");
            Check(source.Contains("ValidateConfiguredTopic(\"publish\")", StringComparison.Ordinal)
                  && source.Contains("ValidateConfiguredTopic(\"ROS2 Bridge publish\")", StringComparison.Ordinal),
                "134-4B-2: publisher base validates topic before WebSocket and ROS2 Bridge preparation");
            Check(source.Contains("if (!ValidateConfiguredTopic(\"publish\")) return;", StringComparison.Ordinal)
                  && source.Contains("if (!ValidateConfiguredTopic(\"ROS2 Bridge publish\")) return;", StringComparison.Ordinal),
                "134-4B-3: publisher base validates topic before direct publish helpers");
            Check(source.Contains("Configure a non-empty topic before publishing", StringComparison.Ordinal)
                  && source.Contains("_lastTopicWarningKey", StringComparison.Ordinal),
                "134-4B-4: invalid topic warnings are actionable and de-duplicated per publisher");
        }

        private static void VerifyManagerRejectsInvalidTopicsBeforeRegistration()
        {
            var source = ReadRepoText(ManagerPublishingPath);
            var manager = ReadRepoText(ManagerPath);
            Check(manager.Contains("_lastInvalidPublishTopicWarningKey", StringComparison.Ordinal),
                "134-4C-1: manager tracks repeated invalid topic warnings");
            Check(source.Contains("private static bool IsValidPublishTopic(string topic)", StringComparison.Ordinal)
                  && source.Contains("!string.IsNullOrWhiteSpace(topic)", StringComparison.Ordinal),
                "134-4C-2: manager has a whitespace-aware topic predicate");
            Check(source.Contains("if (!TryValidatePublishTopic(topic, \"prepare schema publish\"))", StringComparison.Ordinal)
                  && source.Contains("if (!TryValidatePublishTopic(topic, \"prepare ROS2 publish\"))", StringComparison.Ordinal),
                "134-4C-3: manager preflight rejects invalid topics before channel registration");
            Check(source.Contains("if (!TryValidatePublishTopic(topic, \"publish JSON\"))", StringComparison.Ordinal)
                  && source.Contains("if (!TryValidatePublishTopic(topic, \"publish Protobuf\"))", StringComparison.Ordinal)
                  && source.Contains("if (!TryValidatePublishTopic(topic, \"publish ROS2\"))", StringComparison.Ordinal),
                "134-4C-4: manager direct publish APIs reject invalid topics before channel registration");
            Check(source.Contains("throw new System.InvalidOperationException(\"Foxglove publisher topic must be non-empty.\")", StringComparison.Ordinal)
                  && source.Contains("private uint GetOrRegisterChannel(string topic, string encoding)", StringComparison.Ordinal)
                  && source.Contains("public uint GetOrRegisterSchemaChannel(string topic", StringComparison.Ordinal)
                  && source.Contains("public uint GetOrRegisterRos2MsgSchemaChannel(string topic", StringComparison.Ordinal),
                "134-4C-5: low-level channel registration helpers fail closed for invalid topics");
        }

        private static void VerifyInspectorWarnsForInvalidTopics()
        {
            var editor = ReadRepoText(PublisherEditorPath);
            Check(editor.Contains("serializedObject.FindProperty(\"_topic\")", StringComparison.Ordinal),
                "134-4D-1: publisher Inspector reads the serialized topic field");
            Check(editor.Contains("HasValidPublisherTopic(topic.stringValue)", StringComparison.Ordinal)
                  && editor.Contains("Blank publisher topics are not advertised or published.", StringComparison.Ordinal)
                  && editor.Contains("MessageType.Error", StringComparison.Ordinal),
                "134-4D-2: publisher Inspector surfaces blank topics as an error");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git"))
                    || Directory.Exists(Path.Combine(dir, "Packages")))
                    return dir;

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);

            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
