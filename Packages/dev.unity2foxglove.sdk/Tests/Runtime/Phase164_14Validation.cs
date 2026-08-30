using System;
using System.Linq;
using Unity.FoxgloveSDK.Util;

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

            Check(source.Contains("private int _lastEncodingFallbackWarningKey;", StringComparison.Ordinal)
                  && source.Contains("private int _lastEncodingMismatchWarningKey;", StringComparison.Ordinal),
                "164-14B-1: maintained encoding warning paths use integer dedupe keys");
            Check(!fallback.Contains("$\"fallback:", StringComparison.Ordinal)
                  && fallback.Contains("EncodingWarningKey(resolution.Requested, resolution.Effective)", StringComparison.Ordinal),
                "164-14B-2: encoding fallback dedupe avoids interpolated string keys");
            Check(!mismatch.Contains("$\"mismatch:", StringComparison.Ordinal)
                  && mismatch.Contains("AttemptedEncodingWarningKey(attemptedEncoding)", StringComparison.Ordinal),
                "164-14B-3: encoding mismatch dedupe avoids interpolated string keys");
        }

        private static void VerifyFoxRunHubCachesTopicMetadata()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");
            var scheduled = PhaseValidationSourceHelpers.SourceMethod(source, "private void TryPublishScheduled");
            var add = PhaseValidationSourceHelpers.SourceMethod(source, "private bool AddSourceNow");

            Check(source.Contains("Dictionary<IFoxgloveLogSource, SourceState>", StringComparison.Ordinal)
                  && source.Contains("FoxgloveLogTopicInfo[] Topics { get; }", StringComparison.Ordinal)
                  && source.Contains("FixedRatePublishState[] Timers { get; }", StringComparison.Ordinal),
                "164-14C-1: FoxRun hub stores topic metadata beside cadence timers");
            Check(add.Contains("new FoxgloveLogTopicInfo[count]", StringComparison.Ordinal)
                  && add.Contains("new bool[count]", StringComparison.Ordinal)
                  && add.Contains("new FixedRatePublishState[count]", StringComparison.Ordinal)
                  && add.Contains("source.FoxgloveLog_GetTopic(index)", StringComparison.Ordinal)
                  && update.Contains("state.Topics[index]", StringComparison.Ordinal)
                  && update.Contains("ref state.Timers[index]", StringComparison.Ordinal),
                "164-14C-2: topic metadata is captured at registration and reused during Update");
            Check(!scheduled.Contains("FoxgloveLog_GetTopic(topicIndex)", StringComparison.Ordinal)
                  && scheduled.Contains("FoxgloveLogTopicInfo info", StringComparison.Ordinal),
                "164-14C-3: scheduled FoxRun publish path avoids per-frame topic metadata dispatch");
            Check(update.Contains("if (source is MonoBehaviour behaviour)", StringComparison.Ordinal)
                  && Count(update, "is MonoBehaviour") == 1,
                "164-14C-4: FoxRun Update uses one MonoBehaviour type-test per source");
            Check(scheduled.Contains("FoxRunPolicy.FixedRate", StringComparison.Ordinal)
                  && scheduled.Contains("FoxRunPolicy.Change", StringComparison.Ordinal)
                  && scheduled.Contains("timer = default", StringComparison.Ordinal)
                  && scheduled.Contains("explicitTrigger: false", StringComparison.Ordinal)
                  && scheduled.Contains("FoxgloveLog_MarkPublished", StringComparison.Ordinal),
                "164-14C-5: current hub dispatches FixedRate and Change topics through the policy seam");

            var state = default(FixedRatePublishState);
            var first = FixedRatePublishScheduler.ShouldPublish(0d, 10f, ref state, false);
            var beforeDue = FixedRatePublishScheduler.ShouldPublish(0.05d, 10f, ref state, false);
            var due = FixedRatePublishScheduler.ShouldPublish(0.1d, 10f, ref state, false);
            Check(first && !beforeDue && due,
                "164-14C-6: fixed-rate cadence probe publishes on first use and at the due boundary");
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
            var entry = PhaseValidationRegistry.All.Single(item => item.Flag == "--phase164-14");
            var defaultEntry = PhaseValidationRegistry.DefaultValidations(false)
                .SingleOrDefault(item => item.Flag == "--phase164-14");
            Check(registry.Contains("\"--phase164-14\"", StringComparison.Ordinal)
                  && entry.Category == ValidationCategory.CiSafe
                  && entry.IncludeInDefault
                  && defaultEntry != null,
                "164-14E-1: Phase164-14 is a CI-safe member of the default validation lane");
            Check(project.Contains("Phase164_14Validation.cs", StringComparison.Ordinal), "164-14E-2: runtime validation project compiles Phase164-14");
        }

        private static int Count(string value, string needle)
        {
            var count = 0;
            var offset = 0;
            while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += needle.Length;
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
