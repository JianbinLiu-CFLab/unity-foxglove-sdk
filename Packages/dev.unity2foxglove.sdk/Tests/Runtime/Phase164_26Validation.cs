using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_26Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-26 Tests ---");
            _passed = 0;

            VerifyCertificateGeneratorCachesReflectionLookups();
            VerifyR2fuPlayModeGuardReusesStatusSnapshot();
            VerifySchemaEvidenceAvoidsGlobalObjectScans();
            VerifyHashHelpersAvoidAvoidableAllocations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-26: " + _passed + " checks passed.\n");
        }

        private static void VerifyCertificateGeneratorCachesReflectionLookups()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");
            var requireType = PhaseValidationSourceHelpers.SourceMethod(source, "private static Type RequireMonoSecurityType");
            var invoke = PhaseValidationSourceHelpers.SourceMethod(source, "private static object Invoke(object target");
            var invokeStatic = PhaseValidationSourceHelpers.SourceMethod(source, "private static object InvokeStatic");
            var resolveProperty = PhaseValidationSourceHelpers.SourceMethod(source, "private static PropertyInfo ResolveProperty");
            var resolveMethods = PhaseValidationSourceHelpers.SourceMethod(source, "private static MethodInfo[] ResolveMethods");

            Check(source.Contains("private static readonly ConcurrentDictionary<string, Type> MonoSecurityTypeCache", StringComparison.Ordinal)
                  && source.Contains("private static readonly ConcurrentDictionary<string, PropertyInfo> PropertyCache", StringComparison.Ordinal)
                  && source.Contains("private static readonly ConcurrentDictionary<string, MethodInfo[]> MethodCache", StringComparison.Ordinal),
                "164-26A-1: local certificate generator owns bounded static caches for Mono.Security reflection lookups");
            Check(requireType.Contains("MonoSecurityTypeCache.GetOrAdd(fullName, ResolveMonoSecurityType)", StringComparison.Ordinal),
                "164-26A-2: Mono.Security type lookup is cached after the first assembly scan");
            Check(invoke.Contains("ResolveMethods(type, name, BindingFlags.Public | BindingFlags.Instance)", StringComparison.Ordinal)
                  && invokeStatic.Contains("ResolveMethods(type, name, BindingFlags.Public | BindingFlags.Static)", StringComparison.Ordinal)
                  && resolveMethods.Contains("MethodCache.GetOrAdd(key", StringComparison.Ordinal)
                  && !invoke.Contains("GetMethods()", StringComparison.Ordinal)
                  && !invokeStatic.Contains("GetMethods", StringComparison.Ordinal),
                "164-26A-3: certificate invocation helpers reuse cached method candidate lists");
            Check(resolveProperty.Contains("PropertyCache.GetOrAdd(key", StringComparison.Ordinal)
                  && resolveProperty.Contains("type.GetProperty(name)", StringComparison.Ordinal),
                "164-26A-4: certificate property reflection lookups are cached");
        }

        private static void VerifyR2fuPlayModeGuardReusesStatusSnapshot()
        {
            var selection = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs");
            var guard = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs");
            var onExitingEditMode = PhaseValidationSourceHelpers.SourceMethod(guard, "private static void OnExitingEditMode");

            Check(selection.Contains("public static bool IsEditorRestartRequired(Ros2ForUnityRuntimeSelectionStatus status)", StringComparison.Ordinal)
                  && selection.Contains("public static void BindActiveRuntimeForPlayMode(Ros2ForUnityRuntimeSelectionStatus status)", StringComparison.Ordinal),
                "164-26B-1: R2FU runtime selection exposes status-snapshot overloads for restart and play binding checks");
            Check(onExitingEditMode.Contains("var status = Ros2ForUnityRuntimeSelection.GetStatus(projectDirectory);", StringComparison.Ordinal)
                  && onExitingEditMode.Contains("GetRuntimePackageRequiringEditorRestart(status)", StringComparison.Ordinal)
                  && onExitingEditMode.Contains("GetCommunicationModeRequiringEditorRestart(status)", StringComparison.Ordinal)
                  && onExitingEditMode.Contains("BindActiveRuntimeForPlayMode(status)", StringComparison.Ordinal)
                  && !onExitingEditMode.Contains("GetRuntimePackageRequiringEditorRestart(projectDirectory)", StringComparison.Ordinal)
                  && !onExitingEditMode.Contains("GetCommunicationModeRequiringEditorRestart(projectDirectory)", StringComparison.Ordinal),
                "164-26B-2: R2FU Play Mode guard performs one package status scan per ExitingEditMode callback");
        }

        private static void VerifySchemaEvidenceAvoidsGlobalObjectScans()
        {
            var paths = Read("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var settings = Read("Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs");
            var syncOpen = PhaseValidationSourceHelpers.SourceMethod(settings, "internal static void SyncOpenSceneManagers");

            Check(paths.Contains("_cachedEvidenceRootProjectRelativeValid", StringComparison.Ordinal)
                  && paths.Contains("InvalidateCurrentEvidenceRootCache()", StringComparison.Ordinal)
                  && paths.Contains("TryNormalizeAssetsRoot(currentRoot", StringComparison.Ordinal),
                "164-26C-1: schema evidence path normalization is cached and explicitly invalidated");
            Check(settings.Contains("Unity2FoxgloveSchemaEvidencePaths.InvalidateCurrentEvidenceRootCache();", StringComparison.Ordinal),
                "164-26C-2: schema evidence settings invalidate path cache when the configured root changes");
            Check(settings.Contains("TryNormalizeAssetsRootCached(root", StringComparison.Ordinal)
                  && settings.Contains("ResolveCurrentEvidenceRootCached()", StringComparison.Ordinal),
                "164-26C-2B: schema evidence settings cache repaint-time normalization and resolved root labels");
            Check(syncOpen.Contains("for (var i = 0; i < SceneManager.sceneCount; i++)", StringComparison.Ordinal)
                  && syncOpen.Contains("SyncManagersInScene(SceneManager.GetSceneAt(i));", StringComparison.Ordinal)
                  && !settings.Contains("Resources.FindObjectsOfTypeAll", StringComparison.Ordinal),
                "164-26C-3: schema evidence sync walks open scenes instead of scanning all loaded Unity objects");
        }

        private static void VerifyHashHelpersAvoidAvoidableAllocations()
        {
            var distributor = Read("Packages/dev.unity2foxglove.sdk/Runtime/Transport/Security/FoxgloveCertificateDistributor.cs");
            var fingerprint = PhaseValidationSourceHelpers.SourceMethod(distributor, "public static string ComputeSha256Fingerprint");
            var verifier = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/OpenH264/OpenH264ArtifactHashVerifier.cs");
            var toHex = PhaseValidationSourceHelpers.SourceMethod(verifier, "private static string ToUpperHex");

            Check(fingerprint.Contains("File.OpenRead(path)", StringComparison.Ordinal)
                  && fingerprint.Contains("sha.ComputeHash(stream)", StringComparison.Ordinal)
                  && !fingerprint.Contains("File.ReadAllBytes", StringComparison.Ordinal),
                "164-26D-1: certificate distributor fingerprints files through a stream hash");
            Check(toHex.Contains("var chars = new char[bytes.Length * 2];", StringComparison.Ordinal)
                  && toHex.Contains("const string hex = \"0123456789ABCDEF\";", StringComparison.Ordinal)
                  && toHex.Contains("return new string(chars);", StringComparison.Ordinal)
                  && !toHex.Contains("ToString(\"X2\")", StringComparison.Ordinal)
                  && !verifier.Contains("StringBuilder", StringComparison.Ordinal),
                "164-26D-2: OpenH264 hash verifier writes uppercase hex without per-byte string allocations");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-26\"", StringComparison.Ordinal), "164-26E-1: validation registry exposes Phase164-26");
            Check(project.Contains("Phase164_26Validation.cs", StringComparison.Ordinal), "164-26E-2: runtime validation project compiles Phase164-26");
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
