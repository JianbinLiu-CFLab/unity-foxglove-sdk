// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 71 validation for global default publisher rate policy.

using System;
using System.IO;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Manager-level default publish rate policy for ordinary
    /// publishers while guarding FoxRun rate behavior from accidental changes.
    /// </summary>
    public static class Phase71Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all Phase 71 validation checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 71: Global Default Publish Rate Policy ===");
            _passed = 0;

            VerifyManagerDefaultPublishRate();
            VerifyPublisherRatePolicy();
            VerifyPublisherBaseRateResolution();
            VerifyPublisherInspectorUx();
            VerifyFoxRunRateBehaviorUnchanged();

            Console.WriteLine($"Phase 71: {_passed} checks passed.");
        }

        private static void VerifyManagerDefaultPublishRate()
        {
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var publishDataSection = Slice(editorSource, "private void DrawPublishDataSection()", "private void DrawMcapSection()");

            Check(managerSource.Contains("_defaultPublishRateHz = 10f"),
                "71A-1: manager exposes Default Publish Rate Hz serialized default");
            Check(managerSource.Contains("public float DefaultPublishRateHz => _defaultPublishRateHz"),
                "71A-2: manager exposes DefaultPublishRateHz read-only property");
            Check(publishDataSection.Contains("_defaultPublishRateHz"),
                "71A-3: Manager Inspector draws Default Publish Rate Hz in Publish Data");
            Check(IndexOf(publishDataSection, "Subheader(\"Publish Rate\")") < IndexOf(publishDataSection, "Subheader(\"Publisher Encoding\")"),
                "71A-4: Publish Data shows Publish Rate before Publisher Encoding");
        }

        private static void VerifyPublisherRatePolicy()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherRate.cs");
            Check(source.Contains("public enum PublisherRateSource"),
                "71B-1: PublisherRateSource enum exists");
            Check(source.Contains("UseManagerDefault") && source.Contains("OverrideLocal"),
                "71B-2: PublisherRateSource exposes manager default and local override modes");
            Check(source.Contains("public static class PublisherRatePolicy"),
                "71B-3: PublisherRatePolicy helper exists");
            Check(source.Contains("public static float Resolve"),
                "71B-4: PublisherRatePolicy exposes Resolve");

            VerifyPolicyResolve("OverrideLocal", 30f, 5f, hasManager: true, expected: 5f,
                "71B-5: local override wins over manager default");
            VerifyPolicyResolve("UseManagerDefault", 30f, 5f, hasManager: true, expected: 30f,
                "71B-6: manager default applies when requested and manager exists");
            VerifyPolicyResolve("UseManagerDefault", 30f, 5f, hasManager: false, expected: 5f,
                "71B-7: missing manager falls back to local rate");
            VerifyPolicyResolve("OverrideLocal", 30f, 0f, hasManager: true, expected: 0f,
                "71B-8: local non-positive rate passes through");
            VerifyPolicyResolve("UseManagerDefault", 0f, 5f, hasManager: true, expected: 0f,
                "71B-9: manager non-positive rate passes through");
        }

        private static void VerifyPublisherBaseRateResolution()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var shouldPublish = Slice(source, "protected bool ShouldPublishNow()", "protected static string SanitizeFrameId");
            var rateResolution = Slice(source, "private float ResolvePublishRateHz()", "private void WarnIfEncodingFallback");

            Check(source.Contains("_publishRateSource = PublisherRateSource.OverrideLocal"),
                "71C-1: existing publishers default missing serialized rate source to local override");
            Check(source.Contains("protected virtual void Reset()") && source.Contains("PublisherRateSource.UseManagerDefault"),
                "71C-2: newly added publishers default to manager rate");
            Check(source.Contains("public PublisherRateSource PublishRateSource => _publishRateSource"),
                "71C-3: publisher exposes rate source for Inspector and validation");
            Check(source.Contains("public float LocalPublishRateHz => _publishRateHz"),
                "71C-4: publisher exposes local publish rate");
            Check(source.Contains("public float EffectivePublishRateHz => ResolvePublishRateHz()"),
                "71C-5: publisher exposes effective publish rate");
            Check(source.Contains("PublisherRatePolicy.Resolve"),
                "71C-6: publisher resolves rates through PublisherRatePolicy");
            Check(shouldPublish.Contains("EffectivePublishRateHz") && !shouldPublish.Contains("1f / _publishRateHz"),
                "71C-7: ShouldPublishNow uses effective publish rate");
            Check(shouldPublish.Contains("effectiveRateHz <= 0")
                    || shouldPublish.Contains("rateHz <= 0")
                    || shouldPublish.Contains("nonPositivePublishesEveryFrame: true"),
                "71C-8: ShouldPublishNow preserves non-positive no-throttle semantics");
            Check(rateResolution.Contains("FindFirstObjectByType<FoxgloveManager>()"),
                "71C-9: effective rate can preview scene manager defaults without serialized manager reference");
        }

        private static void VerifyPublisherInspectorUx()
        {
            var editorClass = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxglovePublisherBaseEditor.cs");

            Check(editorClass.Contains("prop.name == \"_publishRateSource\""),
                "71D-1: publisher Inspector hides raw rate source from generic loop");
            Check(editorClass.Contains("prop.name == \"_publishRateHz\""),
                "71D-2: publisher Inspector hides raw local rate from generic loop");
            Check(editorClass.Contains("Publish Rate"),
                "71D-3: publisher Inspector has a Publish Rate section");
            Check(editorClass.Contains("Publish Rate Source"),
                "71D-4: publisher Inspector exposes rate source label");
            Check(editorClass.Contains("Publish Rate Hz"),
                "71D-5: publisher Inspector exposes local rate label");
            Check(editorClass.Contains("Effective Publish Rate Hz"),
                "71D-6: publisher Inspector exposes effective rate summary");
        }

        private static void VerifyFoxRunRateBehaviorUnchanged()
        {
            var attrSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunAttribute.cs");
            var generatorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var emitterSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs");
            var topicMetaSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var hubSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");

            Check(attrSource.Contains("public float RateHz { get; set; } = 10f"),
                "71E-1: FoxRunAttribute RateHz default remains 10");
            Check(generatorSource.Contains("float rateHz = 10f") && generatorSource.Contains("named.Key == \"RateHz\""),
                "71E-2: source generator still reads explicit FoxRun RateHz");
            Check(topicMetaSource.Contains("fields.Max(m => m.RateHz)"),
                "71E-3: shared emitter still emits FoxRun topic RateHz metadata");
            Check(hubSource.Contains("var rateHz = info.RateHz")
                    && hubSource.Contains("nonPositivePublishesEveryFrame: false"),
                "71E-4: FoxgloveLogHub passes non-positive FoxRun RateHz through as disabled scheduled publish");
        }

        private static void VerifyPolicyResolve(
            string sourceName,
            float managerRateHz,
            float localRateHz,
            bool hasManager,
            float expected,
            string name)
        {
            var assembly = typeof(Phase71Validation).Assembly;
            var enumType = assembly.GetType("Unity.FoxgloveSDK.Components.PublisherRateSource");
            var policyType = assembly.GetType("Unity.FoxgloveSDK.Components.PublisherRatePolicy");
            Check(enumType != null && policyType != null,
                name + " (policy types are compiled)");

            var source = Enum.Parse(enumType, sourceName);
            var method = policyType.GetMethod(
                "Resolve",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { enumType, typeof(float), typeof(float), typeof(bool) },
                modifiers: null);
            Check(method != null, name + " (Resolve method exists)");

            var actual = (float)method.Invoke(null, new[] { source, managerRateHz, localRateHz, hasManager });
            Check(Math.Abs(actual - expected) < 0.0001f, name);
        }

        private static int IndexOf(string text, string pattern)
        {
            return text.IndexOf(pattern, StringComparison.Ordinal);
        }

        private static string Slice(string text, string start, string end)
        {
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;

            var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            return endIndex < 0
                ? text.Substring(startIndex)
                : text.Substring(startIndex, endIndex - startIndex);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return File.ReadAllText(path);
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}
