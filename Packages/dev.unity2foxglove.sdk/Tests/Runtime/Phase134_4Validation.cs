// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-4 publisher topic guardrails.

using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;

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
            VerifyParameterLifecycleFacadesAndComponentUnregister();
            VerifyParameterTypeDefaultsAndValidation();
            VerifyQuaternionCoordinateRoundTrip();
            VerifyPublisherEncodingResolutionReuse();
            VerifyPublisherInspectorHelpersAvoidHotPathAllocations();

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

        private static void VerifyParameterLifecycleFacadesAndComponentUnregister()
        {
            var runtime = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs");
            var component = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Parameters/FoxgloveParameterComponent.cs");

            Check(runtime.Contains("public bool UnregisterParameter(string name)", StringComparison.Ordinal)
                  && runtime.Contains("_parameters.Unregister(name)", StringComparison.Ordinal),
                "134-4E-1: runtime exposes parameter unregister facade");
            Check(manager.Contains("public bool UnregisterParameter(string name)", StringComparison.Ordinal)
                  && manager.Contains("_runtime?.UnregisterParameter(name) ?? false", StringComparison.Ordinal),
                "134-4E-2: manager exposes parameter unregister facade");
            Check(component.Contains("private void OnDisable()", StringComparison.Ordinal)
                  && component.Contains("private void OnDestroy()", StringComparison.Ordinal)
                  && component.Contains("UnregisterRegisteredParameters()", StringComparison.Ordinal)
                  && component.Contains("_registeredManager.UnregisterParameter(name)", StringComparison.Ordinal),
                "134-4E-3: parameter component unregisters names on disable and destroy");
        }

        private static void VerifyParameterTypeDefaultsAndValidation()
        {
            var component = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Parameters/FoxgloveParameterComponent.cs");
            Check(component.Contains("FoxgloveParameterStore.DefaultValueForType(p.Type)", StringComparison.Ordinal)
                  && component.Contains("TryNormalizeValueForType(p.Type", StringComparison.Ordinal),
                "134-4F-1: parameter component derives empty defaults from declared type and validates parsed values");

            var store = new FoxgloveParameterStore();
            store.Register("/n", null, "number", writable: true);
            store.Register("/s", null, "string", writable: true);
            store.Register("/b", null, "boolean", writable: true);
            store.Register("/a", null, "number[]", writable: true);

            Check(store.GetWireParameter("/n").Value.Type == JTokenType.Integer
                  && (int)store.GetWireParameter("/n").Value == 0,
                "134-4F-2: number parameters default to numeric zero");
            Check(store.GetWireParameter("/s").Value.Type == JTokenType.String
                  && (string)store.GetWireParameter("/s").Value == string.Empty,
                "134-4F-3: string parameters default to empty string");
            Check(store.GetWireParameter("/b").Value.Type == JTokenType.Boolean
                  && (bool)store.GetWireParameter("/b").Value == false,
                "134-4F-4: boolean parameters default to false");
            Check(store.GetWireParameter("/a").Value.Type == JTokenType.Array
                  && !store.GetWireParameter("/a").Value.HasValues,
                "134-4F-5: number array parameters default to an empty array");

            Check(!store.TrySetFromClient("/b", JToken.FromObject(1)),
                "134-4F-6: writable parameters reject client values that do not match their declared type");
            Check(store.Unregister("/b") && store.GetWireParameter("/b") == null,
                "134-4F-7: unregistered parameters are removed from the wire snapshot");
            Check(!store.TrySetFromClient("/b", JToken.FromObject(true)),
                "134-4F-8: unregistered parameters cannot be written by clients");
        }

        private static void VerifyQuaternionCoordinateRoundTrip()
        {
            var converter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/CoordinateConverter.cs");
            Check(converter.Contains("new UnityEngine.Quaternion(-q.z, q.x, -q.y, q.w)", StringComparison.Ordinal)
                  && converter.Contains("new UnityEngine.Quaternion(q.y, -q.z, -q.x, q.w)", StringComparison.Ordinal),
                "134-4G-1: coordinate converter keeps the reviewed quaternion mapping formulas");

            AssertQuaternionRoundTrip(Normalize(new TestQuaternion(0.2f, -0.3f, 0.4f, 0.8f)), "134-4G-2: arbitrary quaternion round-trips");
            AssertQuaternionRoundTrip(AxisAngle(1, 0, 0, 90), "134-4G-3: X-axis basis rotation round-trips");
            AssertQuaternionRoundTrip(AxisAngle(0, 1, 0, 90), "134-4G-4: Y-axis basis rotation round-trips");
            AssertQuaternionRoundTrip(AxisAngle(0, 0, 1, 90), "134-4G-5: Z-axis basis rotation round-trips");
        }

        private static void VerifyPublisherEncodingResolutionReuse()
        {
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisher.cs");
            var publisherBase = ReadRepoText(PublisherBasePath);

            Check(publisherBase.Contains("protected bool TryPreparePublishPayload(out PublisherEncodingResolution resolution)", StringComparison.Ordinal)
                  && publisherBase.Contains("protected void Publish(object message, ulong logTimeNs, PublisherEncodingResolution resolution)", StringComparison.Ordinal),
                "134-4H-1: publisher base can reuse a preflight encoding resolution for publish");
            Check(publisher.Contains("TryPreparePublishPayload(out var resolution)", StringComparison.Ordinal)
                  && publisher.Contains("Publish(message, unixNs, resolution)", StringComparison.Ordinal),
                "134-4H-2: generic publisher update path avoids resolving encoding twice on successful publish");
        }

        private static void VerifyPublisherInspectorHelpersAvoidHotPathAllocations()
        {
            var publisherBase = ReadRepoText(PublisherBasePath);

            Check(publisherBase.Contains("_warnedManagerMissing = false;", StringComparison.Ordinal),
                "134-4I-1: publisher manager-missing warning latch resets on enable");
            Check(publisherBase.Contains("_supportedEncodingSummary ??= BuildSupportedEncodingSummary()", StringComparison.Ordinal)
                  && !publisherBase.Contains("new System.Collections.Generic.List<string>(3)", StringComparison.Ordinal),
                "134-4I-2: supported encoding summary is cached and avoids per-access list allocation");
        }

        private static void AssertQuaternionRoundTrip(TestQuaternion unity, string message)
        {
            var foxglove = UnityToFoxgloveRotation(unity);
            var roundTrip = FoxgloveToUnityRotation(foxglove);
            Check(Approximately(unity.x, roundTrip.x)
                  && Approximately(unity.y, roundTrip.y)
                  && Approximately(unity.z, roundTrip.z)
                  && Approximately(unity.w, roundTrip.w),
                message);
        }

        private static TestQuaternion UnityToFoxgloveRotation(TestQuaternion q)
            => new TestQuaternion(-q.z, q.x, -q.y, q.w);

        private static TestQuaternion FoxgloveToUnityRotation(TestQuaternion q)
            => new TestQuaternion(q.y, -q.z, -q.x, q.w);

        private static TestQuaternion AxisAngle(float x, float y, float z, float degrees)
        {
            var radians = degrees * Math.PI / 180.0;
            var s = (float)Math.Sin(radians / 2.0);
            var c = (float)Math.Cos(radians / 2.0);
            return Normalize(new TestQuaternion(x * s, y * s, z * s, c));
        }

        private static TestQuaternion Normalize(TestQuaternion q)
        {
            var magnitude = Math.Sqrt((q.x * q.x) + (q.y * q.y) + (q.z * q.z) + (q.w * q.w));
            return new TestQuaternion(
                (float)(q.x / magnitude),
                (float)(q.y / magnitude),
                (float)(q.z / magnitude),
                (float)(q.w / magnitude));
        }

        private static bool Approximately(float a, float b)
            => Math.Abs(a - b) <= 0.0001f;

        private readonly struct TestQuaternion
        {
            public readonly float x;
            public readonly float y;
            public readonly float z;
            public readonly float w;

            public TestQuaternion(float x, float y, float z, float w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }
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
