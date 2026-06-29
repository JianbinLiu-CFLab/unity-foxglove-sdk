using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_14Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-14 Tests ---");
            _passed = 0;

            VerifyPublisherRateResolutionUsesHotPathCache();
            VerifyWarningDedupUsesIntegerKeysForFallbacks();
            VerifyFoxRunHubCachesTopicMetadata();
            VerifyLegacyCadenceValidationTracksCacheShape();
            VerifyRegistry();

            Console.WriteLine("Phase 164-14: " + _passed + " checks passed.\n");
        }

        private static void VerifyPublisherRateResolutionUsesHotPathCache()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var shouldPublish = PhaseValidationSourceHelpers.SourceMethod(source, "protected bool ShouldPublishNow");
            var shouldPublishFixed = PhaseValidationSourceHelpers.SourceMethod(source, "protected bool ShouldPublishNowFixed");
            var cached = PhaseValidationSourceHelpers.SourceMethod(source, "private float ResolveCachedPublishRateHz");

            Check(source.Contains("public float EffectivePublishRateHz => ResolvePublishRateHz()", StringComparison.Ordinal),
                "164-14A-1: Inspector-facing effective publish rate still resolves on demand");
            Check(shouldPublish.Contains("ResolveCachedPublishRateHz()", StringComparison.Ordinal)
                  && shouldPublishFixed.Contains("ResolveCachedPublishRateHz()", StringComparison.Ordinal),
                "164-14A-2: per-frame cadence checks use the cached publish-rate resolver");
            Check(cached.Contains("_cachedManagerPublishRateHz != managerRateHz", StringComparison.Ordinal)
                  && cached.Contains("_cachedLocalPublishRateHz != _publishRateHz", StringComparison.Ordinal)
                  && cached.Contains("_cachedPublishRateSource != _publishRateSource", StringComparison.Ordinal)
                  && cached.Contains("PublisherRatePolicy.Resolve", StringComparison.Ordinal),
                "164-14A-3: publish-rate cache invalidates on manager, local, and source changes");
            Check(source.Contains("protected virtual void OnValidate()", StringComparison.Ordinal)
                  && source.Contains("InvalidatePublishRateCache();", StringComparison.Ordinal),
                "164-14A-4: publisher validation invalidates the cadence cache");
        }

        private static void VerifyWarningDedupUsesIntegerKeysForFallbacks()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var fallback = PhaseValidationSourceHelpers.SourceMethod(source, "private void WarnIfEncodingFallback");
            var mismatch = PhaseValidationSourceHelpers.SourceMethod(source, "private void WarnEncodingMismatch");
            var bridge = PhaseValidationSourceHelpers.SourceMethod(source, "private void WarnIfRos2BridgeFallback");

            Check(source.Contains("private int _lastEncodingFallbackWarningKey;", StringComparison.Ordinal)
                  && source.Contains("private int _lastEncodingMismatchWarningKey;", StringComparison.Ordinal)
                  && source.Contains("private int _lastBridgeFallbackWarningKey;", StringComparison.Ordinal),
                "164-14B-1: warning dedupe fallback paths use integer keys");
            Check(!fallback.Contains("$\"fallback:", StringComparison.Ordinal)
                  && fallback.Contains("EncodingWarningKey(resolution.Requested, resolution.Effective)", StringComparison.Ordinal),
                "164-14B-2: encoding fallback dedupe avoids interpolated string keys");
            Check(!mismatch.Contains("$\"mismatch:", StringComparison.Ordinal)
                  && mismatch.Contains("AttemptedEncodingWarningKey(attemptedEncoding)", StringComparison.Ordinal),
                "164-14B-3: encoding mismatch dedupe avoids interpolated string keys");
            Check(!bridge.Contains("$\"fallback:", StringComparison.Ordinal)
                  && bridge.Contains("BridgeWarningKey(resolution.Requested, resolution.Effective)", StringComparison.Ordinal),
                "164-14B-4: ROS2 Bridge fallback dedupe avoids interpolated string keys");
        }

        private static void VerifyFoxRunHubCachesTopicMetadata()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");
            var scheduled = PhaseValidationSourceHelpers.SourceMethod(source, "private bool TryPublishScheduledTopic");
            var add = PhaseValidationSourceHelpers.SourceMethod(source, "private void AddSourceNow");

            Check(source.Contains("Dictionary<IFoxgloveLogSource, FoxgloveLogSourceState>", StringComparison.Ordinal)
                  && source.Contains("public FoxgloveLogTopicInfo[] Topics { get; }", StringComparison.Ordinal),
                "164-14C-1: FoxRun hub stores topic metadata beside cadence timers");
            Check(add.Contains("topics[i] = source.FoxgloveLog_GetTopic(i)", StringComparison.Ordinal)
                  && update.Contains("state.Topics[i]", StringComparison.Ordinal),
                "164-14C-2: topic metadata is captured at registration and reused during Update");
            Check(!scheduled.Contains("FoxgloveLog_GetTopic(topicIndex)", StringComparison.Ordinal)
                  && scheduled.Contains("FoxgloveLogTopicInfo info", StringComparison.Ordinal),
                "164-14C-3: scheduled FoxRun publish path avoids per-frame topic metadata dispatch");
            Check(update.Contains("if (kv.Key is MonoBehaviour mb)", StringComparison.Ordinal)
                  && !update.Contains("mb2", StringComparison.Ordinal),
                "164-14C-4: FoxRun Update uses one MonoBehaviour type-test per source");
        }

        private static void VerifyLegacyCadenceValidationTracksCacheShape()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase71Validation.cs");

            Check(source.Contains("ShouldPublishNow uses cached effective publish rate", StringComparison.Ordinal)
                  && source.Contains("ResolveCachedPublishRateHz()", StringComparison.Ordinal),
                "164-14D-1: legacy cadence validation tracks the optimized hot-path shape");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-14\"", StringComparison.Ordinal), "164-14E-1: validation registry exposes Phase164-14");
            Check(project.Contains("Phase164_14Validation.cs", StringComparison.Ordinal), "164-14E-2: runtime validation project compiles Phase164-14");
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
