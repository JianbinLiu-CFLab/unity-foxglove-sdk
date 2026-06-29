using System;

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
            var drain = PhaseValidationSourceHelpers.SourceMethod(source, "private void DrainPendingRegistrations");

            Check(source.Contains("private static readonly HashSet<IFoxgloveLogSource> PendingRegistrationSet = new();", StringComparison.Ordinal)
                  && source.Contains("private readonly List<IFoxgloveLogSource> _registrationDrainBuffer = new();", StringComparison.Ordinal),
                "164-21A-1: FoxRun hub keeps pending registration set and reusable drain buffer");
            Check(register.Contains("if (PendingRegistrationSet.Add(source))", StringComparison.Ordinal)
                  && !register.Contains("PendingRegistrations.Contains(source)", StringComparison.Ordinal),
                "164-21A-2: FoxRun source registration deduplicates in O(1)");
            Check(unregister.Contains("PendingRegistrationSet.Remove(source);", StringComparison.Ordinal)
                  && reset.Contains("PendingRegistrationSet.Clear();", StringComparison.Ordinal),
                "164-21A-3: pending registration set is cleared on unregister and static reset");
            Check(drain.Contains("_registrationDrainBuffer.AddRange(PendingRegistrations);", StringComparison.Ordinal)
                  && drain.Contains("PendingRegistrationSet.Clear();", StringComparison.Ordinal)
                  && !drain.Contains("PendingRegistrations.ToArray()", StringComparison.Ordinal),
                "164-21A-4: pending registrations drain without per-drain array allocation");
        }

        private static void VerifySchedulerUsesCachedTopicMetadata()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");
            var tryPublish = PhaseValidationSourceHelpers.SourceMethod(source, "private bool TryPublishScheduledTopic");
            var addSource = PhaseValidationSourceHelpers.SourceMethod(source, "private void AddSourceNow");

            Check(source.Contains("public FoxgloveLogTopicInfo[] Topics { get; }", StringComparison.Ordinal)
                  && addSource.Contains("topics[i] = source.FoxgloveLog_GetTopic(i);", StringComparison.Ordinal),
                "164-21B-1: source topic metadata is cached during registration");
            Check(update.Contains("TryPublishScheduledTopic(kv.Key, state.Topics[i], i, ref state.Timers[i]", StringComparison.Ordinal)
                  && !tryPublish.Contains("source.FoxgloveLog_GetTopic(topicIndex)", StringComparison.Ordinal),
                "164-21B-2: scheduled publish path uses cached topic metadata");
        }

        private static void VerifyUpdateUsesSingleMonoBehaviourCast()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(source, "private void Update");

            Check(update.Contains("if (kv.Key is MonoBehaviour mb)", StringComparison.Ordinal)
                  && !update.Contains("kv.Key is MonoBehaviour mb2", StringComparison.Ordinal),
                "164-21C-1: Update uses one MonoBehaviour pattern match per source");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-21\"", StringComparison.Ordinal), "164-21D-1: validation registry exposes Phase164-21");
            Check(project.Contains("Phase164_21Validation.cs", StringComparison.Ordinal), "164-21D-2: runtime validation project compiles Phase164-21");
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
