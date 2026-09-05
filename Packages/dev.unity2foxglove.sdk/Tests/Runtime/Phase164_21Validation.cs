using System;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_21Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-21 Tests ---");
            _passed = 0;

            VerifyPendingRegistrationsUseSetAndDrainBuffer();
            VerifySchedulerUsesCachedTopicMetadata();
            VerifyUpdateUsesSingleMonoBehaviourCast();
            VerifyRegistry();

            Console.WriteLine("Phase 164-21: " + _passed + " checks passed.\n");
        }

        private static void VerifyPendingRegistrationsUseSetAndDrainBuffer()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var register = PhaseValidationSourceHelpers.SourceMethod(source, "public static void RegisterSource");
            var unregister = PhaseValidationSourceHelpers.SourceMethod(source, "public static void UnregisterSource");
            var reset = PhaseValidationSourceHelpers.SourceMethod(source, "private static void ResetStaticState");
            var drain = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrainPending");

            Check(source.Contains("private static readonly object PendingGate", StringComparison.Ordinal)
                  && source.Contains("private static readonly List<IFoxgloveLogSource> Pending", StringComparison.Ordinal)
                  && source.Contains("private static readonly HashSet<IFoxgloveLogSource> PendingSet", StringComparison.Ordinal),
                "164-21A-1: FoxRun hub protects pending registrations with a list, set, and lock");
            Check(register.Contains("if (PendingSet.Add(source))", StringComparison.Ordinal)
                  && register.Contains("Pending.Add(source)", StringComparison.Ordinal)
                  && !register.Contains("Pending.Contains(source)", StringComparison.Ordinal),
                "164-21A-2: FoxRun source registration deduplicates before queueing");
            Check(unregister.Contains("PendingSet.Remove(source)", StringComparison.Ordinal)
                  && unregister.Contains("Pending.Remove(source)", StringComparison.Ordinal)
                  && reset.Contains("PendingSet.Clear();", StringComparison.Ordinal)
                  && reset.Contains("Pending.Clear();", StringComparison.Ordinal),
                "164-21A-3: pending registration set is cleared on unregister and static reset");
            Check(drain.Contains("copy = Pending.ToArray();", StringComparison.Ordinal)
                  && drain.Contains("Pending.Clear();", StringComparison.Ordinal)
                  && drain.Contains("PendingSet.Clear();", StringComparison.Ordinal)
                  && drain.Contains("foreach (var source in copy)", StringComparison.Ordinal)
                  && drain.Contains("QueueAdd(source)", StringComparison.Ordinal),
                "164-21A-4: pending registrations drain as a snapshot and queue each source");

            // The snapshot copy is intentional: the lock is released before
            // QueueAdd invokes sources, so re-entrant registrations are
            // deferred to the next drain rather than mutating this pass.
        }

        private static void VerifySchedulerUsesCachedTopicMetadata()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");
            var tryPublish = PhaseValidationSourceHelpers.SourceMethod(source, "private void TryPublishScheduled");
            var addSource = PhaseValidationSourceHelpers.SourceMethod(source, "private bool AddSourceNow");

            Check(source.Contains("FoxgloveLogTopicInfo[] Topics { get; }", StringComparison.Ordinal)
                  && source.Contains("FixedRatePublishState[] Timers { get; }", StringComparison.Ordinal)
                  && addSource.Contains("topics[index] = info;", StringComparison.Ordinal),
                "164-21B-1: source topic metadata is cached during registration");
            Check(update.Contains("TryPublishScheduled(", StringComparison.Ordinal)
                  && update.Contains("state.Topics[index]", StringComparison.Ordinal)
                  && update.Contains("ref state.Timers[index]", StringComparison.Ordinal)
                  && !tryPublish.Contains("source.FoxgloveLog_GetTopic(topicIndex)", StringComparison.Ordinal),
                "164-21B-2: scheduled publish path uses cached topic metadata");
            Check(tryPublish.Contains("FoxRunPolicy.FixedRate", StringComparison.Ordinal)
                  && tryPublish.Contains("FoxRunPolicy.Change", StringComparison.Ordinal)
                  && tryPublish.Contains("FixedRatePublishScheduler", StringComparison.Ordinal)
                  && tryPublish.Contains("timer = default", StringComparison.Ordinal)
                  && tryPublish.Contains("TryPublish(", StringComparison.Ordinal),
                "164-21B-3: current scheduler dispatch covers FixedRate and Change policies");
        }

        private static void VerifyUpdateUsesSingleMonoBehaviourCast()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");

            Check(Count(update, "is MonoBehaviour") <= 1,
                "164-21C-1: Update performs at most one direct MonoBehaviour type-test per source");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var defaultFlags = PhaseValidationRegistry.DefaultValidations(false)
                .Select(item => item.Flag)
                .ToHashSet(StringComparer.Ordinal);
            Check(registry.Contains("\"--phase164-21\"", StringComparison.Ordinal)
                  && defaultFlags.Contains("--phase164-14")
                  && defaultFlags.Contains("--phase164-21"),
                "164-21D-1: both maintained publish selectors execute in the default validation lane");
            Check(project.Contains("Phase164_21Validation.cs", StringComparison.Ordinal), "164-21D-2: runtime validation project compiles Phase164-21");
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
