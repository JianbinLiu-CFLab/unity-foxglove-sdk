using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_27Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-27 Tests ---");
            _passed = 0;

            VerifyRuntimeSelectionCachesFilesystemInputs();
            VerifyRuntimeSelectionUsesCachedManifestCommunicationModes();
            VerifyRuntimeSelectionBindsManifestIdentityBeforeNativeInitialization();
            VerifyPlayModeGuardUsesCachedManifestFastPath();
            VerifyInspectorAvoidsLinqArrayChurn();
            VerifyRegistry();

            Console.WriteLine("Phase 164-27: " + _passed + " checks passed.\n");
        }

        private static void VerifyRuntimeSelectionCachesFilesystemInputs()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var discover = PhaseValidationSourceHelpers.SourceMethod(source, "public static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> DiscoverCandidateRuntimes");
            var readManifest = PhaseValidationSourceHelpers.SourceMethod(source, "public static IReadOnlyList<string> ReadManifestRuntimePackages");
            var invalidate = PhaseValidationSourceHelpers.SourceMethod(source, "public static void InvalidateStatusCache");
            var switchRuntime = PhaseValidationSourceHelpers.SourceMethod(source, "public static void SwitchActiveRuntimePackage");

            Check(source.Contains("_cachedCandidatesProjectDirectory", StringComparison.Ordinal)
                  && source.Contains("_cachedManifestProjectDirectory", StringComparison.Ordinal)
                  && source.Contains("_cachedManifestWriteTimeUtc", StringComparison.Ordinal)
                  && source.Contains("_cachedManifestLength", StringComparison.Ordinal),
                "164-27A-1: R2FU runtime selection stores editor-session caches for candidate and manifest inputs");
            Check(discover.Contains("_cachedCandidates != null", StringComparison.Ordinal)
                  && discover.Contains("return _cachedCandidates;", StringComparison.Ordinal)
                  && discover.Contains("_cachedCandidates = candidates;", StringComparison.Ordinal),
                "164-27A-2: runtime candidate discovery returns a cached descriptor list between invalidations");
            Check(readManifest.Contains("new FileInfo(manifestPath)", StringComparison.Ordinal)
                  && readManifest.Contains("_cachedManifestWriteTimeUtc == manifestInfo.LastWriteTimeUtc", StringComparison.Ordinal)
                  && readManifest.Contains("_cachedManifestLength == manifestInfo.Length", StringComparison.Ordinal)
                  && readManifest.Contains("return _cachedManifestRuntimePackages;", StringComparison.Ordinal),
                "164-27A-3: manifest runtime package reads are reused when timestamp and length are unchanged");
            Check(invalidate.Contains("_cachedCandidates = null;", StringComparison.Ordinal)
                  && invalidate.Contains("_cachedManifestRuntimePackages = null;", StringComparison.Ordinal)
                  && switchRuntime.Contains("InvalidateStatusCache();", StringComparison.Ordinal),
                "164-27A-4: runtime status cache is invalidated after manifest runtime switches");
        }

        private static void VerifyRuntimeSelectionUsesCachedManifestCommunicationModes()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var modes = PhaseValidationSourceHelpers.SourceMethod(source, "public static IReadOnlyList<string> GetCommunicationModeIds");
            var labels = PhaseValidationSourceHelpers.SourceMethod(source, "public static string[] GetCommunicationModeLabels");

            Check(source.Contains("CommunicationModeIds = BuildCommunicationModeIds(CommunicationModes);", StringComparison.Ordinal)
                  && source.Contains("CommunicationModeLabels = BuildCommunicationModeLabels(CommunicationModes);", StringComparison.Ordinal)
                  && source.Contains("Ros2ForUnityRuntimeCapabilityParser.Parse", StringComparison.Ordinal),
                "164-27B-1: runtime descriptor caches manifest-derived communication ids and labels");
            Check(modes.Contains("runtime?.CommunicationModeIds", StringComparison.Ordinal)
                  && !modes.Contains("new[]", StringComparison.Ordinal),
                "164-27B-2: communication mode ids do not allocate per Inspector repaint");
            Check(labels.Contains("runtime?.CommunicationModeLabels", StringComparison.Ordinal)
                  && !labels.Contains("new[]", StringComparison.Ordinal),
                "164-27B-3: communication mode labels reuse the descriptor cache");
        }

        private static void VerifyRuntimeSelectionBindsManifestIdentityBeforeNativeInitialization()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var applyEnvironment = PhaseValidationSourceHelpers.SourceMethod(source, "private static void ApplySelectedRuntimeEnvironment");
            var applyCommunicationMode = PhaseValidationSourceHelpers.SourceMethod(source, "public static void ApplyCommunicationModeEnvironment");
            var bindPlayMode = PhaseValidationSourceHelpers.SourceMethod(source, "public static void BindActiveRuntimeForPlayMode(Ros2ForUnityRuntimeSelectionStatus status)");

            Check(applyEnvironment.Contains("Environment.SetEnvironmentVariable(\"ROS_DISTRO\", runtime.RosDistro)", StringComparison.Ordinal)
                  && applyEnvironment.Contains("Environment.SetEnvironmentVariable(\"RMW_IMPLEMENTATION\", rmwImplementation)", StringComparison.Ordinal)
                  && applyEnvironment.Contains("GetRmwImplementationForCommunicationMode(runtime, communicationMode)", StringComparison.Ordinal),
                "164-27B-4: selected manifest identity and RMW are applied together without distro transport inference");
            Check(applyCommunicationMode.Contains("ApplySelectedRuntimeEnvironment(status.SelectedRuntime, mode);", StringComparison.Ordinal)
                  && bindPlayMode.Contains("ApplySelectedRuntimeEnvironment(status.SelectedRuntime, communicationMode);", StringComparison.Ordinal),
                "164-27B-5: runtime selection binds manifest identity before both editor and Play Mode native initialization paths");
        }

        private static void VerifyPlayModeGuardUsesCachedManifestFastPath()
        {
            var selection = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var guard = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            var compilationStarted = PhaseValidationSourceHelpers.SourceMethod(guard, "private static void OnCompilationStarted");
            var stop = PhaseValidationSourceHelpers.SourceMethod(guard, "private static bool StopPlayModeBeforeNativeReload");

            Check(selection.Contains("public static bool HasManifestRuntimePackage(string projectDirectory)", StringComparison.Ordinal)
                  && selection.Contains("=> ReadManifestRuntimePackages(projectDirectory).Count > 0;", StringComparison.Ordinal),
                "164-27C-1: runtime selection exposes a manifest-only active runtime check");
            Check(compilationStarted.Contains("Ros2ForUnityRuntimeSelection.InvalidateStatusCache();", StringComparison.Ordinal),
                "164-27C-2: compilation start invalidates R2FU runtime selection caches");
            Check(stop.Contains("HasManifestRuntimePackage(projectDirectory)", StringComparison.Ordinal)
                  && !stop.Contains("GetStatus(projectDirectory)", StringComparison.Ordinal)
                  && !stop.Contains("SelectedRuntime", StringComparison.Ordinal),
                "164-27C-3: native reload guard avoids full candidate discovery while deciding whether to stop Play Mode");
        }

        private static void VerifyInspectorAvoidsLinqArrayChurn()
        {
            var source = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs");
            var drawActive = PhaseValidationSourceHelpers.SourceMethod(source, "public static void DrawActiveRuntimeSelector");
            var communication = PhaseValidationSourceHelpers.SourceMethod(source, "private static void DrawCommunicationModePopup");
            var runtimePopup = PhaseValidationSourceHelpers.SourceMethod(source, "private static void DrawRuntimePopup");

            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal),
                "164-27D-1: R2FU runtime selector inspector keeps LINQ out of repaint drawing");
            Check(drawActive.Contains("var installed = ToRuntimeArray(status.InstalledRuntimes);", StringComparison.Ordinal)
                  && drawActive.Contains("var installedLabels = BuildRuntimeLabels(installed);", StringComparison.Ordinal),
                "164-27D-2: runtime selector builds installed runtime labels once per repaint at the top-level draw call");
            Check(communication.Contains("GetCommunicationModeIds(selectedRuntime)", StringComparison.Ordinal)
                  && communication.Contains("GetCommunicationModeLabels(selectedRuntime)", StringComparison.Ordinal)
                  && communication.Contains("IndexOfMode(modes, selectedMode)", StringComparison.Ordinal)
                  && !communication.Contains("ToArray()", StringComparison.Ordinal)
                  && !communication.Contains("Select(", StringComparison.Ordinal),
                "164-27D-3: communication mode popup avoids per-repaint LINQ array allocation");
            Check(runtimePopup.Contains("string[] installedLabels", StringComparison.Ordinal)
                  && runtimePopup.Contains("Array.Copy(installedLabels", StringComparison.Ordinal)
                  && !runtimePopup.Contains("Concat", StringComparison.Ordinal)
                  && !runtimePopup.Contains("Select(", StringComparison.Ordinal),
                "164-27D-4: runtime popup receives prebuilt labels and avoids LINQ concat allocation");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-27\"", StringComparison.Ordinal), "164-27E-1: validation registry exposes Phase164-27");
            Check(project.Contains("Phase164_27Validation.cs", StringComparison.Ordinal), "164-27E-2: runtime validation project compiles Phase164-27");
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
