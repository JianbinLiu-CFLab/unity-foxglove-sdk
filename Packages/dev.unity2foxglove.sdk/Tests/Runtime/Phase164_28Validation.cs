using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_28Validation
    {
        private static readonly string[] RuntimePackages =
        {
            "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
            "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
        };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-28 Tests ---");
            _passed = 0;

            VerifyComponentExecutorCachesOk();
            VerifyComponentFixedUpdateFastPath();
            VerifyCoreCachesOk();
            VerifySensorExecutorAvoidsDuplicateOk();
            VerifyFrameNamesArePrecomputed();
            VerifySnapshotContractsArePreserved();
            VerifyRegistry();

            Console.WriteLine("Phase 164-28: " + _passed + " checks passed.\n");
        }

        private static void VerifyComponentExecutorCachesOk()
        {
            foreach (var package in RuntimePackages)
            {
                var source = Read(package + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
                var ok = PhaseValidationSourceHelpers.SourceMethod(source, "public bool Ok");
                var tick = PhaseValidationSourceHelpers.SourceMethod(source, "private void Tick");
                var shutdownMarkers = SourceSlices(source, "private void MarkRuntimeShutdown", "private bool StopExecutor", "private void StopExecutor", "private bool TryDetachRuntimeState", "private void Shutdown");

                Check(source.Contains("private volatile bool cachedOk = false;", StringComparison.Ordinal),
                    Label(package, "164-28A-1: ROS2UnityComponent owns a cached Ok flag"));
                Check(ok.Contains("initialized && cachedOk", StringComparison.Ordinal)
                      && ok.Contains("return true;", StringComparison.Ordinal)
                      && ok.Contains("cachedOk = ros2forUnity.Ok();", StringComparison.Ordinal),
                    Label(package, "164-28A-2: ROS2UnityComponent.Ok uses cached runtime state in the steady path"));
                Check(tick.Contains("cachedOk = true;", StringComparison.Ordinal)
                      && tick.Contains("cachedOk = false;", StringComparison.Ordinal),
                    Label(package, "164-28A-3: executor Tick refreshes cached Ok state once per loop"));
                Check(shutdownMarkers.Contains("cachedOk = false;", StringComparison.Ordinal),
                    Label(package, "164-28A-4: shutdown paths invalidate cached Ok state"));
            }
        }

        private static void VerifyComponentFixedUpdateFastPath()
        {
            foreach (var package in RuntimePackages)
            {
                var source = Read(package + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
                var fixedUpdate = PhaseValidationSourceHelpers.SourceMethod(source, "void FixedUpdate");
                var startExecutor = PhaseValidationSourceHelpers.SourceMethod(source, "private void StartExecutor");
                var stopExecutor = SourceSlices(source, "private bool StopExecutor", "private void StopExecutor");

                Check(source.Contains("private volatile bool executorStarted = false;", StringComparison.Ordinal),
                    Label(package, "164-28B-1: ROS2UnityComponent owns a lock-free executor-started flag"));
                Check(fixedUpdate.Contains("if (executorStarted)", StringComparison.Ordinal)
                      && fixedUpdate.IndexOf("if (executorStarted)", StringComparison.Ordinal) < fixedUpdate.IndexOf("StartExecutor();", StringComparison.Ordinal),
                    Label(package, "164-28B-2: FixedUpdate returns before locking once the executor is started"));
                Check(startExecutor.Contains("executorStarted = true;", StringComparison.Ordinal),
                    Label(package, "164-28B-3: StartExecutor publishes the executor-started state"));
                Check(stopExecutor.Contains("executorStarted = false;", StringComparison.Ordinal),
                    Label(package, "164-28B-4: StopExecutor clears the executor-started state"));
            }
        }

        private static void VerifyCoreCachesOk()
        {
            foreach (var package in RuntimePackages)
            {
                var source = Read(package + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs");
                var ok = PhaseValidationSourceHelpers.SourceMethod(source, "public bool Ok");
                var tick = PhaseValidationSourceHelpers.SourceMethod(source, "private void Tick");

                Check(source.Contains("private volatile bool cachedOk = false;", StringComparison.Ordinal),
                    Label(package, "164-28C-1: ROS2UnityCore owns a cached Ok flag"));
                Check(ok.Contains("if (cachedOk)", StringComparison.Ordinal)
                      && ok.Contains("return true;", StringComparison.Ordinal)
                      && ok.Contains("cachedOk = ros2forUnity.Ok();", StringComparison.Ordinal),
                    Label(package, "164-28C-2: ROS2UnityCore.Ok avoids repeated native Ok checks after a cached good tick"));
                Check(tick.Contains("cachedOk = true;", StringComparison.Ordinal)
                      && tick.Contains("cachedOk = false;", StringComparison.Ordinal),
                    Label(package, "164-28C-3: ROS2UnityCore Tick refreshes cached Ok state"));
            }
        }

        private static void VerifySensorExecutorAvoidsDuplicateOk()
        {
            foreach (var package in RuntimePackages)
            {
                var source = Read(package + "/Runtime/Ros2ForUnity/Scripts/Sensor.cs");
                var create = PhaseValidationSourceHelpers.SourceMethod(source, "public override void CreateROSParticipants");
                var executor = PhaseValidationSourceHelpers.SourceMethod(source, "internal void ExecutorThreadSensorPublishAction");

                Check(create.Contains("if (!ros2Unity.Ok())", StringComparison.Ordinal),
                    Label(package, "164-28D-1: sensor creation still validates the ROS2 runtime before registering"));
                Check(!executor.Contains(".Ok()", StringComparison.Ordinal),
                    Label(package, "164-28D-2: executor-thread sensor publish path avoids a duplicate component Ok call"));
            }
        }

        private static void VerifyFrameNamesArePrecomputed()
        {
            foreach (var package in RuntimePackages)
            {
                var source = Read(package + "/Runtime/Ros2ForUnity/Scripts/Sensor.cs");
                var frameName = PhaseValidationSourceHelpers.SourceMethod(source, "public override string frameName");
                var create = PhaseValidationSourceHelpers.SourceMethod(source, "public override void CreateROSParticipants");

                Check(create.Contains("cachedFrameName = String.IsNullOrEmpty(ownerAgentName) ? frameID : ownerAgentName + \"/\" + frameID;", StringComparison.Ordinal),
                    Label(package, "164-28E-1: sensor frame names are cached when ROS participants are created"));
                Check(frameName.Contains("if (cachedFrameName != null)", StringComparison.Ordinal)
                      && !frameName.Contains("cachedFrameNameOwner", StringComparison.Ordinal)
                      && !frameName.Contains("cachedFrameNameFrameId", StringComparison.Ordinal),
                    Label(package, "164-28E-2: frameName avoids per-publish owner/frame string comparisons"));
            }
        }

        private static void VerifySnapshotContractsArePreserved()
        {
            foreach (var package in RuntimePackages)
            {
                var source = Read(package + "/Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs");
                if (!source.Contains("actionsSnapshot", StringComparison.Ordinal))
                    continue;

                Check(source.Contains("actionsSnapshot.AddRange(executableActions);", StringComparison.Ordinal)
                      && source.Contains("nodesSnapshot.AddRange(ros2csNodes);", StringComparison.Ordinal)
                      && source.IndexOf("foreach (Action action in actionsSnapshot)", StringComparison.Ordinal)
                         > source.IndexOf("lock (mutex)", StringComparison.Ordinal),
                    Label(package, "164-28F-1: existing executor snapshot path remains in place"));
            }
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-28\"", StringComparison.Ordinal), "164-28G-1: validation registry exposes Phase164-28");
            Check(project.Contains("Phase164_28Validation.cs", StringComparison.Ordinal), "164-28G-2: runtime validation project compiles Phase164-28");
        }

        private static string SourceSlices(string source, params string[] methodSignatures)
        {
            var slices = new List<string>();
            foreach (var signature in methodSignatures)
            {
                var slice = SourceMethodOrEmpty(source, signature);
                if (!string.IsNullOrEmpty(slice))
                    slices.Add(slice);
            }

            return string.Join("\n", slices);
        }

        private static string SourceMethodOrEmpty(string source, string signature)
        {
            if (!source.Contains(signature, StringComparison.Ordinal))
                return string.Empty;

            return PhaseValidationSourceHelpers.SourceMethod(source, signature);
        }

        private static string Label(string package, string label)
            => label + " (" + package.Substring(package.LastIndexOf('.') + 1) + ")";

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
